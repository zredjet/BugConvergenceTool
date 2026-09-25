using BugConvergenceTool.Models;
using BugConvergenceTool.Optimizers;
using BugConvergenceTool.Services;
using BugConvergenceTool.Services.Diagnostics;

namespace BugConvergenceTool.Tests;

/// <summary>
/// Fisher 情報行列・パラメトリック・ブートストラップ・予測区間・マルチスタート・Poisson 診断の検証
/// </summary>
public class UncertaintyTests
{
    [Fact]
    public void DerivedInterval_OnLogScale_MatchesAnalyticForm()
    {
        // g(θ) = θ₀、Var(θ₀) = 100 → SE[ln g] = 10/150、区間 = 150·exp(±z·10/150)
        var service = new FisherInformationService(0.95);
        var cov = new double[,] { { 100, 0 }, { 0, 1e-6 } };

        var interval = service.CalculateDerivedInterval(p => p[0], new[] { 150.0, 0.05 }, cov, logScale: true);

        double z = 1.959963984540054;
        Assert.Equal(150 * Math.Exp(-z * 10 / 150), interval.Lower, 4);
        Assert.Equal(150 * Math.Exp(z * 10 / 150), interval.Upper, 4);
        Assert.True(interval.Lower > 0);
    }

    [Fact]
    public void FisherInterval_OnGoData_ContainsEstimate()
    {
        var data = TestHelpers.CreateGoelOkumotoData();
        var result = new ModelFitter(data).FitModel(new ExponentialModel());
        var service = new FisherInformationService(0.95);

        var fisher = service.CalculateNHPPStandardErrors(result.Model!, data.GetTimeData(), data.GetCumulativeBugsFound(), result.ParameterVector);
        var total = service.CalculateDerivedInterval(result.Model!.GetAsymptoticTotalBugs, result.ParameterVector, fisher.CovarianceMatrix!);

        Assert.True(fisher.Success, fisher.ErrorMessage);
        Assert.All(fisher.StandardErrors, se => Assert.True(se > 0));
        Assert.True(total.IsValid);
        Assert.InRange(result.EstimatedTotalBugs, total.Lower, total.Upper);
    }

    [Fact]
    public void Bootstrap_ExcludesFailedRefitsInsteadOfReplacingWithEstimate()
    {
        // 以前は失敗した反復を θ̂ で置き換えていたため区間が不当に狭くなっていた
        var data = TestHelpers.CreateGoelOkumotoData();
        int calls = 0;
        var boot = ParametricBootstrap.Run(new ExponentialModel(), data.GetTimeData(), new[] { 150.0, 0.05 },
            _ => Interlocked.Increment(ref calls) % 2 == 0 ? null : new[] { 150.0, 0.05 },
            iterations: 40, seed: 1);

        Assert.Equal(40, boot.Requested);
        Assert.InRange(boot.Succeeded, 1, 39);
    }

    [Fact]
    public void PredictionInterval_WithNoSuccessfulReplicate_ReportsFailure()
    {
        var data = TestHelpers.CreateGoelOkumotoData();
        var boot = ParametricBootstrap.Run(new ExponentialModel(), data.GetTimeData(), new[] { 150.0, 0.05 }, _ => null, 10, seed: 1);

        var pi = new PredictionIntervalService().Calculate(
            new ExponentialModel(), data.GetTimeData(), data.GetCumulativeBugsFound(), new[] { 150.0, 0.05 }, boot, 14);

        Assert.Equal(0, pi.Succeeded);
        Assert.NotEmpty(pi.Warnings);
    }

    [Fact]
    public void PredictionInterval_OnGoData_IsConsistent()
    {
        var data = TestHelpers.CreateGoelOkumotoData();
        var fitter = new ModelFitter(data);
        var result = fitter.FitModel(new ExponentialModel());
        var t = data.GetTimeData();
        var y = data.GetCumulativeBugsFound();

        var boot = ParametricBootstrap.Run(result.Model!, t, result.ParameterVector, fitter.CreateRefitFunction(result.Model!), 60, seed: 3);
        var pi = new PredictionIntervalService().Calculate(result.Model!, t, y, result.ParameterVector, boot, 20, seed: 5);

        Assert.True(pi.Succeeded >= 55);
        Assert.Equal(20, pi.FutureTimes.Length);
        for (int d = 0; d < 20; d++)
        {
            // 観測済みの累積は確定しているので、将来の累積は y(T) を下回らない
            Assert.True(pi.Lower[d] >= y[^1]);
            Assert.True(pi.Lower[d] <= pi.Upper[d]);
            Assert.InRange(pi.PointForecast[d], pi.Lower[d] - 1, pi.Upper[d] + 1);
        }
        Assert.InRange(pi.TotalBugs!.Estimate, pi.TotalBugs.Lower, pi.TotalBugs.Upper);
        Assert.True(pi.RemainingBugs!.Lower >= 0);
        foreach (var m in pi.Milestones)
            Assert.InRange(m.EstimateDay, m.LowerDay, m.UpperDay);
    }

