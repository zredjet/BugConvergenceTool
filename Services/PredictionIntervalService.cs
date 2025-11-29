using BugConvergenceTool.Models;
using BugConvergenceTool.Optimizers;

namespace BugConvergenceTool.Services;

/// <summary>
/// 予測区間の種類
/// </summary>
public enum PredictionIntervalType
{
    /// <summary>
    /// パラメータ不確実性のみ（既存のConfidenceIntervalServiceと同等）
    /// 「真のパラメータはこの範囲にある」
    /// </summary>
    Confidence,
    
    /// <summary>
    /// パラメータ不確実性 + 観測変動（Poisson変動）
    /// 「将来の観測値はこの範囲に入る」
    /// </summary>
    Prediction
}

/// <summary>
/// 予測区間結果
/// </summary>
public class PredictionIntervalResult
{
    /// <summary>区間の種類</summary>
    public PredictionIntervalType Type { get; init; }
    
    /// <summary>予測時刻</summary>
    public double[] Times { get; init; } = Array.Empty<double>();
    
    /// <summary>点推定値</summary>
    public double[] PointEstimates { get; init; } = Array.Empty<double>();
    
    /// <summary>下限</summary>
    public double[] LowerBounds { get; init; } = Array.Empty<double>();
    
    /// <summary>上限</summary>
    public double[] UpperBounds { get; init; } = Array.Empty<double>();
    
    /// <summary>信頼水準（0-1）</summary>
    public double ConfidenceLevel { get; init; }
    
    /// <summary>ブートストラップ反復回数</summary>
    public int BootstrapIterations { get; init; }
    
    /// <summary>成功した反復の割合</summary>
    public double SuccessRate { get; init; }
    
    /// <summary>計算時間（ミリ秒）</summary>
    public long ComputationTimeMs { get; init; }
}

/// <summary>
/// 予測区間計算用の設定
/// </summary>
public class PredictionIntervalSettings
{
    /// <summary>ブートストラップ反復回数</summary>
    public int Iterations { get; init; } = 500;
    
    /// <summary>信頼水準（0-1）</summary>
    public double ConfidenceLevel { get; init; } = 0.95;
    
    /// <summary>オプティマイザの最大反復回数</summary>
    public int OptimizerMaxIterations { get; init; } = 100;
    
    /// <summary>オプティマイザの収束判定閾値</summary>
    public double OptimizerTolerance { get; init; } = 1e-6;
    
    /// <summary>SSE閾値の倍率（元のSSEのこの倍数を超える解は破棄）</summary>
    public double SSEThresholdMultiplier { get; init; } = 10.0;
    
    /// <summary>Poissonノイズの最小λ（数値安定性のため）</summary>
    public double MinLambda { get; init; } = 0.1;
}

/// <summary>
/// パラメトリック・ブートストラップによる予測区間計算サービス
/// </summary>
/// <remarks>
/// 既存の ConfidenceIntervalService との違い:
/// - 残差リサンプリングではなく、パラメトリック・ブートストラップ（モデルからのPoisson再生成）を使用
/// - 予測区間（観測変動を含む）の計算が可能
/// - 将来時点（観測データ外）の予測に適している
/// </remarks>
public class PredictionIntervalService
{
    private readonly PredictionIntervalSettings _settings;
    private readonly bool _verbose;

    // スレッドセーフな乱数生成器
    private static readonly ThreadLocal<Random> _threadLocalRandom =
        new ThreadLocal<Random>(() => new Random(Guid.NewGuid().GetHashCode()));

    /// <summary>
    /// コンストラクタ
    /// </summary>
    public PredictionIntervalService(PredictionIntervalSettings? settings = null, bool verbose = false)
    {
        _settings = settings ?? new PredictionIntervalSettings();
        _verbose = verbose;
    }

    /// <summary>
    /// デフォルト設定でサービスを作成
    /// </summary>
    public static PredictionIntervalService CreateDefault(bool verbose = false)
    {
        return new PredictionIntervalService(null, verbose);
    }

