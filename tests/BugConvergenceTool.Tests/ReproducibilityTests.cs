using BugConvergenceTool.Models;
using BugConvergenceTool.Optimizers;
using BugConvergenceTool.Services;

namespace BugConvergenceTool.Tests;

/// <summary>
/// 乱数シードを指定すれば、最適化・マルチスタート・ブートストラップの結果が再現できること
/// </summary>
public class ReproducibilityTests
{
    public static IEnumerable<object[]> StochasticOptimizers() =>
        new[] { OptimizerType.DifferentialEvolution, OptimizerType.PSO, OptimizerType.GWO, OptimizerType.CMAES, OptimizerType.AutoSelect }
            .Select(t => new object[] { t });

    [Theory]
    [MemberData(nameof(StochasticOptimizers))]
    public void ModelFitter_WithSeed_IsReproducible(OptimizerType type)
    {
        // 以前は最適化手法にシードを渡す口がなく、同じデータでも実行ごとに推定値が変わった
        var data = TestHelpers.SimulateData(new InflectionSModel(), new[] { 120.0, 0.12, 1.5 }, 40, seed: 7);
        var first = new ModelFitter(data, type, seed: 3).FitModel(new InflectionSModel());
        var second = new ModelFitter(data, type, seed: 3).FitModel(new InflectionSModel());

        Assert.True(first.Success);
        Assert.Equal(first.ParameterVector, second.ParameterVector);
    }

    [Fact]
    public void MultiStart_WithSeed_IsReproducible()
    {
        var data = TestHelpers.SimulateData(new InflectionSModel(), new[] { 120.0, 0.12, 1.5 }, 40, seed: 7);
        var first = new ModelFitter(data, OptimizerType.PSO, multiStarts: 4, seed: 11).FitModel(new InflectionSModel());
        var second = new ModelFitter(data, OptimizerType.PSO, multiStarts: 4, seed: 11).FitModel(new InflectionSModel());

        Assert.Equal(first.ParameterVector, second.ParameterVector);
    }

    [Fact]
    public void Bootstrap_WithSeed_IsReproducible_EvenWhenRunInParallel()
    {
        // 再推定は Parallel.For の中で行われるが、推定ごとのシードはデータの内容から決まるので実行順によらない
        var data = TestHelpers.SimulateData(new ExponentialModel(), new[] { 150.0, 0.05 }, 40, seed: 5);
        var model = new ExponentialModel();

        double[] Run()
        {
            var fitter = new ModelFitter(data, OptimizerType.PSO, seed: 2);
            var fit = fitter.FitModel(model);
            var bootstrap = ParametricBootstrap.Run(model, data.GetTimeData(), fit.ParameterVector,
                fitter.CreateRefitFunction(model), 40, seed: 9);
            return bootstrap.Replicates.SelectMany(p => p).ToArray();
        }

        Assert.Equal(Run(), Run());
    }

    [Fact]
    public void DeriveSeed_GivesDistinctStreams()
    {
        Assert.Null(OptimizerFactory.DeriveSeed(null, 1));
        var seeds = Enumerable.Range(0, 100).Select(i => OptimizerFactory.DeriveSeed(1, i)).ToList();
        Assert.Equal(100, seeds.Distinct().Count());
        Assert.All(seeds, s => Assert.True(s >= 0));
    }
}
