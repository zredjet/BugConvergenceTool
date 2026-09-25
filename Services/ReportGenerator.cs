using System.Text;
using BugConvergenceTool.Models;

namespace BugConvergenceTool.Services;

/// <summary>
/// テキストレポートを生成するサービス
/// </summary>
public class ReportGenerator
{
    private readonly TestData _testData;
    
    public ReportGenerator(TestData testData)
    {
        _testData = testData;
    }
    
    /// <summary>
    /// 文字列の表示幅を計算（全角文字は2、半角文字は1）
    /// </summary>
    private static int GetDisplayWidth(string s)
    {
        if (string.IsNullOrEmpty(s)) return 0;
        int width = 0;
        foreach (char c in s)
        {
            width += IsFullWidth(c) ? 2 : 1;
        }
        return width;
    }
    
    /// <summary>
    /// 全角文字かどうかを判定
    /// </summary>
    private static bool IsFullWidth(char c)
    {
        return (c >= 0x1100 && c <= 0x115F) ||  // Hangul Jamo
               (c >= 0x2E80 && c <= 0x9FFF) ||  // CJK
               (c >= 0xAC00 && c <= 0xD7A3) ||  // Hangul Syllables
               (c >= 0xF900 && c <= 0xFAFF) ||  // CJK Compatibility Ideographs
               (c >= 0xFE10 && c <= 0xFE1F) ||  // Vertical Forms
               (c >= 0xFE30 && c <= 0xFE6F) ||  // CJK Compatibility Forms
               (c >= 0xFF00 && c <= 0xFF60) ||  // Fullwidth Forms
               (c >= 0xFFE0 && c <= 0xFFE6) ||  // Fullwidth Forms
               (c >= 0x3000 && c <= 0x303F) ||  // CJK Symbols and Punctuation
               (c >= 0x3040 && c <= 0x309F) ||  // Hiragana
               (c >= 0x30A0 && c <= 0x30FF) ||  // Katakana
               (c >= 0x31F0 && c <= 0x31FF);    // Katakana Phonetic Extensions
    }
    
    /// <summary>
    /// 文字列を指定幅に左寄せでパディング
    /// </summary>
    private static string PadRightByWidth(string s, int totalWidth)
    {
        int currentWidth = GetDisplayWidth(s);
        int padding = totalWidth - currentWidth;
        return padding > 0 ? s + new string(' ', padding) : s;
    }
    
    /// <summary>
    /// 文字列を指定幅に右寄せでパディング
    /// </summary>
    private static string PadLeftByWidth(string s, int totalWidth)
    {
        int currentWidth = GetDisplayWidth(s);
        int padding = totalWidth - currentWidth;
        return padding > 0 ? new string(' ', padding) + s : s;
    }
    
