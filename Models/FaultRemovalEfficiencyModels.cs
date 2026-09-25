namespace BugConvergenceTool.Models;

/// <summary>
/// 欠陥除去効率（Fault Removal Efficiency）モデルの基底クラス
/// 検出された欠陥の修正効率を考慮
/// </summary>
public abstract class FaultRemovalEfficiencyModelBase : ReliabilityGrowthModelBase
{
    public override string Category => "欠陥除去効率";
    
    /// <summary>
    /// 累積検出欠陥数 m_d(t) を計算
    /// </summary>
    public abstract double CalculateDetected(double t, double[] parameters);
    
    /// <summary>
    /// 累積修正欠陥数 m_c(t) を計算
    /// </summary>
    public abstract double CalculateCorrected(double t, double[] parameters);
    
    /// <summary>
    /// 残存欠陥数を計算
    /// </summary>
    public virtual double CalculateRemaining(double t, double[] parameters)
    {
        double totalBugs = parameters[0]; // パラメータa
        return totalBugs - CalculateCorrected(t, parameters);
    }
    
    /// <summary>
    /// 欠陥除去効率（FRE）を取得
    /// </summary>
    public abstract double GetFaultRemovalEfficiency(double t, double[] parameters);
    
    /// <summary>
    /// 平均修正遅れ D の探索範囲の上限（観測日数）と初期値
    /// </summary>
    protected static (double lower, double upper) DelayBounds(double[] tData) => (0.0, Math.Max(1.0, tData.Length));
    protected const double InitialDelay = 1.0;

    /// <summary>
    /// K(β, μ, t) = ∫₀ᵗ e^(-βs)·e^(-μ(t-s)) ds = (e^(-βt) - e^(-μt)) / (μ - β)
    /// </summary>
    /// <remarks>
    /// 修正遅れを指数分布 G(u) = 1 - e^(-μu)（平均 D = 1/μ）とした畳み込みの項。
    /// β と μ について対称なので、小さいほうの指数を外に出し、差が小さいときは expm1 で桁落ちを避ける。
    /// </remarks>
    protected static double ExpKernel(double beta, double mu, double t)
    {
        if (t <= 0) return 0;
        double slow = Math.Min(beta, mu), diff = Math.Abs(mu - beta);
        double x = diff * t;
        double ratio = x < 1e-8 ? t * (1 - x / 2) : -double.ExpM1(-x) / diff;
        return Math.Exp(-slow * t) * ratio;
    }

    /// <summary>
    /// 修正遅れの率 μ = 1/D（D ≤ 0 なら遅れなしを表す +∞）
    /// </summary>
    protected static double DelayRate(double meanDelay) => meanDelay > 1e-9 ? 1.0 / meanDelay : double.PositiveInfinity;

    /// <summary>
    /// 基本のCalculateは検出数を返す
    /// </summary>
    public override double Calculate(double t, double[] parameters)
    {
        return CalculateDetected(t, parameters);
    }
}

/// <summary>
/// 定数欠陥除去効率モデル（修正遅れ付き）
/// m_d(t) = a(1-e^(-bt))、m_c(t) = η∫₀ᵗ m_d'(s)·G(t-s) ds、G(u) = 1-e^(-u/D)
/// η: 欠陥除去効率 (0 &lt; η ≤ 1)、D: 平均修正遅れ（日）
/// </summary>
/// <remarks>
/// <para>
/// 発見されたバグのうち割合 η が、発見から平均 D 日（指数分布）遅れて修正されるとする。
/// 閉形式は m_c(t) = η[m_d(t) - ab·K(b, 1/D, t)]（<see cref="FaultRemovalEfficiencyModelBase.ExpKernel"/>）。
/// D = 0 で以前の m_c = η·m_d に一致する。
/// </para>
/// <para>
/// 以前の m_c = η·m_d は発見と同時に修正されることを前提にしており、修正が遅れるデータでは
/// 修正数の立ち上がりの遅れを検出率 b の小ささで説明してしまい、a が過大・b が過小に偏っていた
/// （真値 a=200, b=0.04、40 日のデータで、平均 3 日遅れで a +11%・b -16%、7 日遅れで a +31%・b -36%）。
/// </para>
/// </remarks>
public class ConstantFREModel : FaultRemovalEfficiencyModelBase
{
    public override string Name => "定数FRE";
    public override string Formula => "m_d(t) = a(1-e^(-bt)), m_c(t) = η∫₀ᵗ m_d'(s)(1-e^(-(t-s)/D))ds";
    public override string Description => "一定の欠陥除去効率 η と平均修正遅れ D";
    public override string[] ParameterNames => new[] { "a", "b", "η", "D" };
    public override int DetectionParameterCount => 2;   // a, b
    
