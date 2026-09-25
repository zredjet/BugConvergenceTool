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
            if (TestHelpers.UsesInfiniteEffort(model, p))
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
        var imperfect = new TEFImperfectDebugModel(tef);
        foreach (var m in new ReliabilityGrowthModelBase[] { exp, delayed, imperfect })
            TestHelpers.PrepareModel(m, data);

        // exp/遅延S字: パラメータは a, b, TEF_a, TEF_b
        var p4 = new[] { 120.0, 0.02, 5.0, 1.0 };
        Assert.Equal(120.0, exp.GetAsymptoticTotalBugs(p4), 9);
        Assert.Equal(120.0, delayed.GetAsymptoticTotalBugs(p4), 9);

        // 不完全デバッグ: パラメータは a, b, α, TEF_a, TEF_b → m(∞) = a/(1-α)
        var p5 = new[] { 120.0, 0.02, 0.2, 5.0, 1.0 };
        Assert.Equal(120.0 / 0.8, imperfect.GetAsymptoticTotalBugs(p5), 9);
    }

    [Fact]
    public void AllModelTypesAreCovered()
    {
        var covered = TestHelpers.CreateAllModelInstances().Select(m => m.GetType()).Distinct().Count();
        var total = typeof(ReliabilityGrowthModelBase).Assembly.GetTypes()
            .Count(t => !t.IsAbstract && t.IsSubclassOf(typeof(ReliabilityGrowthModelBase)));
        Assert.Equal(total, covered);
    }

    [Fact]
    public void ChangePointAsymptote_CountsOnlyDetectedPartOfFirstPopulation()
    {
        // a₁=100, b₁=0.01, τ=10 → m₁(τ) = 100(1-e^(-0.1)) ≈ 9.516, m(∞) = 9.516 + a₂
        var model = new ExponentialChangePointModel();
        var p = new[] { 100.0, 0.01, 50.0, 0.1, 10.0 };
        double expected = 100 * (1 - Math.Exp(-0.1)) + 50;
        Assert.Equal(expected, model.GetAsymptoticTotalBugs(p), 9);
    }

    [Fact]
    public void IntegratedFRE_WithEqualRates_MatchesNoChangePointSolution()
    {
        // b₁ = b₂ なら変化点は無意味で、m(t) = a(1-e^(-b(1-α)t))/(1-α) に一致するはず
        var model = new IntegratedFREModel();
        double a = 100, b = 0.05, eta = 0.9, alpha = 0.2, tau = 12;
        var p = new[] { a, b, b, eta, alpha, tau };

        foreach (double t in new[] { 1.0, 5.0, 12.0, 20.0, 40.0, 100.0 })
        {
            double expected = a * (1 - Math.Exp(-b * (1 - alpha) * t)) / (1 - alpha);
            Assert.Equal(expected, model.Calculate(t, p), 9);
        }
    }

    [Theory]
    [InlineData(typeof(ExponentialChangePointModel))]
    [InlineData(typeof(DelayedSChangePointModel))]
    [InlineData(typeof(ErrorGenerationModel))]
    [InlineData(typeof(ModifiedGompertzModel))]
    [InlineData(typeof(GeneralizedImperfectDebugModel))]
    public void ModelFitter_SetsEstimatedTotalBugsFromAsymptote(Type modelType)
    {
        // パラメータ名が a₁ / a₀ のモデルでも 0 にならず、m(∞) と一致すること
        var data = TestHelpers.CreateGoelOkumotoData();
        var model = (ReliabilityGrowthModelBase)Activator.CreateInstance(modelType)!;
        var fitter = new ModelFitter(data, OptimizerType.DifferentialEvolution);

        var result = fitter.FitModel(model);

        Assert.True(result.Success, result.ErrorMessage);
        Assert.Equal(model.GetAsymptoticTotalBugs(result.ParameterVector), result.EstimatedTotalBugs, 9);
        Assert.True(result.EstimatedTotalBugs > 0);
    }
}