    /// <summary>
    /// 分析レポートを生成
    /// </summary>
    public string GenerateReport(List<FittingResult> results, FittingResult bestResult)
    {
        var sb = new StringBuilder();
        
        // カラム幅の定義
        const int colModel = 28;
        const int colCategory = 14;
        const int colNum = 10;
        const int colMilestone = 16;
        const int colDate = 15;
        
        sb.AppendLine("================================================================================");
        sb.AppendLine("                    バグ収束推定レポート");
        sb.AppendLine("================================================================================");
        sb.AppendLine();
        sb.AppendLine($"生成日時: {DateTime.Now:yyyy/MM/dd HH:mm:ss}");
        sb.AppendLine();
        
        // プロジェクト情報
        sb.AppendLine("--------------------------------------------------------------------------------");
        sb.AppendLine("【プロジェクト情報】");
        sb.AppendLine("--------------------------------------------------------------------------------");
        sb.AppendLine($"  プロジェクト名:     {_testData.ProjectName}");
        sb.AppendLine($"  総テストケース数:   {_testData.TotalTestCases}");
        sb.AppendLine($"  テスト開始日:       {_testData.StartDate?.ToString("yyyy/MM/dd") ?? "-"}");
        sb.AppendLine($"  データ日数:         {_testData.DayCount} 日");
        sb.AppendLine();
        
        // 現在の状況
        sb.AppendLine("--------------------------------------------------------------------------------");
        sb.AppendLine("【現在の状況】");
        sb.AppendLine("--------------------------------------------------------------------------------");
        var cumulativePlanned = _testData.GetCumulativePlanned();
        var cumulativeActual = _testData.GetCumulativeActual();
        var cumulativeFound = _testData.GetCumulativeBugsFound();
        var cumulativeFixed = _testData.GetCumulativeBugsFixed();
        var remaining = _testData.GetRemainingBugs();
        
        sb.AppendLine($"  テスト消化（予定）: {cumulativePlanned.Last():F0} / {_testData.TotalTestCases} ({cumulativePlanned.Last() / _testData.TotalTestCases * 100:F1}%)");
        sb.AppendLine($"  テスト消化（実績）: {cumulativeActual.Last():F0} / {_testData.TotalTestCases} ({cumulativeActual.Last() / _testData.TotalTestCases * 100:F1}%)");
        sb.AppendLine($"  累積バグ発生数:     {cumulativeFound.Last():F0} 件");
        sb.AppendLine($"  累積バグ修正数:     {cumulativeFixed.Last():F0} 件");
        sb.AppendLine($"  残存バグ数:         {remaining.Last():F0} 件");
        sb.AppendLine();
        
        // モデル比較結果
        sb.AppendLine("--------------------------------------------------------------------------------");
        sb.AppendLine("【モデル比較結果】");
        sb.AppendLine("--------------------------------------------------------------------------------");
        sb.AppendLine();
        // AIC は同じデータ・同じ尤度のモデル同士でしか比較できないため、比較グループごとに出力する
        foreach (var (group, groupResults) in ModelComparisonGroup.GroupAndRank(results))
        {
            string criterionName = groupResults[0].ModelSelectionCriterion;
            double minScore = groupResults[0].SelectionScore;
            
            sb.AppendLine($"【比較グループ: {group}】{ModelComparisonGroup.Describe(group)}");
            sb.AppendLine($"{PadRightByWidth("モデル名", colModel)} {PadRightByWidth("カテゴリ", colCategory)} {PadLeftByWidth("R²", colNum)} {PadLeftByWidth("MSE", colNum)} {PadLeftByWidth(criterionName, colNum)} {PadLeftByWidth("Δ" + criterionName, colNum)} {PadLeftByWidth("潜在バグ", colNum)}");
            sb.AppendLine(new string('-', 103));
            
            foreach (var result in groupResults)
            {
                string marker = (result.ModelName == bestResult.ModelName ? " *" : "") + (result.SelectionExclusionReason != null ? " †" : "");
                string modelNameWithMarker = result.ModelName + marker;
                sb.AppendLine($"{PadRightByWidth(modelNameWithMarker, colModel)} {PadRightByWidth(result.Category, colCategory)} {result.R2,colNum:F4} {result.MSE,colNum:F2} {result.SelectionScore,colNum:F2} {result.SelectionScore - minScore,colNum:F2} {result.EstimatedTotalBugs,colNum:F1}");
            }
            sb.AppendLine();
        }
        var criterion = bestResult.ModelSelectionCriterion;
        sb.AppendLine($"  * = 推奨モデル（比較グループ「{bestResult.ComparisonGroup}」内で{criterion}最小、損失関数: {bestResult.LossFunctionUsed}）");
        if (criterion == "AICc")
        {
            sb.AppendLine($"      （小標本補正: n={_testData.DayCount} に対し n/k < 40 のモデルがあるため、グループ内は AICc で統一）");
        }
        var excluded = results.Where(r => ModelComparisonGroup.IsComparable(r) && r.SelectionExclusionReason != null).ToList();
        if (excluded.Count > 0)
        {
            sb.AppendLine("  † = 推奨対象外:");
            foreach (var r in excluded)
                sb.AppendLine($"      {r.ModelName}: {r.SelectionExclusionReason}");
        }
        foreach (var r in results.Where(r => r.ChangePointTest?.Success == true))
        {
            sb.AppendLine($"  変化点の尤度比検定（{r.ModelName}）: {r.ChangePointTest!.Interpretation}");
        }
        if (bestResult.SelectionExclusionReason != null)
        {
            sb.AppendLine($"  ⚠ 推奨条件を満たすモデルがないため、選択基準値が最小のモデルを示しています（{bestResult.SelectionExclusionReason}）。推定総バグ数・収束予測は参考値です。");
        }
        sb.AppendLine();
        
        // 推奨モデルの詳細
        sb.AppendLine("--------------------------------------------------------------------------------");
        sb.AppendLine("【推奨モデル詳細】");
        sb.AppendLine("--------------------------------------------------------------------------------");
        sb.AppendLine($"  モデル名:           {bestResult.ModelName}");
        sb.AppendLine($"  カテゴリ:           {bestResult.Category}");
        sb.AppendLine();
        sb.AppendLine("  パラメータ推定結果:");
        foreach (var (name, value) in bestResult.Parameters)
        {
            sb.AppendLine($"    {ParameterDescriptions.FormatLine(name, value)}");
        }
        foreach (var line in ParameterDescriptions.DerivedLines(bestResult.Model, bestResult.ParameterVector))
        {
            sb.AppendLine($"    {line}");
        }
        sb.AppendLine();
        sb.AppendLine("  適合度指標:");
        sb.AppendLine($"    決定係数 (R²):       {bestResult.R2:F4}");
        sb.AppendLine($"    平均二乗誤差 (MSE):  {bestResult.MSE:F2}");
        sb.AppendLine($"    AIC:                 {bestResult.AIC:F2}");
        sb.AppendLine($"    AICc:                {bestResult.AICc:F2}");
        sb.AppendLine($"    選択基準:            {bestResult.ModelSelectionCriterion} = {bestResult.SelectionScore:F2}");
        sb.AppendLine();
        
        // 推定結果
        var assessment = ConvergenceAssessment.Evaluate(cumulativeFound.Last(), bestResult.EstimatedTotalBugs);
        sb.AppendLine("  推定結果:");
        sb.AppendLine($"    {bestResult.Model?.TotalBugsLabel ?? "推定潜在バグ総数"}:   {bestResult.EstimatedTotalBugs:F1} 件");
        sb.AppendLine($"    現在の発見率:       {ConvergenceAssessment.FormatRatio(assessment.Ratio)}");
        sb.AppendLine($"    残り推定バグ数:     {bestResult.EstimatedTotalBugs - cumulativeFound.Last():F1} 件");
        sb.AppendLine();
        
        // 収束予測
        sb.AppendLine("--------------------------------------------------------------------------------");
        sb.AppendLine("【収束予測】");
        sb.AppendLine("--------------------------------------------------------------------------------");
        sb.AppendLine();
        sb.AppendLine($"{PadRightByWidth("マイルストーン", colMilestone)} {PadLeftByWidth("予測日数", colNum)} {PadLeftByWidth("残り日数", colNum)} {PadLeftByWidth("予測日付", colDate)} {PadLeftByWidth("バグ数", colNum)}");
        sb.AppendLine(new string('-', 72));
        
        foreach (var (name, pred) in bestResult.ConvergencePredictions)
        {
            string dayStr = pred.AlreadyReached ? "到達済み" 
                : pred.PredictedDay?.ToString("F1") ?? "予測不可";
            string remainStr = pred.AlreadyReached ? "-" 
                : pred.RemainingDays?.ToString("F1") ?? "-";
            string dateStr = pred.PredictedDate?.ToString("yyyy/MM/dd") ?? "-";
            
            sb.AppendLine($"{PadRightByWidth(name, colMilestone)} {PadLeftByWidth(dayStr, colNum)} {remainStr,colNum} {dateStr,colDate} {pred.BugsAtPoint,colNum:F1}");
        }
        sb.AppendLine();
        
        // 収束判断の目安
        sb.AppendLine("--------------------------------------------------------------------------------");
        sb.AppendLine("【収束判断の目安】");
        sb.AppendLine("--------------------------------------------------------------------------------");
        sb.AppendLine();
        
        sb.AppendLine("  収束状況の評価:");
        sb.AppendLine($"    {assessment.Stars} {assessment.Message}");
        if (assessment.Note != null)
        {
            sb.AppendLine($"    注意: {assessment.Note}");
        }
        sb.AppendLine();
        
        AppendUncertainty(sb, bestResult);
        
        // 推定の安定性（末尾の日を除いた再推定）
        var stability = bestResult.Stability;
        if (stability != null)
        {
            sb.AppendLine("--------------------------------------------------------------------------------");
            sb.AppendLine("【推定の安定性（末尾の日を除いた再推定）】");
            sb.AppendLine("--------------------------------------------------------------------------------");
            sb.AppendLine();
            sb.AppendLine($"  判定: {stability.Assessment}" +
                (stability.RelativeRange.HasValue ? $"（総数の変動幅 {stability.RelativeRange:P0}）" : ""));
            sb.AppendLine($"    {"除いた日数",10} {"使用日数",8} {"推定総数",10}");
            sb.AppendLine($"    {0,10} {_testData.DayCount,8} {stability.BaseTotalBugs,10:F1}");
            foreach (var point in stability.Points)
            {
                string total = point.AtUpperBound ? "推定不能" : point.TotalBugs?.ToString("F1") ?? "失敗";
                sb.AppendLine($"    {point.RemovedDays,10} {point.UsedDays,8} {total,10}");
            }
            sb.AppendLine($"  * 変動幅 = (最大 - 最小) / 全データでの総数。{StabilityAnalysisResult.StableThreshold:P0} 未満で安定、{StabilityAnalysisResult.UnstableThreshold:P0} 以上で不安定");
            foreach (var warning in stability.Warnings)
                sb.AppendLine($"  ⚠ {warning}");
            sb.AppendLine();
        }
        
        // ホールドアウト検証結果（--holdout-days 指定時）
        AppendHoldoutResults(sb, results);
        
        sb.AppendLine("================================================================================");
        sb.AppendLine("                          レポート終了");
        sb.AppendLine("================================================================================");
        
        return sb.ToString();
    }
    
