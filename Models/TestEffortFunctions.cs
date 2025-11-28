namespace BugConvergenceTool.Models;

/// <summary>
/// テスト工数関数（Test Effort Function）のインターフェース
/// </summary>
public interface ITestEffortFunction
{
    /// <summary>関数名</summary>
    string Name { get; }
    
    /// <summary>説明</summary>
    string Description { get; }
    
    /// <summary>数式</summary>
    string Formula { get; }
    
    /// <summary>パラメータ名</summary>
    string[] ParameterNames { get; }
    
    /// <summary>
    /// 累積テスト工数 W(t) を計算
    /// </summary>
    double CalculateW(double t, double[] parameters);
    
    /// <summary>
    /// 瞬時テスト工数（工数消費率）w(t) = dW/dt を計算
    /// </summary>
    double CalculateRate(double t, double[] parameters);
    
    /// <summary>
    /// 初期パラメータ推定値を取得
    /// </summary>
    double[] GetInitialParameters(double[] tData, double[] effortData);
    
    /// <summary>
    /// パラメータの境界を取得
    /// </summary>
    (double[] lower, double[] upper) GetBounds(double[] tData, double[] effortData);
}

/// <summary>
/// 指数型テスト工数関数
/// W(t) = N(1 - e^(-βt))
/// 工数消費率が単調減少
/// </summary>
public class ExponentialTEF : ITestEffortFunction
{
    public string Name => "指数型TEF";
    public string Description => "工数消費率が単調減少";
    public string Formula => "W(t) = N(1 - e^(-βt))";
    public string[] ParameterNames => new[] { "N", "β" };
    
    public double CalculateW(double t, double[] p)
    {
        double N = p[0], beta = p[1];
        return N * (1 - Math.Exp(-beta * t));
    }
    
    public double CalculateRate(double t, double[] p)
    {
        double N = p[0], beta = p[1];
        return N * beta * Math.Exp(-beta * t);
    }
    
    public double[] GetInitialParameters(double[] tData, double[] effortData)
    {
        double maxEffort = effortData.Max();
        return new[] { maxEffort * 1.2, 0.1 };
    }
    
    public (double[] lower, double[] upper) GetBounds(double[] tData, double[] effortData)
    {
        double maxEffort = effortData.Max();
        return (
            new[] { maxEffort, 0.001 },
            new[] { maxEffort * 5, 2.0 }
        );
    }
}

/// <summary>
/// Rayleigh型テスト工数関数
/// W(t) = N(1 - e^(-(t/β)²))
/// 工数消費率が最初増加、ピーク後減少
/// </summary>
public class RayleighTEF : ITestEffortFunction
{
    public string Name => "Rayleigh型TEF";
    public string Description => "工数消費率が最初増加、ピーク後減少";
    public string Formula => "W(t) = N(1 - e^(-(t/β)²))";
    public string[] ParameterNames => new[] { "N", "β" };
    
    public double CalculateW(double t, double[] p)
    {
        double N = p[0], beta = p[1];
        double ratio = t / beta;
        return N * (1 - Math.Exp(-ratio * ratio));
    }
    
    public double CalculateRate(double t, double[] p)
    {
        double N = p[0], beta = p[1];
        double ratio = t / beta;
        return (2 * N * t / (beta * beta)) * Math.Exp(-ratio * ratio);
    }
    
    public double[] GetInitialParameters(double[] tData, double[] effortData)
    {
        double maxEffort = effortData.Max();
        int n = tData.Length;
        return new[] { maxEffort * 1.2, n / 2.0 };
    }
    
    public (double[] lower, double[] upper) GetBounds(double[] tData, double[] effortData)
    {
        double maxEffort = effortData.Max();
        int n = tData.Length;
        return (
            new[] { maxEffort, 1.0 },
            new[] { maxEffort * 5, n * 2.0 }
        );
    }
}

/// <summary>
/// Weibull型テスト工数関数
/// W(t) = N(1 - e^(-(t/β)^m))
/// 非常に柔軟、m=1で指数型、m=2でRayleigh型
/// </summary>
public class WeibullTEF : ITestEffortFunction
{
    public string Name => "Weibull型TEF";
    public string Description => "汎用（形状可変）、m=1で指数型、m=2でRayleigh型";
    public string Formula => "W(t) = N(1 - e^(-(t/β)^m))";
    public string[] ParameterNames => new[] { "N", "β", "m" };
    
