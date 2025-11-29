using BugConvergenceTool.Models;

namespace BugConvergenceTool.Services.Diagnostics;

/// <summary>
/// 残差の種類
/// </summary>
/// <remarks>
/// <para>
/// SRGMは累積平均関数 m(t) をモデル化するが、NHPPの仮定下では増分（日次バグ数）が
/// 独立なPoisson分布に従う。そのため、残差診断は以下の2つの観点から行える：
/// </para>
/// <para>
/// 1. 増分ベース（日次データ）: Poisson仮定に基づくPearson残差・Deviance残差
///    - 独立性の仮定が満たされやすい
///    - NHPPの理論と整合
/// </para>
/// <para>
/// 2. 累積ベース: 累積データに対する通常残差
///    - モデルの当てはまりを直接評価
///    - ただし累積データは自己相関を持つため、独立性の仮定は成立しない
/// </para>
/// </remarks>
public enum ResidualType
{
    /// <summary>
    /// 通常残差（増分ベース）: e_i = Δy_i - Δm(t_i)
    /// 日次バグ数の観測値と期待値の差
    /// </summary>
    Raw,
    
    /// <summary>
    /// Pearson残差（増分ベース、Poisson用）: (Δy_i - λ_i) / √λ_i
    /// λ_i = m(t_i) - m(t_{i-1}) は期待される日次バグ数
    /// 分散安定化された残差で、Poisson仮定下では近似的に標準正規分布に従う
    /// </summary>
    Pearson,
    
    /// <summary>
    /// Deviance残差（増分ベース）: sign(Δy-λ) * √(2 * [Δy*log(Δy/λ) - (Δy-λ)])
    /// Poisson GLMにおけるモデル逸脱度への寄与を表す残差
    /// </summary>
    Deviance,
    
    /// <summary>
    /// 累積残差: e_i = Y_i - m(t_i)
    /// 累積バグ数の観測値とモデル予測値の差
    /// 注意: 累積データは自己相関を持つため、独立性検定（ラン検定等）の解釈に注意
    /// </summary>
    Cumulative,
    
    /// <summary>
    /// Anscombe残差（Poisson用）: 分散安定化変換に基づく残差
    /// 正規性仮定により近い分布を持つ
    /// </summary>
    Anscombe,
    
    /// <summary>
    /// 標準化Pearson残差: Pearson残差をハット行列の対角成分で調整
    /// レバレッジの影響を考慮
    /// </summary>
    StandardizedPearson
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
    
    /// <summary>Poisson過分散検定結果（Poisson残差の場合のみ）</summary>
    public OverdispersionTestResult? OverdispersionTest { get; init; }
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
/// 過分散検定の結果
/// </summary>
/// <remarks>
/// Poisson分布では分散=平均（等分散性）が成立する。
/// 実データでは分散>平均（過分散）となることが多く、
/// これはPoisson仮定の逸脱を示唆する。
/// </remarks>
public class OverdispersionTestResult
{
    /// <summary>過分散パラメータ φ = Var(y) / E(y)</summary>
    public double DispersionParameter { get; init; }
    
    /// <summary>Pearsonχ²統計量 = Σ(y-λ)²/λ</summary>
    public double PearsonChiSquare { get; init; }
    
    /// <summary>自由度</summary>
    public int DegreesOfFreedom { get; init; }
    
    /// <summary>過分散検定のp値</summary>
    public double PValue { get; init; }
    
    /// <summary>過分散が検出されたか</summary>
    public bool IsOverdispersed { get; init; }
    
    /// <summary>解釈</summary>
    public string Interpretation { get; init; } = "";
}

/// <summary>
/// Poisson仮定との整合性を考慮した残差分析結果
/// </summary>
/// <remarks>
/// NHPPモデルでは日次バグ数がPoisson分布に従うと仮定する。
/// この結果クラスは、Poisson仮定の妥当性を評価するための
/// 複数の診断情報を提供する。
/// </remarks>
public class PoissonConsistentAnalysisResult
{
    /// <summary>Pearson残差（Poisson用の標準的な残差）</summary>
    public double[] PearsonResiduals { get; init; } = Array.Empty<double>();
    
    /// <summary>Deviance残差（GLMにおける逸脱度の分解）</summary>
    public double[] DevianceResiduals { get; init; } = Array.Empty<double>();
    
    /// <summary>Anscombe残差（より正規性に近い分布）</summary>
    public double[] AnscombeResiduals { get; init; } = Array.Empty<double>();
    
    /// <summary>ランダム化分位残差（離散分布でも正規性検定が可能）</summary>
    public double[] RandomizedQuantileResiduals { get; init; } = Array.Empty<double>();
    