    /// <summary>
    /// パラメトリック・ブートストラップによる予測区間を計算
    /// </summary>
    /// <param name="model">信頼度成長モデル</param>
    /// <param name="tData">観測された時刻データ</param>
    /// <param name="yData">観測された累積バグ数</param>
    /// <param name="optimalParams">最適化されたパラメータ</param>
    /// <param name="predictionTimes">予測時刻（観測データ外も可）</param>
    /// <param name="intervalType">区間の種類</param>
    /// <returns>予測区間結果</returns>
    public PredictionIntervalResult Calculate(
        ReliabilityGrowthModelBase model,
        double[] tData,
        double[] yData,
        double[] optimalParams,
        double[] predictionTimes,
        PredictionIntervalType intervalType = PredictionIntervalType.Prediction)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        
        int nPred = predictionTimes.Length;
        int nObs = tData.Length;
        
        // ブートストラップ予測値の格納領域 [時点][反復]
        var bootstrapPredictions = new double[nPred][];
        for (int t = 0; t < nPred; t++)
            bootstrapPredictions[t] = new double[_settings.Iterations];
        
        // 元のSSEを計算（フィルタリング用）
        double originalSSE = model.CalculateSSE(tData, yData, optimalParams);
        double sseThreshold = _settings.SSEThresholdMultiplier > 0 
            ? originalSSE * _settings.SSEThresholdMultiplier 
            : double.MaxValue;
        
        // オプティマイザを作成
        var optimizer = new NelderMeadOptimizer(
            maxIterations: _settings.OptimizerMaxIterations,
            tolerance: _settings.OptimizerTolerance
        );
        
        // 統計カウント
        int successCount = 0;
        int fallbackCount = 0;
        
        // 並列ブートストラップ
        Parallel.For(0, _settings.Iterations, iter =>
        {
            var random = _threadLocalRandom.Value!;
            
            // 1. パラメトリック・ブートストラップ：モデルからデータを再生成
            var syntheticY = GenerateSyntheticData(model, tData, optimalParams, random);
            
            // 2. 再フィッティング
            double[] bootstrapParams;
            try
            {
                var (lower, upper) = model.GetBounds(tData, syntheticY);
                var result = optimizer.Optimize(
                    p => model.CalculateSSE(tData, syntheticY, p),
                    lower, upper,
                    initialGuess: optimalParams
                );
                
                if (result.Success)
                {
                    double newSSE = model.CalculateSSE(tData, syntheticY, result.Parameters);
                    if (newSSE <= sseThreshold)
                    {
                        bootstrapParams = result.Parameters;
                        Interlocked.Increment(ref successCount);
                    }
                    else
                    {
                        bootstrapParams = optimalParams;
                        Interlocked.Increment(ref fallbackCount);
                    }
                }
                else
                {
                    bootstrapParams = optimalParams;
                    Interlocked.Increment(ref fallbackCount);
                }
            }
            catch
            {
                bootstrapParams = optimalParams;
                Interlocked.Increment(ref fallbackCount);
            }
            
            // 3. 予測値を計算
            for (int t = 0; t < nPred; t++)
            {
                double pointEstimate = model.Calculate(predictionTimes[t], bootstrapParams);
                
                if (intervalType == PredictionIntervalType.Prediction)
                {
                    // 予測区間：Poisson変動を追加
                    // 累積バグ数の分散 ≈ 期待値（Poisson近似）
                    // ただし、これは累積なので分散は時点での期待値に比例
                    double variance = Math.Max(1.0, pointEstimate);
                    double noise = NextGaussian(random) * Math.Sqrt(variance);
                    bootstrapPredictions[t][iter] = Math.Max(0, pointEstimate + noise);
                }
                else
                {
                    // 信頼区間：パラメータ不確実性のみ
                    bootstrapPredictions[t][iter] = pointEstimate;
                }
            }
        });
        
        if (_verbose)
        {
            double successRate = (double)successCount / _settings.Iterations * 100;
            Console.WriteLine($"    予測区間ブートストラップ: 成功={successCount}/{_settings.Iterations} ({successRate:F1}%)");
        }
        
