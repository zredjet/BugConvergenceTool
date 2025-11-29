using BugConvergenceTool.Models;

namespace BugConvergenceTool.Services.Diagnostics;

/// <summary>
/// 適合度検定結果
/// </summary>
public class GoodnessOfFitResult
{
    /// <summary>χ²統計量</summary>
    public double ChiSquareStatistic { get; init; }
    
    /// <summary>χ²検定の自由度</summary>
    public int ChiSquareDegreesOfFreedom { get; init; }
    
    /// <summary>χ²検定のp値</summary>
    public double ChiSquarePValue { get; init; }
    
    /// <summary>χ²検定の判定</summary>
    public string ChiSquareInterpretation { get; init; } = "";
    
    /// <summary>使用したビン数</summary>
    public int NumberOfBins { get; init; }
    
    /// <summary>Kolmogorov-Smirnov統計量 D</summary>
    public double KsStatistic { get; init; }
    
    /// <summary>KS検定のp値</summary>
    public double KsPValue { get; init; }
    
    /// <summary>KS検定の判定</summary>
    public string KsInterpretation { get; init; } = "";
    
    /// <summary>Cramer-von Mises統計量 W²</summary>
    public double CramerVonMisesStatistic { get; init; }
    
    /// <summary>CvM検定のp値</summary>
    public double CramerVonMisesPValue { get; init; }
    
    /// <summary>総合的な適合度評価</summary>
    public string OverallAssessment { get; init; } = "";
    
    /// <summary>モデルが適合しているか（5%水準）</summary>
    public bool IsModelAdequate { get; init; }
    
    /// <summary>サンプルサイズが小さい場合の警告</summary>
    public string? SmallSampleWarning { get; init; }
}

/// <summary>
/// NHPP適合度検定サービス
/// </summary>
public class GoodnessOfFitTest
{
    private const double SIGNIFICANCE_LEVEL = 0.05;
    private const int MIN_EXPECTED_PER_BIN = 5; // Cochranの規則

    /// <summary>
    /// χ²適合度検定（Poisson仮定）
    /// </summary>
    /// <param name="model">信頼度成長モデル</param>
    /// <param name="tData">時刻データ</param>
    /// <param name="yData">累積バグ発見数</param>
    /// <param name="parameters">モデルパラメータ</param>
    /// <param name="numBins">ビン数（nullの場合は自動決定）</param>
    /// <returns>(χ²統計量, 自由度, p値, 使用ビン数)</returns>
    public (double statistic, int df, double pValue, int bins) ChiSquareTest(
        ReliabilityGrowthModelBase model,
        double[] tData,
        double[] yData,
        double[] parameters,
        int? numBins = null)
    {
        int n = tData.Length;
        
        if (n < 10)
            return (0, 0, 1.0, 0);
        
        // ビン数の決定（Sturgesの公式またはユーザー指定）
        int bins = numBins ?? Math.Max(5, (int)Math.Ceiling(1 + 3.322 * Math.Log10(n)));
        bins = Math.Min(bins, n / MIN_EXPECTED_PER_BIN);  // 各ビンに最低5個の期待値
        bins = Math.Max(bins, 2);
        
        // 日次データに変換
        var dailyObserved = ResidualAnalyzer.ConvertToDailyData(yData);
        var dailyExpected = ResidualAnalyzer.CalculateDailyExpected(model, tData, parameters);
        
        // ビンに集約（隣接するビンを統合しながら）
        var binResults = AggregateIntoBins(dailyObserved, dailyExpected, bins);
        
        if (binResults.Count < 2)
            return (0, 0, 1.0, 0);
        
        // χ²統計量を計算
        double chiSquare = 0;
        foreach (var (observed, expected) in binResults)
        {
            if (expected > 0)
            {
                chiSquare += Math.Pow(observed - expected, 2) / expected;
            }
        }
        
        // 自由度 = ビン数 - 1 - パラメータ数
        int df = Math.Max(1, binResults.Count - 1 - parameters.Length);
        
        // p値
        double pValue = 1.0 - MathNet.Numerics.Distributions.ChiSquared.CDF(df, chiSquare);
        
        return (chiSquare, df, pValue, binResults.Count);
    }

