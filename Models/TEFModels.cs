namespace BugConvergenceTool.Models;

/// <summary>
/// テスト工数関数を組み込んだ信頼度成長モデルの基底クラス
/// dm(t)/dt = b·w(t)·[a - m(t)]
/// 解: m(t) = a(1 - e^(-b·W(t)))
/// </summary>
public abstract class TEFBasedModelBase : ReliabilityGrowthModelBase
{
    protected readonly ITestEffortFunction _tef;
    
    /// <summary>
    /// 実測累積工数データ（設定されている場合、TEFパラメータ推定に使用）
    /// </summary>
    public double[]? ObservedEffortData { get; set; }
    
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

    /// <summary>
    /// 時刻 t における累積工数 W(t) を計算
    /// </summary>
    /// <remarks>
    /// 観測開始（t=0）からの消費工数 W(t) - W(0) を返す。
    /// ロジスティック型 TEF のように W(0) ≠ 0 の関数でも m(0) = 0 となり、
    /// 実測の累積工数（観測開始から積算）とも同じ基準で比較できる。
    /// </remarks>
    public double CalculateEffort(double t, double[] parameters)
    {
        var tefParams = GetTEFParams(parameters);
        return _tef.CalculateW(t, tefParams) - _tef.CalculateW(0, tefParams);
    }
    
    /// <summary>
    /// 累積工数 W における平均値関数 m(W) を計算
    /// W = +∞（無限工数関数の t→∞）も扱えること
    /// </summary>
    protected abstract double CalculateAtEffort(double W, double[] parameters);
    
    public override double Calculate(double t, double[] parameters)
    {
        return CalculateAtEffort(CalculateEffort(t, parameters), parameters);
    }
    
    /// <summary>
    /// 漸近的総欠陥数: m(∞) = m(W(∞))
    /// </summary>
    /// <remarks>
    /// 有限工数関数（W(∞)=N）では工数を使い切った時点の値となり、a とは一致しない。
    /// </remarks>
    public override double GetAsymptoticTotalBugs(double[] parameters)
    {
        var tefParams = GetTEFParams(parameters);
        double totalEffort = _tef.CalculateTotalEffort(tefParams) - _tef.CalculateW(0, tefParams);
        return CalculateAtEffort(totalEffort, parameters);
    }
    
    /// <summary>
    /// TEFパラメータの初期値/境界用のデータを取得
    /// </summary>
    /// <remarks>
    /// TEFモデルは実測工数データが必須です。
    /// 工数データがない場合に累積バグ数を代替として使用することは、
    /// TEFモデルの理論的前提（テスト工数と欠陥検出の関係）を崩すため、
    /// 学術的に不適切です。工数データがない場合は基本モデルを使用してください。
    /// </remarks>
    /// <exception cref="InvalidOperationException">実測工数データがない場合</exception>
    protected double[] GetEffortDataForTEF(double[] yData)
    {
        if (ObservedEffortData != null && ObservedEffortData.Length > 0 && ObservedEffortData.Any(e => e > 0))
        {
            return ObservedEffortData;
        }
        
        throw new InvalidOperationException(
            "TEFモデルには実測工数データが必要です。" +
            "工数データが利用できない場合は、基本モデル（指数型、遅延S字型等）を使用してください。" +
            "累積バグ数をテスト工数の代替として使用することは、TEFモデルの理論的前提を崩すため推奨されません。");
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
    
    protected override double CalculateAtEffort(double W, double[] parameters)
    {
        double a = parameters[0];
        double b = parameters[1];
        return a * (1 - Math.Exp(-b * W));
    }
    
    public override double[] GetInitialParameters(double[] tData, double[] yData)
    {
        double maxY = yData.Max();
        var effortData = GetEffortDataForTEF(yData);
        var tefInit = _tef.GetInitialParameters(tData, effortData);

        int n = tData.Length;

        // a: 収束度合いに応じて 1.2〜1.8×maxY
        double last = yData[^1];
        double prev = n > 1 ? yData[^2] : yData[^1];
        double increment = last - prev;
        double a0 = increment <= 1.0 ? maxY * 1.2 : maxY * 1.8;

        // b: 平均増分から指数型と同様に推定
        double avgSlope = EstimateAverageSlope(yData);
        double b0 = avgSlope switch
        {
            <= 0.1 => 0.02,
            <= 0.5 => 0.05,
            <= 1.0 => 0.1,
            _ => 0.2
        };

        var initial = new List<double> { a0, b0 };
        initial.AddRange(tefInit);
        return initial.ToArray();
    }
    
    public override (double[] lower, double[] upper) GetBounds(double[] tData, double[] yData)
    {
        double maxY = yData.Max();
        var effortData = GetEffortDataForTEF(yData);
        var (tefLower, tefUpper) = _tef.GetBounds(tData, effortData);
        
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
    
    protected override double CalculateAtEffort(double W, double[] parameters)
    {
        double a = parameters[0];
        double b = parameters[1];
        
        // W=∞ では (1+bW)e^(-bW) が ∞·0 になるため極限値 0 を使う
        if (double.IsPositiveInfinity(W))
            return a;
        
        double bW = b * W;
        return a * (1 - (1 + bW) * Math.Exp(-bW));
    }
    
    public override double[] GetInitialParameters(double[] tData, double[] yData)
    {
        double maxY = yData.Max();
        var effortData = GetEffortDataForTEF(yData);
        var tefInit = _tef.GetInitialParameters(tData, effortData);

        int n = tData.Length;

        // a: 収束度合いに応じて 1.2〜1.8×maxY
        double last = yData[^1];
        double prev = n > 1 ? yData[^2] : yData[^1];
        double increment = last - prev;
        double a0 = increment <= 1.0 ? maxY * 1.2 : maxY * 1.8;

        // b: 遅延S字と同様に平均増分で調整
        double avgSlope = EstimateAverageSlope(yData);
        double b0 = avgSlope switch
        {
            <= 0.1 => 0.03,
            <= 0.5 => 0.08,
            <= 1.0 => 0.15,
            _ => 0.25
        };

        var initial = new List<double> { a0, b0 };
        initial.AddRange(tefInit);
        return initial.ToArray();
    }
    
    public override (double[] lower, double[] upper) GetBounds(double[] tData, double[] yData)
    {
        double maxY = yData.Max();
        var effortData = GetEffortDataForTEF(yData);
        var (tefLower, tefUpper) = _tef.GetBounds(tData, effortData);
        
        var lower = new List<double> { maxY, 0.0001 };
        lower.AddRange(tefLower);
        
        var upper = new List<double> { maxY * 5, 1.0 };
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
