using BugConvergenceTool.Models;

namespace BugConvergenceTool.Services.Diagnostics;

/// <summary>
/// 残差の種類
/// </summary>
public enum ResidualType
{
    /// <summary>通常残差: e_i = y_i - m(t_i)</summary>
    Raw,
    
    /// <summary>Pearson残差（Poisson用）: (y_i - λ_i) / √λ_i</summary>
    Pearson,
    
    /// <summary>Deviance残差: sign(y-λ) * √(2 * [y*log(y/λ) - (y-λ)])</summary>
    Deviance
}

/// <summary>
/// 残差分析結果
/// </summary>
public class ResidualAnalysisResult
{
    /// <summary>残差の種類</summary>
    public ResidualType Type { get; init; }
    
    /// <summary>残差の配列</summary>
    public double[] Residuals { get; init; } = Array.Empty<double>();
    
    /// <summary>残差の平均</summary>
    public double Mean { get; init; }
    
    /// <summary>残差の標準偏差</summary>
    public double StandardDeviation { get; init; }
    
    /// <summary>歪度（Skewness）</summary>
    public double Skewness { get; init; }
    
    /// <summary>尖度（Kurtosis）</summary>
    public double Kurtosis { get; init; }
    
    /// <summary>外れ値のインデックス（|残差| > 2σ）</summary>
    public int[] OutlierIndices { get; init; } = Array.Empty<int>();
    
    /// <summary>系統的パターンの有無</summary>
    public bool HasSystematicPattern { get; init; }
    
    /// <summary>パターンの説明</summary>
    public string? PatternDescription { get; init; }
    
    /// <summary>ラン検定の結果</summary>
    public RunsTestResult? RunsTest { get; init; }
    
    /// <summary>サンプルサイズが小さい場合の警告</summary>
    public string? SmallSampleWarning { get; init; }
}

/// <summary>
/// ラン検定の結果
/// </summary>
public class RunsTestResult
{
    /// <summary>観測されたラン数</summary>
    public int ObservedRuns { get; init; }
    
    /// <summary>期待されるラン数</summary>
    public double ExpectedRuns { get; init; }
    
    /// <summary>Z統計量</summary>
    public double ZScore { get; init; }
    
    /// <summary>p値（両側検定）</summary>
    public double PValue { get; init; }
    
    /// <summary>正の残差の数</summary>
    public int PositiveCount { get; init; }
    
    /// <summary>負の残差の数</summary>
    public int NegativeCount { get; init; }
}

/// <summary>
/// 残差分析サービス
/// </summary>
public class ResidualAnalyzer
{
    /// <summary>
    /// 累積データから日次データに変換
    /// </summary>
    /// <param name="cumulativeData">累積データ</param>
    /// <returns>日次データ（差分）</returns>
    public static double[] ConvertToDailyData(double[] cumulativeData)
    {
        if (cumulativeData.Length == 0)
            return Array.Empty<double>();
        
        var daily = new double[cumulativeData.Length];
        daily[0] = cumulativeData[0];
        
        for (int i = 1; i < cumulativeData.Length; i++)
        {
            daily[i] = cumulativeData[i] - cumulativeData[i - 1];
        }
        
        return daily;
    }

    /// <summary>
    /// モデルから期待される日次バグ数を計算
    /// </summary>
    /// <param name="model">信頼度成長モデル</param>
    /// <param name="tData">時刻データ</param>
    /// <param name="parameters">モデルパラメータ</param>
    /// <returns>期待される日次バグ数</returns>
    public static double[] CalculateDailyExpected(
        ReliabilityGrowthModelBase model,
        double[] tData,
        double[] parameters)
    {
        if (tData.Length == 0)
            return Array.Empty<double>();
        
        var expected = new double[tData.Length];
        
        // 最初の点: m(t_0) - m(0) = m(t_0)（t=0では累積=0と仮定）
        expected[0] = model.Calculate(tData[0], parameters);
        
        for (int i = 1; i < tData.Length; i++)
        {
            double prevM = model.Calculate(tData[i - 1], parameters);
            double currM = model.Calculate(tData[i], parameters);
            expected[i] = currM - prevM;
        }
        
        return expected;
    }

