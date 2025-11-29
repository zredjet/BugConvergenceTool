namespace BugConvergenceTool.Services.Diagnostics;

/// <summary>
/// 自己相関検定結果
/// </summary>
public class AutocorrelationTestResult
{
    /// <summary>Durbin-Watson統計量（0-4、2が無相関）</summary>
    public double DurbinWatsonStatistic { get; init; }
    
    /// <summary>DW検定の判定</summary>
    public string DurbinWatsonInterpretation { get; init; } = "";
    
    /// <summary>各ラグの自己相関係数（ACF）</summary>
    public double[] AutocorrelationCoefficients { get; init; } = Array.Empty<double>();
    
    /// <summary>ACFの95%信頼限界（± 1.96/√n）</summary>
    public double AcfConfidenceLimit { get; init; }
    
    /// <summary>有意な自己相関があるラグのリスト</summary>
    public int[] SignificantLags { get; init; } = Array.Empty<int>();
    
    /// <summary>Ljung-Box Q統計量</summary>
    public double LjungBoxQ { get; init; }
    
    /// <summary>Ljung-Box検定の自由度</summary>
    public int LjungBoxDegreesOfFreedom { get; init; }
    
    /// <summary>Ljung-Box検定のp値</summary>
    public double LjungBoxPValue { get; init; }
    
    /// <summary>有意な自己相関が存在するか</summary>
    public bool HasSignificantAutocorrelation { get; init; }
    
    /// <summary>総合的な解釈</summary>
    public string Interpretation { get; init; } = "";
    
    /// <summary>サンプルサイズが小さい場合の警告</summary>
    public string? SmallSampleWarning { get; init; }
}

/// <summary>
/// 自己相関検定サービス
/// </summary>
public class AutocorrelationTest
{
    private const double SIGNIFICANCE_LEVEL = 0.05;
    private const double DW_LOWER_THRESHOLD = 1.5;
    private const double DW_UPPER_THRESHOLD = 2.5;

    /// <summary>
    /// Durbin-Watson検定を実行
    /// </summary>
    /// <param name="residuals">残差配列</param>
    /// <returns>Durbin-Watson統計量</returns>
    public double CalculateDurbinWatson(double[] residuals)
    {
        int n = residuals.Length;
        if (n < 2) return 2.0;  // 無相関を仮定
        
        double sumSquaredDiff = 0;
        double sumSquared = 0;
        
        for (int i = 0; i < n; i++)
        {
            sumSquared += residuals[i] * residuals[i];
            if (i > 0)
            {
                double diff = residuals[i] - residuals[i - 1];
                sumSquaredDiff += diff * diff;
            }
        }
        
        if (sumSquared <= 0) return 2.0;
        return sumSquaredDiff / sumSquared;
    }

    /// <summary>
    /// 自己相関係数（ACF）を計算
    /// </summary>
    /// <param name="residuals">残差配列</param>
    /// <param name="maxLag">最大ラグ（デフォルト: データ長の1/4）</param>
    /// <returns>各ラグの自己相関係数</returns>
    public double[] CalculateAutocorrelation(double[] residuals, int? maxLag = null)
    {
        int n = residuals.Length;
        int lag = maxLag ?? Math.Max(1, n / 4);
        lag = Math.Min(lag, n - 2);  // 最低2点の重複が必要
        
        if (lag < 1) return Array.Empty<double>();
        
        double mean = residuals.Average();
        double variance = residuals.Sum(r => (r - mean) * (r - mean));
        
        if (variance <= 0) return new double[lag];
        
        var acf = new double[lag];
        
        for (int k = 1; k <= lag; k++)
        {
            double covariance = 0;
            for (int i = k; i < n; i++)
            {
                covariance += (residuals[i] - mean) * (residuals[i - k] - mean);
            }
            
            acf[k - 1] = covariance / variance;
        }
        
        return acf;
    }

    /// <summary>
    /// Ljung-Box検定を実行
    /// </summary>
    /// <param name="residuals">残差配列</param>
    /// <param name="maxLag">最大ラグ（デフォルト: データ長の1/4）</param>
    /// <returns>(Q統計量, p値, 自由度)</returns>
    public (double Q, double pValue, int df) CalculateLjungBox(double[] residuals, int? maxLag = null)
    {
        int n = residuals.Length;
        var acf = CalculateAutocorrelation(residuals, maxLag);
        
        if (acf.Length == 0)
            return (0, 1.0, 0);
        
        double Q = 0;
        for (int k = 0; k < acf.Length; k++)
        {
            int lag = k + 1;
            if (n - lag > 0)
            {
                Q += (acf[k] * acf[k]) / (n - lag);
            }
        }
        Q *= n * (n + 2);
        
        // 自由度 = ラグ数
        int df = acf.Length;
        
        // カイ二乗分布のp値
        double pValue = 1.0 - MathNet.Numerics.Distributions.ChiSquared.CDF(df, Q);
        
        return (Q, pValue, df);
    }

