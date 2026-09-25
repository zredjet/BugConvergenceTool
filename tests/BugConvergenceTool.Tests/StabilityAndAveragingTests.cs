using BugConvergenceTool.Models;
using BugConvergenceTool.Services;

namespace BugConvergenceTool.Tests;

/// <summary>
/// 推定の安定性（末尾打ち切り）とモデル平均化（無条件標準誤差・マイルストーン）の検証
/// </summary>
public class StabilityAndAveragingTests
{
    [Fact]
    public void Stability_IsStableForCleanConvergingData()
    {
        var data = TestHelpers.CreateGoelOkumotoData();
        var fitter = new ModelFitter(data);
        var result = fitter.FitModel(new ExponentialModel());

        var stability = fitter.AnalyzeStability(result)!;

        Assert.Equal(5, stability.Points.Count);
        Assert.All(stability.Points, p => Assert.NotNull(p.TotalBugs));
        Assert.True(stability.RelativeRange < StabilityAnalysisResult.StableThreshold, $"変動幅 {stability.RelativeRange:P1}");
        Assert.Equal("安定", stability.Assessment);
    }

    [Fact]
    public void Stability_FlagsDataWithoutConvergence()
    {
        // 最後の数日だけ発見数が減ったデータ: 末尾を除くと収束の兆候が消え、総数を推定できない
        var data = new TestData { StartDate = new DateTime(2025, 1, 6) };
        for (int i = 0; i < 25; i++)
        {
            data.Dates.Add(data.StartDate.Value.AddDays(i));
            data.PlannedDaily.Add(0);
            data.ActualDaily.Add(0);
            data.BugsFoundDaily.Add(i < 22 ? 6 : 1);
            data.BugsFixedDaily.Add(0);
        }
        var fitter = new ModelFitter(data);
        var result = fitter.FitModel(new ExponentialModel());

        var stability = fitter.AnalyzeStability(result)!;

        Assert.Equal("不安定", stability.Assessment);
        Assert.NotEmpty(stability.Warnings);
    }

    [Fact]
    public void ModelAveraging_IncludesParameterUncertaintyAndReachedMilestones()
    {
        // GO データ（40日で真の発見率 86%）: 80% 発見はすでに到達済みのモデルが多い。
        // 以前は到達済みのモデルを平均から除いていた。また総数の不確実性はモデル間の分散だけだった
        var data = TestHelpers.CreateGoelOkumotoData();
        var results = new ModelFitter(data).FitAllModels();
        var models = results.Where(r => r.Success).ToDictionary(r => r.ModelName, r => r.Model!);

        var averaging = new ModelAveragingService().Average(
            results, models, data.GetTimeData(), data.DayCount, tData: data.GetTimeData(), yData: data.GetCumulativeBugsFound());

        Assert.True(double.IsFinite(averaging.TotalBugsWithinModelStdDev));
        Assert.InRange(averaging.ParameterUncertaintyShare, 0.0, 1.0);
        Assert.True(averaging.TotalBugsUncertainty >= averaging.TotalBugsBetweenModelStdDev);
        int weighted = averaging.ModelWeights.Count(w => w.Value >= 1e-10);
        var milestone80 = averaging.ConvergencePredictions["80%"];
        Assert.Equal(weighted, milestone80.ContributingModels);
        Assert.True(milestone80.AveragedPredictedDay < data.DayCount);
    }
}
