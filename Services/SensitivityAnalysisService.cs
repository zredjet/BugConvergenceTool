using BugConvergenceTool.Models;

namespace BugConvergenceTool.Services;

/// <summary>
/// 感度分析の結果を格納するクラス
/// </summary>
public class SensitivityReport
{
    /// <summary>
    /// 各パラメータの感度分析結果
    /// </summary>
    public List<SensitivityItem> Items { get; set; } = new();
    
    /// <summary>
    /// 分析対象の指標名（例: "推定総バグ数", "95%収束日"）
    /// </summary>
    public string TargetMetricName { get; set; } = "";
    
    /// <summary>
    /// 分析対象の指標値（基準値）
    /// </summary>
    public double TargetMetricValue { get; set; }
    
    /// <summary>
    /// 使用した摂動率（%）
    /// </summary>
    public double PerturbationPercent { get; set; } = 1.0;
    
    /// <summary>
    /// 感度が高いパラメータに対する警告メッセージ
    /// </summary>
    public List<string> Warnings { get; set; } = new();
    
    /// <summary>
    /// 全体的なロバスト性評価
    /// </summary>
    public string OverallRobustness => DetermineOverallRobustness();
    
    private string DetermineOverallRobustness()
    {
        if (Items.Count == 0) return "不明";
        
        double maxAbsElasticity = Items.Max(i => Math.Abs(i.Elasticity));
        
        return maxAbsElasticity switch
        {
            < 1.0 => "高",
            < 5.0 => "中",
            < 10.0 => "低",
            _ => "非常に低"
        };
    }
}

/// <summary>
/// 個別パラメータの感度分析結果
/// </summary>
public class SensitivityItem
{
    /// <summary>
    /// パラメータ名
    /// </summary>
    public string ParameterName { get; set; } = "";
    
    /// <summary>
    /// パラメータの推定値
    /// </summary>
    public double ParameterValue { get; set; }
    
    /// <summary>
    /// 弾力性（Elasticity）: パラメータの1%変化に対する出力の%変化
    /// </summary>
    public double Elasticity { get; set; }
    
    /// <summary>
    /// 感度係数（Sensitivity）: パラメータの絶対変化に対する出力の絶対変化
    /// </summary>
    public double Sensitivity { get; set; }
    
    /// <summary>
    /// +摂動時の指標値
    /// </summary>
    public double PerturbedValuePositive { get; set; }
    
    /// <summary>
    /// -摂動時の指標値
    /// </summary>
    public double PerturbedValueNegative { get; set; }
    
    /// <summary>
    /// ロバスト性評価（High/Medium/Low）
    /// </summary>
    public string Robustness => DetermineRobustness();
    
    private string DetermineRobustness()
    {
        double absElasticity = Math.Abs(Elasticity);
        return absElasticity switch
        {
            < 1.0 => "高",
            < 5.0 => "中",
            < 10.0 => "低",
            _ => "非常に低"
        };
    }
    
    /// <summary>
    /// 感度の方向性（正/負/中立）
    /// </summary>
    public string Direction => Elasticity switch
    {
        > 0.1 => "正",
        < -0.1 => "負",
        _ => "中立"
    };
}

/// <summary>
/// 局所感度分析を実行するサービス
/// パラメータの微小変化に対する出力指標の変化率（弾力性）を計算
/// </summary>
public class SensitivityAnalysisService
{
    private const double DefaultPerturbationRatio = 0.01; // 1%
    
    /// <summary>
    /// 推定総バグ数に対する感度分析を実行
    /// </summary>
    /// <param name="model">分析対象のモデル</param>
    /// <param name="optimalParams">最適化されたパラメータ</param>
    /// <param name="perturbationRatio">摂動率（デフォルト: 1%）</param>
    /// <returns>感度分析レポート</returns>
    public SensitivityReport AnalyzeTotalBugsSensitivity(
        ReliabilityGrowthModelBase model,
        double[] optimalParams,
        double perturbationRatio = DefaultPerturbationRatio)
    {
        double baseValue = model.GetAsymptoticTotalBugs(optimalParams);
        
        return Analyze(
            model,
            optimalParams,
            "推定総バグ数",
            baseValue,
            p => model.GetAsymptoticTotalBugs(p),
            perturbationRatio);
    }
    
    /// <summary>
    /// 特定時点での累積バグ数に対する感度分析を実行
    /// </summary>
    /// <param name="model">分析対象のモデル</param>
    /// <param name="optimalParams">最適化されたパラメータ</param>
    /// <param name="targetDay">評価対象の日数</param>
    /// <param name="perturbationRatio">摂動率（デフォルト: 1%）</param>
    /// <returns>感度分析レポート</returns>
    public SensitivityReport AnalyzeCumulativeBugsSensitivity(
        ReliabilityGrowthModelBase model,
        double[] optimalParams,
        double targetDay,
        double perturbationRatio = DefaultPerturbationRatio)
    {
        double baseValue = model.Calculate(targetDay, optimalParams);
        
        return Analyze(
            model,
            optimalParams,
            $"{targetDay}日目の累積バグ数",
            baseValue,
            p => model.Calculate(targetDay, p),
            perturbationRatio);
    }
    