    /// <summary>
    /// 残差を計算
    /// </summary>
    /// <param name="model">信頼度成長モデル</param>
    /// <param name="tData">時刻データ</param>
    /// <param name="yData">累積バグ発見数データ</param>
    /// <param name="parameters">モデルパラメータ</param>
    /// <param name="type">残差の種類</param>
    /// <returns>残差の配列</returns>
    public double[] CalculateResiduals(
        ReliabilityGrowthModelBase model,
        double[] tData,
        double[] yData,
        double[] parameters,
        ResidualType type = ResidualType.Raw)
    {
        int n = tData.Length;
        var residuals = new double[n];
        
        // 日次データに変換（累積の差分）
        var dailyObserved = ConvertToDailyData(yData);
        var dailyExpected = CalculateDailyExpected(model, tData, parameters);
        
        for (int i = 0; i < n; i++)
        {
            double observed = dailyObserved[i];
            double expected = dailyExpected[i];
            
            residuals[i] = type switch
            {
                ResidualType.Raw => observed - expected,
                ResidualType.Pearson => CalculatePearsonResidual(observed, expected),
                ResidualType.Deviance => CalculateDevianceResidual(observed, expected),
                _ => observed - expected
            };
        }
        
        return residuals;
    }

    /// <summary>
    /// 残差分析を実行
    /// </summary>
    /// <param name="model">信頼度成長モデル</param>
    /// <param name="tData">時刻データ</param>
    /// <param name="yData">累積バグ発見数データ</param>
    /// <param name="parameters">モデルパラメータ</param>
    /// <param name="type">残差の種類（デフォルト: Pearson）</param>
    /// <returns>残差分析結果</returns>
    public ResidualAnalysisResult Analyze(
        ReliabilityGrowthModelBase model,
        double[] tData,
        double[] yData,
        double[] parameters,
        ResidualType type = ResidualType.Pearson)
    {
        var residuals = CalculateResiduals(model, tData, yData, parameters, type);
        
        // 基本統計量
        double mean = CalculateMean(residuals);
        double std = CalculateStandardDeviation(residuals, mean);
        double skewness = CalculateSkewness(residuals, mean, std);
        double kurtosis = CalculateKurtosis(residuals, mean, std);
        
        // 外れ値検出（|残差| > 2σ）
        var outliers = DetectOutliers(residuals, mean, std, threshold: 2.0);
        
        // ラン検定
        var runsTest = PerformRunsTest(residuals);
        
        // 系統的パターン検出
        var (hasPattern, description) = DetectSystematicPattern(residuals, runsTest);
        
        // サンプルサイズ警告
        string? smallSampleWarning = null;
        if (residuals.Length < 10)
        {
            smallSampleWarning = $"サンプルサイズが小さい（n={residuals.Length}）ため、検定結果は参考値です。";
        }
        
        return new ResidualAnalysisResult
        {
            Type = type,
            Residuals = residuals,
            Mean = mean,
            StandardDeviation = std,
            Skewness = skewness,
            Kurtosis = kurtosis,
            OutlierIndices = outliers,
            HasSystematicPattern = hasPattern,
            PatternDescription = description,
            RunsTest = runsTest,
            SmallSampleWarning = smallSampleWarning
        };
    }

    /// <summary>
    /// Pearson残差を計算
    /// </summary>
    private static double CalculatePearsonResidual(double observed, double expected)
    {
        if (expected <= 0) 
            return 0;
        return (observed - expected) / Math.Sqrt(expected);
    }

    /// <summary>
    /// Deviance残差を計算
    /// </summary>
    private static double CalculateDevianceResidual(double observed, double expected)
    {
        if (expected <= 0) 
            return 0;
        
        double term;
        if (observed <= 0)
        {
            // y=0 の場合: deviance = 2 * expected
            term = 2 * expected;
        }
        else
        {
            // 一般式: 2 * [y * log(y/λ) - (y - λ)]
            term = 2 * (observed * Math.Log(observed / expected) - (observed - expected));
        }
        
        return Math.Sign(observed - expected) * Math.Sqrt(Math.Max(0, term));
    }

    /// <summary>
    /// 平均を計算
    /// </summary>
    private static double CalculateMean(double[] data)
    {
        if (data.Length == 0) return 0;
        return data.Average();
    }

    /// <summary>
    /// 標準偏差を計算
    /// </summary>
    private static double CalculateStandardDeviation(double[] data, double mean)
    {
        if (data.Length <= 1) return 0;
        double sumSq = data.Sum(x => (x - mean) * (x - mean));
        return Math.Sqrt(sumSq / (data.Length - 1));
    }

    /// <summary>
    /// 歪度を計算
    /// </summary>
    private static double CalculateSkewness(double[] data, double mean, double std)
    {
        if (data.Length < 3 || std <= 0) return 0;
        
        int n = data.Length;
        double sumCubed = data.Sum(x => Math.Pow((x - mean) / std, 3));
        
        // Fisher-Pearson skewness
        return (n / ((double)(n - 1) * (n - 2))) * sumCubed;
    }

