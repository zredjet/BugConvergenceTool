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
    
    /// <summary>
    /// KS・CvM 検定の p 値の求め方（"パラメトリック・ブートストラップ（N回）" または
    /// "漸近分布（パラメータ既知を仮定、保守的）"）
    /// </summary>
    public string EdfPValueMethod { get; init; } = "";
    
    /// <summary>
    /// KS・CvM の p 値をパラメトリック・ブートストラップで較正し、適合性の判定に使ったか
    /// （false なら漸近 p 値。パラメータを推定しているとほとんど棄却されないため、参考表示にとどめる）
    /// </summary>
    public bool EdfPValuesCalibrated { get; init; }
    
    /// <summary>
    /// 適合性を判定できたか（判定に使える検定が1つもなければ false。そのとき <see cref="IsModelAdequate"/> も false）
    /// </summary>
    public bool AdequacyDetermined { get; init; }
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
    /// <returns>(χ²統計量, 自由度, p値（検定できなければ NaN）, 使用ビン数)</returns>
    public (double statistic, int df, double pValue, int bins) ChiSquareTest(
        ReliabilityGrowthModelBase model,
        double[] tData,
        double[] yData,
        double[] parameters,
        int? numBins = null)
    {
        int n = tData.Length;
        
        // 検定できない場合は p = NaN（以前は p = 1.0 を返しており、適合性の判定で「パス」に数えられていた）
        if (n < 10)
            return (0, 0, double.NaN, 0);
        
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
            return (0, 0, double.NaN, binResults.Count);
        
        // χ²統計量を計算
        double chiSquare = 0;
        foreach (var (observed, expected) in binResults)
        {
            if (expected > 0)
            {
                chiSquare += Math.Pow(observed - expected, 2) / expected;
            }
        }
        
        // 自由度 = ビン数 - 発見数の m(t) に効くパラメータの数
        // 各ビンの件数は独立な Poisson 変数で合計は固定されていない（多項分布ではない）ため、
        // 合計の制約による -1 は不要（以前は -1 しており、自由度が 1 少なかった）。
        // FRE モデルの η・D のように修正数にしか効かないパラメータは数えない。
        // 自由度が残らなければ検定できない（以前は Max(1, …) で自由度 1 をでっち上げていた）
        int df = binResults.Count - model.DetectionParameterCount;
        if (df <= 0)
            return (chiSquare, df, double.NaN, binResults.Count);
        
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
    /// Kolmogorov-Smirnov 検定（発見時刻を U = m(t)/m(T) で変換した値が一様分布に従うか）
    /// </summary>
    public (double statistic, double pValue) KolmogorovSmirnovTest(
        ReliabilityGrowthModelBase model,
        double[] tData,
        double[] yData,
        double[] parameters)
    {
        var u = TransformedBugTimes(model, tData, yData, parameters);
        if (u == null)
            return (0, 1.0);  // サンプル不足
        
        double maxD = KolmogorovSmirnovStatistic(u);
        return (maxD, CalculateKsPValue(maxD, u.Length));
    }
    
    /// <summary>
    /// 発見時刻を U = m(t)/m(T) で変換して昇順に並べた値（件数が 5 未満、または m(T) ≤ 0 なら null）
    /// </summary>
    private static double[]? TransformedBugTimes(
        ReliabilityGrowthModelBase model, double[] tData, double[] yData, double[] parameters)
    {
        var bugTimes = ExtractBugOccurrenceTimes(tData, yData);
        if (bugTimes.Length < 5)
            return null;
        
        double totalIntensity = model.Calculate(tData[^1], parameters);
        if (totalIntensity <= 0)
            return null;
        
        return bugTimes
            .Select(t => model.Calculate(t, parameters) / totalIntensity)
            .OrderBy(u => u)
            .ToArray();
    }
    
    /// <summary>
    /// KS 統計量 D = max |Fₙ(u) - u|（U は昇順）
    /// </summary>
    private static double KolmogorovSmirnovStatistic(double[] sortedUniform)
    {
        int n = sortedUniform.Length;
        double maxD = 0;
        for (int i = 0; i < n; i++)
        {
            double theoreticalCdf = sortedUniform[i];
            double d1 = Math.Abs((i + 1.0) / n - theoreticalCdf);
            double d2 = Math.Abs((double)i / n - theoreticalCdf);
            maxD = Math.Max(maxD, Math.Max(d1, d2));
        }
        return maxD;
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
    /// Cramér-von Mises 検定（発見時刻を U = m(t)/m(T) で変換した値が一様分布に従うか）
    /// </summary>
    public (double statistic, double pValue) CramerVonMisesTest(
        ReliabilityGrowthModelBase model,
        double[] tData,
        double[] yData,
        double[] parameters)
    {
        var u = TransformedBugTimes(model, tData, yData, parameters);
        if (u == null)
            return (0, 1.0);
        
        // p値: 変換後の値が一様分布に従う（分布が完全に指定された Case 0）ときの W² の分布から求める
        double W2 = CramerVonMisesStatistic(u);
        return (W2, CalculateCvMPValue(W2, u.Length));
    }
    
    /// <summary>
    /// Cramér-von Mises 統計量 W² = 1/(12n) + Σ(U₍ᵢ₎ - (2i-1)/(2n))²（U は昇順）
    /// </summary>
    private static double CramerVonMisesStatistic(double[] sortedUniform)
    {
        int n = sortedUniform.Length;
        double sum = 0;
        for (int i = 0; i < n; i++)
        {
            double diff = sortedUniform[i] - (2.0 * (i + 1) - 1) / (2.0 * n);
            sum += diff * diff;
        }
        return 1.0 / (12 * n) + sum;
    }
    
    private const int CvMSimulations = 10000;
    // Lazy で包み、並列に同じ n が要求されても帰無分布の生成は1回にする
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<int, Lazy<double[]>> CvMNullDistributions = new();
    
    /// <summary>
    /// Cramér-von Mises 検定の p 値（Case 0: 分布が完全に指定されている場合）
    /// </summary>
    /// <remarks>
    /// <para>
    /// n 個の一様乱数から W² の帰無分布をシミュレーション（固定シード、n ごとにキャッシュ）して求める。有限の n でも正確。
    /// </para>
    /// <para>
    /// 以前は Stephens (1974) の「正規分布のパラメータを推定した場合（Case 3）」用の修正統計量と近似式を使っており、
    /// 正しいモデル・真のパラメータでも約半数が棄却されていた。
    /// なお本ツールではモデルのパラメータをデータから推定しているため、この p 値は KS 検定と同様にやや大きめ（保守的）になる。
    /// </para>
    /// </remarks>
    private static double CalculateCvMPValue(double W2, int n)
    {
        var nullDistribution = CvMNullDistributions.GetOrAdd(n, size => new Lazy<double[]>(() =>
        {
            var random = new Random(20240601 + size);
            var sample = new double[size];
            var statistics = new double[CvMSimulations];
            for (int s = 0; s < CvMSimulations; s++)
            {
                for (int i = 0; i < size; i++) sample[i] = random.NextDouble();
                Array.Sort(sample);
                statistics[s] = CramerVonMisesStatistic(sample);
            }
            Array.Sort(statistics);
            return statistics;
        })).Value;
        
        // 観測値以上になった割合（+1 補正）
        int index = Array.BinarySearch(nullDistribution, W2);
        if (index < 0) index = ~index;
        while (index > 0 && nullDistribution[index - 1] >= W2) index--;
        int exceed = nullDistribution.Length - index;
        return (exceed + 1.0) / (nullDistribution.Length + 1.0);
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
    /// <summary>
    /// 適合度検定を実行
    /// </summary>
    /// <param name="refit">
    /// 累積データからパラメータを推定し直す関数。指定すると KS・CvM 検定の p 値をパラメトリック・ブートストラップで求める。
    /// パラメータをデータから推定している場合、分布が完全に指定された場合の p 値は極端に保守的になり
    /// （正しいモデルの合成データで 5% 棄却率が 0%）、検定として機能しないため。
    /// </param>
    /// <param name="bootstrapIterations">ブートストラップの反復回数</param>
    /// <param name="seed">乱数シード</param>
    public GoodnessOfFitResult Test(
        ReliabilityGrowthModelBase model,
        double[] tData,
        double[] yData,
        double[] parameters,
        Func<double[], double[]?>? refit = null,
        int bootstrapIterations = 199,
        int? seed = 12345)
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
        
        // パラメータ推定の影響を反映した p 値（パラメトリック・ブートストラップ）
        string edfMethod = "漸近分布（パラメータ既知を仮定。推定しているとほとんど棄却されないため、判定には使わず参考表示）";
        bool edfCalibrated = false;
        if (refit != null)
        {
            var bootstrap = BootstrapEdfPValues(model, tData, parameters, ks, cvm, refit, bootstrapIterations, seed);
            if (bootstrap.HasValue)
            {
                (ksPValue, cvmPValue) = (bootstrap.Value.ksPValue, bootstrap.Value.cvmPValue);
                edfMethod = $"パラメトリック・ブートストラップ（{bootstrap.Value.valid}回）";
                edfCalibrated = true;
            }
        }
        
        // 解釈を生成
        string chi2Interpretation = InterpretChiSquare(chi2PValue);
        string ksInterpretation = InterpretKs(ksPValue);
        
        // 総合評価（判定に使える検定だけで決める。較正していない KS・CvM と、自由度のない χ² は使わない）
        var usable = new List<(string name, double pValue)>();
        if (double.IsFinite(chi2PValue)) usable.Add(("χ²検定", chi2PValue));
        if (edfCalibrated)
        {
            usable.Add(("KS検定", ksPValue));
            usable.Add(("CvM検定", cvmPValue));
        }
        bool determined = usable.Count > 0;
        bool isAdequate = determined && DetermineAdequacy(usable.Select(u => u.pValue).ToList());
        string assessment = determined
            ? GenerateAssessment(usable, isAdequate, model.Name)
            : $"モデル「{model.Name}」の適合性は判定できません（χ² 検定ができず、KS・CvM の p 値もブートストラップで較正できないため）。";
        
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
            SmallSampleWarning = smallSampleWarning,
            EdfPValueMethod = edfMethod,
            EdfPValuesCalibrated = edfCalibrated,
            AdequacyDetermined = determined
        };
    }

    /// <summary>
    /// KS・CvM 統計量の帰無分布をパラメトリック・ブートストラップで求め、p 値を返す
    /// </summary>
    /// <remarks>
    /// θ̂ から発見数を Poisson 再生成し、推定し直した θ* で統計量を計算する（パラメータ推定の影響を含む）。
    /// p = (観測値以上の数 + 1) / (有効な反復数 + 1)。有効な反復が半数未満なら null。
    /// </remarks>
    private (double ksPValue, double cvmPValue, int valid)? BootstrapEdfPValues(
        ReliabilityGrowthModelBase model, double[] tData, double[] parameters,
        double observedKs, double observedCvm, Func<double[], double[]?> refit, int iterations, int? seed)
    {
        int ksExceed = 0, cvmExceed = 0, valid = 0;
        int baseSeed = seed ?? Random.Shared.Next();
        Parallel.For(0, iterations, iter =>
        {
            try
            {
                var simY = ParametricBootstrap.SimulateCumulative(model, tData, parameters, new Random(unchecked(baseSeed + iter * 7919)));
                var p = refit(simY);
                if (p == null) return;
                // 統計量だけを計算する（p 値は観測値について1回求めれば足り、ここで求めると
                // 反復ごとに異なる件数の帰無分布をシミュレーションしてしまう）
                var u = TransformedBugTimes(model, tData, simY, p);
                if (u == null) return;
                double ks = KolmogorovSmirnovStatistic(u);
                double cvm = CramerVonMisesStatistic(u);
                Interlocked.Increment(ref valid);
                if (ks >= observedKs) Interlocked.Increment(ref ksExceed);
                if (cvm >= observedCvm) Interlocked.Increment(ref cvmExceed);
            }
            catch
            {
                // 失敗した反復は数えない
            }
        });
        
        if (valid < iterations / 2) return null;
        return ((ksExceed + 1.0) / (valid + 1.0), (cvmExceed + 1.0) / (valid + 1.0), valid);
    }
    
    /// <summary>
    /// χ²検定の解釈
    /// </summary>
    private static string InterpretChiSquare(double pValue)
    {
        return pValue switch
        {
            double.NaN => "検定できません（データが 10 日未満、またはビン数 ≤ パラメータ数で自由度が残らない）",
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
    /// 総合的な適合判定（判定に使う検定の過半数が 5% 水準をパスすれば適合）
    /// </summary>
    /// <remarks>
    /// 3 つとも使えるときは以前と同じく 2 つ以上のパス、χ² だけのときは χ² のパスで判定する。
    /// </remarks>
    private static bool DetermineAdequacy(IReadOnlyList<double> pValues)
    {
        int passCount = pValues.Count(p => p >= SIGNIFICANCE_LEVEL);
        return passCount * 2 > pValues.Count;
    }

    /// <summary>
    /// 総合評価文を生成
    /// </summary>
    private static string GenerateAssessment(
        IReadOnlyList<(string name, double pValue)> tests,
        bool isAdequate,
        string modelName)
    {
        var failedTests = tests.Where(t => t.pValue < SIGNIFICANCE_LEVEL).Select(t => $"{t.name} (p={t.pValue:F4})").ToList();
        string basis = tests.Count < 3 ? $"（判定に使った検定: {string.Join("、", tests.Select(t => t.name))}）" : "";
        
        if (isAdequate)
        {
            if (failedTests.Count == 0)
            {
                return $"モデル「{modelName}」はすべての適合度検定をパスしました{basis}。" +
                       "データに良く適合しており、予測の信頼性は高いと考えられます。";
            }
            else
            {
                return $"モデル「{modelName}」は概ねデータに適合しています{basis}。" +
                       $"ただし、{string.Join("、", failedTests)} で棄却されました。" +
                       "予測結果は参考にできますが、不確実性に注意してください。";
            }
        }
        else
        {
            return $"モデル「{modelName}」はデータへの適合度が低いです{basis}。" +
                   $"棄却された検定: {string.Join("、", failedTests)}。" +
                   "異なるモデルの使用を検討してください。";
        }
    }
}
