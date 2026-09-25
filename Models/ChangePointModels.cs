namespace BugConvergenceTool.Models;

/// <summary>
/// 変化点モデルの基底クラス
/// </summary>
/// <remarks>
/// <para>
/// 変化点モデルは「共通の潜在バグ総数 a」と「実効テスト時間 u(t)」で表す標準形を用いる。
/// 変化点 τ で欠陥検出率が b₁ から b₂ に変わるとき、u(t) = b₁t（t ≤ τ）、b₁τ + b₂(t-τ)（t &gt; τ）。
/// </para>
/// <para>
/// 以前の実装は変化点で「新しいバグ集団 a₂」を立ち上げる形だったため、
/// m(∞) = m₁(τ) + a₂ となって変化点前の未検出バグが消え、遅延S字型では変化点直後に検出強度が 0 に落ちていた。
/// </para>
/// <para>
/// τ が1つのモデルは τ をパラメータ列の最後に置く（<see cref="FixedTauChangePointModel"/> が前提とする）。
/// </para>
/// <para>
/// 参考: Zhao, M. (1993). "Change-point problems in software and hardware reliability."
/// Communications in Statistics - Theory and Methods, 22(3), 757-768.
/// Huang, C.-Y. (2005). "Performance analysis of software reliability growth models with testing-effort and change-point."
/// Journal of Systems and Software, 76(2), 181-194.
/// </para>
/// </remarks>
public abstract class ChangePointModelBase : ReliabilityGrowthModelBase
{
    public override string Category => "変化点";
    
    /// <summary>
    /// 変化点なしの対応モデル（尤度比検定の帰無モデル）。b₁ = b₂ のときこのモデルに一致する
    /// </summary>
    public abstract ReliabilityGrowthModelBase CreateNullModel();
    
    /// <summary>
    /// 実効テスト時間 u(t) = b₁t（t ≤ τ）、b₁τ + b₂(t-τ)（t &gt; τ）
    /// </summary>
    protected static double EffectiveTime(double t, double b1, double b2, double tau)
    {
        return t <= tau ? b1 * t : b1 * tau + b2 * (t - tau);
    }
    
    /// <summary>
    /// 変化点 τ の初期値（累積比率の到達日。設定から比率を取得）
    /// </summary>
    protected static double InitialChangePoint(double[] cumulative)
    {
        return FindDayForCumulativeRatio(cumulative, GetChangePointRatio());
    }
}

/// <summary>
/// 指数型 + 変化点モデル
/// m(t) = a(1 - e^(-u(t)))
/// </summary>
public class ExponentialChangePointModel : ChangePointModelBase
{
    public override ReliabilityGrowthModelBase CreateNullModel() => new ExponentialModel();
    
    public override string Name => "指数型+変化点";
    public override string Formula => "m(t) = a(1-e^(-u(t))), u(t)=b₁t [t≤τ], b₁τ+b₂(t-τ) [t>τ]";
    public override string Description => "指数型の検出率が変化点 τ で b₁ から b₂ に変わる";
    public override string[] ParameterNames => new[] { "a", "b₁", "b₂", "τ" };
    
    public override double Calculate(double t, double[] p)
    {
        double a = p[0], b1 = p[1], b2 = p[2], tau = p[3];
        return a * (1 - Math.Exp(-EffectiveTime(t, b1, b2, tau)));
    }
    
    public override double[] GetInitialParameters(double[] tData, double[] yData)
    {
        double maxY = yData.Max();
        int n = tData.Length;
        
        double last = yData[^1];
        double prev = n > 1 ? yData[^2] : yData[^1];
        bool isConverged = last - prev <= GetConvergenceThreshold();
        double a0 = maxY * GetScaleFactorAInRange(isConverged, 0.3);
        
        double b0 = GetBValueExponential(EstimateAverageSlope(yData));
        
        return new[] { a0, b0, b0, InitialChangePoint(yData) };
    }
    
    public override (double[] lower, double[] upper) GetBounds(double[] tData, double[] yData)
    {
        double maxY = yData.Max();
        int n = tData.Length;
        
        return (
            new[] { maxY, 0.001, 0.001, 2.0 },
            new[] { maxY * 5, 1.0, 1.0, n - 2.0 }
        );
    }
}

