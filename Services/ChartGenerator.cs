using BugConvergenceTool.Models;
using ScottPlot;

namespace BugConvergenceTool.Services;

/// <summary>
/// グラフ画像を生成するサービス
/// </summary>
public class ChartGenerator
{
    private readonly TestData _testData;
    private readonly int _width = 800;
    private readonly int _height = 500;
    
    public ChartGenerator(TestData testData)
    {
        _testData = testData;
    }
    
    /// <summary>
    /// 全グラフを生成
    /// </summary>
    public void GenerateAllCharts(string outputDir, FittingResult? bestResult = null)
    {
        Directory.CreateDirectory(outputDir);
        
        GenerateTestProgressChart(Path.Combine(outputDir, "test_progress.png"));
        GenerateBugCumulativeChart(Path.Combine(outputDir, "bug_cumulative.png"));
        GenerateRemainingBugsChart(Path.Combine(outputDir, "remaining_bugs.png"));
        GenerateBugConvergenceChart(Path.Combine(outputDir, "bug_convergence.png"));
        
        if (bestResult != null && bestResult.Success)
        {
            GenerateReliabilityGrowthChart(Path.Combine(outputDir, "reliability_growth.png"), bestResult);
        }
    }
    
    /// <summary>
    /// テスト消化曲線（バーンダウン）
    /// </summary>
    public void GenerateTestProgressChart(string filePath)
    {
        var plt = new Plot();
        
        // プロットのフォントを日本語対応フォントに設定
        plt.Font.Set(JapaneseFont);
        
        var days = Enumerable.Range(1, _testData.DayCount).Select(i => (double)i).ToArray();
        var planned = _testData.GetCumulativePlanned();
        var actual = _testData.GetCumulativeActual();
        
        var plannedPlot = plt.Add.Scatter(days, planned);
        plannedPlot.LegendText = "予定消化（累積）";
        plannedPlot.LineWidth = 2;
        plannedPlot.LineStyle.Pattern = LinePattern.Dashed;
        plannedPlot.MarkerSize = 0;
        
        var actualPlot = plt.Add.Scatter(days, actual);
        actualPlot.LegendText = "実績消化（累積）";
        actualPlot.LineWidth = 2;
        actualPlot.MarkerSize = 5;
        
        plt.Title("テスト消化曲線（バーンダウン）");
        plt.XLabel("日数");
        plt.YLabel("累積消化数");
        plt.Legend.IsVisible = true;
        plt.Legend.Alignment = Alignment.LowerRight;
        
        plt.SavePng(filePath, _width, _height);
    }
    
    /// <summary>
    /// バグ累積曲線
    /// </summary>
    public void GenerateBugCumulativeChart(string filePath)
    {
        var plt = new Plot();
        
        // プロットのフォントを日本語対応フォントに設定
        plt.Font.Set(JapaneseFont);
        
        var days = Enumerable.Range(1, _testData.DayCount).Select(i => (double)i).ToArray();
        var found = _testData.GetCumulativeBugsFound();
        var fixedBugs = _testData.GetCumulativeBugsFixed();
        
        var foundPlot = plt.Add.Scatter(days, found);
        foundPlot.LegendText = "バグ発生（累積）";
        foundPlot.LineWidth = 2;
        foundPlot.MarkerSize = 5;
        foundPlot.Color = Colors.Red;
        
        var fixedPlot = plt.Add.Scatter(days, fixedBugs);
        fixedPlot.LegendText = "バグ修正（累積）";
        fixedPlot.LineWidth = 2;
        fixedPlot.MarkerSize = 5;
        fixedPlot.Color = Colors.Green;
        
        plt.Title("バグ累積曲線");
        plt.XLabel("日数");
        plt.YLabel("累積件数");
        plt.Legend.IsVisible = true;
        plt.Legend.Alignment = Alignment.LowerRight;
        
        plt.SavePng(filePath, _width, _height);
    }
    
    /// <summary>
    /// 残存バグ数推移
    /// </summary>
    public void GenerateRemainingBugsChart(string filePath)
    {
        var plt = new Plot();
        
        // プロットのフォントを日本語対応フォントに設定
        plt.Font.Set(JapaneseFont);
        
        var days = Enumerable.Range(1, _testData.DayCount).Select(i => (double)i).ToArray();
        var remaining = _testData.GetRemainingBugs();
        
        var remainingPlot = plt.Add.Scatter(days, remaining);
        remainingPlot.LegendText = "残存バグ数";
        remainingPlot.LineWidth = 2;
        remainingPlot.MarkerSize = 5;
        remainingPlot.Color = Colors.Orange;
        
        plt.Title("残存バグ数推移");
        plt.XLabel("日数");
        plt.YLabel("残存バグ数");
        plt.Legend.IsVisible = true;
        plt.Legend.Alignment = Alignment.UpperRight;
        
        plt.SavePng(filePath, _width, _height);
    }
    
