namespace BugConvergenceTool.Models;

/// <summary>
/// Pham (1993) 型不完全デバッグ指数型モデル
/// m(t) = a * (1 - exp(-b*t)) / (1 + p*exp(-b*t))
/// </summary>
/// <remarks>
/// <para>
/// 不完全デバッグ（Imperfect Debugging）を考慮した指数型NHPPモデル。
/// Pham, H. (1993) の不完全デバッグモデルに基づく形式です。
/// </para>
/// <para>
/// <strong>学術的注記:</strong>
/// このモデルは Pham-Nordmann-Zhang (1999) モデル（PNZモデル）とは異なります。
/// 正確なPNZモデルは m(t) = a(1-e^(-(b+α)t))/(1+(α/b)e^(-bt)) です。
/// 本実装は Pham (1993) の原型に基づく簡略化形式です。
/// </para>
/// <para>
/// パラメータの解釈:
/// - a: 潜在欠陥数のスケールパラメータ
/// - b: 欠陥検出率
/// - p: 不完全デバッグ係数（0 ≤ p &lt; 1 で新規バグ混入、p = 0 で完全デバッグ）
/// </para>
/// <para>
/// 漸近値: t → ∞ で m(t) → a（p の値に依存せず a に収束）
/// </para>
/// <para>
/// 参考文献:
/// - Pham, H. (1993). "Software Reliability Assessment: Imperfect Debugging and Multiple Failure Types in Software Development"
/// - Zhang, X., Teng, X., and Pham, H. (2003). "Considering fault removal efficiency in software reliability assessment"
/// </para>
/// </remarks>
public class ImperfectDebugExponentialModel : ReliabilityGrowthModelBase
{
    public override string Name => "Pham型不完全デバッグ指数";
    public override string Category => "不完全デバッグ";
    public override string Formula => "m(t) = a(1-e^(-bt)) / (1+p·e^(-bt))";
    public override string Description => "Pham (1993) 型。修正時の新バグ発生を考慮した不完全デバッグモデル";
    public override string[] ParameterNames => new[] { "a", "b", "p" };

    public override double Calculate(double t, double[] parameters)
    {
        double a = parameters[0];
        double b = parameters[1];
        double p = parameters[2];
        
        double expBt = Math.Exp(-b * t);
        return a * (1 - expBt) / (1 + p * expBt);
    }

    public override double[] GetInitialParameters(double[] tData, double[] yData)
    {
        double maxY = yData.Max();
        int n = tData.Length;

        // a: 不完全デバッグを考慮してやや大きめ
        double last = yData[^1];
        double prev = n > 1 ? yData[^2] : yData[^1];
        double increment = last - prev;
        bool isConverged = increment <= GetConvergenceThreshold();
        double a0 = maxY * GetScaleFactorAInRange(isConverged, 0.3);  // 不完全デバッグは中程度のスケール

        // b: 設定から指数型の値を取得
        double avgSlope = EstimateAverageSlope(yData);
        double b0 = GetBValueExponential(avgSlope);

        // p: 設定から不完全デバッグ係数の初期値を取得
        double p0 = GetImperfectDebugP0();

        return new[] { a0, b0, p0 };
    }

    public override (double[] lower, double[] upper) GetBounds(double[] tData, double[] yData)
    {
        double maxY = yData.Max();
        // p の下限は 0（学術的標準）。負の p は物理的に「修正時にバグが減る」ことを意味し非標準。
        // ただし、p = 0 で完全デバッグ（従来の指数型と等価）となる。
        return (
            new[] { maxY, 0.001, 0.0 },
            new[] { maxY * 5, 1.0, 0.99 }
        );
    }
}

/// <summary>
/// Weibull型不完全デバッグモデル（一般化Pham型）
/// m(t) = a * (1 - exp(-b*t^c)) / (1 + p*(1 - exp(-b*t^c)))
/// </summary>
/// <remarks>
/// <para>
/// Pham (1993) 型不完全デバッグモデルをWeibull型発見率に拡張した汎用モデル。
/// 形状パラメータ c により、時間依存の発見率を表現できます。
/// c = 1 のときPham型不完全デバッグ指数型と等価。
/// </para>
/// <para>
/// <strong>学術的注記:</strong>
/// この拡張は標準的なWeibull-NHPP + 不完全デバッグの組み合わせです。
/// 文献によっては "Generalized Imperfect Debugging Model" として参照されます。
/// </para>
/// <para>
/// パラメータの解釈:
/// - a: 潜在欠陥数のスケールパラメータ  
/// - b: 欠陥検出率のスケール
/// - c: 形状パラメータ（c &gt; 1 で初期遅延S字型、c &lt; 1 で初期加速凸型、c = 1 で指数型）
/// - p: 不完全デバッグ係数（0 ≤ p &lt; 1）
/// </para>
/// <para>
/// 参考文献:
/// - Pham, H. (2006). "System Software Reliability", Springer Series in Reliability Engineering
/// </para>
/// </remarks>
public class GeneralizedImperfectDebugModel : ReliabilityGrowthModelBase
{
    public override string Name => "Weibull型不完全デバッグ";
    public override string Category => "不完全デバッグ";
    public override string Formula => "m(t) = a(1-e^(-bt^c)) / (1+p(1-e^(-bt^c)))";
    public override string Description => "Pham型をWeibull発見率に拡張。c>1でS字、c=1で指数型、c<1で凸型";
    public override string[] ParameterNames => new[] { "a", "b", "c", "p" };