/// <summary>
/// 遅延S字型 + 変化点モデル
/// m(t) = a[1 - (1+b₁t)e^(-b₁t)]（t ≤ τ）
/// m(t) = a[1 - (1+b₁τ)/(1+b₂τ)·(1+b₂t)·e^(-b₁τ-b₂(t-τ))]（t &gt; τ）
/// </summary>
/// <remarks>
/// 遅延S字型の欠陥検出率（ハザード）h(t) = b²t/(1+bt) の b が τ で b₁ から b₂ に変わるとして導いた式。
/// τ で m(t) は連続だが、検出強度は a·S(τ)·h₁(τ) から a·S(τ)·h₂(τ) へ跳ぶ（b₁ = b₂ のときだけ連続）。
/// 変化点としてはこれが正しい挙動である（以前の実装は変化点直後に検出強度が 0 に落ちていた）。
/// </remarks>
public class DelayedSChangePointModel : ChangePointModelBase
{
    public override ReliabilityGrowthModelBase CreateNullModel() => new DelayedSModel();
    
    public override string Name => "遅延S字型+変化点";
    public override string Formula => "m(t) = a[1-(1+b₁t)e^(-b₁t)] [t≤τ], a[1-(1+b₁τ)/(1+b₂τ)(1+b₂t)e^(-b₁τ-b₂(t-τ))] [t>τ]";
    public override string Description => "遅延S字型の検出率が変化点 τ で b₁ から b₂ に変わる";
    public override string[] ParameterNames => new[] { "a", "b₁", "b₂", "τ" };
    
    public override double Calculate(double t, double[] p)
    {
        double a = p[0], b1 = p[1], b2 = p[2], tau = p[3];
        
        if (t <= tau)
        {
            return a * (1 - (1 + b1 * t) * Math.Exp(-b1 * t));
        }
        
        double survival = (1 + b1 * tau) / (1 + b2 * tau) * (1 + b2 * t) * Math.Exp(-b1 * tau - b2 * (t - tau));
        return a * (1 - survival);
    }
    
    public override double[] GetInitialParameters(double[] tData, double[] yData)
    {
        double maxY = yData.Max();
        int n = tData.Length;
        
        double last = yData[^1];
        double prev = n > 1 ? yData[^2] : yData[^1];
        bool isConverged = last - prev <= GetConvergenceThreshold();
        double a0 = maxY * GetScaleFactorAInRange(isConverged, 0.3);
        
        double b0 = GetBValueSCurve(EstimateAverageSlope(yData));
        
        return new[] { a0, b0, b0, InitialChangePoint(yData) };
    }
    
    public override (double[] lower, double[] upper) GetBounds(double[] tData, double[] yData)
    {
        double maxY = yData.Max();
        int n = tData.Length;
        
        return (
            new[] { maxY, 0.001, 0.001, 2.0 },
            new[] { maxY * 5, 2.0, 2.0, n - 2.0 }
        );
    }
}

/// <summary>
/// 変曲S字型 + 変化点モデル
/// m(t) = a(1 - e^(-u(t))) / (1 + ψ·e^(-u(t)))、ψ = e^(lnψ)
/// </summary>
/// <remarks>
/// Ohba (1984) の変曲S字型に実効時間方式の変化点を導入したもの。m(∞) = a。
/// 以前は「不完全デバッグ+変化点(実効時間)」と表記していたが、ψ は変曲の形状パラメータで
/// 新規バグの混入を表すものではない（<see cref="InflectionSModel"/> 参照）。
/// </remarks>
public class InflectionSChangePointModel : ChangePointModelBase
{
    public override ReliabilityGrowthModelBase CreateNullModel() => new InflectionSModel();
    
    public override string Name => "変曲S字型+変化点";
    public override string Formula => "m(t) = a(1-e^(-u(t)))/(1+ψ·e^(-u(t))), ψ=e^(lnψ), u(t)=b₁t [t≤τ], b₁τ+b₂(t-τ) [t>τ]";
    public override string Description => "変曲S字型（Ohba）の検出率が変化点 τ で b₁ から b₂ に変わる";
    public override string[] ParameterNames => new[] { "a", "b₁", "b₂", "lnψ", "τ" };

    public override double Calculate(double t, double[] p)
    {
        double a = p[0], b1 = p[1], b2 = p[2], logPsi = p[3], tau = p[4];
        double u = EffectiveTime(t, b1, b2, tau);
        return a * (1 - Math.Exp(-u)) / (1 + Math.Exp(logPsi - u));
    }