    /// <summary>
    /// 有意な自己相関を持つラグを検出
    /// </summary>
    /// <param name="acf">自己相関係数配列</param>
    /// <param name="n">サンプルサイズ</param>
    /// <returns>有意なラグのリスト（1始まり）</returns>
    public int[] FindSignificantLags(double[] acf, int n)
    {
        if (n <= 0 || acf.Length == 0)
            return Array.Empty<int>();
        
        // 95%信頼限界: ±1.96/√n
        double limit = 1.96 / Math.Sqrt(n);
        
        var significantLags = new List<int>();
        for (int k = 0; k < acf.Length; k++)
        {
            if (Math.Abs(acf[k]) > limit)
            {
                significantLags.Add(k + 1); // 1始まりのラグ番号
            }
        }
        
        return significantLags.ToArray();
    }

    /// <summary>
    /// 総合的な自己相関検定を実行
    /// </summary>
    /// <param name="residuals">残差配列</param>
    /// <param name="maxLag">最大ラグ（デフォルト: min(10, n/4)）</param>
    /// <returns>自己相関検定結果</returns>
    public AutocorrelationTestResult Test(double[] residuals, int? maxLag = null)
    {
        int n = residuals.Length;
        int effectiveMaxLag = maxLag ?? Math.Min(10, Math.Max(1, n / 4));
        
        // 小サンプル警告
        string? smallSampleWarning = null;
        if (n < 15)
        {
            smallSampleWarning = $"サンプルサイズが小さい（n={n}）ため、自己相関検定の結果は参考値です。";
        }
        
        // Durbin-Watson統計量
        double dw = CalculateDurbinWatson(residuals);
        
        // 自己相関係数
        var acf = CalculateAutocorrelation(residuals, effectiveMaxLag);
        
        // 95%信頼限界
        double acfLimit = n > 0 ? 1.96 / Math.Sqrt(n) : 0;
        
        // 有意なラグの検出
        var significantLags = FindSignificantLags(acf, n);
        
        // Ljung-Box検定
        var (Q, pValue, df) = CalculateLjungBox(residuals, effectiveMaxLag);
        
        // Durbin-Watsonの解釈
        string dwInterpretation = InterpretDurbinWatson(dw);
        
        // 有意な自己相関の判定
        // Ljung-Box p値が有意、または DW が閾値外、または有意なラグが存在
        bool hasSignificant = pValue < SIGNIFICANCE_LEVEL || 
                             dw < DW_LOWER_THRESHOLD || 
                             dw > DW_UPPER_THRESHOLD ||
                             significantLags.Length > 0;
        
        // 総合的な解釈を生成
        string interpretation = GenerateInterpretation(dw, pValue, significantLags, n);
        
        return new AutocorrelationTestResult
        {
            DurbinWatsonStatistic = dw,
            DurbinWatsonInterpretation = dwInterpretation,
            AutocorrelationCoefficients = acf,
            AcfConfidenceLimit = acfLimit,
            SignificantLags = significantLags,
            LjungBoxQ = Q,
            LjungBoxDegreesOfFreedom = df,
            LjungBoxPValue = pValue,
            HasSignificantAutocorrelation = hasSignificant,
            Interpretation = interpretation,
            SmallSampleWarning = smallSampleWarning
        };
    }

    /// <summary>
    /// Durbin-Watson統計量の解釈
    /// </summary>
    private static string InterpretDurbinWatson(double dw)
    {
        return dw switch
        {
            < 1.0 => "強い正の自己相関（モデルが系統的な変動を捉えきれていない）",
            < DW_LOWER_THRESHOLD => "正の自己相関の疑い（モデルの適合に問題がある可能性）",
            > 3.0 => "強い負の自己相関（過剰適合または外れ値の影響）",
            > DW_UPPER_THRESHOLD => "負の自己相関の疑い（過剰適合の可能性）",
            _ => "有意な自己相関なし"
        };
    }

    /// <summary>
    /// 総合的な解釈を生成
    /// </summary>
    private static string GenerateInterpretation(
        double dw, 
        double ljungBoxPValue, 
        int[] significantLags, 
        int n)
    {
        var issues = new List<string>();
        
        if (dw < DW_LOWER_THRESHOLD)
        {
            issues.Add($"Durbin-Watson統計量（{dw:F3}）が低く、正の自己相関を示唆");
        }
        else if (dw > DW_UPPER_THRESHOLD)
        {
            issues.Add($"Durbin-Watson統計量（{dw:F3}）が高く、負の自己相関を示唆");
        }
        
        if (ljungBoxPValue < SIGNIFICANCE_LEVEL)
        {
            issues.Add($"Ljung-Box検定が有意（p={ljungBoxPValue:F4}）");
        }
        
        if (significantLags.Length > 0)
        {
            string lags = string.Join(", ", significantLags.Take(5));
            if (significantLags.Length > 5)
                lags += $" 他{significantLags.Length - 5}個";
            issues.Add($"ラグ {lags} で有意な自己相関");
        }
        
        if (issues.Count == 0)
        {
            return "残差に有意な自己相関は検出されませんでした。モデルは時間的な依存構造を適切に捉えています。";
        }
        
        string summary = string.Join("、", issues);
        return $"残差に自己相関が検出されました: {summary}。" +
               "モデルがデータの時間的パターンを十分に捉えていない可能性があります。" +
               "別のモデルの検討、または変数の追加を検討してください。";
    }
}
