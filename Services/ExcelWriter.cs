using ClosedXML.Excel;
using BugConvergenceTool.Models;

namespace BugConvergenceTool.Services;

/// <summary>
/// 結果をExcelに出力するサービス
/// </summary>
public class ExcelWriter
{
    private readonly TestData _testData;
    
    public ExcelWriter(TestData testData)
    {
        _testData = testData;
    }
    
    /// <summary>
    /// テンプレートを使用して結果Excelを生成
    /// </summary>
    public void WriteResults(
        string templatePath, 
        string outputPath, 
        List<FittingResult> results, 
        FittingResult bestResult)
    {
        using var workbook = new XLWorkbook(templatePath);
        
        // データ入力シートに累積データを書き込み
        WriteDataSheet(workbook);
        
        // モデル選択シートに結果を書き込み
        WriteModelSheet(workbook, results, bestResult);
        
        // 予測データシートを作成
        WritePredictionSheet(workbook, bestResult);
        
        workbook.SaveAs(outputPath);
    }
    
    private void WriteDataSheet(XLWorkbook workbook)
    {
        var ws = workbook.Worksheet("データ入力");
        
        int n = _testData.DayCount;
        var cumulativePlanned = _testData.GetCumulativePlanned();
        var cumulativeActual = _testData.GetCumulativeActual();
        var cumulativeFound = _testData.GetCumulativeBugsFound();
        var cumulativeFixed = _testData.GetCumulativeBugsFixed();
        var remaining = _testData.GetRemainingBugs();
        
        // 累積データを行12-16に書き込み
        for (int i = 0; i < n; i++)
        {
            int col = i + 2;
            ws.Cell(12, col).Value = cumulativePlanned[i];
            ws.Cell(13, col).Value = cumulativeActual[i];
            ws.Cell(14, col).Value = cumulativeFound[i];
            ws.Cell(15, col).Value = cumulativeFixed[i];
            ws.Cell(16, col).Value = remaining[i];
            
            // 消化率
            if (_testData.TotalTestCases > 0)
            {
                ws.Cell(18, col).Value = cumulativePlanned[i] / _testData.TotalTestCases * 100;
                ws.Cell(19, col).Value = cumulativeActual[i] / _testData.TotalTestCases * 100;
            }
        }
    }
    
