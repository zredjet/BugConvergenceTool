using BugConvergenceTool.Models;
using BugConvergenceTool.Optimizers;
using BugConvergenceTool.Services;

namespace BugConvergenceTool.Tests;

/// <summary>
/// 変曲S字型の ψ を ln ψ でパラメータ化した効果（急峻で変曲点の遅い S 字を表せること）
/// </summary>
public class InflectionSParameterizationTests
{
    /// <summary>
    /// 変曲S字型 m(t) = a(1-e^(-bt))/(1+ψe^(-bt)) の Poisson 合成データ（パラメータ化によらない）
    /// </summary>
    private static TestData Simulate(double a, double b, double psi, int days, int seed)
    {
        var random = new Random(seed);
        double M(double t) => a * (1 - Math.Exp(-b * t)) / (1 + psi * Math.Exp(-b * t));
        var cumulative = new double[days];
        double sum = 0;
        for (int i = 0; i < days; i++)
        {
            double lambda = M(i + 1) - M(i);
            sum += lambda > 0 ? MathNet.Numerics.Distributions.Poisson.Sample(random, lambda) : 0;
            cumulative[i] = sum;
        }
        return TestHelpers.FromCumulative(cumulative);
    }

    [Fact]
    public void SteepLateSCurve_IsNotPinnedToBound()
    {
        // 変曲点 t* = 30 日、b = 0.35（ψ = e^(10.5) ≈ 36000）。以前は ψ の上限 1000 に 50/50 回張り付き、総数を過大に推定した
        double b = 0.35, inflection = 30;
        var totals = new List<double>();
        int pinned = 0;
        for (int seed = 0; seed < 50; seed++)
        {
            var data = Simulate(100, b, Math.Exp(b * inflection), 40, seed);
            var fit = new ModelFitter(data, OptimizerType.DifferentialEvolution, seed: seed).FitModel(new InflectionSModel());
            Assert.True(fit.Success);
            totals.Add(fit.EstimatedTotalBugs);
            if (fit.Warnings.Any(w => w.Contains("探索範囲の上限"))) pinned++;
        }

        Assert.Equal(0, pinned);
        Assert.InRange(totals.Average(), 95, 105);
    }

    [Fact]
    public void ExponentialData_LowerBoundOfLogPsi_IsNotWarned()
    {
        // ln ψ の下限は ψ → 0（指数型）に対応する自然な境界なので、張り付いても注意しない
        for (int seed = 0; seed < 10; seed++)
        {
            var data = TestHelpers.SimulateData(new ExponentialModel(), new[] { 150.0, 0.08 }, 40, seed);
            var fit = new ModelFitter(data, seed: seed).FitModel(new InflectionSModel());
            Assert.DoesNotContain(fit.Warnings, w => w.Contains("探索範囲の下限"));
        }
    }

    [Fact]
    public void InflectionPoint_IsReportedAsDerivedQuantity()
    {
        var model = new InflectionSModel();
        var derived = model.GetDerivedQuantities(new[] { 100.0, 0.2, Math.Log(400) }).ToList();
        var inflection = Assert.Single(derived);
        Assert.Equal(Math.Log(400) / 0.2, inflection.Value, 9);

        // ψ ≤ 1（ln ψ ≤ 0）では変曲点が t ≤ 0 にあり、観測期間では凹の曲線
        Assert.Empty(model.GetDerivedQuantities(new[] { 100.0, 0.2, -1.0 }));
    }

    [Fact]
    public void FisherInformation_NearLowerBoundOfLogPsi_IsComputedOnPsiScale()
    {
        // ln ψ が下限付近（ψ ≈ 7e-5）だと尤度は ln ψ についてほぼ平らで、ln ψ 座標のヘッセ行列は特異になる。
        // ψ 座標で求めてヤコビアンで戻すので、総数の標準誤差は GO とほぼ同じになる
        var data = TestHelpers.CreateGoelOkumotoData();
        var t = data.GetTimeData();
        var y = data.GetCumulativeBugsFound();
        var iss = new InflectionSModel();
        var go = new ExponentialModel();
        var goFit = new ModelFitter(data, seed: 1).FitModel(go);
        double[] p = [.. goFit.ParameterVector, -9.5];

        var service = new FisherInformationService();
        var fisher = service.CalculateNHPPStandardErrors(iss, t, y, p);
        Assert.True(fisher.Success, fisher.ErrorMessage);
        var total = service.CalculateDerivedInterval(iss.GetAsymptoticTotalBugs, p, fisher.CovarianceMatrix!, logScale: false);
        var goFisher = service.CalculateNHPPStandardErrors(go, t, y, goFit.ParameterVector);
        var goTotal = service.CalculateDerivedInterval(go.GetAsymptoticTotalBugs, goFit.ParameterVector, goFisher.CovarianceMatrix!, logScale: false);
        Assert.InRange(total.StandardError, goTotal.StandardError, goTotal.StandardError * 3);
    }

    [Fact]
    public void ChangePointVariant_InflectionAfterTau_UsesSecondRate()
    {
        // 実効時間 u(t) は τ 以降 b₂ で増えるので、ln ψ > b₁τ なら変曲点は τ + (ln ψ - b₁τ)/b₂
        // （以前は常に ln ψ / b₁ と表示しており、この例では 60 日になっていた）
        var model = new InflectionSChangePointModel();
        var p = new[] { 100.0, 0.05, 0.2, 3.0, 10.0 };
        var inflection = Assert.Single(model.GetDerivedQuantities(p));
        Assert.Equal(22.5, inflection.Value, 9);

        // 数値的にも日次発見数の山がそこにある
        double peakDay = Enumerable.Range(1, 600).Select(i => i * 0.1)
            .MaxBy(t => model.Calculate(t + 0.05, p) - model.Calculate(t - 0.05, p));
        Assert.InRange(peakDay, 22.0, 23.0);

        // τ より前なら ln ψ / b₁
        Assert.Equal(1.0 / 0.05, Assert.Single(model.GetDerivedQuantities(new[] { 100.0, 0.05, 0.2, 1.0, 30.0 })).Value, 9);
    }
}