        // パーセンタイル法で区間を計算
        double alpha = 1.0 - _settings.ConfidenceLevel;
        double lowerPercentile = alpha / 2.0;
        double upperPercentile = 1.0 - alpha / 2.0;
        
        var lowerBounds = new double[nPred];
        var upperBounds = new double[nPred];
        var pointEstimates = new double[nPred];
        
        for (int t = 0; t < nPred; t++)
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
            pointEstimates[t] = model.Calculate(predictionTimes[t], optimalParams);
        }
        
        stopwatch.Stop();
        
        return new PredictionIntervalResult
        {
            Type = intervalType,
            Times = predictionTimes,
            PointEstimates = pointEstimates,
            LowerBounds = lowerBounds,
            UpperBounds = upperBounds,
            ConfidenceLevel = _settings.ConfidenceLevel,
            BootstrapIterations = _settings.Iterations,
            SuccessRate = (double)successCount / _settings.Iterations,
            ComputationTimeMs = stopwatch.ElapsedMilliseconds
        };
    }

    /// <summary>
    /// FittingResult に予測区間を追加
    /// </summary>
    public void AddToFittingResult(
        ReliabilityGrowthModelBase model,
        double[] tData,
        double[] yData,
        FittingResult result,
        PredictionIntervalType intervalType = PredictionIntervalType.Prediction)
    {
        if (result.ParameterVector == null || result.ParameterVector.Length == 0)
            throw new InvalidOperationException("ParameterVector が設定されていません。");
        
        if (result.PredictionTimes == null || result.PredictionTimes.Length == 0)
            throw new InvalidOperationException("PredictionTimes が設定されていません。");
        
        var intervalResult = Calculate(
            model, tData, yData, 
            result.ParameterVector, 
            result.PredictionTimes,
            intervalType);
        
        result.LowerConfidenceBounds = intervalResult.LowerBounds;
        result.UpperConfidenceBounds = intervalResult.UpperBounds;
    }

    /// <summary>
    /// NHPPからデータを再生成（パラメトリック・ブートストラップ用）
    /// </summary>
    private double[] GenerateSyntheticData(
        ReliabilityGrowthModelBase model,
        double[] tData,
        double[] parameters,
        Random random)
    {
        int n = tData.Length;
        var synthetic = new double[n];
        double cumulative = 0;
        
        for (int i = 0; i < n; i++)
        {
            // 期待される日次バグ数 (m(t_i) - m(t_{i-1}))
            double prevM = i > 0 ? model.Calculate(tData[i - 1], parameters) : 0;
            double currM = model.Calculate(tData[i], parameters);
            double lambda = Math.Max(_settings.MinLambda, currM - prevM);
            
            // Poisson乱数を生成
            int dailyBugs = GeneratePoisson(lambda, random);
            cumulative += dailyBugs;
            synthetic[i] = cumulative;
        }
        
        return synthetic;
    }

    /// <summary>
    /// Poisson乱数を生成
    /// </summary>
    private static int GeneratePoisson(double lambda, Random random)
    {
        if (lambda <= 0)
            return 0;
        
        // 小さいλの場合: Knuthのアルゴリズム
        if (lambda < 30)
        {
            double L = Math.Exp(-lambda);
            int k = 0;
            double p = 1.0;
            
            do
            {
                k++;
                p *= random.NextDouble();
            } while (p > L);
            
            return k - 1;
        }
        else
        {
            // 大きいλの場合: 正規近似
            double normal = NextGaussian(random);
            return (int)Math.Max(0, Math.Round(lambda + normal * Math.Sqrt(lambda)));
        }
    }

    /// <summary>
    /// 標準正規乱数を生成（Box-Muller法）
    /// </summary>
    private static double NextGaussian(Random random)
    {
        double u1 = 1.0 - random.NextDouble(); // (0,1]にするため1から引く
        double u2 = 1.0 - random.NextDouble();
        return Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Sin(2.0 * Math.PI * u2);
    }
}