    /// <summary>
    /// バグ収束確認グラフ（統合）
    /// </summary>
    public void GenerateBugConvergenceChart(string filePath)
    {
        var plt = new Plot();
        
        // プロットのフォントを日本語対応フォントに設定
        plt.Font.Set(JapaneseFont);
        
        var days = Enumerable.Range(1, _testData.DayCount).Select(i => (double)i).ToArray();
        var found = _testData.GetCumulativeBugsFound();
        var fixedBugs = _testData.GetCumulativeBugsFixed();
        var remaining = _testData.GetRemainingBugs();
        
        var foundPlot = plt.Add.Scatter(days, found);
        foundPlot.LegendText = "バグ発生（累積）";
        foundPlot.LineWidth = 2;
        foundPlot.MarkerSize = 4;
        foundPlot.Color = Colors.Red;
        
        var fixedPlot = plt.Add.Scatter(days, fixedBugs);
        fixedPlot.LegendText = "バグ修正（累積）";
        fixedPlot.LineWidth = 2;
        fixedPlot.MarkerSize = 4;
        fixedPlot.Color = Colors.Green;
        
        var remainingPlot = plt.Add.Scatter(days, remaining);
        remainingPlot.LegendText = "残存バグ数";
        remainingPlot.LineWidth = 2;
        remainingPlot.MarkerSize = 4;
        remainingPlot.Color = Colors.Orange;
        
        plt.Title("バグ収束確認グラフ");
        plt.XLabel("日数");
        plt.YLabel("件数");
        plt.Legend.IsVisible = true;
        plt.Legend.Alignment = Alignment.UpperLeft;
        
        plt.SavePng(filePath, _width, _height);
    }
    
    /// <summary>
    /// 信頼度成長曲線（フィッティング結果）
    /// </summary>
    public void GenerateReliabilityGrowthChart(string filePath, FittingResult result)
    {
        var plt = new Plot();
        
        // プロットのフォントを日本語対応フォントに設定
        plt.Font.Set(JapaneseFont);
        
        int n = _testData.DayCount;
        var model = GetModelFromResult(result);
        var band = result.ConfidenceBand?.Succeeded > 0 ? result.ConfidenceBand : null;
        var pi = result.PredictionInterval?.Succeeded > 0 ? result.PredictionInterval : null;
        
        // 実績データ
        var actualDays = Enumerable.Range(1, n).Select(i => (double)i).ToArray();
        var actualBugs = _testData.GetCumulativeBugsFound();
        
        // 予測曲線は観測期間の2倍まで（信頼区間があればその期間）描く
        double[] xAxis = band?.Times ?? Enumerable.Range(1, n * 2).Select(i => (double)i).ToArray();
        double[] predBugs = xAxis.Select(t => model.Calculate(t, result.ParameterVector)).ToArray();
        
        // 1. 予測区間帯（将来の累積発見数。パラメータの不確実性 + Poisson 変動）
        if (pi != null)
        {
            var piFill = plt.Add.FillY(pi.FutureTimes, pi.Lower, pi.Upper);
            piFill.FillColor = Colors.Orange.WithAlpha(0.15);
            piFill.LegendText = $"{pi.ConfidenceLevel:P0}予測区間（観測済み件数を起点とした将来の累積発見数）";
        }
        
        // 2. 信頼区間帯（m(t)。パラメータの不確実性のみ）
        if (band != null)
        {
            var fill = plt.Add.FillY(band.Times, band.Lower, band.Upper);
            fill.FillColor = Colors.Red.WithAlpha(0.2);
            fill.LegendText = $"{band.ConfidenceLevel:P0}信頼区間（期待値 m(t)）";
        }
        
        // 3. 実績データ
        var actualPlot = plt.Add.Scatter(actualDays, actualBugs);
        actualPlot.LegendText = "実績（累積バグ）";
        actualPlot.LineWidth = 0;
        actualPlot.MarkerSize = 8;
        actualPlot.Color = Colors.Blue;
        
        // 4. 予測曲線
        var predPlot = plt.Add.Scatter(xAxis, predBugs);
        predPlot.LegendText = $"予測曲線（{result.ModelName}）";
        predPlot.LineWidth = 2;
        predPlot.MarkerSize = 0;
        predPlot.Color = Colors.Red;
        predPlot.LineStyle.Pattern = LinePattern.Dashed;
        
        // 5. 潜在バグ総数ライン
        double totalBugs = result.EstimatedTotalBugs;
        var totalLine = plt.Add.HorizontalLine(totalBugs);
        totalLine.LegendText = $"推定潜在バグ総数 ({totalBugs:F0})";
        totalLine.LineWidth = 1;
        totalLine.Color = Colors.Gray;
        totalLine.LineStyle.Pattern = LinePattern.Dotted;
        
        plt.Title($"信頼度成長曲線（{result.ModelName}）");
        plt.XLabel("日数");
        plt.YLabel("累積バグ数");
        plt.Legend.IsVisible = true;
        plt.Legend.Alignment = Alignment.LowerRight;
        
        string annotation = $"R² = {result.R2:F4}\n推定残バグ: {totalBugs - actualBugs.Last():F1}";
        if (band?.TotalBugs != null)
        {
            annotation += $"\n潜在バグ総数 {band.ConfidenceLevel:P0}区間: [{band.TotalBugs.Lower:F0}, {band.TotalBugs.Upper:F0}]";
        }
        plt.Add.Annotation(annotation, Alignment.UpperLeft);
        
        plt.SavePng(filePath, _width, _height);
    }

    /// <summary>
    /// 日本語を表示できるインストール済みフォント（OS によって異なるため文字から検出する。
    /// 以前は Windows 専用の "Yu Gothic UI" に固定していたため、他の OS では文字化けしていた）
    /// </summary>
    private static readonly string JapaneseFont = Fonts.Detect("累積バグ数の予測区間");
    
    private static ReliabilityGrowthModelBase GetModelFromResult(FittingResult result)
    {
        // 推定に使ったモデルのインスタンス（TEF の工数データ等を保持）
        return result.Model
            ?? throw new InvalidOperationException($"{result.ModelName} のモデルインスタンスがありません");
    }
}