    public override double CalculateDetected(double t, double[] p)
    {
        double a = p[0], b = p[1];
        return a * (1 - Math.Exp(-b * t));
    }
    
    public override double CalculateCorrected(double t, double[] p)
    {
        double a = p[0], b = p[1], eta = p[2];
        double mu = DelayRate(p[3]);
        double lagged = double.IsPositiveInfinity(mu) ? 0 : a * b * ExpKernel(b, mu, t);
        return eta * (CalculateDetected(t, p) - lagged);
    }
    
    public override double GetFaultRemovalEfficiency(double t, double[] p)
    {
        return p[2]; // η
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
        double a0 = maxY * GetScaleFactorAInRange(isConverged, 0.0);  // 低めのスケール

        // b: 設定から指数型の値を取得
        double avgSlope = EstimateAverageSlope(yData);
        double b0 = GetBValueExponential(avgSlope);

        // η: 設定から初期欠陥除去効率を取得
        double eta0 = GetEta0();

        return new[] { a0, b0, eta0, InitialDelay };
    }
    
    public override (double[] lower, double[] upper) GetBounds(double[] tData, double[] yData)
    {
        double maxY = yData.Max();
        var (dLower, dUpper) = DelayBounds(tData);
        return (
            new[] { maxY, 0.001, 0.3, dLower },
            new[] { maxY * 5, 1.0, 1.0, dUpper }
        );
    }
}

/// <summary>
/// 学習効果付き欠陥除去効率モデル（修正遅れ付き）
/// η(t) = η∞ - (η∞ - η₀)·e^(-λt)、m_c(t) = ∫₀ᵗ η(s)·m_d'(s)·G(t-s) ds、G(u) = 1-e^(-u/D)
/// 時間とともに効率が向上
/// </summary>
/// <remarks>
/// η(s)·m_d'(s) = ab[η∞e^(-bs) - (η∞-η₀)e^(-(b+λ)s)] は指数関数の和なので、m_c は閉形式で書ける。
/// 以前は数値積分（Simpson 法）で m_c = ∫η(s)m_d'(s)ds（修正遅れなし）を求めていた。D = 0 でそれに一致する。
/// </remarks>
public class LearningFREModel : FaultRemovalEfficiencyModelBase
{
    public override string Name => "学習FRE";
    public override string Formula => "η(t) = η∞ - (η∞-η₀)e^(-λt), m_c(t) = ∫₀ᵗ η(s)m_d'(s)(1-e^(-(t-s)/D))ds";
    public override string Description => "学習効果で効率向上（平均修正遅れ D 付き）";
    public override string[] ParameterNames => new[] { "a", "b", "η₀", "η∞", "λ", "D" };
    public override int DetectionParameterCount => 2;   // a, b
    
    public override double CalculateDetected(double t, double[] p)
    {
        double a = p[0], b = p[1];
        return a * (1 - Math.Exp(-b * t));
    }
    
    public override double CalculateCorrected(double t, double[] p)
    {
        double a = p[0], b = p[1];
        double eta0 = p[2], etaInf = p[3], lambda = p[4];
        double mu = DelayRate(p[5]);
        double bl = b + lambda;
        
        // ∫₀ᵗ c·e^(-βs)·(1 - e^(-μ(t-s))) ds = c[(1-e^(-βt))/β - K(β, μ, t)]
        double Term(double beta) =>
            -double.ExpM1(-beta * t) / beta - (double.IsPositiveInfinity(mu) ? 0 : ExpKernel(beta, mu, t));
        
        return a * b * (etaInf * Term(b) - (etaInf - eta0) * Term(bl));
    }
    