    [Fact]
    public void SimulateCumulative_HasPoissonMeanOfModel()
    {
        var model = new ExponentialModel();
        var p = new[] { 150.0, 0.05 };
        var t = Enumerable.Range(1, 40).Select(i => (double)i).ToArray();
        var random = new Random(11);

        double mean = Enumerable.Range(0, 2000).Select(_ => ParametricBootstrap.SimulateCumulative(model, t, p, random)[^1]).Average();

        double expected = model.Calculate(40, p);                  // ≈ 129.7
        Assert.InRange(mean, expected - 4 * Math.Sqrt(expected / 2000), expected + 4 * Math.Sqrt(expected / 2000));
    }

    [Fact]
    public void DayForRatio_MatchesGoelOkumotoClosedForm()
    {
        var p = new[] { 150.0, 0.05 };
        foreach (double r in new[] { 0.9, 0.95, 0.99 })
            Assert.Equal(-Math.Log(1 - r) / 0.05, new ExponentialModel().DayForRatio(r, p), 4);
    }

    [Fact]
    public void MultiStart_ReportsStartsAndConvergence()
    {
        var data = TestHelpers.CreateGoelOkumotoData();
        var single = new ModelFitter(data).FitModel(new ExponentialModel());
        var multi = new ModelFitter(data, multiStarts: 4).FitModel(new ExponentialModel());

        Assert.Equal(4, multi.OptimizationStarts);
        Assert.InRange(multi.StartsConvergedToBest, 1, 4);
        Assert.StartsWith("MultiStart", multi.OptimizerUsed);
        Assert.Equal(single.EstimatedTotalBugs, multi.EstimatedTotalBugs, 0.5);
    }

    [Fact]
    public void Diagnostics_IncludePoissonConsistencyCheck()
    {
        var data = TestHelpers.CreateGoelOkumotoData();
        var result = new ModelFitter(data).FitModel(new ExponentialModel());

        var report = new DiagnosticReportGenerator().Generate(
            result.Model!, data.GetTimeData(), data.GetCumulativeBugsFound(), result.ParameterVector);

        Assert.NotNull(report.PoissonDiagnostics);
        Assert.Equal(data.DayCount, report.PoissonDiagnostics!.RandomizedQuantileResiduals.Length);
        Assert.Contains("Poisson整合性診断", DiagnosticReportGenerator.FormatReport(report));
    }
}

/// <summary>
/// --ci（パラメトリック・ブートストラップによる信頼区間）の検証
/// </summary>
public class ConfidenceBandTests
{
    [Fact]
    public void ConfidenceBand_CoversObservedAndFuturePeriod_AndContainsEstimate()
    {
        var data = TestHelpers.CreateGoelOkumotoData();
        var fitter = new ModelFitter(data);
        var result = fitter.FitModel(new ExponentialModel());
        var boot = ParametricBootstrap.Run(result.Model!, data.GetTimeData(), result.ParameterVector,
            fitter.CreateRefitFunction(result.Model!), 60, seed: 2);
        var times = Enumerable.Range(1, 80).Select(d => (double)d).ToArray();

        var band = new ConfidenceIntervalService().Calculate(result.Model!, result.ParameterVector, boot, times);

        Assert.Equal(80, band.Times.Length);
        Assert.Equal(boot.Succeeded, band.Succeeded);
        for (int i = 0; i < 80; i++)
        {
            Assert.True(band.Lower[i] <= band.Upper[i]);
            Assert.True(band.Upper[i] - band.Lower[i] > 0, $"t={times[i]} で幅が 0");
        }
        Assert.InRange(band.TotalBugs!.Estimate, band.TotalBugs.Lower, band.TotalBugs.Upper);
        Assert.Equal(3, band.Milestones.Count);
    }

    [Fact]
    public void ConfidenceBand_ForTefModel_HasPositiveWidth()
    {
        // 以前は TEF モデルの再推定に工数データが渡されず全反復が失敗し、θ̂ で置き換えられて幅 0 の区間になっていた
        var data = TestHelpers.CreateGoelOkumotoData();
        var fitter = new ModelFitter(data);
        var result = fitter.FitModel(new TEFExponentialModel(new WeibullTEF()));
        var boot = ParametricBootstrap.Run(result.Model!, data.GetTimeData(), result.ParameterVector,
            fitter.CreateRefitFunction(result.Model!), 30, seed: 4);

        var band = new ConfidenceIntervalService().Calculate(result.Model!, result.ParameterVector, boot, data.GetTimeData());

        Assert.True(band.Succeeded >= 25, $"成功 {band.Succeeded}/30");
        Assert.True(band.Upper[^1] - band.Lower[^1] > 1);
    }

    [Fact]
    public void ConfidenceBand_WithNoSuccessfulReplicate_ReportsFailure()
    {
        var data = TestHelpers.CreateGoelOkumotoData();
        var boot = ParametricBootstrap.Run(new ExponentialModel(), data.GetTimeData(), new[] { 150.0, 0.05 }, _ => null, 10, seed: 1);

        var band = new ConfidenceIntervalService().Calculate(new ExponentialModel(), new[] { 150.0, 0.05 }, boot, data.GetTimeData());

        Assert.Equal(0, band.Succeeded);
        Assert.NotEmpty(band.Warnings);
    }
}