    /// <summary>
    /// ビンに集約（Cochranの規則に従って統合）
    /// </summary>
    private static List<(double observed, double expected)> AggregateIntoBins(
        double[] dailyObserved,
        double[] dailyExpected,
        int targetBins)
    {
        int n = dailyObserved.Length;
        int binSize = (int)Math.Ceiling((double)n / targetBins);
        
        var rawBins = new List<(double observed, double expected)>();
        
        for (int b = 0; b < targetBins; b++)
        {
            int start = b * binSize;
            int end = Math.Min(start + binSize, n);
            if (start >= n) break;
            
            double observedSum = 0;
            double expectedSum = 0;
            
            for (int i = start; i < end; i++)
            {
                observedSum += dailyObserved[i];
                expectedSum += dailyExpected[i];
            }
            
            rawBins.Add((observedSum, expectedSum));
        }
        
        // Cochranの規則: 期待値が小さいビンは隣接ビンと統合
        var mergedBins = new List<(double observed, double expected)>();
        double pendingObserved = 0;
        double pendingExpected = 0;
        
        foreach (var (obs, exp) in rawBins)
        {
            pendingObserved += obs;
            pendingExpected += exp;
            
            if (pendingExpected >= MIN_EXPECTED_PER_BIN)
            {
                mergedBins.Add((pendingObserved, pendingExpected));
                pendingObserved = 0;
                pendingExpected = 0;
            }
        }
        
        // 残りを最後のビンに統合
        if (pendingExpected > 0)
        {
            if (mergedBins.Count > 0)
            {
                var last = mergedBins[^1];
                mergedBins[^1] = (last.observed + pendingObserved, last.expected + pendingExpected);
            }
            else
            {
                mergedBins.Add((pendingObserved, pendingExpected));
            }
        }
        
        return mergedBins;
    }

    /// <summary>
    /// Kolmogorov-Smirnov検定（変換時間ベース）
    /// NHPPの場合、累積強度関数で変換した時間が一様分布に従う
    /// </summary>
    /// <param name="model">信頼度成長モデル</param>
    /// <param name="tData">時刻データ</param>
    /// <param name="yData">累積バグ発見数</param>
    /// <param name="parameters">モデルパラメータ</param>
    /// <returns>(D統計量, p値)</returns>
    public (double statistic, double pValue) KolmogorovSmirnovTest(
        ReliabilityGrowthModelBase model,
        double[] tData,
        double[] yData,
        double[] parameters)
    {
        // 累積強度関数 M(t) = m(t) で変換
        // バグ発生時刻を抽出し、U_i = M(t_i) / M(T) が一様(0,1)に従う
        
        var bugTimes = ExtractBugOccurrenceTimes(tData, yData);
        int n = bugTimes.Length;
        
        if (n < 5)
            return (0, 1.0);  // サンプル不足
        
        double totalIntensity = model.Calculate(tData[^1], parameters);
        
        if (totalIntensity <= 0)
            return (0, 1.0);
        
        // 変換時間 U_i = M(t_i) / M(T)
        var transformedTimes = bugTimes
            .Select(t => model.Calculate(t, parameters) / totalIntensity)
            .OrderBy(u => u)
            .ToArray();
        
        // KS統計量 D = max |F_n(x) - F(x)|
        double maxD = 0;
        for (int i = 0; i < n; i++)
        {
            double empiricalCdf = (i + 1.0) / n;
            double theoreticalCdf = transformedTimes[i];
            
            // D+ = max(F_n - F)
            double d1 = Math.Abs(empiricalCdf - theoreticalCdf);
            // D- = max(F - F_{n-1})
            double d2 = Math.Abs((double)i / n - theoreticalCdf);
            
            maxD = Math.Max(maxD, Math.Max(d1, d2));
        }
        
        // p値の計算（Kolmogorov分布の漸近近似）
        double pValue = CalculateKsPValue(maxD, n);
        
        return (maxD, pValue);
    }

    /// <summary>
    /// KS検定のp値を計算（Kolmogorov分布の近似）
    /// </summary>
    private static double CalculateKsPValue(double D, int n)
    {
        // Marsaglia et al. (2003) の近似
        double sqrtN = Math.Sqrt(n);
        double z = D * (sqrtN + 0.12 + 0.11 / sqrtN);
        
        if (z < 0.27)
            return 1.0;
        
        if (z < 1.0)
        {
            double v = Math.Exp(-1.233701 * Math.Pow(z, -2));
            return 1.0 - 2.506628 * (v - Math.Pow(v, 4) + Math.Pow(v, 9)) / z;
        }
        
        // z >= 1.0
        double v2 = Math.Exp(-2 * z * z);
        return 2 * (v2 - Math.Pow(v2, 4) + Math.Pow(v2, 9) - Math.Pow(v2, 16));
    }

