using BugConvergenceTool.Models;
using BugConvergenceTool.Services;

namespace BugConvergenceTool.Tests;

/// <summary>
/// ブートストラップの再推定が探索範囲の上限に張り付いたときの扱いと、TEF の工数の再生成
/// </summary>
public class BootstrapBoundTests
{
    private static readonly double[] Times = Enumerable.Range(1, 40).Select(d => (double)d).ToArray();

    private static (ModelFitter fitter, FittingResult fit, ParametricBootstrapResult bootstrap) RunGo(double a, double b, int dataSeed)
    {
        var model = new ExponentialModel();
        var data = TestHelpers.SimulateData(model, new[] { a, b }, 40, dataSeed);
        var fitter = new ModelFitter(data, seed: 1);
        var fit = fitter.FitModel(model);
        var bootstrap = ParametricBootstrap.Run(model, Times, fit.ParameterVector, fitter.CreateBootstrapRefitFunction(model), 200, seed: 3);
        return (fitter, fit, bootstrap);
    }

    [Fact]
    public void WeaklyConvergedData_UpperLimitIsMarkedAsBoundLimited()
    {
        // 観測最大値 238 件・推定総数 624 件（本推定は上限 1190 に張り付いていない）。
        // 再推定の 1 割強が上限に張り付き、97.5% 点は探索範囲で決まった値になる。以前は警告もなく普通の区間として表示していた
        var (_, fit, bootstrap) = RunGo(600, 0.012, 3);
        Assert.Null(fit.SelectionExclusionReason);
        Assert.InRange(bootstrap.AtUpperBoundFraction, 0.05, 0.5);

        var band = new ConfidenceIntervalService().Calculate(fit.Model!, fit.ParameterVector, bootstrap, Times);
        Assert.True(band.TotalBugs!.UpperIsBoundLimited);
        Assert.Contains("≥", IntervalFormatter.EstimateLine("推定潜在バグ総数", band.TotalBugs));
        Assert.Contains(band.Warnings, w => w.Contains("張り付き") && w.Contains($"{bootstrap.AtUpperBoundCount}/{bootstrap.Succeeded}"));
        // 観測期間内の m(t) は a の張り付きの影響を受けない（1日目の上限に「≥」は付かない）
        Assert.False(band.UpperIsBoundLimited[0]);

        var pi = new PredictionIntervalService().Calculate(fit.Model!, Times, TestHelpers.SimulateData(new ExponentialModel(), new[] { 600, 0.012 }, 40, 3).GetCumulativeBugsFound(),
            fit.ParameterVector, bootstrap, 40, seed: 5);
        Assert.True(pi.RemainingBugs!.UpperIsBoundLimited);
    }

    [Fact]
    public void ConvergedData_IsNotMarked()
    {
        var (_, fit, bootstrap) = RunGo(150, 0.08, 3);
        Assert.Equal(0, bootstrap.AtUpperBoundCount);

        var band = new ConfidenceIntervalService().Calculate(fit.Model!, fit.ParameterVector, bootstrap, Times);
        Assert.False(band.TotalBugs!.UpperIsBoundLimited);
        Assert.DoesNotContain("≥", IntervalFormatter.EstimateLine("推定潜在バグ総数", band.TotalBugs));
        Assert.All(band.Milestones, m => Assert.False(m.UpperIsBoundLimited));
    }

    [Fact]
    public void FewBoundReplicates_BelowTail_DoNotLimitUpper()
    {
        var result = new ParametricBootstrapResult
        {
            Replicates = Enumerable.Range(0, 200).Select(i => new[] { 100.0 + i, 0.05 }).ToList(),
            AtUpperBound = Enumerable.Range(0, 200).Select(i => i < 4).ToList(),
            Requested = 200
        };
        Assert.False(result.IsUpperLimitedByBound(0.975));   // 2% < 2.5%
        Assert.Contains("影響しません", result.BoundWarning(0.975));
    }

    [Fact]
    public void TefBootstrap_ResimulatesEffort()
    {
        // 以前は工数を観測値に固定していたため、再推定しても工数関数のパラメータ（総工数 N など）がほとんど動かず、
        // 工数の不確実性が区間に入らなかった
        var random = new Random(8);
        double totalEffort = 600, beta = 0.05;
        var effortDaily = Times.Select(t => totalEffort * (Math.Exp(-beta * (t - 1)) - Math.Exp(-beta * t)) * (0.7 + 0.6 * random.NextDouble())).ToArray();
        var go = TestHelpers.SimulateData(new ExponentialModel(), new[] { 150.0, 0.05 }, 40, 2);
        var data = TestHelpers.FromCumulative(go.GetCumulativeBugsFound(), effortDaily: effortDaily);
        var fitter = new ModelFitter(data, seed: 1);
        var model = new TEFExponentialModel(TEFFactory.GetAllTEFs().First());
        var fit = fitter.FitModel(model);
        Assert.True(fit.Success, fit.ErrorMessage);

        var withEffort = ParametricBootstrap.Run(model, Times, fit.ParameterVector, fitter.CreateBootstrapRefitFunction(model), 100, seed: 4);
        var fixedEffort = ParametricBootstrap.Run(model, Times, fit.ParameterVector, fitter.CreateBootstrapRefitFunction(model), 100, seed: 4,
            resimulateEffort: false);
        Assert.True(withEffort.EffortResimulated);
        Assert.False(fixedEffort.EffortResimulated);
        // 共有のモデルの工数データは書き換えない
        Assert.Equal(data.GetCumulativeActual(), model.ObservedEffortData);

        // 工数関数の総工数 N（TEF の最初のパラメータ）の再推定値のばらつき
        int index = Array.IndexOf(model.ParameterNames, model.ParameterNames.First(n => n.StartsWith("TEF_")));
        double Spread(ParametricBootstrapResult r)
        {
            var values = r.Replicates.Select(p => p[index]).ToList();
            double mean = values.Average();
            return Math.Sqrt(values.Sum(v => (v - mean) * (v - mean)) / values.Count);
        }
        Assert.True(Spread(withEffort) > 2 * Spread(fixedEffort), $"工数あり {Spread(withEffort)} / 固定 {Spread(fixedEffort)}");
        Assert.Equal("推定総工数で見つかるバグ数", model.TotalBugsLabel);
    }
}
