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
/// Shifted Gompertz SRGM（シフトゴンペルツモデル）
/// m(t) = a(e^(-b·e^(-ct)) - e^(-b))
/// </summary>
/// <remarks>
/// <para>
/// 標準ゴンペルツモデルの m(0) ≠ 0 問題を解決した変形版。
/// t=0 で m(0)=0 を保証し、SRGM の境界条件を満たします。
/// </para>
/// <para>
/// <strong>学術的注記:</strong>
/// このモデルは「Shifted Gompertz」として知られる形式の SRGM への適用です。
/// 標準的なゴンペルツSRGM文献とは異なる独自形式であることに注意してください。
/// 統計学分野のShifted Gompertz分布との関連性があります。
/// </para>
/// <para>
/// 特徴:
/// - m(0) = a(e^(-b) - e^(-b)) = 0（境界条件を満足）
/// - m(∞) = a(1 - e^(-b))（b が大きいほど a に近づく）
/// - 非対称S字型成長（初期は緩やか、後半で急速に飽和）
/// </para>
/// <para>
/// <strong>漸近値に関する重要な注意:</strong>
/// 漸近値は a ではなく a(1-e^(-b)) です。
/// b=3 で約95%、b=5 で約99%が a に到達します。
/// パラメータ a の解釈時にはこの点を考慮してください。
/// </para>
/// <para>
/// 参考文献:
/// - Bemmaor, A.C. (1994). "Modeling the Diffusion of New Durable Goods: Word-of-Mouth Effect Versus Consumer Heterogeneity"
/// - 統計学における Shifted Gompertz 分布の SRGM への応用
/// </para>
/// </remarks>
public class ModifiedGompertzModel : ReliabilityGrowthModelBase
{
    public override string Name => "Shiftedゴンペルツ";
    public override string Category => "基本";
    public override string Formula => "m(t) = a(e^(-b·e^(-ct)) - e^(-b))";
    public override string Description => "シフトゴンペルツ型。m(0)=0保証、漸近値はa(1-e^(-b))";
    public override string[] ParameterNames => new[] { "a", "b", "c" };

    public override double Calculate(double t, double[] parameters)
    {
        double a = parameters[0];
        double b = parameters[1];
        double c = parameters[2];
        double expNegB = Math.Exp(-b);
        return a * (Math.Exp(-b * Math.Exp(-c * t)) - expNegB);
    }
    
    /// <summary>
    /// 漸近的総欠陥数: t→∞ で m(t) → a(1 - e^(-b))
    /// </summary>
    /// <remarks>
    /// 注意: 漸近値は a ではなく a(1-e^(-b)) です。
    /// b が大きいほど a に近づきます（b=3で約95%、b=5で約99%）。
    /// </remarks>
    public override double GetAsymptoticTotalBugs(double[] parameters)
    {
        double a = parameters[0];
        double b = parameters[1];
        return a * (1 - Math.Exp(-b));
    }

    public override double[] GetInitialParameters(double[] tData, double[] yData)
    {
        double maxY = yData.Max();
        int n = tData.Length;

        // a: 漸近値は a(1-e^(-b)) なので、初期 a は maxY より大きめに設定
        double last = yData[^1];
        double prev = n > 1 ? yData[^2] : yData[^1];
        double increment = last - prev;
        bool isConverged = increment <= GetConvergenceThreshold();
        // 漸近値が a(1-e^(-b)) ≈ 0.95a (b=3の場合) なので、少し大きめに
        double a0 = maxY * GetScaleFactorAInRange(isConverged, 0.4);

        // b: 初期遅延係数（大きいほど漸近値が a に近づく）
        // b=3 で 95%、b=5 で 99% なので、3〜5 程度を初期値に
        double b0 = GetGompertzB0();

        // c: 成長率。累積比率から推定
        double day50 = FindDayForCumulativeRatio(yData, GetChangePointRatio());
        double c0 = 1.0 / Math.Max(1.0, day50);

        return new[] { a0, b0, c0 };
    }

    public override (double[] lower, double[] upper) GetBounds(double[] tData, double[] yData)
    {
        double maxY = yData.Max();
        return (
            new[] { maxY, 0.5, 0.001 },      // b >= 0.5 で漸近値が a の約40%以上
            new[] { maxY * 6, 10.0, 1.0 }    // a の上限を少し高めに（漸近値補正のため）
        );
    }
}

/// <summary>
/// Ohbaモデル（一般化指数型 / Weibull型SRGM）
/// m(t) = a(1 - e^(-b·t^c))
/// </summary>
/// <remarks>
/// <para>
/// Ohba (1984) による一般化指数型NHPPモデル。
/// Weibull分布に基づく欠陥検出率を持ち、形状パラメータ c により
/// 多様な成長曲線を表現できる汎用的なモデルです。
/// </para>
/// <para>
/// 特徴:
/// - c = 1: 指数型（Goel-Okumoto）と同等
/// - c &gt; 1: S字型（初期に遅く、中盤に加速）
/// - c &lt; 1: 凸型（初期に急速、後半に減速）
/// </para>
/// <para>
/// 参照: Ohba, M. (1984). "Software Reliability Analysis Models." 
/// IBM Journal of Research and Development, 28(4), 428-443.
/// </para>
/// </remarks>
public class OhbaWeibullModel : ReliabilityGrowthModelBase
{
    public override string Name => "Ohba型（Weibull）";
    public override string Category => "基本";
    public override string Formula => "m(t) = a(1 - e^(-b·t^c))";
    public override string Description => "一般化指数型。c>1でS字、c=1で指数型、c<1で凸型";
    public override string[] ParameterNames => new[] { "a", "b", "c" };

    public override double Calculate(double t, double[] parameters)
    {
        double a = parameters[0];
        double b = parameters[1];
        double c = parameters[2];
        
        // t^c の計算（t=0 の場合は 0）
        double tc = t > 0 ? Math.Pow(t, c) : 0;
        return a * (1 - Math.Exp(-b * tc));
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

        // c: 形状パラメータ。累積曲線の形状から推定
        // 初期の立ち上がりが遅い場合は c > 1（S字型）
        double day50 = FindDayForCumulativeRatio(yData, 0.5);
        double nDouble = Math.Max(1.0, tData.Length);
        double ratio = day50 / nDouble;
        // ratio > 0.5 なら S字型傾向、ratio < 0.5 なら凸型傾向
        double c0 = ratio > 0.5 ? 1.0 + (ratio - 0.5) * 2.0 : 0.5 + ratio;
        c0 = Math.Clamp(c0, 0.5, 2.0);

        // b: スケールパラメータ。平均増分から推定
        double avgSlope = EstimateAverageSlope(yData);
        double b0 = avgSlope switch
        {
            <= 0.1 => 0.01,
            <= 0.5 => 0.05,
            <= 1.0 => 0.1,
            _ => 0.2
        };

        return new[] { a0, b0, c0 };
    }

    public override (double[] lower, double[] upper) GetBounds(double[] tData, double[] yData)
    {
        double maxY = yData.Max();
        return (
            new[] { maxY, 0.0001, 0.3 },     // a >= maxY, b > 0, c >= 0.3
            new[] { maxY * 5, 1.0, 3.0 }     // a <= 5*maxY, b <= 1.0, c <= 3.0
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
