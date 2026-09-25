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
    
    /// <summary>
    /// 「データ入力」シートに入力データ（観測期間）と累積値を書き込む
    /// </summary>
    /// <remarks>
    /// 以前は累積値（行12〜19）だけを書き込んでいたため、行2〜10 にテンプレートのサンプルデータ
    /// （プロジェクト情報と20日分の日次データ）が残り、累積値と食い違っていた。
    /// </remarks>
    private void WriteDataSheet(XLWorkbook workbook)
    {
        var ws = workbook.Worksheet("データ入力");

        int n = _testData.DayCount;
        var cumulativePlanned = _testData.GetCumulativePlanned();
        var cumulativeActual = _testData.GetCumulativeActual();
        var cumulativeFound = _testData.GetCumulativeBugsFound();
        var cumulativeFixed = _testData.GetCumulativeBugsFixed();
        var remaining = _testData.GetRemainingBugs();

        // プロジェクト情報
        ws.Cell("B2").Value = _testData.ProjectName;
        ws.Cell("B3").Value = _testData.TotalTestCases;
        ws.Cell("B4").Value = _testData.StartDate.HasValue ? _testData.StartDate.Value : Blank.Value;

        // テンプレートの日次データ（行6〜19）を消す。書式は残す
        int templateLastCol = Math.Max(2, ws.Row(6).LastCellUsed()?.Address.ColumnNumber ?? 2);
        ws.Range(6, 2, 19, Math.Max(templateLastCol, n + 1)).Clear(XLClearOptions.Contents);

        // テンプレートより日数が多い場合は、最終列の書式と列幅を引き継ぐ
        for (int col = templateLastCol + 1; col <= n + 1; col++)
        {
            for (int row = 6; row <= 19; row++)
                ws.Cell(row, col).Style = ws.Cell(row, templateLastCol).Style;
            ws.Column(col).Width = ws.Column(templateLastCol).Width;
        }

        for (int i = 0; i < n; i++)
        {
            int col = i + 2;

            // 日次データ（行6〜10）
            ws.Cell(6, col).Value = _testData.Dates[i];
            ws.Cell(7, col).Value = _testData.PlannedDaily[i];
            ws.Cell(8, col).Value = _testData.ActualDaily[i];
            ws.Cell(9, col).Value = _testData.BugsFoundDaily[i];
            ws.Cell(10, col).Value = _testData.BugsFixedDaily[i];

            // 累積データ（行12〜16）
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
    
    /// <summary>
    /// 発見率の欄の値（b がないモデルは変化点前の b₁。どちらもなければ "-"）
    /// </summary>
    /// <remarks>
    /// 以前は Parameters["b"] を既定値 0 で読んでいたため、変化点モデルなどでは b=0 と表示されていた。
    /// </remarks>
    private static XLCellValue DetectionRateCell(FittingResult result)
    {
        var name = DetectionRateParameterName(result);
        return name != null ? result.Parameters[name] : "-";
    }
    
    private static string? DetectionRateParameterName(FittingResult result) =>
        new[] { "b", "b₁", "b1" }.FirstOrDefault(result.Parameters.ContainsKey);
    
    /// <summary>
    /// 「その他のパラメータ」欄の値（a・発見率の欄のパラメータ・c 以外。なければ "-"）
    /// </summary>
    private static string OtherParametersText(FittingResult result)
    {
        var shown = new HashSet<string?> { "a", "c", DetectionRateParameterName(result) };
        var others = result.Parameters
            .Where(p => !shown.Contains(p.Key))
            .Select(p => $"{p.Key}={p.Value:G4}")
            .ToList();
        return others.Count > 0 ? string.Join(", ", others) : "-";
    }
    
    /// <summary>
    /// 「利用可能なモデル」欄（行5〜12）を、実装されている基本モデルの一覧で書き直す
    /// </summary>
    /// <remarks>
    /// 以前はテンプレートの記述をそのまま残していたため、削除済みのモデル（修正ゴンペルツ・ロジスティック・
    /// 不完全デバッグ系）や旧式のゴンペルツの式が表示されていた。
    /// </remarks>
    public static void WriteAvailableModels(IXLWorksheet ws)
    {
        const int firstRow = 5;
        const int lastRow = 12;
        ws.Range(firstRow, 1, lastRow, 4).Clear(XLClearOptions.Contents);
        
        int row = firstRow;
        foreach (var model in ModelFactory.GetBasicModels())
        {
            ws.Cell(row, 1).Value = model.Name;
            ws.Cell(row, 2).Value = model.Formula;
            ws.Cell(row, 3).Value = model.Category;
            ws.Cell(row, 4).Value = model.Description;
            row++;
        }
        
        ws.Cell(row, 1).Value = "拡張モデル";
        ws.Cell(row, 2).Value = "--change-point / --tef / --fre で追加";
        ws.Cell(row, 3).Value = "変化点・TEF組込・欠陥除去効率";
        ws.Cell(row, 4).Value = "数式は README を参照";
        
        ws.Cell("A16").Value = "パラメータb（変化点モデルは b₁）";
        ws.Cell("A18").Value = "その他のパラメータ";
    }
    
    private void WriteModelSheet(XLWorkbook workbook, List<FittingResult> results, FittingResult bestResult)
    {
        var ws = workbook.Worksheet("モデル選択");
        WriteAvailableModels(ws);
        
        // 選択モデルの結果
        ws.Cell("B14").Value = bestResult.ModelName;
        ws.Cell("B15").Value = bestResult.Parameters.GetValueOrDefault("a", 0);
        ws.Cell("B16").Value = DetectionRateCell(bestResult);
        ws.Cell("B17").Value = bestResult.Parameters.ContainsKey("c") 
            ? bestResult.Parameters["c"].ToString("F4") : "-";
        ws.Cell("B18").Value = OtherParametersText(bestResult);
        
        // 適合度指標
        ws.Cell("B20").Value = bestResult.R2;
        ws.Cell("B21").Value = bestResult.MSE;
        ws.Cell("B22").Value = bestResult.AIC;
        
        // 推定結果
        ws.Cell("B25").Value = bestResult.EstimatedTotalBugs;
        ws.Cell("B26").Value = DetectionRateCell(bestResult);
        ws.Cell("B27").Value = bestResult.EstimatedTotalBugs - _testData.CurrentCumulativeBugs;
        
        // モデル比較結果（行30から）
        int startRow = 30;
        ws.Cell(startRow, 1).Value = "モデル比較結果";
        ws.Cell(startRow, 1).Style.Font.Bold = true;
        
        // ヘッダー
        // ヘッダー（ホールドアウト検証の列を追加）
        var hasHoldout = results.Any(r => r.Holdout != null);
        var headers = hasHoldout 
            ? new[] { "モデル名", "カテゴリ", "比較グループ", "R²", "MSE", "AIC", "AICc", "選択基準", "Δ(グループ内)", "潜在バグ数", "HO予測発見数", "HO実測発見数", "HO誤差(%)", "HO日次MAE", "損失関数", "95%発見日", "99%発見日" }
            : new[] { "モデル名", "カテゴリ", "比較グループ", "R²", "MSE", "AIC", "AICc", "選択基準", "Δ(グループ内)", "潜在バグ数", "95%発見日", "99%発見日" };
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
        // AIC は比較グループ内でのみ比較可能なため、グループ順・グループ内の選択基準値順に並べる
        var ranked = ModelComparisonGroup.GroupAndRank(results)
            .SelectMany(g => g.Results.Select(r => (Result: r, Delta: r.SelectionScore - g.Results[0].SelectionScore)));
        foreach (var (result, delta) in ranked)
        {
            int col = 1;
            ws.Cell(row, col++).Value = result.ModelName;
            ws.Cell(row, col++).Value = result.Category;
            ws.Cell(row, col++).Value = result.ComparisonGroup;
            ws.Cell(row, col++).Value = result.R2;
            ws.Cell(row, col++).Value = result.MSE;
            ws.Cell(row, col++).Value = result.AIC;
            ws.Cell(row, col++).Value = result.AICc;
            ws.Cell(row, col++).Value = result.ModelSelectionCriterion;
            ws.Cell(row, col++).Value = delta;
            ws.Cell(row, col++).Value = result.EstimatedTotalBugs;
            
            // ホールドアウト検証結果
            if (hasHoldout)
            {
                var h = result.Holdout;
                ws.Cell(row, col++).Value = h != null ? h.PredictedIncrement : "-";
                ws.Cell(row, col++).Value = h != null ? h.ActualIncrement : "-";
                ws.Cell(row, col++).Value = result.HoldoutIncrementErrorPercent.HasValue ? result.HoldoutIncrementErrorPercent.Value : "-";
                ws.Cell(row, col++).Value = h != null ? h.DailyMae : "-";
                ws.Cell(row, col++).Value = result.LossFunctionUsed;
            }
            
            // 収束予測
            if (result.ConvergencePredictions.TryGetValue("95%発見", out var pred95))
            {
                ws.Cell(row, col).Value = pred95.AlreadyReached ? "到達済み" 
                    : pred95.PredictedDay?.ToString("F1") ?? "予測不可";
            }
            col++;
            
            if (result.ConvergencePredictions.TryGetValue("99%発見", out var pred99))
            {
                ws.Cell(row, col).Value = pred99.AlreadyReached ? "到達済み" 
                    : pred99.PredictedDay?.ToString("F1") ?? "予測不可";
            }
            
            int maxCol = col;
            
            // 最適モデルをハイライト
            if (result.ModelName == bestResult.ModelName)
            {
                ws.Range(row, 1, row, maxCol).Style.Fill.BackgroundColor = XLColor.LightGreen;
            }
            
            row++;
        }
        
        // 収束予測セクション
        int convRow = row + 2;
        ws.Cell(convRow, 1).Value = "収束予測";
        ws.Cell(convRow, 1).Style.Font.Bold = true;
        
        ws.Cell(convRow + 1, 1).Value = "現在の発見率";
        ws.Cell(convRow + 1, 2).Value = ConvergenceAssessment.FormatRatio(
            ConvergenceAssessment.Evaluate(_testData.CurrentCumulativeBugs, bestResult.EstimatedTotalBugs).Ratio);
        
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

        // 推定に使ったモデルのインスタンスとパラメータ列（順序を保持）
        var model = bestResult.Model
            ?? throw new InvalidOperationException($"{bestResult.ModelName} のモデルインスタンスがありません");
        var parameters = bestResult.ParameterVector;
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
