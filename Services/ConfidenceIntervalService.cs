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
