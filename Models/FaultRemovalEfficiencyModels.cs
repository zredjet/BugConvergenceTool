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
/// エラー生成モデル
/// 欠陥修正時に新たな欠陥が導入される
/// a(t) = a₀ + α·m_d(t)
/// </summary>
public class ErrorGenerationModel : FaultRemovalEfficiencyModelBase
{
    public override string Name => "エラー生成";
    public override string Formula => "a(t) = a₀ + α·m_d(t)";
    public override string Description => "修正時に新欠陥が導入";
    public override string[] ParameterNames => new[] { "a₀", "b", "α" };
    
    /// <summary>
    /// 漸近的総欠陥数: a₀ / (1 - α)
    /// </summary>
    public override double GetAsymptoticTotalBugs(double[] parameters)
    {
        double a0 = parameters[0];
        double alpha = parameters[2];
        
        if (alpha >= 1.0)
            alpha = 0.99;
        
        return a0 / (1 - alpha);
    }
    
    public override double CalculateDetected(double t, double[] p)
    {
        double a0 = p[0], b = p[1], alpha = p[2];
        
        if (alpha >= 1.0)
            alpha = 0.99;
        
        // 解析解: m_d(t) = a₀(1 - e^(-b(1-α)t)) / (1 - α)
        double factor = 1 - alpha;
        if (factor <= 0)
            return a0;
        
        return a0 * (1 - Math.Exp(-b * factor * t)) / factor;
    }
    
    public override double CalculateCorrected(double t, double[] p)
    {
        // エラー生成モデルでは修正数 = 検出数（完全除去を仮定）
        return CalculateDetected(t, p);
    }
    
    public override double CalculateRemaining(double t, double[] p)
    {
        double a0 = p[0], alpha = p[2];
        double detected = CalculateDetected(t, p);
        
        // 残存 = 初期欠陥 + 導入欠陥 - 修正欠陥
        // = a₀ + α·m_d - m_d = a₀ - (1-α)·m_d
        return a0 - (1 - alpha) * detected;
    }
    
    public override double GetFaultRemovalEfficiency(double t, double[] p)
    {
        return 1.0; // 検出された欠陥は全て修正される
    }
    
    public override double[] GetInitialParameters(double[] tData, double[] yData)
    {
        double maxY = yData.Max();
        int n = tData.Length;

        // a₀: 収束度合いに応じて 1.1〜1.5×maxY（導入分を考慮してやや控えめ）
        double last = yData[^1];
        double prev = n > 1 ? yData[^2] : yData[^1];
        double increment = last - prev;
        double a0 = increment <= 1.0 ? maxY * 1.1 : maxY * 1.5;

        // b: 平均増分から指数型と同様に推定
        double avgSlope = EstimateAverageSlope(yData);
        double b0 = avgSlope switch
        {
            <= 0.1 => 0.05,
            <= 0.5 => 0.1,
            <= 1.0 => 0.2,
            _ => 0.3
        };

        double alpha0 = 0.1;

        return new[] { a0, b0, alpha0 };
    }
    
    public override (double[] lower, double[] upper) GetBounds(double[] tData, double[] yData)
    {
        double maxY = yData.Max();
        return (
            new[] { maxY, 0.001, 0.0 },
            new[] { maxY * 3, 1.0, 0.5 }
        );
    }
}

/// <summary>
/// FRE + エラー生成統合モデル
/// 欠陥除去効率と欠陥導入を両方考慮
/// </summary>
public class FREErrorGenerationModel : FaultRemovalEfficiencyModelBase
{
    public override string Name => "FRE+エラー生成";
    public override string Category => "欠陥除去効率+不完全";
    public override string Formula => "m_c = η·m_d, a(t) = a₀ + α·m_d";
    public override string Description => "除去効率と欠陥導入を統合";
    public override string[] ParameterNames => new[] { "a₀", "b", "η", "α" };
    
    /// <summary>
    /// 漸近的総欠陥数: a₀ / (1 - α)
    /// </summary>
    public override double GetAsymptoticTotalBugs(double[] parameters)
    {
        double a0 = parameters[0];
        double alpha = parameters[3];
        
        if (alpha >= 1.0)
            alpha = 0.99;
        
        return a0 / (1 - alpha);
    }
    
    public override double CalculateDetected(double t, double[] p)
    {
        double a0 = p[0], b = p[1], alpha = p[3];
        
        if (alpha >= 1.0)
            alpha = 0.99;
        
        double factor = 1 - alpha;
        if (factor <= 0)
            return a0;
        
        return a0 * (1 - Math.Exp(-b * factor * t)) / factor;
    }
    
    public override double CalculateCorrected(double t, double[] p)
    {
        double eta = p[2];
        return eta * CalculateDetected(t, p);
    }
    