    public double CalculateW(double t, double[] p)
    {
        double N = p[0], beta = p[1], m = p[2];
        double ratio = t / beta;
        return N * (1 - Math.Exp(-Math.Pow(ratio, m)));
    }
    
    public double CalculateRate(double t, double[] p)
    {
        double N = p[0], beta = p[1], m = p[2];
        double ratio = t / beta;
        return (N * m / beta) * Math.Pow(ratio, m - 1) * Math.Exp(-Math.Pow(ratio, m));
    }
    
    public double[] GetInitialParameters(double[] tData, double[] effortData)
    {
        double maxEffort = effortData.Max();
        int n = tData.Length;
        return new[] { maxEffort * 1.2, n / 2.0, 1.5 };
    }
    
    public (double[] lower, double[] upper) GetBounds(double[] tData, double[] effortData)
    {
        double maxEffort = effortData.Max();
        int n = tData.Length;
        return (
            new[] { maxEffort, 1.0, 0.5 },
            new[] { maxEffort * 5, n * 2.0, 5.0 }
        );
    }
}

/// <summary>
/// ロジスティック型テスト工数関数
/// W(t) = N / (1 + A·e^(-αt))
/// 初期工数がゼロでない場合に適用
/// </summary>
public class LogisticTEF : ITestEffortFunction
{
    public string Name => "ロジスティック型TEF";
    public string Description => "初期工数がゼロでない場合";
    public string Formula => "W(t) = N / (1 + A·e^(-αt))";
    public string[] ParameterNames => new[] { "N", "A", "α" };
    
    public double CalculateW(double t, double[] p)
    {
        double N = p[0], A = p[1], alpha = p[2];
        return N / (1 + A * Math.Exp(-alpha * t));
    }
    
    public double CalculateRate(double t, double[] p)
    {
        double N = p[0], A = p[1], alpha = p[2];
        double expTerm = Math.Exp(-alpha * t);
        double denom = 1 + A * expTerm;
        return (N * A * alpha * expTerm) / (denom * denom);
    }
    
    public double[] GetInitialParameters(double[] tData, double[] effortData)
    {
        double maxEffort = effortData.Max();
        return new[] { maxEffort * 1.2, 5.0, 0.2 };
    }
    
    public (double[] lower, double[] upper) GetBounds(double[] tData, double[] effortData)
    {
        double maxEffort = effortData.Max();
        return (
            new[] { maxEffort, 1.0, 0.01 },
            new[] { maxEffort * 5, 50.0, 2.0 }
        );
    }
}

/// <summary>
/// べき乗則（Log-Power）テスト工数関数
/// W(t) = a·ln(1 + t^b)
/// 無限テスト工数関数（t→∞でW(t)→∞）
/// </summary>
public class LogPowerTEF : ITestEffortFunction
{
    public string Name => "べき乗則TEF";
    public string Description => "無限工数対応（t→∞でW→∞）";
    public string Formula => "W(t) = a·ln(1 + t^b)";
    public string[] ParameterNames => new[] { "a", "b" };
    
    public double CalculateW(double t, double[] p)
    {
        double a = p[0], b = p[1];
        return a * Math.Log(1 + Math.Pow(t, b));
    }
    
    public double CalculateRate(double t, double[] p)
    {
        double a = p[0], b = p[1];
        double tb = Math.Pow(t, b);
        return (a * b * Math.Pow(t, b - 1)) / (1 + tb);
    }
    
    public double[] GetInitialParameters(double[] tData, double[] effortData)
    {
        double maxEffort = effortData.Max();
        int n = tData.Length;
        return new[] { maxEffort / Math.Log(1 + n), 1.0 };
    }
    
    public (double[] lower, double[] upper) GetBounds(double[] tData, double[] effortData)
    {
        double maxEffort = effortData.Max();
        return (
            new[] { 1.0, 0.5 },
            new[] { maxEffort * 2, 3.0 }
        );
    }
}

/// <summary>
/// TEFファクトリ
/// </summary>
public static class TEFFactory
{
    public static IEnumerable<ITestEffortFunction> GetAllTEFs()
    {
        yield return new ExponentialTEF();
        yield return new RayleighTEF();
        yield return new WeibullTEF();
        yield return new LogisticTEF();
        yield return new LogPowerTEF();
    }
}
