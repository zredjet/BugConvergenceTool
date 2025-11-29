namespace BugConvergenceTool.Models;

/// <summary>
/// 不完全デバッグ指数型モデル（Pham-Nordmann-Zhang型）
/// m(t) = a * (1 - exp(-b*t)) / (1 + p*exp(-b*t))
/// </summary>
public class ImperfectDebugExponentialModel : ReliabilityGrowthModelBase
{
    public override string Name => "不完全デバッグ指数型";
    public override string Category => "不完全デバッグ";
    public override string Formula => "m(t) = a(1-e^(-bt)) / (1+p·e^(-bt))";
    public override string Description => "Pham-Nordmann-Zhang型。修正時の新バグ発生を考慮";
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
        return (
            new[] { maxY, 0.001, -0.5 },
            new[] { maxY * 5, 1.0, 0.99 }
        );
    }
}

/// <summary>
/// 一般化不完全デバッグモデル
/// m(t) = a * (1 - exp(-b*t^c)) / (1 + p*(1 - exp(-b*t^c)))
/// </summary>
public class GeneralizedImperfectDebugModel : ReliabilityGrowthModelBase
{
    public override string Name => "一般化不完全デバッグ";
    public override string Category => "不完全デバッグ";
    public override string Formula => "m(t) = a(1-e^(-bt^c)) / (1+p(1-e^(-bt^c)))";
    public override string Description => "発見率変化と不完全デバッグを統合した汎用モデル";
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
        return (
            new[] { maxY, 0.001, 0.5, -0.5 },
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
        yield return new ModifiedGompertzModel();
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
