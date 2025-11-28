namespace BugConvergenceTool.Models;

/// <summary>
/// テスト工数関数を組み込んだ信頼度成長モデルの基底クラス
/// dm(t)/dt = b·w(t)·[a - m(t)]
/// 解: m(t) = a(1 - e^(-b·W(t)))
/// </summary>
public abstract class TEFBasedModelBase : ReliabilityGrowthModelBase
{
    protected readonly ITestEffortFunction _tef;
    
    protected TEFBasedModelBase(ITestEffortFunction tef)
    {
        _tef = tef;
    }
    
    public override string Category => "TEF組込";
    
    /// <summary>
    /// TEFのパラメータインデックス開始位置
    /// </summary>
    protected abstract int TEFParamStartIndex { get; }
    
    /// <summary>
    /// TEFパラメータを抽出
    /// </summary>
    protected double[] GetTEFParams(double[] allParams)
    {
        int tefParamCount = _tef.ParameterNames.Length;
        var tefParams = new double[tefParamCount];
        Array.Copy(allParams, TEFParamStartIndex, tefParams, 0, tefParamCount);
        return tefParams;
    }
}

/// <summary>
/// TEF組込指数型モデル
/// m(t) = a(1 - e^(-b·W(t)))
/// </summary>
public class TEFExponentialModel : TEFBasedModelBase
{
    public TEFExponentialModel(ITestEffortFunction tef) : base(tef) { }
    
    public override string Name => $"TEF指数型({_tef.Name})";
    public override string Formula => $"m(t) = a(1 - e^(-b·W(t))), {_tef.Formula}";
    public override string Description => $"指数型 + {_tef.Description}";
    
    public override string[] ParameterNames
    {
        get
        {
            var names = new List<string> { "a", "b" };
            names.AddRange(_tef.ParameterNames.Select(n => $"TEF_{n}"));
            return names.ToArray();
        }
    }
    
    protected override int TEFParamStartIndex => 2;
    
    public override double Calculate(double t, double[] parameters)
    {
        double a = parameters[0];
        double b = parameters[1];
        var tefParams = GetTEFParams(parameters);
        
        double W = _tef.CalculateW(t, tefParams);
        return a * (1 - Math.Exp(-b * W));
    }
    
    public override double[] GetInitialParameters(double[] tData, double[] yData)
    {
        double maxY = yData.Max();
        var tefInit = _tef.GetInitialParameters(tData, yData);
        
        var initial = new List<double> { maxY * 1.5, 0.01 };
        initial.AddRange(tefInit);
        return initial.ToArray();
    }
    
    public override (double[] lower, double[] upper) GetBounds(double[] tData, double[] yData)
    {
        double maxY = yData.Max();
        var (tefLower, tefUpper) = _tef.GetBounds(tData, yData);
        
        var lower = new List<double> { maxY, 0.0001 };
        lower.AddRange(tefLower);
        
        var upper = new List<double> { maxY * 5, 1.0 };
        upper.AddRange(tefUpper);
        
        return (lower.ToArray(), upper.ToArray());
    }
}

/// <summary>
/// TEF組込遅延S字型モデル
/// m(t) = a(1 - (1 + b·W(t))·e^(-b·W(t)))
/// </summary>
public class TEFDelayedSModel : TEFBasedModelBase
{
    public TEFDelayedSModel(ITestEffortFunction tef) : base(tef) { }
    
    public override string Name => $"TEF遅延S字({_tef.Name})";
    public override string Formula => $"m(t) = a(1 - (1+bW)e^(-bW)), {_tef.Formula}";
    public override string Description => $"遅延S字型 + {_tef.Description}";
    
    public override string[] ParameterNames
    {
        get
        {
            var names = new List<string> { "a", "b" };
            names.AddRange(_tef.ParameterNames.Select(n => $"TEF_{n}"));
            return names.ToArray();
        }
    }
    
    protected override int TEFParamStartIndex => 2;
    
    public override double Calculate(double t, double[] parameters)
    {
        double a = parameters[0];
        double b = parameters[1];
        var tefParams = GetTEFParams(parameters);
        
        double W = _tef.CalculateW(t, tefParams);
        double bW = b * W;
        return a * (1 - (1 + bW) * Math.Exp(-bW));
    }
    
    public override double[] GetInitialParameters(double[] tData, double[] yData)
    {
        double maxY = yData.Max();
        var tefInit = _tef.GetInitialParameters(tData, yData);
        
        var initial = new List<double> { maxY * 1.5, 0.02 };
        initial.AddRange(tefInit);
        return initial.ToArray();
    }
    
