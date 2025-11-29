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
/// 修正ゴンペルツモデル（ロジスティック型ゴンペルツ）
/// m(t) = a / (1 + b * exp(-c*t))
/// </summary>
/// <remarks>
/// <para>
/// 標準的な修正ゴンペルツモデル（ロジスティック・ゴンペルツ型）。
/// ソフトウェア信頼度成長モデルとして広く使用される形式です。
/// </para>
/// <para>
/// 特徴:
/// - t=0 で m(0) = a / (1 + b)
/// - t→∞ で m(t) → a（漸近値）
/// - b が大きいほど初期値が小さくなる（遅延効果）
/// - c は収束速度を制御
/// </para>
/// <para>
/// 参照: Ohba, M. (1984). Software Reliability Analysis Models.
/// </para>
/// </remarks>
public class ModifiedGompertzModel : ReliabilityGrowthModelBase
{
    public override string Name => "修正ゴンペルツ";
    public override string Category => "基本";
    public override string Formula => "m(t) = a / (1 + b·e^(-ct))";
    public override string Description => "ロジスティック型ゴンペルツ。初期遅延と収束を柔軟に表現";
    public override string[] ParameterNames => new[] { "a", "b", "c" };

    public override double Calculate(double t, double[] parameters)
    {
        double a = parameters[0];
        double b = parameters[1];
        double c = parameters[2];
        return a / (1 + b * Math.Exp(-c * t));
    }
    
    /// <summary>
    /// 漸近的総欠陥数: t→∞ で m(t) → a
    /// </summary>
    public override double GetAsymptoticTotalBugs(double[] parameters)
    {
        return parameters[0];  // a
    }

    public override double[] GetInitialParameters(double[] tData, double[] yData)
    {
        double maxY = yData.Max();
        int n = tData.Length;

        // a: 漸近値（総欠陥数）
        double last = yData[^1];
        double prev = n > 1 ? yData[^2] : yData[^1];
        double increment = last - prev;
        bool isConverged = increment <= GetConvergenceThreshold();
        double a0 = maxY * GetScaleFactorAInRange(isConverged, 0.3);

        // b: 初期遅延係数。t=0でm(0)=a/(1+b)なので、初期値が小さいほどbは大きい
        // 累積50%到達日が遅いほど初期遅延が大きいと判断
        double day50 = FindDayForCumulativeRatio(yData, 0.5);
        double nDouble = Math.Max(1.0, tData.Length);
        double ratio = Math.Clamp(day50 / nDouble, 0.2, 0.8);
        // ratioが大きい（初期の立ち上がりが遅い）ほどbを大きく
        double b0 = 1.0 + (ratio - 0.2) / (0.8 - 0.2) * 9.0;  // 1〜10の範囲

        // c: 収束速度。平均増分から推定
        double avgSlope = EstimateAverageSlope(yData);
        double c0 = avgSlope switch
        {
            <= 0.1 => 0.05,
            <= 0.5 => 0.1,
            <= 1.0 => 0.2,
            _ => 0.3
        };

        return new[] { a0, b0, c0 };
    }

    public override (double[] lower, double[] upper) GetBounds(double[] tData, double[] yData)
    {
        double maxY = yData.Max();
        return (
            new[] { maxY, 0.1, 0.001 },      // a >= maxY, b >= 0.1, c >= 0.001
            new[] { maxY * 5, 50.0, 1.0 }    // a <= 5*maxY, b <= 50, c <= 1.0
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
