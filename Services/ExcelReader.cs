using ClosedXML.Excel;

namespace BugConvergenceTool.Services;

/// <summary>
/// Excelファイルからデータを読み込むサービス
/// </summary>
public class ExcelReader
{
    /// <summary>
    /// Excelファイルからテストデータを読み込む
    /// </summary>
    public TestData ReadFromExcel(string filePath)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException($"ファイルが見つかりません: {filePath}");
        
        using var workbook = new XLWorkbook(filePath);
        var worksheet = workbook.Worksheet("データ入力");
        
        if (worksheet == null)
            throw new InvalidOperationException("「データ入力」シートが見つかりません");
        
        var data = new TestData();
        
        // プロジェクト情報の読み込み
        data.ProjectName = worksheet.Cell("B2").GetString();
        data.TotalTestCases = worksheet.Cell("B3").GetValue<int>();
        
        var startDateCell = worksheet.Cell("B4");
        if (startDateCell.TryGetValue<DateTime>(out var startDate))
        {
            data.StartDate = startDate;
        }
        
        // データ範囲を特定（B列から右へ）
        int col = 2; // B列
        while (!worksheet.Cell(6, col).IsEmpty())
        {
            col++;
        }
        int dateCount = col - 2;
        
        // 観測期間 = バグ発生（行9）が入力されている最後の日まで。
        // 予定消化だけを先の日付まで入力したシートで、未来の日を「バグ 0 件の観測日」として読まないようにする
        int dataCount = 0;
        for (int i = 0; i < dateCount; i++)
        {
            if (!worksheet.Cell(9, i + 2).IsEmpty())
                dataCount = i + 1;
        }
        if (dataCount < dateCount)
        {
            data.Warnings.Add($"バグ発生数が未入力の末尾 {dateCount - dataCount} 日分（予定のみの日付）は観測期間に含めていません。");
        }
        
        if (dataCount < 3)
            throw new InvalidOperationException($"データが不足しています。最低3日分のデータが必要です。（バグ発生数が入力された日: {dataCount}日分）");
        
        // データの読み込み
        var blankFoundDays = new List<DateTime>();
        for (int i = 0; i < dataCount; i++)
        {
            int currentCol = i + 2; // B列から開始
            
            // 日付
            var dateCell = worksheet.Cell(6, currentCol);
            if (dateCell.TryGetValue<DateTime>(out var date))
            {
                data.Dates.Add(date);
            }
            else
            {
                // 日付がない場合は開始日から計算
                data.Dates.Add(data.StartDate?.AddDays(i) ?? DateTime.Today.AddDays(i));
            }
            
            // 予定消化数（日次） - 行7
            data.PlannedDaily.Add(GetCellValue(worksheet, 7, currentCol));
            
            // 実績消化数（日次） - 行8
            data.ActualDaily.Add(GetCellValue(worksheet, 8, currentCol));
            
            // バグ発生件数（日次） - 行9（観測期間内の空欄は 0 件として扱い、注意を出す）
            if (worksheet.Cell(9, currentCol).IsEmpty())
                blankFoundDays.Add(data.Dates[^1]);
            data.BugsFoundDaily.Add(GetCellValue(worksheet, 9, currentCol));
            
            // バグ修正件数（日次） - 行10
            data.BugsFixedDaily.Add(GetCellValue(worksheet, 10, currentCol));
        }
        
        if (blankFoundDays.Count > 0)
        {
            data.Warnings.Add($"観測期間内でバグ発生数が空欄の {blankFoundDays.Count} 日（{string.Join(", ", blankFoundDays.Take(5).Select(d => d.ToString("MM/dd")))}{(blankFoundDays.Count > 5 ? " など" : "")}）を 0 件として扱いました。");
        }
        if (data.UsesBusinessDays)
        {
            data.Warnings.Add("観測日に土日が含まれないため、予測日付は土日を除いた営業日で数えます（祝日は考慮しません）。");
        }
        
        return data;
    }
    
    private double GetCellValue(IXLWorksheet worksheet, int row, int col)
    {
        var cell = worksheet.Cell(row, col);
        if (cell.IsEmpty())
            return 0;
        
        if (cell.TryGetValue<double>(out var value))
            return value;
        
        return 0;
    }
}
