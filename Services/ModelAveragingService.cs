using BugConvergenceTool.Models;

namespace BugConvergenceTool.Services;

/// <summary>
/// モデル平均化の結果
/// </summary>
public class ModelAveragingResult
{
    /// <summary>各モデルのAIC重み</summary>
    public Dictionary<string, double> ModelWeights { get; init; } = new();
    
    /// <summary>加重平均された予測値</summary>
    public double[] AveragedPredictions { get; init; } = Array.Empty<double>();
    
    /// <summary>予測の標準誤差（モデル間分散）</summary>
    public double[] PredictionStandardErrors { get; init; } = Array.Empty<double>();
    
    /// <summary>加重平均された推定総バグ数</summary>
    public double AveragedTotalBugs { get; init; }
    
    /// <summary>総バグ数の不確実性（モデル間標準偏差）</summary>
    public double TotalBugsUncertainty { get; init; }
    
    /// <summary>予測時刻</summary>
    public double[] PredictionTimes { get; init; } = Array.Empty<double>();
    
    /// <summary>有効なモデル数（重み > 1%）</summary>
    public int EffectiveModelCount { get; init; }
    
    /// <summary>最良モデルの重み</summary>
    public double BestModelWeight { get; init; }
    
    /// <summary>最良モデル名</summary>
    public string BestModelName { get; init; } = "";
    
    /// <summary>使用された評価基準</summary>
    public string CriterionUsed { get; init; } = "";
    
    /// <summary>収束予測（マイルストーンごと）</summary>
    public Dictionary<string, ConvergencePredictionWithUncertainty> ConvergencePredictions { get; init; } = new();
}

/// <summary>
/// 不確実性付き収束予測
/// </summary>
public class ConvergencePredictionWithUncertainty
{
    /// <summary>マイルストーン名（例: "90%"）</summary>
    public string Milestone { get; init; } = "";
    
    /// <summary>目標割合（0-1）</summary>
    public double Ratio { get; init; }
    
    /// <summary>加重平均された予測日</summary>
    public double? AveragedPredictedDay { get; init; }
    
    /// <summary>予測日の標準偏差</summary>
    public double? PredictedDayStdDev { get; init; }
    
    /// <summary>予測日の下限（-1σ）</summary>
    public double? LowerBound { get; init; }
    
    /// <summary>予測日の上限（+1σ）</summary>
    public double? UpperBound { get; init; }
    
    /// <summary>予測に貢献したモデル数</summary>
    public int ContributingModels { get; init; }
}

/// <summary>
/// AIC重みによるモデル平均化サービス
/// Burnham & Anderson (2002) の方法に基づく
/// </summary>
public class ModelAveragingService
{
    private const double MIN_WEIGHT_THRESHOLD = 0.01; // 1%未満のモデルは「有効」カウントから除外

    /// <summary>
    /// AIC重みを計算
    /// </summary>
    /// <param name="results">フィッティング結果のリスト</param>
    /// <param name="useAICc">AICc（小標本補正）を使用するか</param>
    /// <returns>モデル名と重みの辞書</returns>
    public Dictionary<string, double> CalculateAicWeights(
        IEnumerable<FittingResult> results,
        bool? useAICc = null)
    {
        var validResults = results
            .Where(r => r.Success && !double.IsInfinity(r.AIC) && !double.IsNaN(r.AIC))
            .ToList();
        
        if (!validResults.Any())
            return new Dictionary<string, double>();
        
        // 自動判定: n/k < 40 なら AICc を使用
        bool shouldUseAICc = useAICc ?? validResults.All(r => r.ModelSelectionCriterion == "AICc");
        
        // 最小AICを基準にΔAICを計算
        double minAic = shouldUseAICc 
            ? validResults.Min(r => r.AICc) 
            : validResults.Min(r => r.AIC);
        
        var deltaAics = validResults.ToDictionary(
            r => r.ModelName,
            r => (shouldUseAICc ? r.AICc : r.AIC) - minAic);
        
        // AIC重み = exp(-ΔAIC/2) / Σexp(-ΔAIC/2)
        var expTerms = deltaAics.ToDictionary(
            kvp => kvp.Key,
            kvp => Math.Exp(-kvp.Value / 2));
        
        double sumExp = expTerms.Values.Sum();
        
        if (sumExp <= 0)
            return new Dictionary<string, double>();
        
        return expTerms.ToDictionary(
            kvp => kvp.Key,
            kvp => kvp.Value / sumExp);
    }

