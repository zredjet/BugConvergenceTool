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
    /// 分析レポートを生成
    /// </summary>
    public string GenerateReport(List<FittingResult> results, FittingResult bestResult)
    {
        var sb = new StringBuilder();
        
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
        sb.AppendLine($"{"モデル名",-25} {"カテゴリ",-12} {"R²",10} {"MSE",10} {"AIC",10} {"潜在バグ",10}");
        sb.AppendLine(new string('-', 80));
        
        foreach (var result in results.Where(r => r.Success).OrderBy(r => r.AIC))
        {
            string marker = result.ModelName == bestResult.ModelName ? " *" : "";
            sb.AppendLine($"{result.ModelName + marker,-25} {result.Category,-12} {result.R2,10:F4} {result.MSE,10:F2} {result.AIC,10:F2} {result.EstimatedTotalBugs,10:F1}");
        }
        sb.AppendLine();
        sb.AppendLine("  * = 推奨モデル（AIC最小）");
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
        sb.AppendLine($"{"マイルストーン",-12} {"予測日数",10} {"残り日数",10} {"予測日付",15} {"バグ数",10}");
        sb.AppendLine(new string('-', 60));
        
        foreach (var (name, pred) in bestResult.ConvergencePredictions)
        {
            string dayStr = pred.AlreadyReached ? "到達済み" 
                : pred.PredictedDay?.ToString("F1") ?? "予測不可";
            string remainStr = pred.AlreadyReached ? "-" 
                : pred.RemainingDays?.ToString("F1") ?? "-";
            string dateStr = pred.PredictedDate?.ToString("yyyy/MM/dd") ?? "-";
            
            sb.AppendLine($"{name,-12} {dayStr,10} {remainStr,10} {dateStr,15} {pred.BugsAtPoint,10:F1}");
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
    /// レポートをファイルに保存
    /// </summary>
    public void SaveReport(string filePath, List<FittingResult> results, FittingResult bestResult)
    {
        string report = GenerateReport(results, bestResult);
        File.WriteAllText(filePath, report, Encoding.UTF8);
    }
}