    public override double[] GetInitialParameters(double[] tData, double[] yData)
    {
        double maxY = yData.Max();
        int n = tData.Length;

        double last = yData[^1];
        double prev = n > 1 ? yData[^2] : yData[^1];
        bool isConverged = last - prev <= GetConvergenceThreshold();
        double a0 = maxY * GetScaleFactorAInRange(isConverged, 0.3);

        double b0 = GetBValueExponential(EstimateAverageSlope(yData));

        return new[] { a0, b0, b0, 0.0, InitialChangePoint(yData) };
    }

    public override (double[] lower, double[] upper) GetBounds(double[] tData, double[] yData)
    {
        double maxY = yData.Max();
        int n = tData.Length;

        return (
            new[] { maxY, 0.001, 0.001, InflectionSModel.LogPsiLower, 2.0 },
            new[] { maxY * 5, 1.0, 1.0, InflectionSModel.LogPsiUpper, n - 2.0 }
        );
    }

    public override bool IsNaturalBound(int index, bool upper, double bound)
        => (index == 3 && !upper) || base.IsNaturalBound(index, upper, bound);

    // 変曲点は変化点前の発見率 b₁ で見た t* = ln ψ / b₁
    public override IEnumerable<(string Name, double Value, string Description)> GetDerivedQuantities(double[] parameters)
        => InflectionSModel.InflectionPoint(parameters[1], parameters[3]);

    public override double[] ToFisherScale(double[] parameters) => InflectionSModel.LogToLinear(parameters, 3);
    public override double[] FromFisherScale(double[] fisherParameters) => InflectionSModel.LinearToLog(fisherParameters, 3);
}

/// <summary>
/// 複数変化点モデル（指数型）
/// m(t) = a(1 - e^(-u(t)))、u(t) は変化点ごとに傾き bᵢ が変わる区分線形の実効時間
/// </summary>
public class MultipleChangePointModel : ChangePointModelBase
{
    private readonly int _numChangePoints;
    
    public MultipleChangePointModel(int numChangePoints = 2)
    {
        _numChangePoints = Math.Min(3, Math.Max(1, numChangePoints));
    }
    
    public override ReliabilityGrowthModelBase CreateNullModel() => new ExponentialModel();
    
    public override string Name => $"複数変化点({_numChangePoints}点)";
    public override string Formula => $"m(t) = a(1-e^(-u(t))), u(t) は {_numChangePoints} 個の変化点で傾きが変わる区分線形";
    public override string Description => $"{_numChangePoints}個の変化点で欠陥検出率が変化";
    
    public override string[] ParameterNames
    {
        get
        {
            var names = new List<string> { "a" };
            for (int i = 1; i <= _numChangePoints + 1; i++)
                names.Add($"b{i}");
            for (int i = 1; i <= _numChangePoints; i++)
                names.Add($"τ{i}");
            return names.ToArray();
        }
    }
    
    public override double Calculate(double t, double[] p)
    {
        int numSegments = _numChangePoints + 1;
        double a = p[0];
        
        var tau = new double[_numChangePoints];
        for (int i = 0; i < _numChangePoints; i++)
            tau[i] = p[1 + numSegments + i];
        Array.Sort(tau);
        
        // 区分線形の実効時間 u(t) = Σ bᵢ × (区間 i 内で経過した時間)
        double u = 0;
        double segStart = 0;
        for (int i = 0; i < numSegments; i++)
        {
            if (t <= segStart) break;
            double segEnd = i < _numChangePoints ? tau[i] : double.PositiveInfinity;
            // 変化点が等しい（長さ 0 の区間）場合も打ち切らず次の区間へ進む
            u += p[1 + i] * (Math.Min(t, segEnd) - segStart);
            segStart = segEnd;
        }
        
        return a * (1 - Math.Exp(-u));
    }
    
    public override double[] GetInitialParameters(double[] tData, double[] yData)
    {
        double maxY = yData.Max();
        int n = tData.Length;
        int numSegments = _numChangePoints + 1;
        
        double last = yData[^1];
        double prev = n > 1 ? yData[^2] : yData[^1];
        bool isConverged = last - prev <= GetConvergenceThreshold();
        
        var initial = new List<double> { maxY * GetScaleFactorAInRange(isConverged, 0.3) };
        
        double b0 = GetBValueExponential(EstimateAverageSlope(yData));
        for (int i = 0; i < numSegments; i++)
            initial.Add(b0);
        
        // 変化点は累積 1/(k+1), 2/(k+1), ... 到達日をそれぞれ候補に
        for (int i = 1; i <= _numChangePoints; i++)
            initial.Add(FindDayForCumulativeRatio(yData, i / (double)numSegments));
        
        return initial.ToArray();
    }
    
