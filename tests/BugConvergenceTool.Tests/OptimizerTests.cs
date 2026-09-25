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
}