    public override double GetFaultRemovalEfficiency(double t, double[] p)
    {
        double eta0 = p[2], etaInf = p[3], lambda = p[4];
        return etaInf - (etaInf - eta0) * Math.Exp(-lambda * t);
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
        double a0 = maxY * GetScaleFactorAInRange(isConverged, 0.0);  // 低めのスケール

        // b: 設定から指数型の値を取得
        double avgSlope = EstimateAverageSlope(yData);
        double b0 = GetBValueExponential(avgSlope);

        // η関連: 設定から取得
        double eta0 = GetEta0() - 0.3;  // 初期はやや低め
        double etaInf0 = GetEtaInfinity();
        double lambda0 = 0.1;

        return new[] { a0, b0, eta0, etaInf0, lambda0, InitialDelay };
    }
    
    public override (double[] lower, double[] upper) GetBounds(double[] tData, double[] yData)
    {
        double maxY = yData.Max();
        var (dLower, dUpper) = DelayBounds(tData);
        return (
            new[] { maxY, 0.001, 0.1, 0.7, 0.01, dLower },
            new[] { maxY * 5, 1.0, 0.9, 1.0, 1.0, dUpper }
        );
    }
}

/// <summary>
/// ロジスティック型FRF（欠陥削減係数）モデル
/// FRF(t) = 1 / (1 + β·e^(-γt))
/// </summary>
public class LogisticFRFModel : FaultRemovalEfficiencyModelBase
{
    public override string Name => "ロジスティックFRF";
    public override string Formula => "FRF(t) = 1/(1+β·e^(-γt))";
    public override string Description => "S字型の欠陥削減係数";
    public override string[] ParameterNames => new[] { "a", "b", "β", "γ" };
    public override int DetectionParameterCount => 2;   // a, b
    
    public override double CalculateDetected(double t, double[] p)
    {
        double a = p[0], b = p[1];
        return a * (1 - Math.Exp(-b * t));
    }
    
    public override double CalculateCorrected(double t, double[] p)
    {
        double beta = p[2], gamma = p[3];
        double detected = CalculateDetected(t, p);
        double frf = 1.0 / (1 + beta * Math.Exp(-gamma * t));
        return frf * detected;
    }
    
    public override double GetFaultRemovalEfficiency(double t, double[] p)
    {
        double beta = p[2], gamma = p[3];
        return 1.0 / (1 + beta * Math.Exp(-gamma * t));
    }
    
    public override double[] GetInitialParameters(double[] tData, double[] yData)
    {
        double maxY = yData.Max();
        return new[] { maxY * 1.5, 0.1, 5.0, 0.2 };
    }
    
    public override (double[] lower, double[] upper) GetBounds(double[] tData, double[] yData)
    {
        double maxY = yData.Max();
        return (
            new[] { maxY, 0.001, 0.5, 0.01 },
            new[] { maxY * 5, 1.0, 50.0, 2.0 }
        );
    }
}

