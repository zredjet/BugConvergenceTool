namespace BugConvergenceTool.Models;

/// <summary>
/// 信頼度成長モデルのフィッティング結果
/// </summary>
public class FittingResult
{
    public string ModelName { get; set; } = "";
    public string Category { get; set; } = "基本";
    public Dictionary<string, double> Parameters { get; set; } = new();
    public double R2 { get; set; }
    public double MSE { get; set; }
    public double AIC { get; set; }
    public double[] PredictedValues { get; set; } = Array.Empty<double>();
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    
    // 収束予測
    public Dictionary<string, ConvergencePrediction> ConvergencePredictions { get; set; } = new();
    
    // 推定潜在バグ数（パラメータa）
    public double EstimatedTotalBugs => Parameters.GetValueOrDefault("a", 0);
    
    // 不完全デバッグ率（パラメータp）
    public double? ImperfectDebugRate => Parameters.ContainsKey("p") ? Parameters["p"] : null;
    
    // オプティマイザ情報
    public string OptimizerUsed { get; set; } = "";
    public long OptimizationTimeMs { get; set; }
    public int FunctionEvaluations { get; set; }
}

/// <summary>
/// 収束予測結果
/// </summary>
public class ConvergencePrediction
{
    public string Milestone { get; set; } = "";
    public double Ratio { get; set; }
    public double? PredictedDay { get; set; }
    public double? RemainingDays { get; set; }
    public DateTime? PredictedDate { get; set; }
    public double BugsAtPoint { get; set; }
    public bool AlreadyReached { get; set; }
}

/// <summary>
/// 信頼度成長モデルの基底クラス
/// </summary>
public abstract class ReliabilityGrowthModelBase
{
    public abstract string Name { get; }
    public abstract string Category { get; }
    public abstract string Formula { get; }
    public abstract string Description { get; }
    public abstract string[] ParameterNames { get; }
    
    /// <summary>
    /// モデル関数 m(t) を計算
    /// </summary>
    public abstract double Calculate(double t, double[] parameters);
    
    /// <summary>
    /// パラメータの初期値を取得
    /// </summary>
    public abstract double[] GetInitialParameters(double[] tData, double[] yData);
    
    /// <summary>
    /// パラメータの下限・上限を取得
    /// </summary>
    public abstract (double[] lower, double[] upper) GetBounds(double[] tData, double[] yData);
    
    /// <summary>
    /// 残差二乗和を計算
    /// </summary>
    public double CalculateSSE(double[] tData, double[] yData, double[] parameters)
    {
        double sse = 0;
        for (int i = 0; i < tData.Length; i++)
        {
            double predicted = Calculate(tData[i], parameters);
            double residual = yData[i] - predicted;
            sse += residual * residual;
        }
        return sse;
    }
    
    /// <summary>
    /// 決定係数 R² を計算
    /// </summary>
    public double CalculateR2(double[] tData, double[] yData, double[] parameters)
    {
        double yMean = yData.Average();
        double ssTot = yData.Sum(y => (y - yMean) * (y - yMean));
        double ssRes = CalculateSSE(tData, yData, parameters);
        
        if (ssTot == 0) return 0;
        return 1 - (ssRes / ssTot);
    }
    
    /// <summary>
    /// AIC（赤池情報量規準）を計算
    /// </summary>
    public double CalculateAIC(double[] tData, double[] yData, double[] parameters)
    {
        int n = tData.Length;
        int k = parameters.Length;
        double sse = CalculateSSE(tData, yData, parameters);
        
        if (sse <= 0) return double.MaxValue;
        return n * Math.Log(sse / n) + 2 * k;
    }
    
    /// <summary>
    /// 指定割合に到達する日を予測
    /// </summary>
    public double? PredictDayForRatio(double ratio, double[] parameters, int currentDay)
    {
        double totalBugs = parameters[0]; // パラメータa
        double target = totalBugs * ratio;
        double currentValue = Calculate(currentDay, parameters);
        
        // 既に到達済み
        if (currentValue >= target)
            return null;
        
        // 二分法で探索
        double tLow = currentDay;
        double tHigh = currentDay * 10;
        
        // 上限を拡大
        while (Calculate(tHigh, parameters) < target && tHigh < 10000)
            tHigh *= 2;
        
        if (Calculate(tHigh, parameters) < target)
            return double.PositiveInfinity;
        
        // 二分法
        for (int i = 0; i < 100; i++)
        {
            double tMid = (tLow + tHigh) / 2;
            double valMid = Calculate(tMid, parameters);
            
            if (Math.Abs(valMid - target) < 0.01)
                return tMid;
            
            if (valMid < target)
                tLow = tMid;
            else
                tHigh = tMid;
        }
        
        return (tLow + tHigh) / 2;
    }
}
