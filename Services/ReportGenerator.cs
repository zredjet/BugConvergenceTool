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
        sb.AppendLine($"{PadRightByWidth("モデル名", colModel)} {PadRightByWidth("カテゴリ", colCategory)} {PadLeftByWidth("R²", colNum)} {PadLeftByWidth("MSE", colNum)} {PadLeftByWidth("AIC", colNum)} {PadLeftByWidth("潜在バグ", colNum)}");
        sb.AppendLine(new string('-', 92));
        
        foreach (var result in results.Where(r => r.Success && !r.ModelSelectionCriterion.StartsWith("Invalid")).OrderBy(r => r.SelectionScore))
        {
            string marker = result.ModelName == bestResult.ModelName ? " *" : "";
            string modelNameWithMarker = result.ModelName + marker;
            sb.AppendLine($"{PadRightByWidth(modelNameWithMarker, colModel)} {PadRightByWidth(result.Category, colCategory)} {result.R2,colNum:F4} {result.MSE,colNum:F2} {result.AIC,colNum:F2} {result.EstimatedTotalBugs,colNum:F1}");
        }
        sb.AppendLine();
        var criterion = bestResult.ModelSelectionCriterion;
        sb.AppendLine($"  * = 推奨モデル（{criterion}最小）");
        if (criterion == "AICc")
        {
            int n = _testData.DayCount;
            int k = bestResult.ParameterVector.Length;
            sb.AppendLine($"      （小標本補正適用: n={n}, k={k}, n/k={n/(double)k:F1} < 40）");
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
            string desc = name switch
            {
                "a" => "（潜在バグ総数）",
                "b" => "（バグ発見率）",
                "c" => "（形状パラメータ）",
                "p" => "（不完全デバッグ率）",
                _ => ""
            };
            
            if (name == "p")
                sb.AppendLine($"    {name} = {value:F4} ({value * 100:F1}%) {desc}");
            else
                sb.AppendLine($"    {name} = {value:F4} {desc}");
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
        sb.AppendLine("  推定結果:");
        sb.AppendLine($"    推定潜在バグ総数:   {bestResult.EstimatedTotalBugs:F1} 件");
        sb.AppendLine($"    現在の発見率:       {cumulativeFound.Last() / bestResult.EstimatedTotalBugs * 100:F1}%");
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
        
        double currentRatio = cumulativeFound.Last() / bestResult.EstimatedTotalBugs;
        double remainingBugs = bestResult.EstimatedTotalBugs - cumulativeFound.Last();
        
        sb.AppendLine("  収束状況の評価:");
        if (currentRatio >= 0.99)
        {
            sb.AppendLine("    ★★★ 十分に収束しています。リリース可能な状態です。");
        }
        else if (currentRatio >= 0.95)
        {
            sb.AppendLine("    ★★☆ ほぼ収束しています。ベータリリースに適した状態です。");
        }
        else if (currentRatio >= 0.90)
        {
            sb.AppendLine("    ★☆☆ 収束傾向にあります。継続的なテストが推奨されます。");
        }
        else
        {
            sb.AppendLine("    ☆☆☆ まだ収束していません。テスト継続が必要です。");
        }
        sb.AppendLine();
        
        // 感度分析セクション
        if (bestResult.SensitivityAnalysis != null && bestResult.SensitivityAnalysis.Items.Count > 0)
        {
            sb.AppendLine("--------------------------------------------------------------------------------");
            sb.AppendLine("【感度分析（パラメータ感度）】");
            sb.AppendLine("--------------------------------------------------------------------------------");
            sb.AppendLine();
            sb.AppendLine($"  分析対象: {bestResult.SensitivityAnalysis.TargetMetricName}");
            sb.AppendLine($"  基準値:   {bestResult.SensitivityAnalysis.TargetMetricValue:F2}");
            sb.AppendLine($"  摂動率:   ±{bestResult.SensitivityAnalysis.PerturbationPercent:F1}%");
            sb.AppendLine($"  総合ロバスト性: {bestResult.SensitivityAnalysis.OverallRobustness}");
            sb.AppendLine();
            sb.AppendLine($"{"パラメータ",-12} {"推定値",12} {"弾力性",12} {"ロバスト性",12} {"方向性",8}");
            sb.AppendLine(new string('-', 60));
            
            foreach (var item in bestResult.SensitivityAnalysis.Items.OrderByDescending(i => Math.Abs(i.Elasticity)))
            {
                sb.AppendLine($"{item.ParameterName,-12} {item.ParameterValue,12:F4} {item.Elasticity,12:F2} {item.Robustness,12} {item.Direction,8}");
            }
            sb.AppendLine();
            sb.AppendLine("  * 弾力性: パラメータが1%変化した時の予測値の変化率（%）");
            sb.AppendLine("  * ロバスト性: 高=安定 (|E|<1), 中=注意 (1≤|E|<5), 低=不安定 (|E|≥5)");
            sb.AppendLine();
            
            // 感度分析の警告
            if (bestResult.SensitivityAnalysis.Warnings.Count > 0)
            {
                sb.AppendLine("  ⚠ 感度分析の警告:");
                foreach (var warning in bestResult.SensitivityAnalysis.Warnings)
                {
                    sb.AppendLine($"    {warning}");
                }
                sb.AppendLine();
            }
        }
        
        // 変化点探索結果セクション
        if (bestResult.ChangePointSearchResult != null && bestResult.ChangePointSearchResult.Success)
        {
            var cpResult = bestResult.ChangePointSearchResult;
            sb.AppendLine("--------------------------------------------------------------------------------");
            sb.AppendLine("【変化点探索結果（プロファイル尤度法）】");
            sb.AppendLine("--------------------------------------------------------------------------------");
            sb.AppendLine();
            sb.AppendLine($"  最適変化点:     τ = {cpResult.BestTau} 日目");
            sb.AppendLine($"  最小AICc:       {cpResult.BestAICc:F2}");
            sb.AppendLine($"  変化点の信頼性: {cpResult.ChangePointReliability}");
            sb.AppendLine($"  探索時間:       {cpResult.ElapsedMilliseconds}ms");
            sb.AppendLine();
            
            // プロファイルAICcの概要（上位5候補）
            if (cpResult.ProfileAICc.Count > 1)
            {
                sb.AppendLine("  AICcプロファイル（上位5候補）:");
                var topCandidates = cpResult.ProfileAICc
                    .OrderBy(x => x.Value)
                    .Take(5)
                    .ToList();
                
                foreach (var (tau, aicc) in topCandidates)
                {
                    string marker = tau == cpResult.BestTau ? " *" : "";
                    sb.AppendLine($"    τ={tau,3}: AICc={aicc,10:F2}{marker}");
                }
                sb.AppendLine();
            }
        }
        
        // 不完全デバッグに関する注意
        if (bestResult.ImperfectDebugRate.HasValue && bestResult.ImperfectDebugRate.Value > 0.1)
        {
            sb.AppendLine("  ⚠ 注意:");
            sb.AppendLine($"    不完全デバッグ率が {bestResult.ImperfectDebugRate.Value * 100:F1}% と高めです。");
            sb.AppendLine("    バグ修正時に新たなバグが混入している可能性があります。");
            sb.AppendLine("    修正プロセスやコードレビューの強化を検討してください。");
            sb.AppendLine();
        }
        
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
    /// ホールドアウト検証結果をレポートに追加
    /// </summary>
    private void AppendHoldoutResults(StringBuilder sb, List<FittingResult> results)
    {
        var resultsWithHoldout = results.Where(r => r.Success && r.HoldoutMse.HasValue).ToList();
        if (!resultsWithHoldout.Any()) return;
        
        sb.AppendLine("--------------------------------------------------------------------------------");
        sb.AppendLine("【ホールドアウト検証結果】");
        sb.AppendLine("--------------------------------------------------------------------------------");
        sb.AppendLine();
        sb.AppendLine($"{"モデル名",-25} {"MSE",12} {"MAE",12} {"MAPE(%)",12}");
        sb.AppendLine(new string('-', 65));
        
        foreach (var result in resultsWithHoldout.OrderBy(r => r.HoldoutMape ?? double.MaxValue))
        {
            string mapeStr = result.HoldoutMape.HasValue ? $"{result.HoldoutMape:F2}" : "-";
            string mseStr = result.HoldoutMse.HasValue ? $"{result.HoldoutMse:F4}" : "-";
            string maeStr = result.HoldoutMae.HasValue ? $"{result.HoldoutMae:F4}" : "-";
            
            sb.AppendLine($"{result.ModelName,-25} {mseStr,12} {maeStr,12} {mapeStr,12}");
        }
        sb.AppendLine();
        sb.AppendLine("  * MAPE = Mean Absolute Percentage Error（平均絶対パーセント誤差）");
        sb.AppendLine("  * 値が小さいほど予測精度が高い");
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
