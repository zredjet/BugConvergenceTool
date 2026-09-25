using BugConvergenceTool.Models;
using BugConvergenceTool.Services;
using ClosedXML.Excel;

namespace BugConvergenceTool.Tests;

/// <summary>
/// Excel 読み込み（観測期間の判定）と日付の対応（営業日）の検証
/// </summary>
public class ExcelReaderTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"bct_test_{Guid.NewGuid():N}.xlsx");

    public void Dispose()
    {
        if (File.Exists(_path)) File.Delete(_path);
    }

    /// <summary>
    /// 平日だけの日付で、観測 found.Length 日 + 予定のみ plannedOnlyDays 日のシートを作る（null は空欄）
    /// </summary>
    private void CreateSheet(DateTime start, double?[] found, int plannedOnlyDays)
    {
        using var workbook = new XLWorkbook();
        var ws = workbook.AddWorksheet("データ入力");
        ws.Cell("B2").Value = "テスト";
        ws.Cell("B3").Value = 100;
        ws.Cell("B4").Value = start;

        var date = start;
        for (int i = 0; i < found.Length + plannedOnlyDays; i++)
        {
            while (date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) date = date.AddDays(1);
            int col = i + 2;
            ws.Cell(6, col).Value = date;
            ws.Cell(7, col).Value = 10;                      // 予定消化（未来の日も入力済み）
            if (i < found.Length)
            {
                ws.Cell(8, col).Value = 10;
                if (found[i].HasValue) ws.Cell(9, col).Value = found[i]!.Value;
                ws.Cell(10, col).Value = 0;
            }
            date = date.AddDays(1);
        }
        workbook.SaveAs(_path);
    }

    [Fact]
    public void FutureDatesWithoutBugCounts_AreNotReadAsZeroBugDays()
    {
        // 以前は日付行が続く限り読み込み、未入力の未来の日を「バグ 0 件の観測日」として扱っていた
        var found = Enumerable.Range(0, 15).Select(i => (double?)(10 - i / 2)).ToArray();
        CreateSheet(new DateTime(2025, 1, 6), found, plannedOnlyDays: 10);

        var data = new ExcelReader().ReadFromExcel(_path);

        Assert.Equal(15, data.DayCount);
        Assert.Contains(data.Warnings, w => w.Contains("10 日分"));
    }

    [Fact]
    public void FormulaCellsReturningEmptyString_AreTreatedAsBlank()
    {
        // =IF(...,"") のように空文字を返す数式を未来の日まで入れたシートでも、未来の日を観測日としない
        var found = Enumerable.Range(0, 12).Select(i => (double?)(8 - i / 2)).ToArray();
        CreateSheet(new DateTime(2025, 1, 6), found, plannedOnlyDays: 6);
        using (var workbook = new XLWorkbook(_path))
        {
            var ws = workbook.Worksheet("データ入力");
            for (int col = 14; col < 14 + 6; col++)
                ws.Cell(9, col).FormulaA1 = "IF(TRUE,\"\",1)";
            workbook.Save();
        }

        var data = new ExcelReader().ReadFromExcel(_path);

        Assert.Equal(12, data.DayCount);
    }

    [Fact]
    public void BlankBugCountInsideObservedPeriod_IsZeroWithWarning()
    {
        var found = new double?[] { 5, 4, null, 3, 2, 2 };
        CreateSheet(new DateTime(2025, 1, 6), found, plannedOnlyDays: 0);

        var data = new ExcelReader().ReadFromExcel(_path);

        Assert.Equal(6, data.DayCount);
        Assert.Equal(0, data.BugsFoundDaily[2]);
        Assert.Contains(data.Warnings, w => w.Contains("空欄"));
    }

    [Fact]
    public void WeekdayOnlyData_ExtrapolatesDatesByBusinessDays()
    {
        // 2025/1/6(月)〜1/24(金) の平日 15 日。16 日目は 1/27(月)、20 日目は 1/31(金)
        var found = Enumerable.Range(0, 15).Select(_ => (double?)3).ToArray();
        CreateSheet(new DateTime(2025, 1, 6), found, plannedOnlyDays: 0);
        var data = new ExcelReader().ReadFromExcel(_path);

        Assert.True(data.UsesBusinessDays);
        Assert.Equal(new DateTime(2025, 1, 24), data.DateForDay(15));
        Assert.Equal(new DateTime(2025, 1, 27), data.DateForDay(16));
        Assert.Equal(new DateTime(2025, 1, 31), data.DateForDay(20));
        Assert.Equal(new DateTime(2025, 1, 27), data.DateForDay(15.2)); // 15.2 日目は 16 日目に含まれる
    }

    [Fact]
    public void CalendarData_ExtrapolatesDatesByCalendarDays()
    {
        var data = TestHelpers.CreateGoelOkumotoData();   // 2025/1/6 から毎日（土日を含む）

        Assert.False(data.UsesBusinessDays);
        Assert.Equal(data.Dates[^1].AddDays(5), data.DateForDay(data.DayCount + 5));
    }

    [Fact]
    public void ConvergencePredictionDates_UseBusinessDays()
    {
        var found = Enumerable.Range(0, 20).Select(i => (double?)Math.Max(1, 12 - i / 2)).ToArray();
        CreateSheet(new DateTime(2025, 1, 6), found, plannedOnlyDays: 0);
        var data = new ExcelReader().ReadFromExcel(_path);

        var result = new ModelFitter(data).FitModel(new ExponentialModel());

        foreach (var prediction in result.ConvergencePredictions.Values.Where(p => p.PredictedDate.HasValue))
            Assert.DoesNotContain(prediction.PredictedDate!.Value.DayOfWeek, new[] { DayOfWeek.Saturday, DayOfWeek.Sunday });
    }
}
