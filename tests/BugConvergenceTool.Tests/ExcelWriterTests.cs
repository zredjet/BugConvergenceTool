using BugConvergenceTool.Models;
using BugConvergenceTool.Services;
using ClosedXML.Excel;

namespace BugConvergenceTool.Tests;

/// <summary>
/// 結果 Excel の「データ入力」シートの検証
/// </summary>
/// <remarks>
/// 結果 Excel はテンプレート（20日分のサンプルデータ入り）から作るため、
/// 入力データで置き換えないとサンプルのプロジェクト情報・日次データが残る。
/// </remarks>
public class ExcelWriterTests : IDisposable
{
    private const int TemplateDays = 20;

    private static readonly string TemplatePath = Path.Combine(AppContext.BaseDirectory, "Templates", "Template.xlsx");

    private readonly string _path = Path.Combine(Path.GetTempPath(), $"bct_writer_{Guid.NewGuid():N}.xlsx");

    public void Dispose()
    {
        if (File.Exists(_path)) File.Delete(_path);
    }

    private void Write(TestData data)
    {
        var result = new ModelFitter(data).FitModel(new ExponentialModel());
        new ExcelWriter(data).WriteResults(TemplatePath, _path, [result], result);
    }

    [Theory]
    [InlineData(10)]   // テンプレートより短い
    [InlineData(30)]   // テンプレートより長い
    public void DataSheet_ContainsInputData_AndCanBeReadBack(int days)
    {
        var data = TestHelpers.CreateGoelOkumotoData(days);
        Write(data);

        var readBack = new ExcelReader().ReadFromExcel(_path);

        Assert.Equal(data.ProjectName, readBack.ProjectName);
        Assert.Equal(data.TotalTestCases, readBack.TotalTestCases);
        Assert.Equal(data.StartDate, readBack.StartDate);
        Assert.Equal(data.Dates, readBack.Dates);
        Assert.Equal(data.PlannedDaily, readBack.PlannedDaily);
        Assert.Equal(data.ActualDaily, readBack.ActualDaily);
        Assert.Equal(data.BugsFoundDaily, readBack.BugsFoundDaily);
        Assert.Equal(data.BugsFixedDaily, readBack.BugsFixedDaily);
    }

    [Fact]
    public void DataSheet_ShorterThanTemplate_LeavesNoSampleValuesAfterLastDay()
    {
        const int days = 10;
        Write(TestHelpers.CreateGoelOkumotoData(days));

        using var workbook = new XLWorkbook(_path);
        var ws = workbook.Worksheet("データ入力");
        for (int row = 6; row <= 19; row++)
        {
            for (int col = days + 2; col <= TemplateDays + 1; col++)
            {
                Assert.True(ws.Cell(row, col).IsEmpty(), $"{ws.Cell(row, col).Address} にテンプレートの値が残っています");
            }
        }
    }

    [Fact]
    public void DataSheet_LongerThanTemplate_ExtendsTemplateFormatting()
    {
        const int days = 30;
        Write(TestHelpers.CreateGoelOkumotoData(days));

        using var workbook = new XLWorkbook(_path);
        var ws = workbook.Worksheet("データ入力");
        int lastCol = days + 1;
        Assert.Equal(ws.Cell(6, 2).Style.NumberFormat.Format, ws.Cell(6, lastCol).Style.NumberFormat.Format);
        Assert.Equal(ws.Cell(9, 2).Style.Fill.BackgroundColor, ws.Cell(9, lastCol).Style.Fill.BackgroundColor);
        Assert.Equal(ws.Column(2).Width, ws.Column(lastCol).Width);
    }

    [Fact]
    public void DataSheet_WithoutStartDate_ClearsTemplateStartDate()
    {
        var data = TestHelpers.CreateGoelOkumotoData(10);
        data.StartDate = null;
        Write(data);

        using var workbook = new XLWorkbook(_path);
        Assert.True(workbook.Worksheet("データ入力").Cell("B4").IsEmpty());
    }
}