    /// <summary>
    /// Cramer-von Mises検定（変換時間ベース）
    /// KS検定より検出力が高い場合がある
    /// </summary>
    /// <param name="model">信頼度成長モデル</param>
    /// <param name="tData">時刻データ</param>
    /// <param name="yData">累積バグ発見数</param>
    /// <param name="parameters">モデルパラメータ</param>
    /// <returns>(W²統計量, p値)</returns>
    public (double statistic, double pValue) CramerVonMisesTest(
        ReliabilityGrowthModelBase model,
        double[] tData,
        double[] yData,
        double[] parameters)
    {
        var bugTimes = ExtractBugOccurrenceTimes(tData, yData);
        int n = bugTimes.Length;
        
        if (n < 5)
            return (0, 1.0);
        
        double totalIntensity = model.Calculate(tData[^1], parameters);
        
        if (totalIntensity <= 0)
            return (0, 1.0);
        
        // 変換時間
        var transformedTimes = bugTimes
            .Select(t => model.Calculate(t, parameters) / totalIntensity)
            .OrderBy(u => u)
            .ToArray();
        
        // Cramer-von Mises統計量
        // W² = 1/(12n) + Σ[(U_i - (2i-1)/(2n))²]
        double sum = 0;
        for (int i = 0; i < n; i++)
        {
            double diff = transformedTimes[i] - (2.0 * (i + 1) - 1) / (2.0 * n);
            sum += diff * diff;
        }
        
        double W2 = 1.0 / (12 * n) + sum;
        
        // 修正統計量 (Stephens, 1974)
        double W2Star = W2 * (1 + 0.5 / n);
        
        // p値の近似
        double pValue = CalculateCvMPValue(W2Star);
        
        return (W2Star, pValue);
    }

    /// <summary>
    /// Cramer-von Mises検定のp値を計算
    /// </summary>
    private static double CalculateCvMPValue(double W2)
    {
        // Stephens (1974) の近似
        if (W2 < 0.0275)
            return 1.0 - Math.Exp(-13.953 + 775.5 * W2 - 12542.6 * W2 * W2);
        else if (W2 < 0.051)
            return 1.0 - Math.Exp(-5.903 + 179.5 * W2 - 1515.3 * W2 * W2);
        else if (W2 < 0.092)
            return Math.Exp(0.886 - 31.62 * W2 + 10.89 * W2 * W2);
        else if (W2 < 1.1)
            return Math.Exp(1.111 - 34.24 * W2 + 12.83 * W2 * W2);
        else
            return 0.0001;
    }

    /// <summary>
    /// 累積データからバグ発生時刻を近似的に抽出
    /// </summary>
    private static double[] ExtractBugOccurrenceTimes(double[] tData, double[] yData)
    {
        var times = new List<double>();
        
        for (int i = 0; i < tData.Length; i++)
        {
            // この日の新規バグ数
            int dailyBugs = (int)(i == 0 ? yData[i] : yData[i] - yData[i - 1]);
            
            if (dailyBugs <= 0)
                continue;
            
            // 日内で均等に分布すると仮定
            // t_i の区間を [t_{i-1}, t_i] として、その中に配置
            double intervalStart = i > 0 ? tData[i - 1] : 0;
            double intervalEnd = tData[i];
            double intervalLength = intervalEnd - intervalStart;
            
            for (int j = 0; j < dailyBugs; j++)
            {
                double offset = (j + 0.5) / dailyBugs;
                times.Add(intervalStart + intervalLength * offset);
            }
        }
        
        return times.ToArray();
    }

