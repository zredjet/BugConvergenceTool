using BugConvergenceTool.Models;
using BugConvergenceTool.Optimizers;
using BugConvergenceTool.Services;

namespace BugConvergenceTool.Tests;

/// <summary>
/// 最適化器の修正（Nelder-Mead の退化、勾配降下のスケール、成功判定、収束判定）の検証
/// </summary>
public class OptimizerTests
{
    public static IEnumerable<object[]> AllOptimizerTypes() =>
        new[] { OptimizerType.DifferentialEvolution, OptimizerType.PSO, OptimizerType.GWO, OptimizerType.CMAES, OptimizerType.NelderMead, OptimizerType.GridSearchGradient }
            .Select(t => new object[] { t });

    [Theory]
    [MemberData(nameof(AllOptimizerTypes))]
    public void Optimizer_ReportsFailureWhenEveryEvaluationFails(OptimizerType type)
    {
        // 以前は評価がすべて失敗しても（目的関数値 = double.MaxValue）Success = true だった
        var result = OptimizerFactory.Create(type).Optimize(_ => double.NaN, new[] { 0.0, 0.0 }, new[] { 1.0, 1.0 });
        Assert.False(result.Success);
    }

    [Theory]
    [InlineData(LossType.Sse)]
    [InlineData(LossType.Mle)]
    public void NelderMead_ReachesDifferentialEvolutionOptimum_OnGoelOkumoto(LossType lossType)
    {
        // 以前の Nelder-Mead は境界への切り詰めで単体が退化し、SSE では a が下限 maxY に張り付いていた
        var data = TestHelpers.CreateGoelOkumotoData();
        var t = data.GetTimeData();
        var y = data.GetCumulativeBugsFound();
        var model = new ExponentialModel();
        var loss = LossFunctionFactory.Create(lossType);
        var (lower, upper) = model.GetBounds(t, y);
        Func<double[], double> objective = p => loss.Evaluate(t, y, model, p);

        var nm = new NelderMeadOptimizer().Optimize(objective, lower, upper, model.GetInitialParameters(t, y));
        var de = new DEOptimizer(seed: 1).Optimize(objective, lower, upper);

        Assert.True(nm.Success);
        Assert.True(nm.ObjectiveValue <= de.ObjectiveValue + 1e-6 * (1 + Math.Abs(de.ObjectiveValue)),
            $"NM={nm.ObjectiveValue} DE={de.ObjectiveValue}");
        Assert.True(nm.Parameters[0] > lower[0] * 1.01, "a が下限に張り付いている");
    }

    [Fact]
    public void NelderMead_ApproachesOptimumOnBoundary()
    {
        // 最適点が境界の外 (x=-1) にある場合、境界 x=0 に近づく（変換空間なので厳密に 0 にはならない）
        var result = new NelderMeadOptimizer().Optimize(
            p => (p[0] + 1) * (p[0] + 1) + (p[1] - 2) * (p[1] - 2), new[] { 0.0, 0.0 }, new[] { 5.0, 5.0 }, new[] { 3.0, 3.0 });

        Assert.True(result.Success);
        Assert.InRange(result.Parameters[0], 0, 1e-3);
        Assert.Equal(2.0, result.Parameters[1], 4);
    }

    [Fact]
    public void GridSearchGradient_RefinesBeyondGridForBadlyScaledParameters()
    {
        // スケールの大きく異なる2パラメータ。以前は固定学習率の勾配降下がほとんど動かずグリッド点のままだった
        Func<double[], double> f = p => Math.Pow((p[0] - 123.4) / 10, 2) + Math.Pow((p[1] - 0.0321) / 0.001, 2);
        var result = new GridSearchGradientOptimizer().Optimize(f, new[] { 50.0, 0.001 }, new[] { 500.0, 1.0 });

        Assert.True(result.Success);
        Assert.Equal(123.4, result.Parameters[0], 1);
        Assert.Equal(0.0321, result.Parameters[1], 3);
    }

    [Fact]
    public void DifferentialEvolution_StopsWhenPopulationConverges()
    {
        var result = new DEOptimizer(maxIterations: 2000, seed: 3).Optimize(
            p => (p[0] - 1) * (p[0] - 1) + (p[1] - 2) * (p[1] - 2), new[] { -5.0, -5.0 }, new[] { 5.0, 5.0 });

        Assert.True(result.Converged);
        Assert.True(result.Iterations < 2000);
        Assert.Equal(1.0, result.Parameters[0], 3);
    }

