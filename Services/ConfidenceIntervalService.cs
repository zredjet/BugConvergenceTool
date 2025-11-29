using BugConvergenceTool.Models;
using BugConvergenceTool.Optimizers;

namespace BugConvergenceTool.Services;

/// <summary>
/// 信頼区間計算サービス
/// パラメトリック・ブートストラップ法により信頼区間を計算する
/// </summary>
/// <remarks>
/// 前提条件：
/// - ReliabilityGrowthModelBase.Calculate は純粋関数（内部状態を持たない）であること
/// - FittingResult.ParameterVector と PredictionTimes が事前にセットされていること
/// 
/// 設計方針：
/// - ブートストラップ用には軽量 Nelder-Mead を使用（初期値は元の最適解θ*）
/// - 擬似データはθ*からのわずかな揺らぎなので、真の最適解は大きく離れない前提
/// - 収束失敗やSSEが極端に悪い解は破棄してフォールバック
/// </remarks>
public class ConfidenceIntervalService
{
    private readonly IOptimizer _bootstrapOptimizer;
    private readonly BootstrapSettings _settings;
    private readonly bool _verbose;

    // 並列環境でシードが衝突しない RNG
    private static readonly ThreadLocal<Random> _threadLocalRandom =
        new ThreadLocal<Random>(() => new Random(Guid.NewGuid().GetHashCode()));

    /// <summary>
    /// コンストラクタ（設定クラスを使用）
    /// </summary>
    /// <param name="settings">ブートストラップ設定</param>
    /// <param name="verbose">詳細出力するかどうか</param>
    public ConfidenceIntervalService(BootstrapSettings settings, bool verbose = false)
    {
        _settings = settings;
        _verbose = verbose;
        
        // 設定に基づいてブートストラップ用オプティマイザを作成
        _bootstrapOptimizer = CreateBootstrapOptimizer(
            _settings.OptimizerMaxIterations,
            _settings.OptimizerTolerance
        );
    }

    /// <summary>
    /// コンストラクタ（後方互換性のため維持）
    /// </summary>
    /// <param name="bootstrapOptimizer">ブートストラップ用の軽量オプティマイザ</param>
    /// <param name="bootstrapIterations">ブートストラップ反復回数（デフォルト: 200）</param>
    /// <param name="verbose">詳細出力するかどうか</param>
    public ConfidenceIntervalService(
        IOptimizer bootstrapOptimizer,
        int bootstrapIterations = 200,
        bool verbose = false)
    {
        _bootstrapOptimizer = bootstrapOptimizer;
        _settings = new BootstrapSettings
        {
            Iterations = bootstrapIterations
        };
        _verbose = verbose;
    }

