namespace BugConvergenceTool.Services.Diagnostics;

/// <summary>
/// 正規性検定結果
/// </summary>
public class NormalityTestResult
{
    /// <summary>Jarque-Bera統計量</summary>
    public double JarqueBeraStatistic { get; init; }
    
    /// <summary>Jarque-Bera検定のp値</summary>
    public double JarqueBeraPValue { get; init; }
    
    /// <summary>Anderson-Darling統計量</summary>
    public double AndersonDarlingStatistic { get; init; }
    
    /// <summary>Anderson-Darling検定のp値（近似）</summary>
    public double AndersonDarlingPValue { get; init; }
    
    /// <summary>歪度（Skewness）</summary>
    public double Skewness { get; init; }
    
    /// <summary>超過尖度（Excess Kurtosis = Kurtosis - 3）</summary>
    public double ExcessKurtosis { get; init; }
    
    /// <summary>正規性が棄却されるか（5%水準）</summary>
    public bool IsNormalityRejected { get; init; }
    
    /// <summary>解釈</summary>
    public string Interpretation { get; init; } = "";
    
    /// <summary>サンプルサイズが小さい場合の警告</summary>
    public string? SmallSampleWarning { get; init; }
}

/// <summary>
/// 正規性検定サービス
/// </summary>
public class NormalityTest
{
    private const double SIGNIFICANCE_LEVEL = 0.05;

    /// <summary>
    /// Jarque-Bera検定を実行
    /// H0: データは正規分布に従う
    /// </summary>
    /// <param name="data">データ配列</param>
    /// <returns>(JB統計量, p値)</returns>
    public (double statistic, double pValue) CalculateJarqueBera(double[] data)
    {
        int n = data.Length;
        if (n < 8)
            return (0, 1.0);  // サンプルサイズ不足
        
        double mean = data.Average();
        
        // 中心モーメントを計算
        double m2 = data.Sum(x => Math.Pow(x - mean, 2)) / n;
        double m3 = data.Sum(x => Math.Pow(x - mean, 3)) / n;
        double m4 = data.Sum(x => Math.Pow(x - mean, 4)) / n;
        
        if (m2 <= 0)
            return (0, 1.0);
        
        // 歪度（Skewness）
        double skewness = m3 / Math.Pow(m2, 1.5);
        
        // 超過尖度（Excess Kurtosis）
        double excessKurtosis = m4 / (m2 * m2) - 3;
        
        // JB統計量: JB = n/6 * (S² + K²/4)
        double jb = (n / 6.0) * (skewness * skewness + excessKurtosis * excessKurtosis / 4.0);
        
        // カイ二乗分布（自由度2）のp値
        double pValue = 1.0 - MathNet.Numerics.Distributions.ChiSquared.CDF(2, jb);
        
        return (jb, Math.Max(0.0001, pValue));
    }

    /// <summary>
    /// Anderson-Darling検定を実行
    /// より検出力の高い正規性検定
    /// </summary>
    /// <param name="data">データ配列</param>
    /// <returns>(AD統計量, p値)</returns>
    public (double statistic, double pValue) CalculateAndersonDarling(double[] data)
    {
        int n = data.Length;
        if (n < 8)
            return (0, 1.0);
        
        // 標準化
        double mean = data.Average();
        double std = Math.Sqrt(data.Sum(x => Math.Pow(x - mean, 2)) / (n - 1));
        
        if (std <= 0)
            return (0, 1.0);
        
        // 昇順ソート
        var sorted = data.Select(x => (x - mean) / std).OrderBy(x => x).ToArray();
        
        // 正規CDF値を計算
        double sum = 0;
        for (int i = 0; i < n; i++)
        {
            double phi_i = MathNet.Numerics.Distributions.Normal.CDF(0, 1, sorted[i]);
            double phi_n_i = MathNet.Numerics.Distributions.Normal.CDF(0, 1, sorted[n - 1 - i]);
            
            // 境界値のクリッピング
            phi_i = Math.Max(1e-10, Math.Min(1 - 1e-10, phi_i));
            phi_n_i = Math.Max(1e-10, Math.Min(1 - 1e-10, phi_n_i));
            
            sum += (2 * (i + 1) - 1) * (Math.Log(phi_i) + Math.Log(1 - phi_n_i));
        }
        
        double A2 = -n - sum / n;
        
        // 小標本補正 (Stephens, 1974)
        double A2Star = A2 * (1 + 0.75 / n + 2.25 / (n * n));
        
        // p値の近似（Marsaglia & Marsaglia, 2004の近似式）
        double pValue = CalculateAndersonDarlingPValue(A2Star);
        
        return (A2Star, pValue);
    }