    /// <summary>
    /// モデル平均化を実行
    /// </summary>
    /// <param name="results">フィッティング結果のリスト</param>
    /// <param name="models">モデル名とモデルインスタンスの辞書</param>
    /// <param name="predictionTimes">予測時刻</param>
    /// <param name="currentDay">現在の日数（収束予測用）</param>
    /// <param name="useAICc">AICc（小標本補正）を使用するか</param>
    /// <returns>モデル平均化結果</returns>
    public ModelAveragingResult Average(
        IEnumerable<FittingResult> results,
        Dictionary<string, ReliabilityGrowthModelBase> models,
        double[] predictionTimes,
        int currentDay = 0,
        bool? useAICc = null)
    {
        var validResults = results
            .Where(r => r.Success && !double.IsInfinity(r.AIC))
            .ToList();
        
        if (!validResults.Any())
        {
            return new ModelAveragingResult
            {
                EffectiveModelCount = 0,
                CriterionUsed = "N/A"
            };
        }
        
        // AIC重みを計算
        var weights = CalculateAicWeights(validResults, useAICc);
        
        // 使用された基準を判定
        bool usedAICc = useAICc ?? validResults.All(r => r.ModelSelectionCriterion == "AICc");
        string criterionUsed = usedAICc ? "AICc" : "AIC";
        
        int nTimes = predictionTimes.Length;
        var averagedPredictions = new double[nTimes];
        var predictionVariances = new double[nTimes];
        
        double averagedTotalBugs = 0;
        double totalBugsWeightedSqSum = 0;
        double totalWeight = 0;
        
        // 最良モデルを特定
        string bestModelName = "";
        double bestModelWeight = 0;
        
        foreach (var result in validResults)
        {
            if (!weights.TryGetValue(result.ModelName, out double weight) || weight < 1e-10)
                continue;
            
            if (!models.TryGetValue(result.ModelName, out var model))
                continue;
            
            var parameters = result.ParameterVector;
            
            // 予測値の加重平均
            for (int t = 0; t < nTimes; t++)
            {
                double pred = model.Calculate(predictionTimes[t], parameters);
                averagedPredictions[t] += weight * pred;
                predictionVariances[t] += weight * pred * pred;
            }
            
            // 総バグ数の加重平均
            double totalBugs = model.GetAsymptoticTotalBugs(parameters);
            averagedTotalBugs += weight * totalBugs;
            totalBugsWeightedSqSum += weight * totalBugs * totalBugs;
            totalWeight += weight;
            
            // 最良モデルの更新
            if (weight > bestModelWeight)
            {
                bestModelWeight = weight;
                bestModelName = result.ModelName;
            }
        }
        
        // モデル間分散を計算（条件付き分散の公式）
        // Var = E[X²] - E[X]² （重み付き）
        var predictionStdErrors = new double[nTimes];
        for (int t = 0; t < nTimes; t++)
        {
            double variance = predictionVariances[t] - averagedPredictions[t] * averagedPredictions[t];
            predictionStdErrors[t] = Math.Sqrt(Math.Max(0, variance));
        }
        
        double totalBugsUncertainty = Math.Sqrt(
            Math.Max(0, totalBugsWeightedSqSum - averagedTotalBugs * averagedTotalBugs));
        
        // 有効モデル数（重み > 1%）
        int effectiveCount = weights.Count(kvp => kvp.Value > MIN_WEIGHT_THRESHOLD);
        
        // 収束予測を計算
        var convergencePredictions = PredictConvergence(
            validResults, models, weights,
            new[] { 0.80, 0.90, 0.95, 0.99 },
            currentDay);
        
        return new ModelAveragingResult
        {
            ModelWeights = weights,
            AveragedPredictions = averagedPredictions,
            PredictionStandardErrors = predictionStdErrors,
            AveragedTotalBugs = averagedTotalBugs,
            TotalBugsUncertainty = totalBugsUncertainty,
            PredictionTimes = predictionTimes,
            EffectiveModelCount = effectiveCount,
            BestModelWeight = bestModelWeight,
            BestModelName = bestModelName,
            CriterionUsed = criterionUsed,
            ConvergencePredictions = convergencePredictions
        };
    }

