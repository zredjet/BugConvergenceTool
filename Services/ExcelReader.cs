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
        int dataCount = col - 2;
        
        if (dataCount < 3)
            throw new InvalidOperationException($"データが不足しています。最低3日分のデータが必要です。（現在: {dataCount}日分）");
        
        // データの読み込み
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
            
            // バグ発生件数（日次） - 行9
            data.BugsFoundDaily.Add(GetCellValue(worksheet, 9, currentCol));
            
            // バグ修正件数（日次） - 行10
            data.BugsFixedDaily.Add(GetCellValue(worksheet, 10, currentCol));
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
