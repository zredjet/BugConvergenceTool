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
    /// 基本のCalculateは検出数を返す
    /// </summary>
    public override double Calculate(double t, double[] parameters)
    {
        return CalculateDetected(t, parameters);
    }
}

/// <summary>
/// 定数欠陥除去効率モデル
/// m_c(t) = η · m_d(t)
/// η: 欠陥除去効率 (0 < η ≤ 1)
/// </summary>
public class ConstantFREModel : FaultRemovalEfficiencyModelBase
{
    public override string Name => "定数FRE";
    public override string Formula => "m_c(t) = η·m_d(t), m_d(t) = a(1-e^(-bt))";
    public override string Description => "一定の欠陥除去効率";
    public override string[] ParameterNames => new[] { "a", "b", "η" };
    
    public override double CalculateDetected(double t, double[] p)
    {
        double a = p[0], b = p[1];
        return a * (1 - Math.Exp(-b * t));
    }
    
    public override double CalculateCorrected(double t, double[] p)
    {
        double eta = p[2];
        return eta * CalculateDetected(t, p);
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

        return new[] { a0, b0, eta0 };
    }
    
    public override (double[] lower, double[] upper) GetBounds(double[] tData, double[] yData)
    {
        double maxY = yData.Max();
        return (
            new[] { maxY, 0.001, 0.3 },
            new[] { maxY * 5, 1.0, 1.0 }
        );
    }
}

/// <summary>
/// 学習効果付き欠陥除去効率モデル
/// η(t) = η∞ - (η∞ - η₀)·e^(-λt)
/// 時間とともに効率が向上
/// </summary>
public class LearningFREModel : FaultRemovalEfficiencyModelBase
{
    public override string Name => "学習FRE";
    public override string Formula => "η(t) = η∞ - (η∞-η₀)e^(-λt)";
    public override string Description => "学習効果で効率向上";
    public override string[] ParameterNames => new[] { "a", "b", "η₀", "η∞", "λ" };
    
    public override double CalculateDetected(double t, double[] p)
    {
        double a = p[0], b = p[1];
        return a * (1 - Math.Exp(-b * t));
    }
    
    public override double CalculateCorrected(double t, double[] p)
    {
        // 適応的数値積分（Simpson法）で計算（累積修正数）
        // m_c(t) = ∫₀ᵗ η(s) · dm_d/ds · ds
        double a = p[0], b = p[1];
        double eta0 = p[2], etaInf = p[3], lambda = p[4];
        
        // 適応的積分: tに応じてステップ数を調整
        // 小さなtでは少ないステップで十分、大きなtでは精度確保のため増やす
        int baseSteps = 50;
        int additionalSteps = (int)Math.Ceiling(t * 10);  // tに比例して増加
        int steps = Math.Min(500, Math.Max(baseSteps, baseSteps + additionalSteps));
        
        double dt = t / steps;
        
        // Simpson法による積分（精度向上）
        double mc = 0;
        for (int i = 0; i < steps; i++)
        {
            double s0 = i * dt;
            double s1 = (i + 0.5) * dt;
            double s2 = (i + 1) * dt;
            
            double f0 = IntegrandEtaDmdt(s0, a, b, eta0, etaInf, lambda);
            double f1 = IntegrandEtaDmdt(s1, a, b, eta0, etaInf, lambda);
            double f2 = IntegrandEtaDmdt(s2, a, b, eta0, etaInf, lambda);
            
            // Simpson則: ∫ = (dt/6) * (f0 + 4*f1 + f2)
            mc += (dt / 6.0) * (f0 + 4.0 * f1 + f2);
        }
        
        return mc;
    }
    
    /// <summary>
    /// 積分の被積分関数: η(s) · dm_d/ds
    /// </summary>
    private static double IntegrandEtaDmdt(double s, double a, double b, double eta0, double etaInf, double lambda)
    {
        double eta = etaInf - (etaInf - eta0) * Math.Exp(-lambda * s);
        double dmdt = a * b * Math.Exp(-b * s);
        return eta * dmdt;
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

        return new[] { a0, b0, eta0, etaInf0, lambda0 };
    }
    
    public override (double[] lower, double[] upper) GetBounds(double[] tData, double[] yData)
    {
        double maxY = yData.Max();
        return (
            new[] { maxY, 0.001, 0.1, 0.7, 0.01 },
            new[] { maxY * 5, 1.0, 0.9, 1.0, 1.0 }
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
/// FRE + 変化点モデル
/// m_d(t) = a(1 - e^(-u(t)))、u(t) = b₁t（t ≤ τ）、b₁τ + b₂(t-τ)（t &gt; τ）
/// m_c(t) = η·m_d(t)
/// </summary>
/// <remarks>
/// 以前の「統合FRE」（FRE + エラー生成 + 変化点）からバグ混入率 α を除いたもの。
/// 定数 α のエラー生成モデルの解 a/(1-α)·(1-e^(-b(1-α)t)) は A(1-e^(-Bt))（A=a/(1-α), B=b(1-α)）と恒等的に等しく、
/// 発見数・修正数のデータから α を推定できない（識別不能）ため。
/// </remarks>
public class FREChangePointModel : FaultRemovalEfficiencyModelBase
{
    public override string Name => "FRE+変化点";
    public override string Formula => "m_d = a(1-e^(-u(t))), u(t)=b₁t [t≤τ], b₁τ+b₂(t-τ) [t>τ]; m_c = η·m_d";
    public override string Description => "欠陥除去効率 η と検出率の変化点を組み合わせたモデル";
    public override string[] ParameterNames => new[] { "a", "b₁", "b₂", "η", "τ" };
    
    public override double CalculateDetected(double t, double[] p)
    {
        double a = p[0], b1 = p[1], b2 = p[2], tau = p[4];
        double u = t <= tau ? b1 * t : b1 * tau + b2 * (t - tau);
        return a * (1 - Math.Exp(-u));
    }
    
    public override double CalculateCorrected(double t, double[] p)
    {
        double eta = p[3];
        return eta * CalculateDetected(t, p);
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

        return new[] { a0, b0, b0, eta0, tau0 };
    }
    
    public override (double[] lower, double[] upper) GetBounds(double[] tData, double[] yData)
    {
        double maxY = yData.Max();
        int n = tData.Length;
        return (
            new[] { maxY, 0.001, 0.001, 0.3, 2.0 },
            new[] { maxY * 5, 1.0, 1.0, 1.0, n - 2.0 }
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