    /// <summary>
    /// 最適値（DE を数回と Nelder-Mead で探した最良値）と目的関数
    /// </summary>
    private static (Func<double[], double> objective, double[] lower, double[] upper, double[] initial, double best) Problem(
        ReliabilityGrowthModelBase dataModel, double[] trueParameters, int dataSeed, ReliabilityGrowthModelBase fitModel, LossType lossType)
    {
        var t = Enumerable.Range(1, 40).Select(d => (double)d).ToArray();
        var y = ParametricBootstrap.SimulateCumulative(dataModel, t, trueParameters, new Random(dataSeed));
        var loss = LossFunctionFactory.Create(lossType);
        var (lower, upper) = fitModel.GetBounds(t, y);
        var initial = fitModel.GetInitialParameters(t, y);
        Func<double[], double> objective = p => loss.Evaluate(t, y, fitModel, p);
        double best = Enumerable.Range(0, 5)
            .Select(s => new DEOptimizer(maxIterations: 3000, seed: s).Optimize(objective, lower, upper, initial).ObjectiveValue)
            .Append(new NelderMeadOptimizer().Optimize(objective, lower, upper, initial).ObjectiveValue)
            .Min();
        return (objective, lower, upper, initial, best);
    }

    [Fact]
    public void CmaEs_ReachesOptimum_AcrossSeeds()
    {
        // 以前の CMA-ES はやり直しがなく、最良値が 51 世代更新されないだけで「収束」として止まっていた。
        // 収束の遅い GO データでは 200 回中 9 回、NLL が最適値より 0.17〜0.3 大きい解を Converged=true で返していた
        int failures = 0, falseConvergence = 0;
        foreach (int dataSeed in new[] { 100, 102 })
        {
            var (objective, lower, upper, initial, best) =
                Problem(new ExponentialModel(), new[] { 300.0, 0.015 }, dataSeed, new ExponentialModel(), LossType.Mle);
            for (int seed = 0; seed < 100; seed++)
            {
                var result = new CMAESOptimizer(seed: seed).Optimize(objective, lower, upper, initial);
                bool failed = !result.Success || result.ObjectiveValue - best > 1e-4;
                if (failed) failures++;
                if (failed && result.Converged) falseConvergence++;
            }
        }

        Assert.True(failures <= 2, $"最適値に届かなかった回数 {failures}/200");
        Assert.Equal(0, falseConvergence);
    }

    [Fact]
    public void CmaEs_IsNotConverged_WhenStoppedByIterationLimit()
    {
        var (objective, lower, upper, initial, _) =
            Problem(new ExponentialModel(), new[] { 300.0, 0.015 }, 100, new ExponentialModel(), LossType.Mle);
        var result = new CMAESOptimizer(maxIterations: 3, seed: 1, maxRestarts: 0).Optimize(objective, lower, upper, initial);

        Assert.True(result.Success);
        Assert.False(result.Converged);
        Assert.Equal(3, result.Iterations);
    }

    [Fact]
    public void CmaEs_StartingOnBound_IsNotStuck()
    {
        // 初期点が境界上だと u = logit(0) ≈ -27.6 でシグモイドが飽和し、σ_u = 0.5 では動けなかった
        Func<double[], double> f = p => (p[0] - 3) * (p[0] - 3) + (p[1] - 7) * (p[1] - 7);
        var result = new CMAESOptimizer(seed: 4, maxRestarts: 0).Optimize(f, new[] { 0.0, 0.0 }, new[] { 10.0, 10.0 }, new[] { 0.0, 10.0 });

        Assert.Equal(3.0, result.Parameters[0], 4);
        Assert.Equal(7.0, result.Parameters[1], 4);
    }

    [Theory]
    [InlineData(LossType.Sse)]
    [InlineData(LossType.Mle)]
    public void Gwo_ReachesOptimum_AndReportsConvergenceHonestly(LossType lossType)
    {
        // 以前の GWO は群れが縮みきる前に最良値の停滞で打ち切り、SSE では 30 回中 30 回、
        // 最適値に届かないまま Converged=true を返していた
        var (objective, lower, upper, initial, best) =
            Problem(new DelayedSModel(), new[] { 150.0, 0.08 }, 100, new InflectionSModel(), lossType);
        for (int seed = 0; seed < 10; seed++)
        {
            var result = new GWOOptimizer(seed: seed).Optimize(objective, lower, upper, initial);
            double relativeGap = (result.ObjectiveValue - best) / (1 + Math.Abs(best));
            Assert.True(relativeGap < 1e-6, $"seed={seed}: 相対差 {relativeGap}");
            Assert.True(result.Converged);
        }
    }
}
