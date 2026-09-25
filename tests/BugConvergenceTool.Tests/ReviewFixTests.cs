using System.Diagnostics;
using BugConvergenceTool.Models;
using BugConvergenceTool.Optimizers;
using BugConvergenceTool.Services;
using BugConvergenceTool.Services.Diagnostics;

namespace BugConvergenceTool.Tests;

/// <summary>
/// コードレビューで見つかった問題の回帰テスト
/// </summary>
public class ReviewFixTests
{
    private static readonly double[] Times = Enumerable.Range(1, 40).Select(d => (double)d).ToArray();

    [Fact]
    public void PredictiveInterval_WithLargeUncertainty_FinishesQuickly()
    {
        // 以前は x = 0, 1, 2, … と線形に走査しており、s = 6・予測 100 件では上限が 1e7 を超え、数十億回の CDF 評価で止まっていた
        var stopwatch = Stopwatch.StartNew();
        var (lower, upper, tail) = ValidationUtility.PredictiveInterval(100, 6, 50);
        Assert.True(stopwatch.ElapsedMilliseconds < 5000, $"{stopwatch.ElapsedMilliseconds}ms");
        Assert.True(double.IsFinite(upper) && upper > 1e6);
        Assert.True(lower >= 0 && lower < 100);
        Assert.InRange(tail, 0, 1);
    }

    [Fact]
    public void PredictiveInterval_MatchesPoissonQuantiles()
    {
        // 二分探索でも線形走査と同じ分位点（Poisson(20) の 2.5% 点 12、97.5% 点 29）
        var (lower, upper, _) = ValidationUtility.PredictiveInterval(20, 0, 20);
        Assert.Equal(12, lower);
        Assert.Equal(29, upper);
        Assert.Equal((0.0, 0.0), (ValidationUtility.PredictiveInterval(0, 0, 0).Lower, ValidationUtility.PredictiveInterval(0, 0, 0).Upper));
    }

    [Fact]
    public void PredictiveInterval_WithInvalidPrediction_IsNotJudged()
    {
        var (lower, upper, tail) = ValidationUtility.PredictiveInterval(double.NaN, 0.3, 5);
        Assert.True(double.IsNaN(lower) && double.IsNaN(upper) && double.IsNaN(tail));
        var holdout = new HoldoutValidationResult { ActualIncrement = 5, PredictionLower = lower, PredictionUpper = upper };
        Assert.False(holdout.IsOutsidePredictionInterval);
    }

    [Fact]
    public void ChiSquare_WithTooFewDays_IsNotCountedAsPassing()
    {
        // 以前は 10 日未満で p = 1.0 を返し、適合性の判定で「パス」に数えられていた
        var model = new ExponentialModel();
        var t = Enumerable.Range(1, 8).Select(d => (double)d).ToArray();
        var y = ParametricBootstrap.SimulateCumulative(model, t, new[] { 50.0, 0.1 }, new Random(1));
        var (_, _, pValue, _) = new GoodnessOfFitTest().ChiSquareTest(model, t, y, new[] { 50.0, 0.1 });
        Assert.True(double.IsNaN(pValue));

        var result = new GoodnessOfFitTest().Test(model, t, y, new[] { 50.0, 0.1 }, refit: null);
        Assert.False(result.AdequacyDetermined);
        Assert.False(result.IsModelAdequate);
    }

    [Fact]
    public void FreHoldout_IncludesParameterUncertainty()
    {
        // 以前は修正数にしか効かない η・D を固定しなかったため、発見数だけのヘッセ行列が特異になり、
        // FRE モデルのホールドアウトは常に Poisson 変動だけの区間になっていた
        var data = TestHelpers.SimulateDetectionAndCorrection(200, 0.04, 0.9, 3, 40, 1);
        var fit = new ModelFitter(data, holdoutDays: 10, seed: 1).FitModel(new ConstantFREModel());
        Assert.NotNull(fit.Holdout);
        Assert.True(fit.Holdout!.IncludesParameterUncertainty);

        var mask = FisherInformationService.DetectionLikelihoodMask(new FREChangePointModel());
        Assert.Equal(new[] { false, false, false, true, true, true }, mask);   // η, D, τ を固定
        Assert.Equal(4, new FREChangePointModel().DetectionParameterCount);
    }

    [Fact]
    public void CmaEs_AllEvaluationsFail_IsNotConverged()
    {
        var result = new CMAESOptimizer(seed: 1).Optimize(_ => double.NaN, new[] { 0.0, 0.0 }, new[] { 1.0, 1.0 });
        Assert.False(result.Success);
        Assert.False(result.Converged);
    }

    [Fact]
    public void Milestone_BoundReplicatesBelowQuantile_StillMarked()
    {
        // 張り付いた反復の到達日は過小（早すぎる）。分位点より下にあっても本来は分位点を押し上げうるので「≥」を付ける
        var model = new ExponentialModel();
        var replicates = new List<double[]>();
        var atUpper = new List<bool>();
        for (int i = 0; i < 100; i++)
        {
            bool pinned = i < 10;
            replicates.Add(new[] { 150.0, pinned ? 0.2 : 0.03 + 0.0005 * i });   // 張り付いた反復は b が大きく到達日が早い
            atUpper.Add(pinned);
        }
        var bootstrap = new ParametricBootstrapResult { Replicates = replicates, AtUpperBound = atUpper, Requested = 100 };
        var milestone = PredictionIntervalService.CalculateMilestone(model, new[] { 150.0, 0.05 }, bootstrap, 0.95, 0.025, 0.975);
        Assert.True(milestone.UpperIsBoundLimited);
    }

    [Fact]
    public void ModelAveraging_IncludesChangePointModelsWithTauFixed()
    {
        // 以前は τ を含むモデルがあるとモデル内の分散を計算せず、パラメータ推定の不確実性が丸ごと抜けていた
        var data = TestHelpers.SimulateData(new ExponentialModel(), new[] { 150.0, 0.05 }, 40, 3);
        var fitter = new ModelFitter(data, seed: 1);
        var results = fitter.FitModels(new ReliabilityGrowthModelBase[] { new ExponentialModel(), new ExponentialChangePointModel() });
        var models = results.Where(r => r.Success).ToDictionary(r => r.ModelName, r => r.Model!);
        var averaging = new ModelAveragingService().Average(
            results, models, Times, data.DayCount, tData: Times, yData: data.GetCumulativeBugsFound());
        Assert.True(double.IsFinite(averaging.TotalBugsWithinModelStdDev));
    }

    [Fact]
    public void FisherLine_ShowsFixedParameterInsteadOfNaN()
    {
        var model = new ExponentialChangePointModel();
        var data = TestHelpers.SimulateData(new ExponentialModel(), new[] { 150.0, 0.05 }, 40, 3);
        var fit = new ModelFitter(data, seed: 1).FitModel(model);
        var fisher = new FisherInformationService().CalculateNHPPStandardErrors(
            model, Times, data.GetCumulativeBugsFound(), fit.ParameterVector, FisherInformationService.DetectionLikelihoodMask(model));
        Assert.True(fisher.Success, fisher.ErrorMessage);
        string tauLine = IntervalFormatter.FisherParameterLine(fisher, Array.IndexOf(model.ParameterNames, "τ"));
        Assert.Contains("固定", tauLine);
        Assert.DoesNotContain("NaN", tauLine);
    }
}
