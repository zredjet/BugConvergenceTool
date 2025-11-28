namespace BugConvergenceTool.Models;

/// <summary>
/// 指数型モデル（Goel-Okumoto）
/// m(t) = a * (1 - exp(-b*t))
/// </summary>
public class ExponentialModel : ReliabilityGrowthModelBase
{
    public override string Name => "指数型（Goel-Okumoto）";
    public override string Category => "基本";
    public override string Formula => "m(t) = a(1 - e^(-bt))";
    public override string Description => "最もシンプル、バグ発見率一定";
    public override string[] ParameterNames => new[] { "a", "b" };

    public override double Calculate(double t, double[] parameters)
    {
        double a = parameters[0];
        double b = parameters[1];
        return a * (1 - Math.Exp(-b * t));
    }

    public override double[] GetInitialParameters(double[] tData, double[] yData)
    {
        double maxY = yData.Max();
        return new[] { maxY * 1.5, 0.1 };
    }

    public override (double[] lower, double[] upper) GetBounds(double[] tData, double[] yData)
    {
        double maxY = yData.Max();
        return (
            new[] { maxY, 0.001 },
            new[] { maxY * 5, 1.0 }
        );
    }
}

/// <summary>
/// 遅延S字型モデル
/// m(t) = a * (1 - (1 + b*t) * exp(-b*t))
/// </summary>
public class DelayedSModel : ReliabilityGrowthModelBase
{
    public override string Name => "遅延S字型";
    public override string Category => "基本";
    public override string Formula => "m(t) = a(1 - (1+bt)e^(-bt))";
    public override string Description => "テスト初期の習熟を考慮";
    public override string[] ParameterNames => new[] { "a", "b" };

    public override double Calculate(double t, double[] parameters)
    {
        double a = parameters[0];
        double b = parameters[1];
        return a * (1 - (1 + b * t) * Math.Exp(-b * t));
    }

    public override double[] GetInitialParameters(double[] tData, double[] yData)
    {
        double maxY = yData.Max();
        return new[] { maxY * 1.5, 0.15 };
    }

    public override (double[] lower, double[] upper) GetBounds(double[] tData, double[] yData)
    {
        double maxY = yData.Max();
        return (
            new[] { maxY, 0.001 },
            new[] { maxY * 5, 1.0 }
        );
    }
}

/// <summary>
/// ゴンペルツモデル
/// m(t) = a * exp(-b * exp(-c*t))
/// </summary>
public class GompertzModel : ReliabilityGrowthModelBase
{
    public override string Name => "ゴンペルツ";
    public override string Category => "基本";
    public override string Formula => "m(t) = a·e^(-b·e^(-ct))";
    public override string Description => "終盤の収束が急";
    public override string[] ParameterNames => new[] { "a", "b", "c" };

    public override double Calculate(double t, double[] parameters)
    {
        double a = parameters[0];
        double b = parameters[1];
        double c = parameters[2];
        return a * Math.Exp(-b * Math.Exp(-c * t));
    }

    public override double[] GetInitialParameters(double[] tData, double[] yData)
    {
        double maxY = yData.Max();
        return new[] { maxY * 1.5, 2.0, 0.15 };
    }

    public override (double[] lower, double[] upper) GetBounds(double[] tData, double[] yData)
    {
        double maxY = yData.Max();
        return (
            new[] { maxY, 0.1, 0.001 },
            new[] { maxY * 5, 10.0, 1.0 }
        );
    }
}

/// <summary>
/// 修正ゴンペルツモデル
/// m(t) = a * (c - exp(-b*t))
/// </summary>
public class ModifiedGompertzModel : ReliabilityGrowthModelBase
{
    public override string Name => "修正ゴンペルツ";
    public override string Category => "基本";
    public override string Formula => "m(t) = a(c - e^(-bt))";
    public override string Description => "S字カーブの柔軟性向上";
    public override string[] ParameterNames => new[] { "a", "b", "c" };

    public override double Calculate(double t, double[] parameters)
    {
        double a = parameters[0];
        double b = parameters[1];
        double c = parameters[2];
        return a * (c - Math.Exp(-b * t));
    }

    public override double[] GetInitialParameters(double[] tData, double[] yData)
    {
        double maxY = yData.Max();
        return new[] { maxY, 0.1, 1.2 };
    }

    public override (double[] lower, double[] upper) GetBounds(double[] tData, double[] yData)
    {
        double maxY = yData.Max();
        return (
            new[] { 1.0, 0.001, 1.01 },
            new[] { maxY * 5, 1.0, 2.0 }
        );
    }
}

/// <summary>
/// ロジスティックモデル
/// m(t) = a / (1 + exp(-b*(t-c)))
/// </summary>
public class LogisticModel : ReliabilityGrowthModelBase
{
    public override string Name => "ロジスティック";
    public override string Category => "基本";
    public override string Formula => "m(t) = a / (1 + e^(-b(t-c)))";
    public override string Description => "対称S字カーブ";
    public override string[] ParameterNames => new[] { "a", "b", "c" };

    public override double Calculate(double t, double[] parameters)
    {
        double a = parameters[0];
        double b = parameters[1];
        double c = parameters[2];
        return a / (1 + Math.Exp(-b * (t - c)));
    }

    public override double[] GetInitialParameters(double[] tData, double[] yData)
    {
        double maxY = yData.Max();
        int n = tData.Length;
        return new[] { maxY * 1.5, 0.3, n / 2.0 };
    }

    public override (double[] lower, double[] upper) GetBounds(double[] tData, double[] yData)
    {
        double maxY = yData.Max();
        int n = tData.Length;
        return (
            new[] { maxY, 0.01, 1.0 },
            new[] { maxY * 5, 2.0, n * 2.0 }
        );
    }
}