    /// <summary>過分散検定結果</summary>
    public OverdispersionTestResult OverdispersionTest { get; init; } = new();
    
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
    
    /// <summary>診断に基づく警告メッセージ</summary>
    public List<string> Warnings { get; init; } = new();
    
    /// <summary>
    /// 正規性検定に最適な残差を取得
    /// </summary>
    /// <remarks>
    /// 離散分布（Poisson）の場合、ランダム化分位残差が正規性検定に最適。
    /// </remarks>
    public double[] GetResidualsForNormalityTest() => RandomizedQuantileResiduals;
    
    /// <summary>
    /// 独立性検定に最適な残差を取得
    /// </summary>
    /// <remarks>
    /// 独立性（ラン検定、自己相関）にはPearson残差を使用。
    /// </remarks>
    public double[] GetResidualsForIndependenceTest() => PearsonResiduals;
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
    /// <remarks>
    /// 残差タイプによる計算方法の違い：
    /// - Raw, Pearson, Deviance: 増分（日次）ベースで計算。NHPPのPoisson仮定に適合。
    /// - Cumulative: 累積ベースで計算。モデルの当てはまりを直接評価。
    /// </remarks>
    public double[] CalculateResiduals(
        ReliabilityGrowthModelBase model,
        double[] tData,
        double[] yData,
        double[] parameters,
        ResidualType type = ResidualType.Raw)
    {
        int n = tData.Length;
        var residuals = new double[n];
        
        // 累積ベースの残差
        if (type == ResidualType.Cumulative)
        {
            for (int i = 0; i < n; i++)
            {
                double observed = yData[i];
                double expected = model.Calculate(tData[i], parameters);
                residuals[i] = observed - expected;
            }
            return residuals;
        }
        
        // 増分（日次）ベースの残差
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
                ResidualType.Anscombe => CalculateAnscombeResidual(observed, expected),
                ResidualType.StandardizedPearson => CalculatePearsonResidual(observed, expected), // 基本形（後でレバレッジ調整）
                _ => observed - expected
            };
        }
        
