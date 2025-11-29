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
        plt.Font.Set("Yu Gothic UI"); // または "Meiryo", "MS Gothic" など
        
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
        plt.Axes.Title.Label.FontName = "Yu Gothic UI";
        plt.XLabel("日数");
        plt.Axes.Bottom.Label.FontName = "Yu Gothic UI";
        plt.YLabel("累積消化数");
        plt.Axes.Left.Label.FontName = "Yu Gothic UI";
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
        plt.Font.Set("Yu Gothic UI"); // または "Meiryo", "MS Gothic" など
        
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
        plt.Font.Set("Yu Gothic UI"); // または "Meiryo", "MS Gothic" など
        
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
        plt.Font.Set("Yu Gothic UI"); // または "Meiryo", "MS Gothic" など
        
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
        plt.Font.Set("Yu Gothic UI"); // または "Meiryo", "MS Gothic" など
        
        int n = _testData.DayCount;
        int predDays = (int)(n * 2); // 2倍の期間まで予測
        
        // 実績データ
        var actualDays = Enumerable.Range(1, n).Select(i => (double)i).ToArray();
        var actualBugs = _testData.GetCumulativeBugsFound();
        
        // X軸は PredictionTimes を優先して使用（信頼区間と合わせるため）
        double[] xAxis;
        double[] predBugs;
        var model = GetModelFromResult(result);
        
        if (result.PredictionTimes != null && result.PredictionTimes.Length > 0)
        {
            // FittingResult に格納された予測時刻を使用
            xAxis = result.PredictionTimes;
            predBugs = result.PredictedValues;
        }
        else
        {
            // 従来の方式：2倍の期間まで予測
            xAxis = Enumerable.Range(1, predDays).Select(i => (double)i).ToArray();
            var parameters = result.Parameters.Values.ToArray();
            predBugs = xAxis.Select(t => model.Calculate(t, parameters)).ToArray();
        }
        
        // 1. 信頼区間帯（ある場合）
        if (result.LowerConfidenceBounds != null &&
            result.UpperConfidenceBounds != null &&
            result.LowerConfidenceBounds.Length == xAxis.Length)
        {
            var fill = plt.Add.FillY(
                xAxis,
                result.LowerConfidenceBounds,
                result.UpperConfidenceBounds
            );
            
            // 半透明の赤色で塗りつぶし（アルファ値 0-1 の範囲）
            fill.FillColor = Colors.Red.WithAlpha(0.2);
            fill.LegendText = "95%予測区間";
        }
        
        // 2. 実績データ
        var actualPlot = plt.Add.Scatter(actualDays, actualBugs);
        actualPlot.LegendText = "実績（累積バグ）";
        actualPlot.LineWidth = 0;
        actualPlot.MarkerSize = 8;
        actualPlot.Color = Colors.Blue;
        
        // 3. 予測曲線
        var predPlot = plt.Add.Scatter(xAxis, predBugs);
        predPlot.LegendText = $"予測曲線（{result.ModelName}）";
        predPlot.LineWidth = 2;
        predPlot.MarkerSize = 0;
        predPlot.Color = Colors.Red;
        predPlot.LineStyle.Pattern = LinePattern.Dashed;
        
        // 4. 潜在バグ総数ライン
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
        
        // 注釈（信頼区間がある場合はその旨を追記）
        string annotation = $"R² = {result.R2:F4}\n推定残バグ: {totalBugs - actualBugs.Last():F1}";
        if (result.LowerConfidenceBounds != null)
        {
            annotation += "\n（95%信頼区間付き）";
        }
        plt.Add.Annotation(annotation, Alignment.UpperLeft);
        
        plt.SavePng(filePath, _width, _height);
    }

    private ReliabilityGrowthModelBase GetModelFromResult(FittingResult result)
    {
        // 全拡張モデルから検索
        var allModels = ModelFactory.GetAllExtendedModels(
            includeChangePoint: true,
            includeTEF: true,
            includeFRE: true,
            includeCoverage: true);
        return allModels.FirstOrDefault(m => m.Name == result.ModelName)
            ?? ModelFactory.GetAllModels().First(m => m.Name == result.ModelName);
    }
}
