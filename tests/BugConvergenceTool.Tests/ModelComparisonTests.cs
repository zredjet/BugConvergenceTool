using BugConvergenceTool.Models;
using BugConvergenceTool.Optimizers;
using BugConvergenceTool.Services;

namespace BugConvergenceTool.Tests;

/// <summary>
/// AIC による比較を「同じデータ・同じ尤度」のモデル同士に限定していることを検証する
/// </summary>
public class ModelComparisonTests
{
    private static FittingResult Result(string name, string group, double aic, double aicc, string criterion = "AICc")
    {
        return new FittingResult
        {
            ModelName = name,
            Success = true,
            ComparisonGroup = group,
            AIC = aic,
            AICc = aicc,
            ModelSelectionCriterion = criterion
        };
    }

    [Fact]
    public void GetBestModel_IgnoresModelsWithDifferentLikelihood()
    {
        // FRE（修正数を含む尤度）の AICc が数値上は最小でも、推奨モデルにしてはいけない
        var results = new List<FittingResult>
        {
            Result("GO", ModelComparisonGroup.DetectionOnly, 100, 101),
            Result("遅延S字", ModelComparisonGroup.DetectionOnly, 110, 111),
            Result("定数FRE", ModelComparisonGroup.DetectionAndCorrection, 10, 11),
            Result("TEF", ModelComparisonGroup.DetectionAndEffort, 20, 21),
        };
        var fitter = new ModelFitter(TestHelpers.CreateGoelOkumotoData());

        var best = fitter.GetBestModel(results);

        Assert.Equal("GO", best!.ModelName);
    }

    [Fact]
    public void GetBestModel_FallsBackToNextGroupWhenNoDetectionOnlyModel()
    {
        var results = new List<FittingResult>
        {
            Result("定数FRE", ModelComparisonGroup.DetectionAndCorrection, 10, 11),
            Result("TEF", ModelComparisonGroup.DetectionAndEffort, 20, 21),
        };
        var fitter = new ModelFitter(TestHelpers.CreateGoelOkumotoData());

        Assert.Equal("TEF", fitter.GetBestModel(results)!.ModelName);
    }

    [Fact]
    public void HarmonizeCriterion_DoesNotMixAicAndAiccWithinGroup()
    {
        // n=90: k=2 は n/k=45 で AIC、k=3 は n/k=30 で AICc と個別判定される。
        // 混在すると AIC と AICc の値を直接比べることになるため、グループ内は AICc に揃える
        var k2 = Result("k2", ModelComparisonGroup.DetectionOnly, 100.0, 100.1, criterion: "AIC");
        var k3 = Result("k3", ModelComparisonGroup.DetectionOnly, 99.9, 100.2, criterion: "AICc");
        var other = Result("FRE", ModelComparisonGroup.DetectionAndCorrection, 50, 50.1, criterion: "AIC");

        ModelComparisonGroup.HarmonizeCriterion(new[] { k2, k3, other });

        Assert.Equal("AICc", k2.ModelSelectionCriterion);
        Assert.Equal("AICc", k3.ModelSelectionCriterion);
        Assert.Equal("AIC", other.ModelSelectionCriterion); // 別グループには影響しない
        Assert.True(k2.SelectionScore < k3.SelectionScore);
    }

    [Fact]
    public void ModelAveraging_WeightsOnlyPrimaryGroup()
    {
        var results = new[]
        {
            Result("GO", ModelComparisonGroup.DetectionOnly, 100, 101),
            Result("遅延S字", ModelComparisonGroup.DetectionOnly, 102, 103),
            Result("定数FRE", ModelComparisonGroup.DetectionAndCorrection, 10, 11),
        };

        var weights = new ModelAveragingService().CalculateAicWeights(results);

        Assert.Equal(new[] { "GO", "遅延S字" }.Order(), weights.Keys.Order());
        Assert.Equal(1.0, weights.Values.Sum(), 12);
        Assert.False(weights.ContainsKey("定数FRE"));
    }

    [Fact]
    public void ModelFitter_DefaultsToMle()
    {
        var fitter = new ModelFitter(TestHelpers.CreateGoelOkumotoData());
        var result = fitter.FitModel(new ExponentialModel());
        Assert.Equal("MLE", result.LossFunctionUsed);
    }

    [Fact]
    public void ModelFitter_AssignsComparisonGroupByLikelihoodData()
    {
        var data = TestHelpers.CreateGoelOkumotoData();
        var fitter = new ModelFitter(data, OptimizerType.DifferentialEvolution);

        var results = fitter.FitModels(new ReliabilityGrowthModelBase[]
        {
            new ExponentialModel(),
            new ConstantFREModel(),
            new TEFExponentialModel(new WeibullTEF())
        });

        Assert.Equal(ModelComparisonGroup.DetectionOnly, results[0].ComparisonGroup);
        Assert.Equal(ModelComparisonGroup.DetectionAndCorrection, results[1].ComparisonGroup);
        Assert.Equal(ModelComparisonGroup.DetectionAndEffort, results[2].ComparisonGroup);
    }

    [Fact]
    public void PseudoCoverageModelsAreRemoved()
    {
        // 擬似Coverageモデルは基本モデルの再パラメータ化で同一モデルのため削除した
        var names = TestHelpers.CreateAllModelInstances().Select(m => m.Name).ToList();
        Assert.DoesNotContain(names, n => n.Contains("擬似Coverage"));
        Assert.DoesNotContain("Coverage", ModelFactory.GetCategories());
    }
}
