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
    
    /// <summary>総バグ数のモデル間の標準偏差（重み付き）</summary>
    public double TotalBugsBetweenModelStdDev { get; init; }
    
    /// <summary>
    /// 総バグ数のモデル内（パラメータ推定）の標準偏差 √Σwᵢ·Var(N̂ᵢ)（Fisher 情報行列・デルタ法）。
    /// 計算できないモデルがある場合は NaN
    /// </summary>
    public double TotalBugsWithinModelStdDev { get; init; } = double.NaN;
    
    /// <summary>
    /// 総バグ数の分散に占めるパラメータ推定の不確実性の割合（計算できない場合は NaN）
    /// </summary>
    public double ParameterUncertaintyShare { get; init; } = double.NaN;
    
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
    
    /// <summary>探索範囲内で到達しないモデルの重みの合計</summary>
    public double UnreachableWeight { get; init; }
}

/// <summary>
/// AIC重みによるモデル平均化サービス
/// Burnham & Anderson (2002) の方法に基づく
/// </summary>
public class ModelAveragingService
{
    private const double MIN_WEIGHT_THRESHOLD = 0.01; // 1%未満のモデルは「有効」カウントから除外

    /// <summary>
    /// AIC 重みを計算できる結果（推奨モデルと同じ比較グループ）だけを選ぶ
    /// </summary>
    /// <remarks>
    /// 尤度に含むデータが異なるモデル（FRE・TEF）とは AIC を比較できないため、
    /// 異なる比較グループのモデルに重みを付けない。推奨対象外のモデルも除く。
    /// </remarks>
    private static List<FittingResult> SelectComparableResults(IEnumerable<FittingResult> results)
    {
        var comparable = results
            .Where(ModelComparisonGroup.IsComparable)
            .Where(r => double.IsFinite(r.AIC) && double.IsFinite(r.AICc))
            .ToList();
        var primaryGroup = ModelComparisonGroup.SelectPrimaryGroup(comparable);
        var inGroup = comparable.Where(r => r.ComparisonGroup == primaryGroup).ToList();
        
        // 推奨対象外のモデル（総数が境界に張り付き・変化点が有意でない等）には重みを付けない
        var eligible = inGroup.Where(r => r.SelectionExclusionReason == null).ToList();
        return eligible.Count > 0 ? eligible : inGroup;
    }

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
        var validResults = SelectComparableResults(results);
        
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
        bool? useAICc = null,
        double[]? tData = null,
        double[]? yData = null)
    {
        var validResults = SelectComparableResults(results);
        
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
        
        // モデル内（パラメータ推定）の分散 Σ wᵢ Var(N̂ᵢ)
        double withinVariance = 0;
        bool withinAvailable = tData != null && yData != null;
        var fisherService = new FisherInformationService();
        
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
            
            // モデル内の分散（MLE・τ なしのモデルのみ Fisher 情報行列で計算できる）
            if (withinAvailable)
            {
                double variance = TotalBugsVariance(fisherService, model, result, tData!, yData!);
                if (double.IsFinite(variance)) withinVariance += weight * variance;
                else withinAvailable = false;
            }
            
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
        
        double betweenVariance = Math.Max(0, totalBugsWeightedSqSum - averagedTotalBugs * averagedTotalBugs);
        
        // 無条件標準誤差（Burnham & Anderson 2002, 式 6.12）: √Σ wᵢ [Var(N̂ᵢ) + (N̂ᵢ - N̄)²]
        // モデル内の分散が計算できない場合はモデル間の分散のみ
        double totalBugsUncertainty = withinAvailable
            ? Math.Sqrt(withinVariance + betweenVariance)
            : Math.Sqrt(betweenVariance);
        
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
            TotalBugsBetweenModelStdDev = Math.Sqrt(betweenVariance),
            TotalBugsWithinModelStdDev = withinAvailable ? Math.Sqrt(withinVariance) : double.NaN,
            ParameterUncertaintyShare = withinAvailable && withinVariance + betweenVariance > 0
                ? withinVariance / (withinVariance + betweenVariance)
                : double.NaN,
            PredictionTimes = predictionTimes,
            EffectiveModelCount = effectiveCount,
            BestModelWeight = bestModelWeight,
            BestModelName = bestModelName,
            CriterionUsed = criterionUsed,
            ConvergencePredictions = convergencePredictions
        };
    }

    /// <summary>
    /// モデルの推定総バグ数のパラメータ推定分散（Fisher 情報行列・デルタ法）。計算できなければ NaN
    /// </summary>
    private static double TotalBugsVariance(
        FisherInformationService service, ReliabilityGrowthModelBase model, FittingResult result, double[] tData, double[] yData)
    {
        // Fisher 情報行列は発見数のみの Poisson-NHPP 尤度の最尤推定値でのみ有効（FRE・TEF は別の尤度）。τ は微分できない
        if (result.LossFunctionUsed != "MLE"
            || result.ComparisonGroup != ModelComparisonGroup.DetectionOnly
            || model.ParameterNames.Any(n => n.StartsWith("τ")))
            return double.NaN;
        var fisher = service.CalculateNHPPStandardErrors(model, tData, yData, result.ParameterVector);
        if (!fisher.Success || fisher.CovarianceMatrix == null)
            return double.NaN;
        var interval = service.CalculateDerivedInterval(model.GetAsymptoticTotalBugs, result.ParameterVector, fisher.CovarianceMatrix, logScale: false);
        return double.IsFinite(interval.StandardError) ? interval.StandardError * interval.StandardError : double.NaN;
    }
    
    /// <summary>
    /// 収束マイルストーン到達日のモデル平均
    /// </summary>
    /// <remarks>
    /// 各モデルで m(t) = ratio × m(∞) となる日を t=0 から求める（すでに到達済みのモデルは観測期間内の日になる）。
    /// 以前は「到達済み」「到達しない」モデルを平均から除いていたため、残ったモデルに偏っていた。
    /// 到達しないモデルの重みは UnreachableWeight として示し、平均は到達するモデルの重みで正規化する。
    /// </remarks>
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
            double weightedDay = 0, weightedDaySq = 0, reachableWeight = 0, unreachableWeight = 0;
            int contributingCount = 0;
            
            foreach (var result in results)
            {
                if (!weights.TryGetValue(result.ModelName, out double weight) || weight < 1e-10)
                    continue;
                if (!models.TryGetValue(result.ModelName, out var model))
                    continue;
                
                double day = model.DayForRatio(ratio, result.ParameterVector);
                if (double.IsFinite(day))
                {
                    weightedDay += weight * day;
                    weightedDaySq += weight * day * day;
                    reachableWeight += weight;
                    contributingCount++;
                }
                else
                {
                    unreachableWeight += weight;
                }
            }
            
            if (reachableWeight > 0)
            {
                double avgDay = weightedDay / reachableWeight;
                double stdDev = Math.Sqrt(Math.Max(0, weightedDaySq / reachableWeight - avgDay * avgDay));
                predictions[milestone] = new ConvergencePredictionWithUncertainty
                {
                    Milestone = milestone,
                    Ratio = ratio,
                    AveragedPredictedDay = avgDay,
                    PredictedDayStdDev = stdDev,
                    LowerBound = avgDay - stdDev,
                    UpperBound = avgDay + stdDev,
                    ContributingModels = contributingCount,
                    UnreachableWeight = unreachableWeight
                };
            }
            else
            {
                predictions[milestone] = new ConvergencePredictionWithUncertainty
                {
                    Milestone = milestone,
                    Ratio = ratio,
                    AveragedPredictedDay = null,
                    ContributingModels = 0,
                    UnreachableWeight = unreachableWeight
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
        if (double.IsFinite(result.TotalBugsWithinModelStdDev))
        {
            sb.AppendLine($"  無条件標準誤差: ±{result.TotalBugsUncertainty:F1}（Burnham & Anderson）");
            sb.AppendLine($"    パラメータ推定の不確実性: ±{result.TotalBugsWithinModelStdDev:F1}（分散の {result.ParameterUncertaintyShare:P0}）");
            sb.AppendLine($"    モデル選択の不確実性:     ±{result.TotalBugsBetweenModelStdDev:F1}（分散の {1 - result.ParameterUncertaintyShare:P0}）");
        }
        else
        {
            sb.AppendLine($"  モデル間標準偏差: ±{result.TotalBugsUncertainty:F1}");
            sb.AppendLine($"  ※ パラメータ推定の不確実性は含まれていません（SSE 推定または変化点モデルを含むため Fisher 情報行列で計算できません）。");
        }
        sb.AppendLine();
        
        // 収束予測
        if (result.ConvergencePredictions.Count > 0)
        {
            sb.AppendLine("─────────────────────────────────────────────────────────────────");
            sb.AppendLine("  収束予測（モデル平均化）");
            sb.AppendLine("─────────────────────────────────────────────────────────────────");
            
            foreach (var (milestone, pred) in result.ConvergencePredictions.OrderBy(x => x.Value.Ratio))
            {
                string unreachable = pred.UnreachableWeight > 0.001 ? $"、到達しないモデルの重み {pred.UnreachableWeight:P0}" : "";
                if (pred.AveragedPredictedDay.HasValue)
                {
                    sb.AppendLine($"  {milestone}: 日{pred.AveragedPredictedDay:F0} " +
                                  $"(モデル間 ±{pred.PredictedDayStdDev:F0}日, {pred.ContributingModels}モデル{unreachable})");
                }
                else
                {
                    sb.AppendLine($"  {milestone}: 予測不可{unreachable}");
                }
            }
            sb.AppendLine();
        }
        
        sb.AppendLine("═══════════════════════════════════════════════════════════════");
        
        return sb.ToString();
    }
}