    /// <summary>
    /// 指定されたモデルとパラメータに対する感度分析を実行
    /// </summary>
    /// <param name="model">分析対象のモデル</param>
    /// <param name="optimalParams">最適化されたパラメータ</param>
    /// <param name="metricName">分析対象の指標名</param>
    /// <param name="baseMetricValue">指標の基準値</param>
    /// <param name="metricCalculator">パラメータから指標を計算する関数</param>
    /// <param name="perturbationRatio">摂動率（デフォルト: 1%）</param>
    /// <returns>感度分析レポート</returns>
    public SensitivityReport Analyze(
        ReliabilityGrowthModelBase model,
        double[] optimalParams,
        string metricName,
        double baseMetricValue,
        Func<double[], double> metricCalculator,
        double perturbationRatio = DefaultPerturbationRatio)
    {
        var report = new SensitivityReport
        {
            TargetMetricName = metricName,
            TargetMetricValue = baseMetricValue,
            PerturbationPercent = perturbationRatio * 100
        };
        
        // 基準値が0または非常に小さい場合は分析不可
        if (Math.Abs(baseMetricValue) < 1e-10)
        {
            report.Warnings.Add("基準値が0に近いため、弾力性の計算ができません。");
            return report;
        }
        
        for (int i = 0; i < optimalParams.Length; i++)
        {
            string paramName = model.ParameterNames[i];
            double originalVal = optimalParams[i];
            
            // パラメータ値が0の場合はスキップ
            if (Math.Abs(originalVal) < 1e-10)
            {
                report.Items.Add(new SensitivityItem
                {
                    ParameterName = paramName,
                    ParameterValue = originalVal,
                    Elasticity = 0,
                    Sensitivity = 0,
                    PerturbedValuePositive = baseMetricValue,
                    PerturbedValueNegative = baseMetricValue
                });
                continue;
            }
            
            // +摂動
            var perturbedParamsPos = (double[])optimalParams.Clone();
            perturbedParamsPos[i] = originalVal * (1.0 + perturbationRatio);
            double perturbedMetricPos = metricCalculator(perturbedParamsPos);
            
            // -摂動
            var perturbedParamsNeg = (double[])optimalParams.Clone();
            perturbedParamsNeg[i] = originalVal * (1.0 - perturbationRatio);
            double perturbedMetricNeg = metricCalculator(perturbedParamsNeg);
            
            // 中央差分による感度係数の計算
            double deltaParam = 2.0 * originalVal * perturbationRatio;
            double deltaMetric = perturbedMetricPos - perturbedMetricNeg;
            double sensitivity = deltaMetric / deltaParam;
            
            // 弾力性の計算: (ΔY/Y) / (Δθ/θ) = (dY/dθ) × (θ/Y)
            double elasticity = sensitivity * (originalVal / baseMetricValue);
            
            report.Items.Add(new SensitivityItem
            {
                ParameterName = paramName,
                ParameterValue = originalVal,
                Elasticity = elasticity,
                Sensitivity = sensitivity,
                PerturbedValuePositive = perturbedMetricPos,
                PerturbedValueNegative = perturbedMetricNeg
            });
        }
        
        // 警告の生成
        GenerateWarnings(report);
        
        return report;
    }
    
    /// <summary>
    /// 高感度パラメータに対する警告を生成
    /// </summary>
    private void GenerateWarnings(SensitivityReport report)
    {
        foreach (var item in report.Items)
        {
            double absElasticity = Math.Abs(item.Elasticity);
            
            if (absElasticity >= 10.0)
            {
                report.Warnings.Add(
                    $"警告: パラメータ '{item.ParameterName}' の感度が極めて高い ({item.Elasticity:F2}) です。" +
                    $"このパラメータの推定誤差がわずかでも、{report.TargetMetricName}の予測が大きく変動するリスクがあります。");
            }
            else if (absElasticity >= 5.0)
            {
                report.Warnings.Add(
                    $"注意: パラメータ '{item.ParameterName}' の感度が高い ({item.Elasticity:F2}) です。" +
                    $"予測の信頼性に影響する可能性があります。");
            }
        }
    }
    
    /// <summary>
    /// 複数の指標に対する感度分析を一括実行
    /// </summary>
    /// <param name="model">分析対象のモデル</param>
    /// <param name="optimalParams">最適化されたパラメータ</param>
    /// <param name="currentDay">現在の日数</param>
    /// <param name="perturbationRatio">摂動率</param>
    /// <returns>指標名をキーとする感度分析レポートの辞書</returns>
    public Dictionary<string, SensitivityReport> AnalyzeAll(
        ReliabilityGrowthModelBase model,
        double[] optimalParams,
        int currentDay,
        double perturbationRatio = DefaultPerturbationRatio)
    {
        var reports = new Dictionary<string, SensitivityReport>();
        
        // 推定総バグ数
        reports["TotalBugs"] = AnalyzeTotalBugsSensitivity(model, optimalParams, perturbationRatio);
        
        // 現在日の累積バグ数
        reports["CurrentCumulative"] = AnalyzeCumulativeBugsSensitivity(
            model, optimalParams, currentDay, perturbationRatio);
        
        // 将来時点（現在の1.5倍の日数）の累積バグ数
        double futureDay = currentDay * 1.5;
        reports["FutureCumulative"] = AnalyzeCumulativeBugsSensitivity(
            model, optimalParams, futureDay, perturbationRatio);
        
        return reports;
    }
}