/// <summary>
/// FRE + 変化点モデル（修正遅れ付き）
/// m_d(t) = a(1 - e^(-u(t)))、u(t) = b₁t（t ≤ τ）、b₁τ + b₂(t-τ)（t &gt; τ）
/// m_c(t) = η∫₀ᵗ m_d'(s)·G(t-s) ds、G(u) = 1-e^(-u/D)
/// </summary>
/// <remarks>
/// <para>
/// m_d' は区間ごとに指数関数なので、m_c = η[m_d(t) - L(t)] の L(t) = ∫₀ᵗ m_d'(s)e^(-μ(t-s))ds（μ = 1/D）は
/// t ≤ τ で a·b₁·K(b₁, μ, t)、t &gt; τ で e^(-μ(t-τ))·L(τ) + a·b₂·e^(-b₁τ)·K(b₂, μ, t-τ)。D = 0 で m_c = η·m_d。
/// </para>
/// <para>
/// 以前の「統合FRE」（FRE + エラー生成 + 変化点）からバグ混入率 α を除いたもの。
/// 定数 α のエラー生成モデルの解 a/(1-α)·(1-e^(-b(1-α)t)) は A(1-e^(-Bt))（A=a/(1-α), B=b(1-α)）と恒等的に等しく、
/// 発見数・修正数のデータから α を推定できない（識別不能）ため。
/// </para>
/// </remarks>
public class FREChangePointModel : FaultRemovalEfficiencyModelBase
{
    public override string Name => "FRE+変化点";
    public override string Formula => "m_d = a(1-e^(-u(t))), u(t)=b₁t [t≤τ], b₁τ+b₂(t-τ) [t>τ]; m_c = η∫₀ᵗ m_d'(s)(1-e^(-(t-s)/D))ds";
    public override string Description => "欠陥除去効率 η・平均修正遅れ D と検出率の変化点を組み合わせたモデル";
    public override string[] ParameterNames => new[] { "a", "b₁", "b₂", "η", "D", "τ" };
    public override int DetectionParameterCount => 4;   // a, b₁, b₂, τ
    
    public override double CalculateDetected(double t, double[] p)
    {
        double a = p[0], b1 = p[1], b2 = p[2], tau = p[5];
        double u = t <= tau ? b1 * t : b1 * tau + b2 * (t - tau);
        return a * (1 - Math.Exp(-u));
    }
    
    public override double CalculateCorrected(double t, double[] p)
    {
        double a = p[0], b1 = p[1], b2 = p[2], eta = p[3], tau = p[5];
        double mu = DelayRate(p[4]);
        double lagged = 0;
        if (!double.IsPositiveInfinity(mu))
        {
            lagged = t <= tau
                ? a * b1 * ExpKernel(b1, mu, t)
                : Math.Exp(-mu * (t - tau)) * a * b1 * ExpKernel(b1, mu, tau)
                  + a * b2 * Math.Exp(-b1 * tau) * ExpKernel(b2, mu, t - tau);
        }
        return eta * (CalculateDetected(t, p) - lagged);
    }
    
    public override double GetFaultRemovalEfficiency(double t, double[] p)
    {
        return p[3]; // η
    }
    
    public override double[] GetInitialParameters(double[] tData, double[] yData)
    {
        double maxY = yData.Max();
        int n = tData.Length;

        // a: 収束度合いに応じて 1.3〜1.7×maxY
        double last = yData[^1];
        double prev = n > 1 ? yData[^2] : yData[^1];
        double increment = last - prev;
        double a0 = increment <= 1.0 ? maxY * 1.3 : maxY * 1.7;

        // b₁, b₂: 平均増分から指数型と同様に初期化し、まずは同じ値から開始
        double b0 = GetBValueExponential(EstimateAverageSlope(yData));

        double eta0 = 0.8;

        // τ: 累積50%到達日を変化点候補に
        double tau0 = FindDayForCumulativeRatio(yData, 0.5);

        return new[] { a0, b0, b0, eta0, InitialDelay, tau0 };
    }
    
    public override (double[] lower, double[] upper) GetBounds(double[] tData, double[] yData)
    {
        double maxY = yData.Max();
        int n = tData.Length;
        var (dLower, dUpper) = DelayBounds(tData);
        return (
            new[] { maxY, 0.001, 0.001, 0.3, dLower, 2.0 },
            new[] { maxY * 5, 1.0, 1.0, 1.0, dUpper, n - 2.0 }
        );
    }
}

/// <summary>
/// FREモデルのファクトリ
/// </summary>
public static class FREModelFactory
{
    public static IEnumerable<ReliabilityGrowthModelBase> GetAllFREModels()
    {
        yield return new ConstantFREModel();
        yield return new LearningFREModel();
        yield return new LogisticFRFModel();
        yield return new FREChangePointModel();
    }
    
    public static IEnumerable<ReliabilityGrowthModelBase> GetBasicFREModels()
    {
        yield return new ConstantFREModel();
        yield return new LearningFREModel();
    }
}