    public override (double[] lower, double[] upper) GetBounds(double[] tData, double[] yData)
    {
        double maxY = yData.Max();
        int n = tData.Length;
        int numSegments = _numChangePoints + 1;
        
        var lower = new List<double> { maxY };
        var upper = new List<double> { maxY * 5 };
        
        for (int i = 0; i < numSegments; i++)
        {
            lower.Add(0.001);
            upper.Add(1.0);
        }
        
        for (int i = 1; i <= _numChangePoints; i++)
        {
            lower.Add(2.0);
            upper.Add(n - 2.0);
        }
        
        return (lower.ToArray(), upper.ToArray());
    }
}

/// <summary>
/// 変化点 τ を固定した変化点モデル（プロファイル尤度法用）
/// </summary>
/// <remarks>
/// τ をパラメータ列の最後に持つ変化点モデルを包み、τ を除いたパラメータで推定できるようにする。
/// 式は元のモデルのものをそのまま使うため、元のモデルと食い違うことがない。
/// </remarks>
public sealed class FixedTauChangePointModel : ReliabilityGrowthModelBase
{
    private readonly ChangePointModelBase _baseModel;
    
    /// <summary>固定した変化点 τ</summary>
    public double FixedTau { get; }
    
    /// <summary>元の変化点モデル</summary>
    public ChangePointModelBase BaseModel => _baseModel;
    
    public FixedTauChangePointModel(ChangePointModelBase baseModel, double fixedTau)
    {
        if (!Supports(baseModel))
            throw new ArgumentException($"{baseModel.Name} は τ を最後のパラメータに1つだけ持つ変化点モデルではありません", nameof(baseModel));
        _baseModel = baseModel;
        FixedTau = fixedTau;
    }
    
    /// <summary>
    /// τ を最後のパラメータに1つだけ持つ変化点モデルか
    /// </summary>
    public static bool Supports(ChangePointModelBase model) => model.ParameterNames[^1] == "τ";
    
    public override string Name => $"{_baseModel.Name}(τ={FixedTau:0.##})";
    public override string Category => _baseModel.Category;
    public override string Formula => _baseModel.Formula;
    public override string Description => $"変化点 τ={FixedTau:0.##} で固定した{_baseModel.Name}";
    public override string[] ParameterNames => _baseModel.ParameterNames[..^1];
    
    /// <summary>
    /// τ を付け加えた元のモデルのパラメータ列
    /// </summary>
    public double[] ToFullParameters(double[] parameters) => [.. parameters, FixedTau];
    
    public override double Calculate(double t, double[] parameters)
        => _baseModel.Calculate(t, ToFullParameters(parameters));
    
    public override double GetAsymptoticTotalBugs(double[] parameters)
        => _baseModel.GetAsymptoticTotalBugs(ToFullParameters(parameters));
    
    public override double[] GetInitialParameters(double[] tData, double[] yData)
        => _baseModel.GetInitialParameters(tData, yData)[..^1];
    
    public override (double[] lower, double[] upper) GetBounds(double[] tData, double[] yData)
    {
        var (lower, upper) = _baseModel.GetBounds(tData, yData);
        return (lower[..^1], upper[..^1]);
    }
    
    public override bool IsNaturalBound(int index, bool upper, double bound)
        => _baseModel.IsNaturalBound(index, upper, bound);
    
    public override IEnumerable<(string Name, double Value, string Description)> GetDerivedQuantities(double[] parameters)
        => _baseModel.GetDerivedQuantities(ToFullParameters(parameters));
    
    public override double[] ToFisherScale(double[] parameters)
        => _baseModel.ToFisherScale(ToFullParameters(parameters))[..^1];
    
    public override double[] FromFisherScale(double[] fisherParameters)
        => _baseModel.FromFisherScale([.. fisherParameters, FixedTau])[..^1];
}


/// <summary>
/// 変化点モデルのファクトリ
/// </summary>
public static class ChangePointModelFactory
{
    public static IEnumerable<ReliabilityGrowthModelBase> GetAllChangePointModels()
    {
        yield return new ExponentialChangePointModel();
        yield return new DelayedSChangePointModel();
        yield return new InflectionSChangePointModel();
        yield return new MultipleChangePointModel(2);
    }

    public static IEnumerable<ReliabilityGrowthModelBase> GetBasicChangePointModels()
    {
        yield return new ExponentialChangePointModel();
        yield return new DelayedSChangePointModel();
    }
}
