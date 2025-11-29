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
        int n = tData.Length;

        // a: 設定から収束しきい値とスケール係数を取得
        double last = yData[^1];
        double prev = n > 1 ? yData[^2] : yData[^1];
        double increment = last - prev;
        bool isConverged = increment <= GetConvergenceThreshold();
        double a0 = maxY * GetScaleFactorAInRange(isConverged, 0.0);  // 指数型は低めのスケール

        // b: 設定から取得
        double avgSlope = EstimateAverageSlope(yData);
        double b0 = GetBValueExponential(avgSlope);

        return new[] { a0, b0 };
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
        int n = tData.Length;

        // a: 設定から収束しきい値とスケール係数を取得
        double last = yData[^1];
        double prev = n > 1 ? yData[^2] : yData[^1];
        double increment = last - prev;
        bool isConverged = increment <= GetConvergenceThreshold();
        double a0 = maxY * GetScaleFactorAInRange(isConverged, 0.3);  // S字型は中程度のスケール

        // b: S字型用の設定から取得
        double avgSlope = EstimateAverageSlope(yData);
        double b0 = GetBValueSCurve(avgSlope);

        return new[] { a0, b0 };
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
        int n = tData.Length;

        // a: 設定から収束しきい値とスケール係数を取得
        double last = yData[^1];
        double prev = n > 1 ? yData[^2] : yData[^1];
        double increment = last - prev;
        bool isConverged = increment <= GetConvergenceThreshold();
        double a0 = maxY * GetScaleFactorAInRange(isConverged, 0.3);  // S字型は中程度のスケール

        // c: 変曲点を累積比率から推定（設定から比率を取得）
        double day50 = FindDayForCumulativeRatio(yData, GetChangePointRatio());
        double c0 = 1.0 / Math.Max(1.0, day50);

        // b: ゴンペルツの初期遅延係数（設定から取得）
        double b0 = GetGompertzB0();

        return new[] { a0, b0, c0 };
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
    
    /// <summary>
    /// 漸近的総欠陥数: t→∞ で m(t) → a*c
    /// </summary>
    public override double GetAsymptoticTotalBugs(double[] parameters)
    {
        double a = parameters[0];
        double c = parameters[2];
        return a * c;
    }

    public override double[] GetInitialParameters(double[] tData, double[] yData)
    {
        double maxY = yData.Max();
        int n = tData.Length;

        // a: 修正ゴンペルツでは a*c が漸近値なので a はやや控えめに
        double last = yData[^1];
        double prev = n > 1 ? yData[^2] : yData[^1];
        double increment = last - prev;
        bool isConverged = increment <= GetConvergenceThreshold();
        // 修正ゴンペルツはスケールを低めに（a*cが漸近値のため）
        double a0 = isConverged ? maxY * 0.9 : maxY * 1.2;

        // c: 1.0〜2.0 の範囲で、累積50%到達タイミングが遅いほど大きめに
        double day50 = FindDayForCumulativeRatio(yData, 0.5);
        double nDouble = Math.Max(1.0, tData.Length);
        double ratio = Math.Clamp(day50 / nDouble, 0.2, 0.9);
        double c0 = 1.0 + (ratio - 0.2) / (0.9 - 0.2) * (2.0 - 1.0);

        // b: 設定から取得
        double avgSlope = EstimateAverageSlope(yData);
        double b0 = GetBValueExponential(avgSlope);

        return new[] { a0, b0, c0 };
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

        // a: 設定から収束しきい値とスケール係数を取得
        double last = yData[^1];
        double prev = n > 1 ? yData[^2] : yData[^1];
        double increment = last - prev;
        bool isConverged = increment <= GetConvergenceThreshold();
        double a0 = maxY * GetScaleFactorAInRange(isConverged, 0.0);  // ロジスティックは低めのスケール

        // c: 累積比率到達日を変曲点候補に（設定から比率を取得）
        double day50 = FindDayForCumulativeRatio(yData, GetChangePointRatio());
        double c0 = day50;

        // b: 立ち上がりの鋭さ。平均増分で調整
        double avgSlope = EstimateAverageSlope(yData);
        double b0 = avgSlope switch
        {
            <= 0.1 => 0.1,
            <= 0.5 => 0.3,
            <= 1.0 => 0.6,
            _ => 1.0
        };

        return new[] { a0, b0, c0 };
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