    public override double CalculateRemaining(double t, double[] p)
    {
        double a0 = p[0], eta = p[2], alpha = p[3];
        double detected = CalculateDetected(t, p);
        
        // 現在の潜在欠陥 = 初期 + 導入 - 修正
        double totalBugs = a0 + alpha * detected;
        double corrected = eta * detected;
        return totalBugs - corrected;
    }
    
    public override double GetFaultRemovalEfficiency(double t, double[] p)
    {
        return p[2]; // η
    }
    
    public override double[] GetInitialParameters(double[] tData, double[] yData)
    {
        double maxY = yData.Max();
        int n = tData.Length;

        // a₀: 収束度合いに応じて 1.2〜1.8×maxY
        double last = yData[^1];
        double prev = n > 1 ? yData[^2] : yData[^1];
        double increment = last - prev;
        double a0 = increment <= 1.0 ? maxY * 1.2 : maxY * 1.8;

        // b: 平均増分から指数型と同様に推定
        double avgSlope = EstimateAverageSlope(yData);
        double b0 = avgSlope switch
        {
            <= 0.1 => 0.05,
            <= 0.5 => 0.1,
            <= 1.0 => 0.2,
            _ => 0.3
        };

        double eta0 = 0.8;
        double alpha0 = 0.1;

        return new[] { a0, b0, eta0, alpha0 };
    }
    
    public override (double[] lower, double[] upper) GetBounds(double[] tData, double[] yData)
    {
        double maxY = yData.Max();
        return (
            new[] { maxY, 0.001, 0.3, 0.0 },
            new[] { maxY * 3, 1.0, 1.0, 0.5 }
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
/// 統合モデル（FRE + エラー生成 + 変化点）
/// </summary>
public class IntegratedFREModel : FaultRemovalEfficiencyModelBase
{
    public override string Name => "統合FRE";
    public override string Category => "統合";
    public override string Formula => "FRE + エラー生成 + 変化点";
    public override string Description => "全要素を統合した高度モデル";
    public override string[] ParameterNames => new[] { "a", "b₁", "b₂", "η", "α", "τ" };
    
    /// <summary>
    /// 漸近的総欠陥数: a / (1 - α)
    /// </summary>
    public override double GetAsymptoticTotalBugs(double[] parameters)
    {
        double a = parameters[0];
        double alpha = parameters[4];
        
        if (alpha >= 1.0)
            alpha = 0.99;
        
        return a / (1 - alpha);
    }
    
    public override double CalculateDetected(double t, double[] p)
    {
        double a = p[0], b1 = p[1], b2 = p[2], alpha = p[4], tau = p[5];
        
        if (alpha >= 1.0)
            alpha = 0.99;
        
        double factor = 1 - alpha;
        
        if (t <= tau)
        {
            return a * (1 - Math.Exp(-b1 * factor * t)) / factor;
        }
        else
        {
            // 変化点での値
            double m_tau = a * (1 - Math.Exp(-b1 * factor * tau)) / factor;
            
            // 変化点後
            double remainingA = a + alpha * m_tau - m_tau * factor;
            if (remainingA <= 0)
                return m_tau;
            
            double dt = t - tau;
            double increment = remainingA * (1 - Math.Exp(-b2 * factor * dt)) / factor;
            
            return m_tau + Math.Max(0, increment);
        }
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
        double avgSlope = EstimateAverageSlope(yData);
        double b1 = avgSlope switch
        {
            <= 0.1 => 0.05,
            <= 0.5 => 0.1,
            <= 1.0 => 0.2,
            _ => 0.3
        };
        double b2 = b1;

        double eta0 = 0.8;
        double alpha0 = 0.1;

        // τ: 累積50%到達日を変化点候補に
        double tau0 = FindDayForCumulativeRatio(yData, 0.5);

        return new[] { a0, b1, b2, eta0, alpha0, tau0 };
    }
    
    public override (double[] lower, double[] upper) GetBounds(double[] tData, double[] yData)
    {
        double maxY = yData.Max();
        int n = tData.Length;
        return (
            new[] { maxY, 0.001, 0.001, 0.3, 0.0, 2.0 },
            new[] { maxY * 5, 1.0, 1.0, 1.0, 0.5, n - 2.0 }
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
        yield return new ErrorGenerationModel();
        yield return new FREErrorGenerationModel();
        yield return new LogisticFRFModel();
        yield return new IntegratedFREModel();
    }
    
    public static IEnumerable<ReliabilityGrowthModelBase> GetBasicFREModels()
    {
        yield return new ConstantFREModel();
        yield return new ErrorGenerationModel();
        yield return new FREErrorGenerationModel();
    }
}