    /// <summary>
    /// Anderson-Darling検定のp値を近似計算
    /// Marsaglia & Marsaglia (2004) の近似式
    /// </summary>
    private static double CalculateAndersonDarlingPValue(double A2)
    {
        // D'Agostino & Stephens (1986) の近似
        if (A2 < 0.2)
            return 1 - Math.Exp(-13.436 + 101.14 * A2 - 223.73 * A2 * A2);
        else if (A2 < 0.34)
            return 1 - Math.Exp(-8.318 + 42.796 * A2 - 59.938 * A2 * A2);
        else if (A2 < 0.6)
            return Math.Exp(0.9177 - 4.279 * A2 - 1.38 * A2 * A2);
        else if (A2 < 10)
            return Math.Exp(1.2937 - 5.709 * A2 + 0.0186 * A2 * A2);
        else
            return 0.0001;
    }

    /// <summary>
    /// 総合的な正規性検定を実行
    /// </summary>
    /// <param name="data">データ配列</param>
    /// <returns>正規性検定結果</returns>
    public NormalityTestResult Test(double[] data)
    {
        int n = data.Length;
        
        // サンプルサイズ警告
        string? smallSampleWarning = null;
        if (n < 8)
        {
            smallSampleWarning = $"サンプルサイズが非常に小さい（n={n}）ため、正規性検定は実行できません。";
            return new NormalityTestResult
            {
                IsNormalityRejected = false,
                Interpretation = "サンプルサイズ不足により正規性検定は実行できませんでした。",
                SmallSampleWarning = smallSampleWarning
            };
        }
        else if (n < 20)
        {
            smallSampleWarning = $"サンプルサイズが小さい（n={n}）ため、正規性検定の結果は参考値です。";
        }
        
        // Jarque-Bera検定
        var (jb, jbPValue) = CalculateJarqueBera(data);
        
        // Anderson-Darling検定
        var (ad, adPValue) = CalculateAndersonDarling(data);
        
        // 歪度と超過尖度を計算
        double mean = data.Average();
        double m2 = data.Sum(x => Math.Pow(x - mean, 2)) / n;
        double m3 = data.Sum(x => Math.Pow(x - mean, 3)) / n;
        double m4 = data.Sum(x => Math.Pow(x - mean, 4)) / n;
        
        double skewness = m2 > 0 ? m3 / Math.Pow(m2, 1.5) : 0;
        double excessKurtosis = m2 > 0 ? m4 / (m2 * m2) - 3 : 0;
        
        // 正規性の棄却判定（両検定のうちいずれかが有意なら棄却）
        bool isRejected = jbPValue < SIGNIFICANCE_LEVEL || adPValue < SIGNIFICANCE_LEVEL;
        
        // 解釈を生成
        string interpretation = GenerateInterpretation(
            isRejected, jbPValue, adPValue, skewness, excessKurtosis);
        
        return new NormalityTestResult
        {
            JarqueBeraStatistic = jb,
            JarqueBeraPValue = jbPValue,
            AndersonDarlingStatistic = ad,
            AndersonDarlingPValue = adPValue,
            Skewness = skewness,
            ExcessKurtosis = excessKurtosis,
            IsNormalityRejected = isRejected,
            Interpretation = interpretation,
            SmallSampleWarning = smallSampleWarning
        };
    }

    /// <summary>
    /// 解釈を生成
    /// </summary>
    private static string GenerateInterpretation(
        bool isRejected,
        double jbPValue,
        double adPValue,
        double skewness,
        double excessKurtosis)
    {
        if (!isRejected)
        {
            return "正規性は棄却されませんでした。残差は正規分布に近い分布をしています。";
        }
        
        var issues = new List<string>();
        
        // 歪度の解釈
        if (Math.Abs(skewness) > 1.0)
        {
            string direction = skewness > 0 ? "右（正）" : "左（負）";
            issues.Add($"強い{direction}の歪み（S={skewness:F2}）");
        }
        else if (Math.Abs(skewness) > 0.5)
        {
            string direction = skewness > 0 ? "右（正）" : "左（負）";
            issues.Add($"中程度の{direction}の歪み（S={skewness:F2}）");
        }
        
        // 尖度の解釈
        if (excessKurtosis > 2.0)
        {
            issues.Add($"裾が重い分布（K={excessKurtosis:F2}）。外れ値の影響がある可能性");
        }
        else if (excessKurtosis < -1.0)
        {
            issues.Add($"裾が軽い分布（K={excessKurtosis:F2}）");
        }
        
        string details = issues.Count > 0 
            ? string.Join("、", issues) 
            : "正規分布からの逸脱";
        
        return $"正規性が棄却されました（JB p={jbPValue:F4}, AD p={adPValue:F4}）。{details}。" +
               "Poisson過程からの残差では正規性からの逸脱はある程度許容されますが、" +
               "極端な非正規性はモデルの不適合を示唆する可能性があります。";
    }
}