    /// <summary>
    /// 尖度を計算（超過尖度 = 尖度 - 3）
    /// </summary>
    private static double CalculateKurtosis(double[] data, double mean, double std)
    {
        if (data.Length < 4 || std <= 0) return 0;
        
        int n = data.Length;
        double sumFourth = data.Sum(x => Math.Pow((x - mean) / std, 4));
        
        // Fisher kurtosis (excess kurtosis)
        double term1 = (n * (n + 1.0)) / ((n - 1.0) * (n - 2.0) * (n - 3.0)) * sumFourth;
        double term2 = (3.0 * (n - 1.0) * (n - 1.0)) / ((n - 2.0) * (n - 3.0));
        
        return term1 - term2;
    }

    /// <summary>
    /// 外れ値を検出
    /// </summary>
    private static int[] DetectOutliers(double[] residuals, double mean, double std, double threshold)
    {
        if (std <= 0) return Array.Empty<int>();
        
        var outliers = new List<int>();
        for (int i = 0; i < residuals.Length; i++)
        {
            double zScore = Math.Abs((residuals[i] - mean) / std);
            if (zScore > threshold)
            {
                outliers.Add(i);
            }
        }
        
        return outliers.ToArray();
    }

    /// <summary>
    /// ラン検定を実行
    /// </summary>
    private static RunsTestResult PerformRunsTest(double[] residuals)
    {
        if (residuals.Length == 0)
        {
            return new RunsTestResult
            {
                ObservedRuns = 0,
                ExpectedRuns = 0,
                ZScore = 0,
                PValue = 1.0,
                PositiveCount = 0,
                NegativeCount = 0
            };
        }
        
        // 正負のカウント
        int nPositive = residuals.Count(r => r > 0);
        int nNegative = residuals.Count(r => r <= 0);
        int n = residuals.Length;
        
        // ラン数をカウント
        int runs = CountRuns(residuals);
        
        // 特殊ケース: 全て同符号
        if (nPositive == 0 || nNegative == 0)
        {
            return new RunsTestResult
            {
                ObservedRuns = runs,
                ExpectedRuns = 1,
                ZScore = double.NaN,
                PValue = 0.0, // 明らかに非ランダム
                PositiveCount = nPositive,
                NegativeCount = nNegative
            };
        }
        
        // ラン数の期待値と分散
        double expectedRuns = 1.0 + (2.0 * nPositive * nNegative) / n;
        double varianceRuns = (2.0 * nPositive * nNegative * (2.0 * nPositive * nNegative - n)) 
                             / ((double)n * n * (n - 1.0));
        
        double zScore = 0;
        double pValue = 1.0;
        
        if (varianceRuns > 0)
        {
            zScore = (runs - expectedRuns) / Math.Sqrt(varianceRuns);
            // 両側検定のp値
            pValue = 2 * (1.0 - MathNet.Numerics.Distributions.Normal.CDF(0, 1, Math.Abs(zScore)));
        }
        
        return new RunsTestResult
        {
            ObservedRuns = runs,
            ExpectedRuns = expectedRuns,
            ZScore = zScore,
            PValue = pValue,
            PositiveCount = nPositive,
            NegativeCount = nNegative
        };
    }

    /// <summary>
    /// ラン数をカウント
    /// </summary>
    private static int CountRuns(double[] data)
    {
        if (data.Length == 0) return 0;
        
        int runs = 1;
        bool previousPositive = data[0] > 0;
        
        for (int i = 1; i < data.Length; i++)
        {
            bool currentPositive = data[i] > 0;
            if (currentPositive != previousPositive)
            {
                runs++;
                previousPositive = currentPositive;
            }
        }
        
        return runs;
    }

    /// <summary>
    /// 系統的パターンを検出
    /// </summary>
    private static (bool hasPattern, string? description) DetectSystematicPattern(
        double[] residuals, 
        RunsTestResult runsTest)
    {
        if (residuals.Length == 0)
            return (false, null);
        
        // 全残差が同符号
        if (runsTest.PositiveCount == 0 || runsTest.NegativeCount == 0)
        {
            return (true, "全残差が同符号（モデルが系統的にずれている）");
        }
        
        // ラン検定による判定（5%有意水準）
        if (runsTest.PValue < 0.05 && !double.IsNaN(runsTest.ZScore))
        {
            string pattern = runsTest.ZScore < 0 
                ? "残差に正の自己相関（クラスタリング）が検出されました。モデルがデータの変動を捉えきれていない可能性があります。" 
                : "残差に負の自己相関（交互パターン）が検出されました。過剰適合の可能性があります。";
            return (true, pattern);
        }
        
        return (false, null);
    }
}