    /// <summary>
    /// 信頼区間を計算し、FittingResult に設定する
    /// </summary>
    /// <param name="model">信頼度成長モデル</param>
    /// <param name="tData">時刻データ（観測データ）</param>
    /// <param name="yData">累積バグ数データ（観測データ）</param>
    /// <param name="result">フィッティング結果（ParameterVector と PredictionTimes がセット済みであること）</param>
    /// <exception cref="InvalidOperationException">ParameterVector または PredictionTimes が不正な場合</exception>
    public void CalculateIntervals(
        ReliabilityGrowthModelBase model,
        double[] tData,
        double[] yData,
        FittingResult result)
    {
        int n = tData.Length;
        int predictionLength = result.PredictedValues.Length;

        // 前提条件のチェック
        if (result.ParameterVector == null || result.ParameterVector.Length == 0)
            throw new InvalidOperationException("ParameterVector が設定されていません。");

        if (result.PredictionTimes == null || result.PredictionTimes.Length != predictionLength)
            throw new InvalidOperationException("PredictionTimes が不正です。");

        var originalParams = (double[])result.ParameterVector.Clone();

        // 元のフィットのSSEを計算（フィルタリング用）
        double originalSSE = model.CalculateSSE(tData, yData, originalParams);
        double sseThreshold = _settings.SSEThresholdMultiplier > 0 
            ? originalSSE * _settings.SSEThresholdMultiplier 
            : double.MaxValue;

        // 1. 残差 e_i = y_i - m(t_i; θ*) を計算
        var residuals = new double[n];
        for (int i = 0; i < n; i++)
        {
            residuals[i] = yData[i] - model.Calculate(tData[i], originalParams);
        }

        // 2. ブートストラップ予測値の格納領域 [t][iter]
        var bootstrapPredictions = new double[predictionLength][];
        for (int i = 0; i < predictionLength; i++)
            bootstrapPredictions[i] = new double[_settings.Iterations];

        // 3. 統計カウント用（スレッドセーフ）
        int successCount = 0;
        int fallbackCount = 0;
        int sseFilteredCount = 0;

        // 4. Parallel.For でブートストラップ実行
        Parallel.For(0, _settings.Iterations, iter =>
        {
            var random = _threadLocalRandom.Value!;
            var syntheticY = new double[n];

            // 4-1. 擬似データの生成（残差リサンプリング）
            for (int i = 0; i < n; i++)
            {
                double pred = model.Calculate(tData[i], originalParams);
                double randomResidual = residuals[random.Next(n)];
                double y = pred + randomResidual;
                if (y < 0) y = 0; // 累積バグ数は非負
                syntheticY[i] = y;
            }

            double[] useParams = originalParams; // デフォルトはフォールバック

            try
            {
                // 4-2. Bounds を再計算（擬似データに合わせて）
                var (lower, upper) = model.GetBounds(tData, syntheticY);

                // 4-3. ブートストラップ用最適化（初期値に θ* を使用）
                var optResult = _bootstrapOptimizer.Optimize(
                    p => model.CalculateSSE(tData, syntheticY, p),
                    lower, upper,
                    initialGuess: originalParams
                );

                if (optResult.Success)
                {
                    // 4-4. SSE フィルタリング（極端に悪い解は破棄）
                    double newSSE = model.CalculateSSE(tData, syntheticY, optResult.Parameters);
                    
                    if (newSSE <= sseThreshold)
                    {
                        useParams = optResult.Parameters;
                        Interlocked.Increment(ref successCount);
                    }
                    else
                    {
                        // SSE が悪すぎる場合はフォールバック
                        Interlocked.Increment(ref sseFilteredCount);
                        Interlocked.Increment(ref fallbackCount);
                    }
                }
                else
                {
                    Interlocked.Increment(ref fallbackCount);
                }
            }
            catch
            {
                // 例外発生時もフォールバック
                Interlocked.Increment(ref fallbackCount);
            }

            // 4-5. 予測曲線を計算（PredictionTimes ベース）
            for (int t = 0; t < predictionLength; t++)
            {
                double timePoint = result.PredictionTimes[t];
                bootstrapPredictions[t][iter] = model.Calculate(timePoint, useParams);
            }
        });

        if (_verbose)
        {
            Console.WriteLine($"    ブートストラップ: 成功={successCount}, フォールバック={fallbackCount}" +
                (sseFilteredCount > 0 ? $" (SSEフィルタ={sseFilteredCount})" : ""));
        }

        // 5. パーセンタイル法で信頼区間を計算
        double alpha = 1.0 - _settings.ConfidenceLevel;
        double lowerPercentile = alpha / 2.0;      // 例: 0.025 for 95%
        double upperPercentile = 1.0 - alpha / 2.0; // 例: 0.975 for 95%

        var lowerBounds = new double[predictionLength];
        var upperBounds = new double[predictionLength];

        for (int t = 0; t < predictionLength; t++)
        {
            var samples = bootstrapPredictions[t];
            Array.Sort(samples);

            int b = _settings.Iterations;
            int idxLow = (int)Math.Round((b - 1) * lowerPercentile);
            int idxHigh = (int)Math.Round((b - 1) * upperPercentile);

            idxLow = Math.Clamp(idxLow, 0, b - 1);
            idxHigh = Math.Clamp(idxHigh, 0, b - 1);

            lowerBounds[t] = samples[idxLow];
            upperBounds[t] = samples[idxHigh];
        }

        result.LowerConfidenceBounds = lowerBounds;
        result.UpperConfidenceBounds = upperBounds;
    }

    /// <summary>
    /// ブートストラップ用の軽量オプティマイザを作成する
    /// </summary>
    /// <param name="maxIterations">最大反復回数（デフォルト: 80）</param>
    /// <param name="tolerance">収束判定閾値（デフォルト: 1e-6）</param>
    /// <returns>軽量 Nelder-Mead オプティマイザ</returns>
    public static IOptimizer CreateBootstrapOptimizer(int maxIterations = 80, double tolerance = 1e-6)
    {
        return new NelderMeadOptimizer(
            maxIterations: maxIterations,
            tolerance: tolerance
        );
    }
    
