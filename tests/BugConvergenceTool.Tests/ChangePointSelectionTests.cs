using BugConvergenceTool.Models;
using BugConvergenceTool.Optimizers;
using BugConvergenceTool.Services;

namespace BugConvergenceTool.Tests;

/// <summary>
/// 推奨対象外（境界への張り付き・変化点が有意でない）とプロファイル尤度・尤度比検定の検証
/// </summary>
public class ChangePointSelectionTests
{
    /// <summary>
    /// 指数型の検出率が τ で b₁ から b₂ に変わる決定的なデータ（期待値の丸め）
    /// </summary>
    private static TestData CreateChangePointData(int days, double a, double b1, double b2, double tau)
    {
        var model = new ExponentialChangePointModel();
        var p = new[] { a, b1, b2, tau };
        var data = new TestData { ProjectName = "変化点", TotalTestCases = 100, StartDate = new DateTime(2025, 1, 6) };
        double prev = 0;
        for (int i = 0; i < days; i++)
        {
            double rounded = Math.Round(model.Calculate(i + 1, p));
            data.Dates.Add(data.StartDate.Value.AddDays(i));
            data.PlannedDaily.Add(0);
            data.ActualDaily.Add(0);
            data.BugsFoundDaily.Add(rounded - prev);
            data.BugsFixedDaily.Add(0);
            prev = rounded;
        }
        return data;
    }

    private static FittingResult Fake(string name, double aicc, string? exclusion = null) => new()
    {
        ModelName = name,
        Success = true,
        AIC = aicc,
        AICc = aicc,
        ModelSelectionCriterion = "AICc",
        SelectionExclusionReason = exclusion
    };

    [Fact]
    public void GetBestModel_SkipsExcludedModels()
    {
        var fitter = new ModelFitter(TestHelpers.CreateGoelOkumotoData());
        var results = new List<FittingResult>
        {
            Fake("変化点", 100, "変化点の尤度比検定で有意でない"),
            Fake("GO", 101),
        };
        Assert.Equal("GO", fitter.GetBestModel(results)!.ModelName);
    }

    [Fact]
    public void GetBestModel_FallsBackWhenAllExcluded()
    {
        var fitter = new ModelFitter(TestHelpers.CreateGoelOkumotoData());
        var results = new List<FittingResult> { Fake("A", 100, "理由A"), Fake("B", 101, "理由B") };
        var best = fitter.GetBestModel(results)!;
        Assert.Equal("A", best.ModelName);
        Assert.NotNull(best.SelectionExclusionReason); // 呼び出し側が警告できるよう理由は残す
    }

    [Fact]
    public void TotalBugsScaleAtUpperBound_IsExcludedFromRecommendation()
    {
        // 毎日 5 件ずつ発見（収束の兆候なし）→ 指数型の a は上限に張り付き、総数は推定できない
        var data = new TestData { ProjectName = "線形", TotalTestCases = 100, StartDate = new DateTime(2025, 1, 6) };
        for (int i = 0; i < 20; i++)
        {
            data.Dates.Add(data.StartDate.Value.AddDays(i));
            data.PlannedDaily.Add(0);
            data.ActualDaily.Add(0);
            data.BugsFoundDaily.Add(5);
            data.BugsFixedDaily.Add(0);
        }

        var result = new ModelFitter(data).FitModel(new ExponentialModel());

        Assert.True(result.Success);
        Assert.NotNull(result.SelectionExclusionReason);
        Assert.Contains(result.Warnings, w => w.Contains("上限"));
    }

    [Fact]
    public void ChangePointModel_IsFittedByProfileLikelihood_WithFullParameterVector()
    {
        // 以前の FitChangePointModel は τ を除いたパラメータ列を τ 付きのモデルに渡していた
        var data = CreateChangePointData(35, 150, 0.02, 0.15, 15);
        var model = new ExponentialChangePointModel();

        var result = new ModelFitter(data).FitModel(model);

        Assert.True(result.Success, result.ErrorMessage);
        Assert.NotNull(result.ChangePointSearchResult);
        Assert.Equal(4, result.ParameterVector.Length);
        Assert.Equal(result.ChangePointSearchResult!.BestTau, result.ParameterVector[3]);
        Assert.InRange(result.ParameterVector[3], 13, 17);
        Assert.Equal(model.GetAsymptoticTotalBugs(result.ParameterVector), result.EstimatedTotalBugs, 9);
        Assert.Same(model, result.Model);
    }

    [Fact]
    public void LikelihoodRatioTest_DetectsRealChangePoint()
    {
        var data = CreateChangePointData(35, 150, 0.02, 0.15, 15);
        var fitter = new ModelFitter(data);
        var results = fitter.FitModels(new ReliabilityGrowthModelBase[] { new ExponentialModel(), new ExponentialChangePointModel() });

        int tested = fitter.TestChangePoints(results, simulations: 39, seed: 7);

        var changePoint = results.Single(r => r.Model is ExponentialChangePointModel);
        Assert.Equal(1, tested);
        Assert.True(changePoint.ChangePointTest!.Success);
        Assert.True(changePoint.ChangePointTest.IsChangePointSignificant, changePoint.ChangePointTest.Interpretation);
        Assert.Null(changePoint.SelectionExclusionReason);
        Assert.Equal(changePoint.ModelName, fitter.GetBestModel(results)!.ModelName);
    }

    [Fact]
    public void LikelihoodRatioTest_SkipsChangePointModelThatCannotBeRecommended()
    {
        var fitter = new ModelFitter(TestHelpers.CreateGoelOkumotoData());
        var go = Fake("指数型（Goel-Okumoto）", 100);
        go.Model = new ExponentialModel();
        var cp = Fake("指数型+変化点", 105);
        cp.Model = new ExponentialChangePointModel();

        Assert.Equal(0, fitter.TestChangePoints(new List<FittingResult> { go, cp }, simulations: 19));
        Assert.Null(cp.ChangePointTest);
    }

    [Fact]
    public void LikelihoodRatioTest_PValueUsesOnlyValidSimulations()
    {
        // 失敗したシミュレーションを「超過しなかった」と数えると p 値が小さく偏る
        var data = TestHelpers.CreateGoelOkumotoData();
        var nullModel = new ExponentialModel();
        var altModel = new ExponentialChangePointModel();
        var t = data.GetTimeData();
        var y = data.GetCumulativeBugsFound();
        int calls = 0;

        var test = new ChangePointLRTService(simulationIterations: 40, seed: 1).Test(
            t, y, nullModel, altModel,
            new[] { 150.0, 0.05 }, new[] { 150.0, 0.05, 0.05, 20.0 },
            refitNull: _ => Interlocked.Increment(ref calls) % 2 == 0 ? null : new[] { 150.0, 0.05 },
            refitAlternative: _ => new[] { 150.0, 0.05, 0.05, 20.0 });

        Assert.True(test.Success);
        Assert.InRange(test.ValidSimulations, 1, 39);
        // p = (超過数 + 1) / (有効数 + 1) なので、p × (有効数 + 1) は 1 以上 有効数+1 以下の整数になる
        double scaled = test.SimulatedPValue * (test.ValidSimulations + 1);
        Assert.Equal(Math.Round(scaled), scaled, 9);
        Assert.InRange(scaled, 1, test.ValidSimulations + 1);
    }
}