    /// <summary>
    /// 警告セクションを含むレポートを生成
    /// </summary>
    public string GenerateReport(List<FittingResult> results, FittingResult bestResult, IEnumerable<string>? warnings)
    {
        var sb = new StringBuilder(GenerateReport(results, bestResult));
        
        // 警告セクションを追加（レポート終了の前に挿入）
        if (warnings != null && warnings.Any())
        {
            var warningSection = new StringBuilder();
            warningSection.AppendLine();
            warningSection.AppendLine("--------------------------------------------------------------------------------");
            warningSection.AppendLine("【警告・注意事項】");
            warningSection.AppendLine("--------------------------------------------------------------------------------");
            warningSection.AppendLine();
            
            int i = 1;
            foreach (var warning in warnings)
            {
                warningSection.AppendLine($"  {i}. {warning}");
                i++;
            }
            warningSection.AppendLine();
            
            // "レポート終了" の前に挿入
            string report = sb.ToString();
            int insertPos = report.LastIndexOf("================================================================================\n                          レポート終了");
            if (insertPos > 0)
            {
                return report.Insert(insertPos, warningSection.ToString());
            }
            else
            {
                // 挿入位置が見つからない場合は末尾に追加
                return report + warningSection.ToString();
            }
        }
        
        return sb.ToString();
    }
    