    /// <summary>
    /// 設定からブートストラップサービスを作成するファクトリメソッド
    /// </summary>
    /// <param name="verbose">詳細出力するかどうか</param>
    /// <returns>設定済みの ConfidenceIntervalService</returns>
    public static ConfidenceIntervalService CreateFromConfig(bool verbose = false)
    {
        var settings = ConfigurationService.Current.Bootstrap;
        return new ConfidenceIntervalService(settings, verbose);
    }
}

/// <summary>
/// Fisher情報行列に基づく解析的信頼区間計算サービス
/// </summary>
/// <remarks>
/// <para>
/// パラメータの漸近的な信頼区間を計算します。
/// ブートストラップ法より高速ですが、以下の仮定に依存します：
/// - 大標本近似（漸近正規性）
/// - モデルが正しく特定されている
/// - 観測値が独立
/// </para>
/// <para>
/// 小標本やモデル特定に不確実性がある場合は、
/// ブートストラップ法との比較を推奨します。
/// </para>
/// </remarks>
public class FisherInformationService
{
    private readonly double _confidenceLevel;
    private readonly double _h; // 数値微分のステップサイズ
    
    /// <summary>
    /// コンストラクタ
    /// </summary>
    /// <param name="confidenceLevel">信頼水準（デフォルト: 0.95）</param>
    /// <param name="stepSize">数値微分のステップサイズ（デフォルト: 1e-5）</param>
    public FisherInformationService(double confidenceLevel = 0.95, double stepSize = 1e-5)
    {
        _confidenceLevel = confidenceLevel;
        _h = stepSize;
    }
    
    /// <summary>
    /// パラメータの漸近的標準誤差を計算
    /// </summary>
    /// <param name="model">信頼度成長モデル</param>
    /// <param name="tData">時刻データ</param>
    /// <param name="yData">累積バグ数データ</param>
    /// <param name="parameters">推定パラメータ</param>
    /// <returns>各パラメータの標準誤差（計算失敗時はNaN）</returns>
    public double[] CalculateParameterStandardErrors(
        ReliabilityGrowthModelBase model,
        double[] tData,
        double[] yData,
        double[] parameters)
    {
        int n = tData.Length;
        int k = parameters.Length;
        
        // SSEベースの分散推定
        double sse = model.CalculateSSE(tData, yData, parameters);
        double sigma2 = sse / (n - k); // 残差分散
        
        // 数値ヘッセ行列（SSEの2階微分）の計算
        var hessian = CalculateHessian(
            p => model.CalculateSSE(tData, yData, p),
            parameters);
        
        // ヘッセ行列の逆行列 = 分散共分散行列の近似
        var covMatrix = InvertMatrix(hessian);
        
        if (covMatrix == null)
        {
            // 逆行列計算失敗
            return Enumerable.Repeat(double.NaN, k).ToArray();
        }
        
        // 対角成分から標準誤差を計算
        var standardErrors = new double[k];
        for (int i = 0; i < k; i++)
        {
            double variance = covMatrix[i, i] * sigma2 * 2; // SSEのヘッセ = 2 * I
            standardErrors[i] = variance > 0 ? Math.Sqrt(variance) : double.NaN;
        }
        
        return standardErrors;
    }
    
    /// <summary>
    /// パラメータの信頼区間を計算
    /// </summary>
    public (double[] lower, double[] upper) CalculateParameterConfidenceIntervals(
        ReliabilityGrowthModelBase model,
        double[] tData,
        double[] yData,
        double[] parameters)
    {
        int k = parameters.Length;
        var se = CalculateParameterStandardErrors(model, tData, yData, parameters);
        
        // 正規分布の分位点（大標本近似）
        double alpha = 1 - _confidenceLevel;
        double z = MathNet.Numerics.Distributions.Normal.InvCDF(0, 1, 1 - alpha / 2);
        
        var lower = new double[k];
        var upper = new double[k];
        
        for (int i = 0; i < k; i++)
        {
            if (double.IsNaN(se[i]))
            {
                lower[i] = double.NaN;
                upper[i] = double.NaN;
            }
            else
            {
                lower[i] = parameters[i] - z * se[i];
                upper[i] = parameters[i] + z * se[i];
            }
        }
        
        return (lower, upper);
    }
    