    /// <summary>
    /// 総合的な適合度検定を実行
    /// </summary>
    /// <param name="model">信頼度成長モデル</param>
    /// <param name="tData">時刻データ</param>
    /// <param name="yData">累積バグ発見数</param>
    /// <param name="parameters">モデルパラメータ</param>
    /// <returns>適合度検定結果</returns>
    public GoodnessOfFitResult Test(
        ReliabilityGrowthModelBase model,
        double[] tData,
        double[] yData,
        double[] parameters)
    {
        int n = tData.Length;
        
        // サンプルサイズ警告
        string? smallSampleWarning = null;
        if (n < 10)
        {
            smallSampleWarning = $"サンプルサイズが非常に小さい（n={n}）ため、適合度検定の結果は参考値です。";
        }
        else if (n < 20)
        {
            smallSampleWarning = $"サンプルサイズが小さい（n={n}）ため、適合度検定の精度が限定的です。";
        }
        
        // χ²検定
        var (chi2, df, chi2PValue, bins) = ChiSquareTest(model, tData, yData, parameters);
        
        // KS検定
        var (ks, ksPValue) = KolmogorovSmirnovTest(model, tData, yData, parameters);
        
        // Cramer-von Mises検定
        var (cvm, cvmPValue) = CramerVonMisesTest(model, tData, yData, parameters);
        
        // 解釈を生成
        string chi2Interpretation = InterpretChiSquare(chi2PValue);
        string ksInterpretation = InterpretKs(ksPValue);
        
        // 総合評価
        // 複数の検定を組み合わせて判断
        bool isAdequate = DetermineAdequacy(chi2PValue, ksPValue, cvmPValue);
        string assessment = GenerateAssessment(chi2PValue, ksPValue, cvmPValue, isAdequate, model.Name);
        
        return new GoodnessOfFitResult
        {
            ChiSquareStatistic = chi2,
            ChiSquareDegreesOfFreedom = df,
            ChiSquarePValue = chi2PValue,
            ChiSquareInterpretation = chi2Interpretation,
            NumberOfBins = bins,
            KsStatistic = ks,
            KsPValue = ksPValue,
            KsInterpretation = ksInterpretation,
            CramerVonMisesStatistic = cvm,
            CramerVonMisesPValue = cvmPValue,
            OverallAssessment = assessment,
            IsModelAdequate = isAdequate,
            SmallSampleWarning = smallSampleWarning
        };
    }

    /// <summary>
    /// χ²検定の解釈
    /// </summary>
    private static string InterpretChiSquare(double pValue)
    {
        return pValue switch
        {
            >= 0.10 => "モデルはデータに良く適合しています",
            >= 0.05 => "モデルは許容範囲で適合しています",
            >= 0.01 => "適合度に疑問があります（5%水準で棄却）",
            _ => "モデルはデータに適合していません（1%水準で棄却）"
        };
    }

    /// <summary>
    /// KS検定の解釈
    /// </summary>
    private static string InterpretKs(double pValue)
    {
        return pValue switch
        {
            >= 0.10 => "累積分布が理論分布に良く一致しています",
            >= 0.05 => "累積分布は許容範囲で理論分布に一致しています",
            >= 0.01 => "累積分布に差異があります（5%水準で棄却）",
            _ => "累積分布が理論分布から著しく乖離しています"
        };
    }

    /// <summary>
    /// 総合的な適合判定
    /// </summary>
    private static bool DetermineAdequacy(double chi2PValue, double ksPValue, double cvmPValue)
    {
        // 厳格な判定: すべての検定が5%水準をパス
        // 寛容な判定: 2つ以上の検定が5%水準をパス
        
        int passCount = 0;
        if (chi2PValue >= SIGNIFICANCE_LEVEL) passCount++;
        if (ksPValue >= SIGNIFICANCE_LEVEL) passCount++;
        if (cvmPValue >= SIGNIFICANCE_LEVEL) passCount++;
        
        // 2つ以上パスで適合と判断
        return passCount >= 2;
    }

    /// <summary>
    /// 総合評価文を生成
    /// </summary>
    private static string GenerateAssessment(
        double chi2PValue,
        double ksPValue,
        double cvmPValue,
        bool isAdequate,
        string modelName)
    {
        var failedTests = new List<string>();
        var passedTests = new List<string>();
        
        if (chi2PValue < SIGNIFICANCE_LEVEL)
            failedTests.Add($"χ²検定 (p={chi2PValue:F4})");
        else
            passedTests.Add("χ²検定");
        
        if (ksPValue < SIGNIFICANCE_LEVEL)
            failedTests.Add($"KS検定 (p={ksPValue:F4})");
        else
            passedTests.Add("KS検定");
        
        if (cvmPValue < SIGNIFICANCE_LEVEL)
            failedTests.Add($"CvM検定 (p={cvmPValue:F4})");
        else
            passedTests.Add("CvM検定");
        
        if (isAdequate)
        {
            if (failedTests.Count == 0)
            {
                return $"モデル「{modelName}」はすべての適合度検定をパスしました。" +
                       "データに良く適合しており、予測の信頼性は高いと考えられます。";
            }
            else
            {
                return $"モデル「{modelName}」は概ねデータに適合しています。" +
                       $"ただし、{string.Join("、", failedTests)} で棄却されました。" +
                       "予測結果は参考にできますが、不確実性に注意してください。";
            }
        }
        else
        {
            return $"モデル「{modelName}」はデータへの適合度が低いです。" +
                   $"棄却された検定: {string.Join("、", failedTests)}。" +
                   "異なるモデルの使用を検討してください。";
        }
    }
}