    /// <summary>
    /// Fisher 情報行列による信頼区間と予測区間をレポートに追加
    /// </summary>
    private void AppendUncertainty(StringBuilder sb, FittingResult bestResult)
    {
        var band = bestResult.ConfidenceBand;
        if (band != null)
        {
            sb.AppendLine("--------------------------------------------------------------------------------");
            sb.AppendLine($"【信頼区間（パラメトリック・ブートストラップ、{band.ConfidenceLevel:P0}）】");
            sb.AppendLine("--------------------------------------------------------------------------------");
            sb.AppendLine();
            sb.AppendLine($"  再推定の成功: {band.Succeeded}/{band.Requested}（失敗した反復は除外）");
            foreach (var warning in band.Warnings)
                sb.AppendLine($"  注意: {warning}");
            if (band.Succeeded > 0)
            {
                if (band.TotalBugs != null)
                    sb.AppendLine($"  {IntervalFormatter.EstimateLine(bestResult.Model!.TotalBugsLabel, band.TotalBugs)}");
                AppendMilestones(sb, band.Milestones);
                sb.AppendLine("  ※ パラメータ推定の不確実性のみ。m(t) の区間はグラフ（reliability_growth.png）に描画しています。");
            }
            sb.AppendLine();
        }
        
        var fisher = bestResult.FisherInformation;
        if (fisher != null && fisher.Success)
        {
            sb.AppendLine("--------------------------------------------------------------------------------");
            sb.AppendLine("【パラメータの信頼区間（Fisher情報行列）】");
            sb.AppendLine("--------------------------------------------------------------------------------");
            sb.AppendLine();
            for (int i = 0; i < fisher.ParameterNames.Length; i++)
            {
                sb.AppendLine($"  {IntervalFormatter.FisherParameterLine(fisher, i)}");
            }
            var total = bestResult.TotalBugsFisherInterval;
            if (total != null && total.IsValid)
            {
                sb.AppendLine($"  {IntervalFormatter.FisherTotalBugsLine(total, bestResult.Model!.TotalBugsLabel)}");
            }
            sb.AppendLine("  ※ 漸近近似。パラメータが探索範囲の境界にある場合やデータが少ない場合は不正確です。");
            sb.AppendLine();
        }
        
        var pi = bestResult.PredictionInterval;
        if (pi != null)
        {
            sb.AppendLine("--------------------------------------------------------------------------------");
            sb.AppendLine($"【予測区間（パラメトリック・ブートストラップ、{pi.ConfidenceLevel:P0}）】");
            sb.AppendLine("--------------------------------------------------------------------------------");
            sb.AppendLine();
            sb.AppendLine($"  再推定の成功: {pi.Succeeded}/{pi.Requested}（失敗した反復は除外）");
            foreach (var warning in pi.Warnings)
                sb.AppendLine($"  注意: {warning}");
            if (pi.Succeeded > 0)
            {
                // 総数・収束日の区間は信頼区間と同じ値なので、信頼区間を出力済みなら省略する
                bool shownInBand = band?.Succeeded > 0;
                if (pi.TotalBugs != null && !shownInBand)
                    sb.AppendLine($"  {IntervalFormatter.EstimateLine(bestResult.Model!.TotalBugsLabel, pi.TotalBugs, suffix: "（信頼区間）")}");
                if (pi.RemainingBugs != null)
                    sb.AppendLine($"  {IntervalFormatter.EstimateLine("今後発見される件数", pi.RemainingBugs, "F0", "（予測区間）")}");
                if (!shownInBand)
                    AppendMilestones(sb, pi.Milestones);
                sb.AppendLine();
                sb.AppendLine("  将来の累積発見数（予測区間）:");
                sb.AppendLine($"    {"日",6} {"日付",12} {"予測",8} {"下限",8} {"上限",8}");
                for (int d = 0; d < pi.FutureTimes.Length; d++)
                {
                    string date = _testData.DateForDay(pi.FutureTimes[d])?.ToString("yyyy/MM/dd") ?? "-";
                    sb.AppendLine($"    {pi.FutureTimes[d],6:F0} {date,12} {pi.PointForecast[d],8:F1} {pi.Lower[d],8:F0} {IntervalFormatter.Upper(pi.Upper[d], pi.UpperIsBoundLimited.ElementAtOrDefault(d), "F0"),8}");
                }
            }
            sb.AppendLine();
        }
    }
    