    /// <summary>
    /// 予測値の信頼区間を計算（デルタ法）
    /// </summary>
    public (double[] lower, double[] upper) CalculatePredictionConfidenceIntervals(
        ReliabilityGrowthModelBase model,
        double[] tData,
        double[] yData,
        double[] parameters,
        double[] predictionTimes)
    {
        int n = tData.Length;
        int k = parameters.Length;
        int nPred = predictionTimes.Length;
        
        // 残差分散
        double sse = model.CalculateSSE(tData, yData, parameters);
        double sigma2 = sse / (n - k);
        
        // ヘッセ行列の逆行列
        var hessian = CalculateHessian(
            p => model.CalculateSSE(tData, yData, p),
            parameters);
        var covMatrix = InvertMatrix(hessian);
        
        if (covMatrix == null)
        {
            return (
                Enumerable.Repeat(double.NaN, nPred).ToArray(),
                Enumerable.Repeat(double.NaN, nPred).ToArray()
            );
        }
        
        // 分位点
        double alpha = 1 - _confidenceLevel;
        double z = MathNet.Numerics.Distributions.Normal.InvCDF(0, 1, 1 - alpha / 2);
        
        var lower = new double[nPred];
        var upper = new double[nPred];
        
        for (int t = 0; t < nPred; t++)
        {
            double time = predictionTimes[t];
            
            // 勾配ベクトル ∂m/∂θ
            var gradient = CalculateGradient(
                p => model.Calculate(time, p),
                parameters);
            
            // 予測分散 = σ² * g' * (H⁻¹) * g
            double variance = 0;
            for (int i = 0; i < k; i++)
            {
                for (int j = 0; j < k; j++)
                {
                    variance += gradient[i] * covMatrix[i, j] * gradient[j] * sigma2 * 2;
                }
            }
            
            double se = variance > 0 ? Math.Sqrt(variance) : 0;
            double pred = model.Calculate(time, parameters);
            
            lower[t] = pred - z * se;
            upper[t] = pred + z * se;
        }
        
        return (lower, upper);
    }
    
    /// <summary>
    /// 数値微分による勾配計算
    /// </summary>
    private double[] CalculateGradient(Func<double[], double> f, double[] x)
    {
        int n = x.Length;
        var grad = new double[n];
        
        for (int i = 0; i < n; i++)
        {
            var xPlus = (double[])x.Clone();
            var xMinus = (double[])x.Clone();
            
            double step = Math.Max(_h, Math.Abs(x[i]) * _h);
            xPlus[i] += step;
            xMinus[i] -= step;
            
            grad[i] = (f(xPlus) - f(xMinus)) / (2 * step);
        }
        
        return grad;
    }
    
    /// <summary>
    /// 数値微分によるヘッセ行列計算
    /// </summary>
    private double[,] CalculateHessian(Func<double[], double> f, double[] x)
    {
        int n = x.Length;
        var hessian = new double[n, n];
        
        for (int i = 0; i < n; i++)
        {
            for (int j = i; j < n; j++)
            {
                double step_i = Math.Max(_h, Math.Abs(x[i]) * _h);
                double step_j = Math.Max(_h, Math.Abs(x[j]) * _h);
                
                var x_pp = (double[])x.Clone(); x_pp[i] += step_i; x_pp[j] += step_j;
                var x_pm = (double[])x.Clone(); x_pm[i] += step_i; x_pm[j] -= step_j;
                var x_mp = (double[])x.Clone(); x_mp[i] -= step_i; x_mp[j] += step_j;
                var x_mm = (double[])x.Clone(); x_mm[i] -= step_i; x_mm[j] -= step_j;
                
                double d2f = (f(x_pp) - f(x_pm) - f(x_mp) + f(x_mm)) / (4 * step_i * step_j);
                
                hessian[i, j] = d2f;
                hessian[j, i] = d2f; // 対称行列
            }
        }
        
        return hessian;
    }
    
    /// <summary>
    /// 行列の逆行列を計算（LU分解）
    /// </summary>
    private static double[,]? InvertMatrix(double[,] matrix)
    {
        try
        {
            int n = matrix.GetLength(0);
            var m = MathNet.Numerics.LinearAlgebra.Matrix<double>.Build.DenseOfArray(matrix);
            
            // 条件数チェック（特異に近い場合は失敗）
            var svd = m.Svd();
            double conditionNumber = svd.S[0] / svd.S[n - 1];
            if (conditionNumber > 1e10 || double.IsNaN(conditionNumber))
            {
                return null;
            }
            
            var inv = m.Inverse();
            return inv.ToArray();
        }
        catch
        {
            return null;
        }
    }
}