    public override (double[] lower, double[] upper) GetBounds(double[] tData, double[] yData)
    {
        double maxY = yData.Max();
        var (tefLower, tefUpper) = _tef.GetBounds(tData, yData);
        
        var lower = new List<double> { maxY, 0.0001 };
        lower.AddRange(tefLower);
        
        var upper = new List<double> { maxY * 5, 1.0 };
        upper.AddRange(tefUpper);
        
        return (lower.ToArray(), upper.ToArray());
    }
}

/// <summary>
/// TEF組込不完全デバッグモデル
/// a(t) = a + α·m(t)
/// dm(t)/dt = b·w(t)·[a(t) - m(t)]
/// </summary>
public class TEFImperfectDebugModel : TEFBasedModelBase
{
    public TEFImperfectDebugModel(ITestEffortFunction tef) : base(tef) { }
    
    public override string Name => $"TEF不完全デバッグ({_tef.Name})";
    public override string Category => "TEF+不完全";
    public override string Formula => $"dm/dt = bw(a+αm-m), {_tef.Formula}";
    public override string Description => $"不完全デバッグ + {_tef.Description}";
    
    public override string[] ParameterNames
    {
        get
        {
            var names = new List<string> { "a", "b", "α" };
            names.AddRange(_tef.ParameterNames.Select(n => $"TEF_{n}"));
            return names.ToArray();
        }
    }
    
    protected override int TEFParamStartIndex => 3;
    
    public override double Calculate(double t, double[] parameters)
    {
        double a = parameters[0];
        double b = parameters[1];
        double alpha = parameters[2];
        var tefParams = GetTEFParams(parameters);
        
        double W = _tef.CalculateW(t, tefParams);
        
        // 解析解: m(t) = a(1 - e^(-b(1-α)W)) / (1 - α(1 - e^(-b(1-α)W)))
        // ただし α < 1 の場合
        if (alpha >= 1.0)
            alpha = 0.99;
        
        double factor = 1 - alpha;
        double expTerm = Math.Exp(-b * factor * W);
        double numerator = a * (1 - expTerm);
        double denominator = 1 - alpha * (1 - expTerm);
        
        if (denominator <= 0)
            return a;
        
        return numerator / denominator;
    }
    
    public override double[] GetInitialParameters(double[] tData, double[] yData)
    {
        double maxY = yData.Max();
        var tefInit = _tef.GetInitialParameters(tData, yData);
        
        var initial = new List<double> { maxY * 1.5, 0.01, 0.1 };
        initial.AddRange(tefInit);
        return initial.ToArray();
    }
    
    public override (double[] lower, double[] upper) GetBounds(double[] tData, double[] yData)
    {
        double maxY = yData.Max();
        var (tefLower, tefUpper) = _tef.GetBounds(tData, yData);
        
        var lower = new List<double> { maxY, 0.0001, 0.0 };
        lower.AddRange(tefLower);
        
        var upper = new List<double> { maxY * 5, 1.0, 0.5 };
        upper.AddRange(tefUpper);
        
        return (lower.ToArray(), upper.ToArray());
    }
}

/// <summary>
/// TEF組込モデルのファクトリ
/// </summary>
public static class TEFModelFactory
{
    /// <summary>
    /// 指定TEFで全組込モデルを取得
    /// </summary>
    public static IEnumerable<ReliabilityGrowthModelBase> GetModelsWithTEF(ITestEffortFunction tef)
    {
        yield return new TEFExponentialModel(tef);
        yield return new TEFDelayedSModel(tef);
        yield return new TEFImperfectDebugModel(tef);
    }
    
    /// <summary>
    /// 全TEF × 全モデルの組み合わせを取得
    /// </summary>
    public static IEnumerable<ReliabilityGrowthModelBase> GetAllTEFModels()
    {
        foreach (var tef in TEFFactory.GetAllTEFs())
        {
            foreach (var model in GetModelsWithTEF(tef))
            {
                yield return model;
            }
        }
    }
    
    /// <summary>
    /// 推奨TEFモデルを取得（Weibull TEF + 各種モデル）
    /// </summary>
    public static IEnumerable<ReliabilityGrowthModelBase> GetRecommendedTEFModels()
    {
        var weibullTef = new WeibullTEF();
        return GetModelsWithTEF(weibullTef);
    }
}