    /// <summary>
    /// ホールドアウト検証結果をレポートに追加
    /// </summary>
    private static void AppendMilestones(StringBuilder sb, IEnumerable<MilestoneInterval> milestones)
    {
        sb.AppendLine();
        sb.AppendLine("  収束予測日の区間:");
        foreach (var m in milestones)
        {
            sb.AppendLine($"    {IntervalFormatter.MilestoneLine(m)}");
        }
    }
    
    private void AppendHoldoutResults(StringBuilder sb, List<FittingResult> results)
    {
        var resultsWithHoldout = results.Where(r => r.Success && r.Holdout != null).ToList();
        if (!resultsWithHoldout.Any()) return;
        
        sb.AppendLine("--------------------------------------------------------------------------------");
        sb.AppendLine("【ホールドアウト検証結果】");
        sb.AppendLine("--------------------------------------------------------------------------------");
        sb.AppendLine();
        sb.AppendLine($"  末尾 {resultsWithHoldout[0].Holdout!.TestCount} 日を除いた訓練区間でパラメータを推定し直し、末尾期間の発見数を予測して評価しています。");
        sb.AppendLine("  （他のセクションの結果は全データで推定した最終結果です）");
        sb.AppendLine();
        sb.AppendLine($"{PadRightByWidth("モデル名", 28)} {PadLeftByWidth("予測発見数", 12)} {PadLeftByWidth("95%予測区間", 14)} {PadLeftByWidth("実測発見数", 12)} {PadLeftByWidth("判定", 6)} {PadLeftByWidth("誤差(%)", 10)} {PadLeftByWidth("日次MAE", 10)} {PadLeftByWidth("日次RMSE", 10)}");
        sb.AppendLine(new string('-', 110));
        
        foreach (var result in resultsWithHoldout.OrderBy(r => r.HoldoutAbsIncrementErrorPercent ?? double.MaxValue))
        {
            var h = result.Holdout!;
            string errStr = result.HoldoutIncrementErrorPercent.HasValue ? $"{result.HoldoutIncrementErrorPercent:+0.0;-0.0}" : "-";
            string range = double.IsFinite(h.PredictionLower) ? $"[{h.PredictionLower:F0}, {h.PredictionUpper:F0}]" : "-";
            string verdict = h.IsOutsidePredictionInterval ? "区間外" : "区間内";
            sb.AppendLine($"{PadRightByWidth(result.ModelName, 28)} {h.PredictedIncrement,12:F1} {range,14} {h.ActualIncrement,12:F0} {PadLeftByWidth(verdict, 6)} {errStr,10} {h.DailyMae,10:F2} {h.DailyRmse,10:F2}");
        }
        sb.AppendLine();
        sb.AppendLine("  * 予測区間 = Poisson 変動と訓練区間の推定の不確実性（Fisher 情報行列）を含む区間。区間外なら予測が外れていると判定");
        sb.AppendLine("  * 誤差 = (予測発見数 - 実測発見数) / 実測発見数。正は過大予測、負は過小予測（件数が少ないと大きくなりやすいので参考）");
        sb.AppendLine("  * 日次MAE/RMSE = 日次発見数の予測誤差（件/日）");
        sb.AppendLine();
    }
    
    /// <summary>
    /// レポートをファイルに保存
    /// </summary>
    public void SaveReport(string filePath, List<FittingResult> results, FittingResult bestResult)
    {
        string report = GenerateReport(results, bestResult);
        File.WriteAllText(filePath, report, Encoding.UTF8);
    }
    
    /// <summary>
    /// 警告を含むレポートをファイルに保存
    /// </summary>
    public void SaveReport(string filePath, List<FittingResult> results, FittingResult bestResult, IEnumerable<string>? warnings)
    {
        string report = GenerateReport(results, bestResult, warnings);
        File.WriteAllText(filePath, report, Encoding.UTF8);
    }
}
