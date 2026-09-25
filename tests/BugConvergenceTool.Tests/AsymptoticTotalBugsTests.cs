using BugConvergenceTool.Models;
using BugConvergenceTool.Optimizers;
using BugConvergenceTool.Services;

namespace BugConvergenceTool.Tests;

/// <summary>
/// GetAsymptoticTotalBugs が各モデルの実際の m(∞) と一致することを検証する
/// </summary>
public class AsymptoticTotalBugsTests
{
    // exp(-b·t^c) 等が十分 0 になる時刻（b, c の下限付近でも収束する大きさ）
    private const double VeryLargeTime = 1e12;

    public static IEnumerable<object[]> AllModelNames()
    {
        return TestHelpers.CreateAllModelInstances()
            .Select(m => new object[] { TestHelpers.ModelKey(m) });
    }

    private static ReliabilityGrowthModelBase FindModel(string key)
    {
        return TestHelpers.CreateAllModelInstances()
            .Single(m => TestHelpers.ModelKey(m) == key);
    }

    [Theory]
    [MemberData(nameof(AllModelNames))]
    public void GetAsymptoticTotalBugs_MatchesLongRunMeanValue(string modelKey)
    {
        var model = FindModel(modelKey);
        var data = TestHelpers.CreateGoelOkumotoData();
        TestHelpers.PrepareModel(model, data);

        foreach (var p in TestHelpers.SampleParameterPoints(model, data))
        {
            if (TestHelpers.UsesInfiniteEffort(model))
                continue; // 別テストで検証

            double asymptote = model.GetAsymptoticTotalBugs(p);
            double longRun = model.Calculate(VeryLargeTime, p);

            Assert.True(double.IsFinite(asymptote), $"{model.Name}: 漸近値が有限でない ({asymptote}) p=[{string.Join(", ", p)}]");
            double tolerance = 1e-6 * Math.Max(1.0, Math.Abs(longRun));
            Assert.True(Math.Abs(asymptote - longRun) <= tolerance,
                $"{model.Name}: GetAsymptoticTotalBugs={asymptote} だが m({VeryLargeTime:E0})={longRun} p=[{string.Join(", ", p)}]");
        }
    }

    [Fact]
    public void InfiniteEffortTEF_AsymptoteIsAnalyticLimit()
    {
        var data = TestHelpers.CreateGoelOkumotoData();
        var tef = new LogPowerTEF();

        var exp = new TEFExponentialModel(tef);
        var delayed = new TEFDelayedSModel(tef);
        foreach (var m in new ReliabilityGrowthModelBase[] { exp, delayed })
            TestHelpers.PrepareModel(m, data);

        // パラメータは a, b, TEF_a, TEF_b。W(∞)=∞ なので m(∞)=a
        var p4 = new[] { 120.0, 0.02, 5.0, 1.0 };
        Assert.Equal(120.0, exp.GetAsymptoticTotalBugs(p4), 9);
        Assert.Equal(120.0, delayed.GetAsymptoticTotalBugs(p4), 9);
    }

    [Fact]
    public void AllModelTypesAreCovered()
    {
        var covered = TestHelpers.CreateAllModelInstances().Select(m => m.GetType()).Distinct().Count();
        var total = typeof(ReliabilityGrowthModelBase).Assembly.GetTypes()
            .Count(t => !t.IsAbstract && t.IsSubclassOf(typeof(ReliabilityGrowthModelBase)));
        Assert.Equal(total, covered);
    }

    [Theory]
    [InlineData(typeof(ExponentialChangePointModel))]
    [InlineData(typeof(DelayedSChangePointModel))]
    [InlineData(typeof(FREChangePointModel))]
    [InlineData(typeof(GompertzModel))]
    [InlineData(typeof(TEFExponentialModel))]
    public void ModelFitter_SetsEstimatedTotalBugsFromAsymptote(Type modelType)
    {
        // パラメータ名が a₁ / a₀ のモデルでも 0 にならず、m(∞) と一致すること
        var data = TestHelpers.CreateGoelOkumotoData();
        var model = modelType == typeof(TEFExponentialModel)
            ? new TEFExponentialModel(new WeibullTEF())
            : (ReliabilityGrowthModelBase)Activator.CreateInstance(modelType)!;
        var fitter = new ModelFitter(data, OptimizerType.DifferentialEvolution);

        var result = fitter.FitModel(model);

        Assert.True(result.Success, result.ErrorMessage);
        Assert.Equal(model.GetAsymptoticTotalBugs(result.ParameterVector), result.EstimatedTotalBugs, 9);
        Assert.True(result.EstimatedTotalBugs > 0);
    }
}