    private void WriteModelSheet(XLWorkbook workbook, List<FittingResult> results, FittingResult bestResult)
    {
        var ws = workbook.Worksheet("モデル選択");
        
        // 選択モデルの結果
        ws.Cell("B14").Value = bestResult.ModelName;
        ws.Cell("B15").Value = bestResult.Parameters.GetValueOrDefault("a", 0);
        ws.Cell("B16").Value = bestResult.Parameters.GetValueOrDefault("b", 0);
        ws.Cell("B17").Value = bestResult.Parameters.ContainsKey("c") 
            ? bestResult.Parameters["c"].ToString("F4") : "-";
        ws.Cell("B18").Value = bestResult.ImperfectDebugRate.HasValue 
            ? $"{bestResult.ImperfectDebugRate.Value * 100:F1}%" : "-";
        
        // 適合度指標
        ws.Cell("B20").Value = bestResult.R2;
        ws.Cell("B21").Value = bestResult.MSE;
        ws.Cell("B22").Value = bestResult.AIC;
        
        // 推定結果
        ws.Cell("B25").Value = bestResult.EstimatedTotalBugs;
        ws.Cell("B26").Value = bestResult.Parameters.GetValueOrDefault("b", 0);
        ws.Cell("B27").Value = bestResult.EstimatedTotalBugs - _testData.CurrentCumulativeBugs;
        
        // モデル比較結果（行30から）
        int startRow = 30;
        ws.Cell(startRow, 1).Value = "モデル比較結果";
        ws.Cell(startRow, 1).Style.Font.Bold = true;
        
        // ヘッダー
        var headers = new[] { "モデル名", "カテゴリ", "R²", "MSE", "AIC", "潜在バグ数", "不完全デバッグ率", "95%発見日", "99%発見日" };
        for (int i = 0; i < headers.Length; i++)
        {
            var cell = ws.Cell(startRow + 1, i + 1);
            cell.Value = headers[i];
            cell.Style.Font.Bold = true;
            cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#4472C4");
            cell.Style.Font.FontColor = XLColor.White;
        }
        
        // データ
        int row = startRow + 2;
        foreach (var result in results.Where(r => r.Success).OrderBy(r => r.AIC))
        {
            ws.Cell(row, 1).Value = result.ModelName;
            ws.Cell(row, 2).Value = result.Category;
            ws.Cell(row, 3).Value = result.R2;
            ws.Cell(row, 4).Value = result.MSE;
            ws.Cell(row, 5).Value = result.AIC;
            ws.Cell(row, 6).Value = result.EstimatedTotalBugs;
            ws.Cell(row, 7).Value = result.ImperfectDebugRate.HasValue 
                ? $"{result.ImperfectDebugRate.Value * 100:F1}%" : "-";
            
            // 収束予測
            if (result.ConvergencePredictions.TryGetValue("95%発見", out var pred95))
            {
                ws.Cell(row, 8).Value = pred95.AlreadyReached ? "到達済み" 
                    : pred95.PredictedDay?.ToString("F1") ?? "予測不可";
            }
            if (result.ConvergencePredictions.TryGetValue("99%発見", out var pred99))
            {
                ws.Cell(row, 9).Value = pred99.AlreadyReached ? "到達済み" 
                    : pred99.PredictedDay?.ToString("F1") ?? "予測不可";
            }
            
            // 最適モデルをハイライト
            if (result.ModelName == bestResult.ModelName)
            {
                ws.Range(row, 1, row, 9).Style.Fill.BackgroundColor = XLColor.LightGreen;
            }
            // 不完全デバッグモデルを別色
            else if (result.Category == "不完全デバッグ")
            {
                ws.Range(row, 1, row, 9).Style.Fill.BackgroundColor = XLColor.FromHtml("#FFE4B5");
            }
            
            row++;
        }
        
        // 収束予測セクション
        int convRow = row + 2;
        ws.Cell(convRow, 1).Value = "収束予測";
        ws.Cell(convRow, 1).Style.Font.Bold = true;
        
        ws.Cell(convRow + 1, 1).Value = "現在の発見率";
        ws.Cell(convRow + 1, 2).Value = $"{_testData.CurrentCumulativeBugs / bestResult.EstimatedTotalBugs * 100:F1}%";
        
        ws.Cell(convRow + 3, 1).Value = "マイルストーン";
        ws.Cell(convRow + 3, 2).Value = "予測日数";
        ws.Cell(convRow + 3, 3).Value = "残り日数";
        ws.Cell(convRow + 3, 4).Value = "予測日付";
        
        int predRow = convRow + 4;
        foreach (var (name, pred) in bestResult.ConvergencePredictions)
        {
            ws.Cell(predRow, 1).Value = name;
            ws.Cell(predRow, 2).Value = pred.AlreadyReached ? "到達済み" 
                : pred.PredictedDay?.ToString("F1") ?? "予測不可";
            ws.Cell(predRow, 3).Value = pred.AlreadyReached ? "-" 
                : pred.RemainingDays?.ToString("F1") ?? "-";
            ws.Cell(predRow, 4).Value = pred.PredictedDate?.ToString("yyyy/MM/dd") ?? "-";
            predRow++;
        }
        
        // 列幅調整
        ws.Column(1).Width = 25;
        ws.Column(2).Width = 15;
        ws.Columns(3, 9).Width = 12;
    }
    
    private void WritePredictionSheet(XLWorkbook workbook, FittingResult bestResult)
    {
        // 予測データシートがあれば使用、なければ作成
        IXLWorksheet ws;
        if (workbook.Worksheets.TryGetWorksheet("予測データ", out var existingWs))
        {
            ws = existingWs;
        }
        else
        {
            ws = workbook.AddWorksheet("予測データ");
        }
        
        int n = _testData.DayCount;
        int predDays = n * 2;
        
        // ヘッダー
        ws.Cell(1, 1).Value = "日数";
        ws.Cell(1, 2).Value = "実績（累積バグ）";
        ws.Cell(1, 3).Value = "予測値";
        ws.Cell(1, 4).Value = "潜在バグ総数";
        ws.Cell(1, 5).Value = "残存バグ予測";
        
        ws.Range(1, 1, 1, 5).Style.Font.Bold = true;
        ws.Range(1, 1, 1, 5).Style.Fill.BackgroundColor = XLColor.FromHtml("#4472C4");
        ws.Range(1, 1, 1, 5).Style.Font.FontColor = XLColor.White;

        // モデル取得（全拡張モデルから検索）
        var allModels = ModelFactory.GetAllExtendedModels(
            includeChangePoint: true,
            includeTEF: true,
            includeFRE: true,
            includeCoverage: true);
        var model = allModels.FirstOrDefault(m => m.Name == bestResult.ModelName)
            ?? ModelFactory.GetAllModels().First(m => m.Name == bestResult.ModelName);
        var parameters = bestResult.Parameters.Values.ToArray();
        var actualBugs = _testData.GetCumulativeBugsFound();
        
        for (int i = 1; i <= predDays; i++)
        {
            int row = i + 1;
            ws.Cell(row, 1).Value = i;
            
            if (i <= n)
            {
                ws.Cell(row, 2).Value = actualBugs[i - 1];
            }
            
            double predicted = model.Calculate(i, parameters);
            ws.Cell(row, 3).Value = predicted;
            ws.Cell(row, 4).Value = bestResult.EstimatedTotalBugs;
            ws.Cell(row, 5).Value = bestResult.EstimatedTotalBugs - predicted;
        }
        
        // 列幅調整
        ws.Columns().AdjustToContents();
    }
}