    /// <summary>
    /// モデル平均化による収束予測
    /// </summary>
    /// <param name="results">フィッティング結果</param>
    /// <param name="models">モデル辞書</param>
    /// <param name="weights">AIC重み</param>
    /// <param name="targetRatios">目標割合のリスト</param>
    /// <param name="currentDay">現在の日数</param>
    /// <returns>マイルストーンごとの収束予測</returns>
    private static Dictionary<string, ConvergencePredictionWithUncertainty> PredictConvergence(
        IEnumerable<FittingResult> results,
        Dictionary<string, ReliabilityGrowthModelBase> models,
        Dictionary<string, double> weights,
        double[] targetRatios,
        int currentDay)
    {
        var predictions = new Dictionary<string, ConvergencePredictionWithUncertainty>();
        
        foreach (double ratio in targetRatios)
        {
            string milestone = $"{ratio * 100:F0}%";
            
            double weightedDay = 0;
            double weightedDaySq = 0;
            double contributingWeight = 0;
            int contributingCount = 0;
            
            foreach (var result in results)
            {
                if (!weights.TryGetValue(result.ModelName, out double weight) || weight < 1e-10)
                    continue;
                
                if (!models.TryGetValue(result.ModelName, out var model))
                    continue;
                
                var predictedDay = model.PredictDayForRatio(ratio, result.ParameterVector, currentDay);
                
                if (predictedDay.HasValue && 
                    !double.IsInfinity(predictedDay.Value) && 
                    !double.IsNaN(predictedDay.Value) &&
                    predictedDay.Value > 0)
                {
                    weightedDay += weight * predictedDay.Value;
                    weightedDaySq += weight * predictedDay.Value * predictedDay.Value;
                    contributingWeight += weight;
                    contributingCount++;
                }
            }
            
            if (contributingWeight > 0 && contributingCount > 0)
            {
                double avgDay = weightedDay / contributingWeight;
                double variance = weightedDaySq / contributingWeight - avgDay * avgDay;
                double stdDev = Math.Sqrt(Math.Max(0, variance));
                
                predictions[milestone] = new ConvergencePredictionWithUncertainty
                {
                    Milestone = milestone,
                    Ratio = ratio,
                    AveragedPredictedDay = avgDay,
                    PredictedDayStdDev = stdDev,
                    LowerBound = avgDay - stdDev,
                    UpperBound = avgDay + stdDev,
                    ContributingModels = contributingCount
                };
            }
            else
            {
                predictions[milestone] = new ConvergencePredictionWithUncertainty
                {
                    Milestone = milestone,
                    Ratio = ratio,
                    AveragedPredictedDay = null,
                    ContributingModels = 0
                };
            }
        }
        
        return predictions;
    }

    /// <summary>
    /// モデル平均化結果をテキスト形式でフォーマット
    /// </summary>
    public static string FormatResult(ModelAveragingResult result)
    {
        var sb = new System.Text.StringBuilder();
        
        sb.AppendLine("═══════════════════════════════════════════════════════════════");
        sb.AppendLine("  モデル平均化結果");
        sb.AppendLine("═══════════════════════════════════════════════════════════════");
        sb.AppendLine();
        
        // 基本情報
        sb.AppendLine($"  使用基準: {result.CriterionUsed}");
        sb.AppendLine($"  有効モデル数: {result.EffectiveModelCount}");
        sb.AppendLine($"  最良モデル: {result.BestModelName} (重み={result.BestModelWeight:P1})");
        sb.AppendLine();
        
        // AIC重み一覧
        sb.AppendLine("─────────────────────────────────────────────────────────────────");
        sb.AppendLine("  モデル別 AIC 重み");
        sb.AppendLine("─────────────────────────────────────────────────────────────────");
        
        foreach (var (modelName, weight) in result.ModelWeights.OrderByDescending(x => x.Value))
        {
            string bar = new string('█', (int)(weight * 40));
            sb.AppendLine($"  {modelName,-25} {weight:P1}  {bar}");
        }
        sb.AppendLine();
        
        // 総バグ数推定
        sb.AppendLine("─────────────────────────────────────────────────────────────────");
        sb.AppendLine("  推定総バグ数（モデル平均化）");
        sb.AppendLine("─────────────────────────────────────────────────────────────────");
        sb.AppendLine($"  平均: {result.AveragedTotalBugs:F1}");
        sb.AppendLine($"  モデル間標準偏差: ±{result.TotalBugsUncertainty:F1}");
        sb.AppendLine($"  ※ この不確実性は「モデル間の違い」に由来するものであり、");
        sb.AppendLine($"    各モデルのパラメータ推定の不確実性は含まれていません。");
        sb.AppendLine();
        
        // 収束予測
        if (result.ConvergencePredictions.Count > 0)
        {
            sb.AppendLine("─────────────────────────────────────────────────────────────────");
            sb.AppendLine("  収束予測（モデル平均化）");
            sb.AppendLine("─────────────────────────────────────────────────────────────────");
            
            foreach (var (milestone, pred) in result.ConvergencePredictions.OrderBy(x => x.Value.Ratio))
            {
                if (pred.AveragedPredictedDay.HasValue)
                {
                    sb.AppendLine($"  {milestone}: 日{pred.AveragedPredictedDay:F0} " +
                                  $"(±{pred.PredictedDayStdDev:F0}日, {pred.ContributingModels}モデル)");
                }
                else
                {
                    sb.AppendLine($"  {milestone}: 予測不可");
                }
            }
            sb.AppendLine();
        }
        
        sb.AppendLine("═══════════════════════════════════════════════════════════════");
        
        return sb.ToString();
    }
}
