using BugConvergenceTool.Models;
using BugConvergenceTool.Optimizers;

namespace BugConvergenceTool.Services;

/// <summary>
/// 信頼区間計算サービス
/// パラメトリック・ブートストラップ法により95%信頼区間を計算する
/// </summary>
/// <remarks>
/// 前提条件：
/// - ReliabilityGrowthModelBase.Calculate は純粋関数（内部状態を持たない）であること
/// - FittingResult.ParameterVector と PredictionTimes が事前にセットされていること
/// </remarks>
public class ConfidenceIntervalService
{
    private readonly IOptimizer _bootstrapOptimizer;
    private readonly int _bootstrapIterations;
    private readonly bool _verbose;

    // 並列環境でシードが衝突しない RNG
    private static readonly ThreadLocal<Random> _threadLocalRandom =
        new ThreadLocal<Random>(() => new Random(Guid.NewGuid().GetHashCode()));

    /// <summary>
    /// コンストラクタ
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
        _bootstrapIterations = bootstrapIterations;
        _verbose = verbose;
    }

    /// <summary>
    /// 95%信頼区間を計算し、FittingResult に設定する
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

        // 1. 残差 e_i = y_i - m(t_i; θ*) を計算
        var residuals = new double[n];
        for (int i = 0; i < n; i++)
        {
            residuals[i] = yData[i] - model.Calculate(tData[i], originalParams);
        }

        // 2. ブートストラップ予測値の格納領域 [t][iter]
        var bootstrapPredictions = new double[predictionLength][];
        for (int i = 0; i < predictionLength; i++)
            bootstrapPredictions[i] = new double[_bootstrapIterations];

        // 3. 成功/失敗カウント用（スレッドセーフ）
        int successCount = 0;
        int fallbackCount = 0;

        // 4. Parallel.For でブートストラップ実行
        Parallel.For(0, _bootstrapIterations, iter =>
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

                double[] useParams;
                if (optResult.Success)
                {
                    useParams = optResult.Parameters;
                    Interlocked.Increment(ref successCount);
                }
                else
                {
                    useParams = originalParams; // フォールバック
                    Interlocked.Increment(ref fallbackCount);
                }

                // 4-4. 予測曲線を再計算（PredictionTimes ベース）
                for (int t = 0; t < predictionLength; t++)
                {
                    double timePoint = result.PredictionTimes[t];
                    bootstrapPredictions[t][iter] = model.Calculate(timePoint, useParams);
                }
            }
            catch
            {
                // 例外発生時もフォールバック（元の予測値）
                Interlocked.Increment(ref fallbackCount);
                for (int t = 0; t < predictionLength; t++)
                {
                    bootstrapPredictions[t][iter] = result.PredictedValues[t];
                }
            }
        });

        if (_verbose)
        {
            Console.WriteLine($"    ブートストラップ: 成功={successCount}, フォールバック={fallbackCount}");
        }

        // 5. パーセンタイル法で 95% 信頼区間を計算
        var lowerBounds = new double[predictionLength];
        var upperBounds = new double[predictionLength];

        for (int t = 0; t < predictionLength; t++)
        {
            var samples = bootstrapPredictions[t];
            Array.Sort(samples);

            int b = _bootstrapIterations;
            int idxLow = (int)Math.Round((b - 1) * 0.025);
            int idxHigh = (int)Math.Round((b - 1) * 0.975);

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
}