    public override double Calculate(double t, double[] parameters)
    {
        double a = parameters[0];
        double b = parameters[1];
        double c = parameters[2];
        double p = parameters[3];
        
        double tc = Math.Pow(t, c);
        double expBtc = Math.Exp(-b * tc);
        double detected = 1 - expBtc;
        
        return a * detected / (1 + p * detected);
    }

    public override double[] GetInitialParameters(double[] tData, double[] yData)
    {
        double maxY = yData.Max();
        int n = tData.Length;

        // a: 設定から取得
        double last = yData[^1];
        double prev = n > 1 ? yData[^2] : yData[^1];
        double increment = last - prev;
        bool isConverged = increment <= GetConvergenceThreshold();
        double a0 = maxY * GetScaleFactorAInRange(isConverged, 0.3);  // 中程度のスケール

        // c: 形状パラメータ。とりあえず 1.0 から開始
        double c0 = 1.0;

        // b: 設定から指数型の値を取得
        double avgSlope = EstimateAverageSlope(yData);
        double b0 = GetBValueExponential(avgSlope);

        // p: 設定から取得
        double p0 = GetImperfectDebugP0();

        return new[] { a0, b0, c0, p0 };
    }

    public override (double[] lower, double[] upper) GetBounds(double[] tData, double[] yData)
    {
        double maxY = yData.Max();
        // p の下限は 0（学術的標準）
        return (
            new[] { maxY, 0.001, 0.5, 0.0 },
            new[] { maxY * 5, 1.0, 2.0, 0.99 }
        );
    }
}

/// <summary>
/// 全モデルを取得するファクトリ
/// </summary>
public static class ModelFactory
{
    /// <summary>
    /// 全モデルを取得（基本 + 不完全デバッグ）
    /// </summary>
    public static IEnumerable<ReliabilityGrowthModelBase> GetAllModels()
    {
        // 基本モデル
        foreach (var m in GetBasicModels())
            yield return m;
        
        // 不完全デバッグモデル
        foreach (var m in GetImperfectDebugModels())
            yield return m;
    }
    
    /// <summary>
    /// 拡張モデルを含む全モデルを取得
    /// </summary>
    public static IEnumerable<ReliabilityGrowthModelBase> GetAllExtendedModels(
        bool includeChangePoint = true,
        bool includeTEF = true,
        bool includeFRE = true,
        bool includeCoverage = true)
    {
        // 基本モデル
        foreach (var m in GetBasicModels())
            yield return m;

        // 不完全デバッグモデル
        foreach (var m in GetImperfectDebugModels())
            yield return m;

        // 変化点モデル
        if (includeChangePoint)
        {
            foreach (var m in ChangePointModelFactory.GetBasicChangePointModels())
                yield return m;
        }

        // TEF組込モデル（推奨のWeibull TEFのみ）
        if (includeTEF)
        {
            foreach (var m in TEFModelFactory.GetRecommendedTEFModels())
                yield return m;
        }

        // 欠陥除去効率モデル
        if (includeFRE)
        {
            foreach (var m in FREModelFactory.GetBasicFREModels())
                yield return m;
        }

        // Coverage モデル
        if (includeCoverage)
        {
            foreach (var m in CoverageModelFactory.GetRecommendedCoverageModels())
                yield return m;
        }
    }
    
    /// <summary>
    /// 基本モデルのみ取得
    /// </summary>
    public static IEnumerable<ReliabilityGrowthModelBase> GetBasicModels()
    {
        yield return new ExponentialModel();
        yield return new DelayedSModel();
        yield return new GompertzModel();
        yield return new ModifiedGompertzModel();  // Shiftedゴンペルツ
        yield return new OhbaWeibullModel();
        yield return new LogisticModel();
    }
    
    /// <summary>
    /// 不完全デバッグモデルを取得
    /// </summary>
    public static IEnumerable<ReliabilityGrowthModelBase> GetImperfectDebugModels()
    {
        yield return new ImperfectDebugExponentialModel();
        yield return new GeneralizedImperfectDebugModel();
    }
    
    /// <summary>
    /// モデルカテゴリ一覧を取得
    /// </summary>
    public static IEnumerable<string> GetCategories()
    {
        yield return "基本";
        yield return "不完全デバッグ";
        yield return "変化点";
        yield return "TEF組込";
        yield return "欠陥除去効率";
        yield return "Coverage";
        yield return "統合";
    }
}