        return residuals;
    }
    
    /// <summary>
    /// Poisson整合性を重視した残差分析を実行
    /// </summary>
    /// <param name="model">信頼度成長モデル</param>
    /// <param name="tData">時刻データ</param>
    /// <param name="yData">累積バグ発見数データ</param>
    /// <param name="parameters">モデルパラメータ</param>
    /// <returns>Poisson仮定の診断を含む残差分析結果</returns>
    /// <remarks>
    /// このメソッドはNHPP/Poisson仮定に基づく診断を重視し、以下を実施します：
    /// <list type="bullet">
    /// <item>Pearson残差による分析（Poisson仮定と整合）</item>
    /// <item>過分散検定（Poisson仮定の妥当性チェック）</item>
    /// <item>正規性検定結果に対する適切な警告</item>
    /// </list>
    /// </remarks>
    public PoissonConsistentAnalysisResult AnalyzeWithPoissonDiagnostics(
        ReliabilityGrowthModelBase model,
        double[] tData,
        double[] yData,
        double[] parameters)
    {
        // 日次データと期待値を計算
        var dailyObserved = ConvertToDailyData(yData);
        var dailyExpected = CalculateDailyExpected(model, tData, parameters);
        
        // 各種残差を計算
        var pearsonResiduals = CalculateResiduals(model, tData, yData, parameters, ResidualType.Pearson);
        var devianceResiduals = CalculateResiduals(model, tData, yData, parameters, ResidualType.Deviance);
        var anscombeResiduals = CalculateResiduals(model, tData, yData, parameters, ResidualType.Anscombe);
        
        // 過分散検定
        var overdispersionTest = PerformOverdispersionTest(dailyObserved, dailyExpected, parameters.Length);
        
        // Pearson残差の基本統計
        double mean = CalculateMean(pearsonResiduals);
        double std = CalculateStandardDeviation(pearsonResiduals, mean);
        double skewness = CalculateSkewness(pearsonResiduals, mean, std);
        double kurtosis = CalculateKurtosis(pearsonResiduals, mean, std);
        
        // 外れ値検出
        var outliers = DetectOutliers(pearsonResiduals, mean, std, threshold: 2.0);
        
        // ラン検定
        var runsTest = PerformRunsTest(pearsonResiduals);
        
        // 系統的パターン検出
        var (hasPattern, description) = DetectSystematicPattern(pearsonResiduals, runsTest);
        
        // ランダム化分位残差
        var randomizedQuantileResiduals = CalculateRandomizedQuantileResiduals(dailyObserved, dailyExpected);
        
        // 警告メッセージの生成
        var warnings = GeneratePoissonDiagnosticWarnings(
            overdispersionTest, dailyObserved, dailyExpected, pearsonResiduals.Length);
        
        return new PoissonConsistentAnalysisResult
        {
            PearsonResiduals = pearsonResiduals,
            DevianceResiduals = devianceResiduals,
            AnscombeResiduals = anscombeResiduals,
            RandomizedQuantileResiduals = randomizedQuantileResiduals,
            OverdispersionTest = overdispersionTest,
            Mean = mean,
            StandardDeviation = std,
            Skewness = skewness,
            Kurtosis = kurtosis,
            OutlierIndices = outliers,
            HasSystematicPattern = hasPattern,
            PatternDescription = description,
            RunsTest = runsTest,
            Warnings = warnings
        };
    }
    
    /// <summary>
    /// 過分散検定を実行
    /// </summary>
    /// <param name="observed">観測された日次バグ数</param>
    /// <param name="expected">期待される日次バグ数（λ_i）</param>
    /// <param name="numParameters">推定されたパラメータ数</param>
    /// <returns>過分散検定の結果</returns>
    /// <remarks>
    /// Poisson分布では Var(Y) = E(Y) が成立する。
    /// 過分散（Var > E）の場合、負の二項分布やquasi-Poissonモデルを検討すべき。
    /// </remarks>
    public static OverdispersionTestResult PerformOverdispersionTest(
        double[] observed, 
        double[] expected, 
        int numParameters)
    {
        int n = observed.Length;
        int df = n - numParameters;
        
        if (df <= 0)
        {
            return new OverdispersionTestResult
            {
                DispersionParameter = double.NaN,
                PearsonChiSquare = double.NaN,
                DegreesOfFreedom = df,
                PValue = double.NaN,
                IsOverdispersed = false,
                Interpretation = "自由度が不足しています。パラメータ数に対してデータ点が少なすぎます。"
            };
        }
        
        // Pearson χ² 統計量 = Σ (y_i - λ_i)² / λ_i
        double pearsonChiSq = 0;
        int validCount = 0;
        
        for (int i = 0; i < n; i++)
        {
            if (expected[i] > 0)
            {
                double diff = observed[i] - expected[i];
                pearsonChiSq += (diff * diff) / expected[i];
                validCount++;
            }
        }
        
        // 有効な観測数で自由度を調整
        df = validCount - numParameters;
        if (df <= 0)
        {
            return new OverdispersionTestResult
            {
                DispersionParameter = double.NaN,
                PearsonChiSquare = pearsonChiSq,
                DegreesOfFreedom = df,
                PValue = double.NaN,
                IsOverdispersed = false,
                Interpretation = "有効なデータ点が不足しています。"
            };
        }
        
        // 分散パラメータ φ = χ²/df
        double phi = pearsonChiSq / df;
        
        // χ²検定のp値（上側確率）
        // H0: φ = 1 (Poisson仮定が正しい)
        // H1: φ > 1 (過分散)
        double pValue = 1.0 - MathNet.Numerics.Distributions.ChiSquared.CDF(df, pearsonChiSq);
        
        bool isOverdispersed = phi > 1.5 && pValue < 0.05;
        
        string interpretation;
        if (phi < 0.8)
        {
            interpretation = $"過小分散 (φ={phi:F3}): 分散が期待より小さい。データに制約がある可能性。";
        }
        else if (phi <= 1.2)
        {
            interpretation = $"等分散 (φ={phi:F3}): Poisson仮定は妥当。";
        }
        else if (phi <= 1.5)
        {
            interpretation = $"軽度の過分散 (φ={phi:F3}): Poisson仮定は概ね妥当だが、やや分散が大きい。";
        }
        else if (phi <= 3.0)
        {
            interpretation = $"中程度の過分散 (φ={phi:F3}): quasi-Poissonモデルの使用を推奨。信頼区間は過小評価の可能性。";
        }
        else
        {
            interpretation = $"重度の過分散 (φ={phi:F3}): 負の二項分布モデルの使用を強く推奨。Poisson仮定は不適切。";
        }
        
        return new OverdispersionTestResult
        {
            DispersionParameter = phi,
            PearsonChiSquare = pearsonChiSq,
            DegreesOfFreedom = df,
            PValue = pValue,
            IsOverdispersed = isOverdispersed,
            Interpretation = interpretation
        };
    }
    
    /// <summary>
    /// ランダム化分位残差を計算（Dunn & Smyth, 1996）
    /// </summary>
    /// <param name="observed">観測された日次バグ数</param>
    /// <param name="expected">期待される日次バグ数（λ_i）</param>
    /// <returns>ランダム化分位残差（正規分布に従うはず）</returns>
    /// <remarks>
    /// Poisson分布のような離散分布では、通常の分位残差は離散的になる。
    /// ランダム化分位残差は、CDF(y-1) と CDF(y) の間で一様乱数を使い、
    /// 連続的な残差を生成する。これにより、正規性検定が適切に機能する。
    /// </remarks>
    public static double[] CalculateRandomizedQuantileResiduals(double[] observed, double[] expected)
    {
        int n = observed.Length;
        var residuals = new double[n];
        var random = new Random(42); // 再現性のため固定シード
        
        for (int i = 0; i < n; i++)
        {
            double lambda = expected[i];
            int y = (int)Math.Round(observed[i]);
            
            if (lambda <= 0)
            {
                residuals[i] = 0;
                continue;
            }
            
            // Poisson CDF: F(y) = P(Y <= y)
            double cdfY = MathNet.Numerics.Distributions.Poisson.CDF(lambda, y);
            double cdfYMinus1 = y > 0 ? MathNet.Numerics.Distributions.Poisson.CDF(lambda, y - 1) : 0;
            
            // [F(y-1), F(y)] の間で一様乱数を生成
            double u = cdfYMinus1 + random.NextDouble() * (cdfY - cdfYMinus1);
            
            // 標準正規分布の逆関数で変換
            // u が 0 または 1 に極端に近い場合のクリッピング
            u = Math.Max(1e-10, Math.Min(1 - 1e-10, u));
            residuals[i] = MathNet.Numerics.Distributions.Normal.InvCDF(0, 1, u);
        }
        
        return residuals;
    }
    
    /// <summary>
    /// Poisson診断に基づく警告メッセージを生成
    /// </summary>
    private static List<string> GeneratePoissonDiagnosticWarnings(
        OverdispersionTestResult overdispersionTest,
        double[] dailyObserved,
        double[] dailyExpected,
        int n)
    {
        var warnings = new List<string>();
        
        // サンプルサイズの警告
        if (n < 10)
        {
            warnings.Add($"サンプルサイズが小さい（n={n}）。統計的検定の信頼性が低下します。");
        }
        
        // 過分散の警告
        if (overdispersionTest.IsOverdispersed)
        {
            warnings.Add($"過分散が検出されました（φ={overdispersionTest.DispersionParameter:F2}）。" +
                        "信頼区間・予測区間は過小評価されている可能性があります。");
        }
        
        // 期待値が小さい場合の警告
        int smallLambdaCount = dailyExpected.Count(lambda => lambda > 0 && lambda < 5);
        if (smallLambdaCount > n * 0.3)
        {
            warnings.Add($"期待値が小さい（λ<5）データ点が{smallLambdaCount}個あります。" +
                        "Pearson残差の正規近似は信頼性が低い可能性があります。" +
                        "ランダム化分位残差の使用を推奨します。");
        }
        
        // ゼロ過剰の警告
        int zeroCount = dailyObserved.Count(y => y == 0);
        double expectedZeros = dailyExpected.Sum(lambda => lambda > 0 ? Math.Exp(-lambda) : 0);
        if (zeroCount > expectedZeros * 1.5 && zeroCount > 3)
        {
            warnings.Add($"ゼロの数（{zeroCount}）が期待値（{expectedZeros:F1}）より多い。" +
                        "ゼロ過剰Poissonモデル（ZIP）の使用を検討してください。");
        }
        
        return warnings;
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
    /// <remarks>
    /// 累積残差（Cumulative）を使用する場合、累積データは本質的に自己相関を持つため、
    /// ラン検定やDurbin-Watson検定の結果は参考値として扱う必要があります。
    /// 独立性の検定にはPearson残差またはDeviance残差（増分ベース）を推奨します。
    /// </remarks>
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
        else if (type == ResidualType.Cumulative)
        {
            smallSampleWarning = "累積残差は自己相関を持つため、独立性検定（ラン検定、DW検定）の結果は参考値です。";
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
    /// Anscombe残差を計算（Poisson分布用）
    /// </summary>
    /// <param name="observed">観測値</param>
    /// <param name="expected">期待値（λ）</param>
    /// <returns>Anscombe残差</returns>
    /// <remarks>
    /// Anscombe残差は分散安定化変換に基づく残差で、
    /// Pearson残差やDeviance残差よりも正規分布に近い分布を持つ。
    /// 計算式: (3/2) * (y^(2/3) - λ^(2/3)) / λ^(1/6)
    /// </remarks>
    private static double CalculateAnscombeResidual(double observed, double expected)
    {
        if (expected <= 0)
            return 0;
        
        // Anscombe (1953) の分散安定化変換
        double yPow = Math.Pow(Math.Max(0, observed), 2.0 / 3.0);
        double lambdaPow = Math.Pow(expected, 2.0 / 3.0);
        double denominator = Math.Pow(expected, 1.0 / 6.0);
        
        return 1.5 * (yPow - lambdaPow) / denominator;
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
