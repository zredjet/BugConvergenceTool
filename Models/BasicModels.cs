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
/// ゴンペルツモデル（切断型）
/// m(t) = a·(e^(-b·e^(-ct)) - e^(-b)) / (1 - e^(-b))
/// </summary>
/// <remarks>
/// <para>
/// ゴンペルツ曲線 G(t) = e^(-b·e^(-ct)) は G(0) = e^(-b) ≠ 0 のため、そのまま m(t) = a·G(t) とすると
/// 観測開始前に a·e^(-b) 件が発見済みという扱いになる。NHPP の尤度は増分 m(tᵢ) - m(tᵢ₋₁) で決まるため、
/// この観測されない質量が a に含まれ、推定総バグ数が過大になる。
/// </para>
/// <para>
/// そこで G を t ≥ 0 に切断して正規化した m(t) = a·(G(t) - G(0)) / (1 - G(0)) を用いる。
/// m(0) = 0、m(∞) = a となり、a がそのまま潜在バグ総数を表す。
/// ゴンペルツ型 SRGM を NHPP として扱う場合の標準的な形であり、b が大きいと通常のゴンペルツ曲線に近づく。
/// </para>
/// </remarks>
public class GompertzModel : ReliabilityGrowthModelBase
{
    public override string Name => "ゴンペルツ（切断型）";
    public override string Category => "基本";
    public override string Formula => "m(t) = a(e^(-b·e^(-ct)) - e^(-b)) / (1 - e^(-b))";
    public override string Description => "非対称S字。終盤の収束が急。m(0)=0 に切断・正規化したゴンペルツ曲線";
    public override string[] ParameterNames => new[] { "a", "b", "c" };

    public override double Calculate(double t, double[] parameters)
    {
        double a = parameters[0];
        double b = parameters[1];
        double c = parameters[2];
        double g0 = Math.Exp(-b);
        return a * (Math.Exp(-b * Math.Exp(-c * t)) - g0) / (1 - g0);
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
            new[] { maxY * 5, 20.0, 1.0 }
        );
    }
}

/// <summary>
/// 一般化 Goel-Okumoto モデル（Weibull型）
/// m(t) = a(1 - e^(-b·t^c))
/// </summary>
/// <remarks>
/// <para>
/// Goel (1985) による Goel-Okumoto モデルの一般化。欠陥検出率が Weibull 型で、
/// 形状パラメータ c により多様な成長曲線を表現できる。
/// </para>
/// <para>
/// 特徴:
/// - c = 1: 指数型（Goel-Okumoto）と同等
/// - c &gt; 1: S字型（初期に遅く、中盤に加速）
/// - c &lt; 1: 凸型（初期に急速、後半に減速）
/// </para>
/// <para>
/// 参照: Goel, A.L. (1985). "Software Reliability Models: Assumptions, Limitations, and Applicability."
/// IEEE Transactions on Software Engineering, SE-11(12), 1411-1423.
/// （以前は Ohba 型と表記していたが、この式は Goel (1985) のもの）
/// </para>
/// </remarks>
public class GeneralizedGoelOkumotoModel : ReliabilityGrowthModelBase
{
    public override string Name => "Goel一般化（Weibull型）";
    public override string Category => "基本";
    public override string Formula => "m(t) = a(1 - e^(-b·t^c))";
    public override string Description => "Goel (1985) の一般化GO。c>1でS字、c=1で指数型、c<1で凸型";
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
/// 変曲S字型モデル（Ohba 1984）
/// m(t) = a(1 - e^(-bt)) / (1 + ψ·e^(-bt))
/// </summary>
/// <remarks>
/// <para>
/// Ohba (1984) の変曲S字型（inflection S-shaped）NHPP モデル。
/// ψ は変曲の度合いを表す形状パラメータ（ψ = (1-r)/r、r は検出可能な欠陥の割合）で、
/// ψ = 0 で指数型（Goel-Okumoto）に一致し、ψ が大きいほど立ち上がりの遅い S 字になる。
/// </para>
/// <para>
/// ロジスティック曲線 L(t) = 1/(1+e^(-b(t-c))) を t ≥ 0 に切断・正規化した (L(t)-L(0))/(1-L(0)) は、
/// ψ = e^(bc) とおくとこの式と恒等的に等しい。つまり変曲S字型は m(0)=0 のロジスティック曲線であり、
/// 変曲点は t* = ln(ψ)/b（ψ &gt; 1 のとき）。そのため別途ロジスティックモデルは設けない。
/// </para>
/// <para>
/// m(∞) = a（ψ に依存しない）。
/// 以前は「Pham型不完全デバッグ指数」と表記し ψ を「不完全デバッグ率 p」と解釈していたが、
/// この式は新規バグの混入を表すものではない。
/// </para>
/// <para>
/// 参照: Ohba, M. (1984). "Inflection S-shaped software reliability growth model."
/// Stochastic Models in Reliability Theory, Lecture Notes in Economics and Mathematical Systems 235, 144-162.
/// </para>
/// </remarks>
public class InflectionSModel : ReliabilityGrowthModelBase
{
    public override string Name => "変曲S字型（Ohba）";
    public override string Category => "基本";
    public override string Formula => "m(t) = a(1-e^(-bt)) / (1+ψ·e^(-bt))";
    public override string Description => "Ohba (1984) の変曲S字型。ψ=0 で指数型、ψ が大きいほど立ち上がりが遅い";
    public override string[] ParameterNames => new[] { "a", "b", "ψ" };

    public override double Calculate(double t, double[] parameters)
    {
        double a = parameters[0];
        double b = parameters[1];
        double psi = parameters[2];
        
        double expBt = Math.Exp(-b * t);
        return a * (1 - expBt) / (1 + psi * expBt);
    }

    public override double[] GetInitialParameters(double[] tData, double[] yData)
    {
        double maxY = yData.Max();
        int n = tData.Length;

        double last = yData[^1];
        double prev = n > 1 ? yData[^2] : yData[^1];
        double increment = last - prev;
        bool isConverged = increment <= GetConvergenceThreshold();
        double a0 = maxY * GetScaleFactorAInRange(isConverged, 0.3);

        // b: 設定から指数型の値を取得
        double avgSlope = EstimateAverageSlope(yData);
        double b0 = GetBValueExponential(avgSlope);

        // ψ: 中程度の S 字から開始
        double psi0 = 1.0;

        return new[] { a0, b0, psi0 };
    }

    public override (double[] lower, double[] upper) GetBounds(double[] tData, double[] yData)
    {
        double maxY = yData.Max();
        // ψ ≥ 0（ψ = 0 で指数型）。変曲点 t* = ln(ψ)/b なので、上限 1000 は t* ≈ 6.9/b に相当し
        // 切断ロジスティックとして表せる範囲（変曲点が観測期間の後半〜期間外）も含む
        return (
            new[] { maxY, 0.001, 0.0 },
            new[] { maxY * 5, 1.0, 1000.0 }
        );
    }
}
