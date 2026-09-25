using BugConvergenceTool.Models;
using BugConvergenceTool.Optimizers;
using BugConvergenceTool.Services;

namespace BugConvergenceTool.Tests;

/// <summary>
/// ホールドアウト検証が最終結果に影響しないこと、評価が増分ベースであることを検証する
/// </summary>
public class HoldoutValidationTests
{
    [Fact]
    public void Holdout_DoesNotChangeFinalEstimates()
    {
        // 以前は訓練区間のみのパラメータが最終結果に使われ、--holdout-days の有無で収束予測がずれた
        var data = TestHelpers.CreateGoelOkumotoData();
        var model = new ExponentialModel();

        var withoutHoldout = new ModelFitter(data, OptimizerType.DifferentialEvolution).FitModel(model);
        var withHoldout = new ModelFitter(data, OptimizerType.DifferentialEvolution, holdoutDays: 10).FitModel(model);

        Assert.Null(withoutHoldout.Holdout);
        Assert.NotNull(withHoldout.Holdout);
        // DE はシードなしのため、最適化誤差程度の差は許容する
        Assert.Equal(withoutHoldout.EstimatedTotalBugs, withHoldout.EstimatedTotalBugs, 0.5);
        Assert.Equal(withoutHoldout.AIC, withHoldout.AIC, 0.01);
        Assert.Equal(data.DayCount, withHoldout.PredictionTimes.Length);
    }

    [Fact]
    public void Holdout_UsesOnlyTrainingDataForValidationFit()
    {
        // 30日間は GO、末尾10日で発見数が急増するデータ。
        // 訓練区間のみで推定していれば末尾期間の発見数は大きく過小予測になるはず
        var data = TestHelpers.CreateGoelOkumotoData(days: 30);
        for (int i = 0; i < 10; i++)
        {
            data.Dates.Add(data.Dates[^1].AddDays(1));
            data.PlannedDaily.Add(20);
            data.ActualDaily.Add(20);
            data.BugsFoundDaily.Add(12);
            data.BugsFixedDaily.Add(10);
        }

        var result = new ModelFitter(data, OptimizerType.DifferentialEvolution, holdoutDays: 10)
            .FitModel(new ExponentialModel());

        Assert.NotNull(result.Holdout);
        Assert.Equal(120, result.Holdout!.ActualIncrement, 9);
        Assert.True(result.HoldoutIncrementErrorPercent < -50,
            $"訓練区間のみの推定なら大きく過小予測になるはず（実際: {result.HoldoutIncrementErrorPercent:F1}%）");
        Assert.NotNull(result.HoldoutTrainParameters);
        Assert.NotEqual(result.HoldoutTrainParameters![0], result.ParameterVector[0], 1);
        // 急増は予測区間の外として警告する
        Assert.True(result.Holdout.IsOutsidePredictionInterval);
        Assert.Contains(WarningService.GenerateHoldoutWarnings(result, new[] { result }), w => w.Contains("予測区間") && w.Contains("過小"));
    }

    [Theory]
    [InlineData(5)]
    [InlineData(10)]
    public void CorrectModel_RarelyWarns(int holdoutDays)
    {
        // 以前は相対誤差 30%・60% のしきい値で警告しており、正しいモデル（GO データに GO）でも
        // Poisson 変動だけで末尾 5 日の 60%・末尾 10 日の 41% に警告が出ていた
        int warned = 0, total = 0;
        for (int seed = 0; seed < 200; seed++)
        {
            var data = TestHelpers.SimulateData(new ExponentialModel(), new[] { 150.0, 0.05 }, 40, 1000 + seed);
            var result = new ModelFitter(data, OptimizerType.NelderMead, holdoutDays: holdoutDays, seed: seed).FitModel(new ExponentialModel());
            if (result.Holdout == null) continue;
            total++;
            if (WarningService.GenerateHoldoutWarnings(result, new[] { result }).Count > 0) warned++;
        }
        Assert.True(total >= 190);
        Assert.True(warned <= total * 0.10, $"警告 {warned}/{total}");
    }

    [Fact]
    public void WrongModel_IsOftenOutsidePredictionInterval()
    {
        // 遅延S字型のデータに GO を当てはめると、末尾 10 日の発見数を大きく外す
        int outside = 0;
        for (int seed = 0; seed < 50; seed++)
        {
            var data = TestHelpers.SimulateData(new DelayedSModel(), new[] { 150.0, 0.1 }, 40, 1000 + seed);
            var result = new ModelFitter(data, OptimizerType.NelderMead, holdoutDays: 10, seed: seed).FitModel(new ExponentialModel());
            if (result.Holdout?.IsOutsidePredictionInterval == true) outside++;
        }
        Assert.True(outside >= 25, $"区間外 {outside}/50");
    }

    [Fact]
    public void PredictiveInterval_WidensWithParameterUncertainty()
    {
        var (poissonLower, poissonUpper, _) = ValidationUtility.PredictiveInterval(20, 0, 20);
        var (mixedLower, mixedUpper, tail) = ValidationUtility.PredictiveInterval(20, 0.3, 20);
        Assert.InRange(poissonLower, 11, 13);   // Poisson(20) の 2.5% 点 = 12
        Assert.InRange(poissonUpper, 28, 30);   // 97.5% 点 = 29
        Assert.True(mixedLower < poissonLower && mixedUpper > poissonUpper);
        Assert.True(tail > 0.5);
        Assert.True(ValidationUtility.PredictiveInterval(20, 0, 40).TailProbability < 0.01);
    }

    [Fact]
    public void IncrementMetrics_AreComputedOnIncrementsNotCumulativeLevel()
    {
        // 累積値は 100 前後なので累積ベースの MAPE なら ~2% だが、期間増分は 10 件予測 / 5 件実測で +100%
        var predicted = new[] { 102.0, 104.0, 106.0, 108.0, 110.0 };
        var actual = new[] { 101.0, 102.0, 103.0, 104.0, 105.0 };

        var m = ValidationUtility.CalculateIncrementMetrics(predicted, 100.0, actual, 100.0);

        Assert.Equal(10.0, m.PredictedIncrement, 12);
        Assert.Equal(5.0, m.ActualIncrement, 12);
        Assert.Equal(100.0, m.IncrementErrorPercent, 12);
        Assert.Equal(1.0, m.DailyMae, 12);
        Assert.Equal(1.0, m.DailyRmse, 12);
    }

    [Fact]
    public void IncrementMetrics_StartFromModelValueAtTrainEnd()
    {
        // モデルの訓練最終時点の値と実測値のずれ（水準のずれ）は増分の誤差に含めない
        var m = ValidationUtility.CalculateIncrementMetrics(
            predictedCumulative: new[] { 95.0, 97.0 }, predictedAtTrainEnd: 93.0,
            actualCumulative: new[] { 102.0, 104.0 }, actualAtTrainEnd: 100.0);

        Assert.Equal(0.0, m.IncrementErrorPercent, 12);
        Assert.Equal(0.0, m.DailyMae, 12);
    }

    [Fact]
    public void IncrementMetrics_ZeroActualIncrement_IsNaNWithWarning()
    {
        var m = ValidationUtility.CalculateIncrementMetrics(new[] { 101.0 }, 100.0, new[] { 100.0 }, 100.0);

        Assert.True(double.IsNaN(m.IncrementErrorPercent));
        Assert.NotEmpty(m.Warnings);
        Assert.Equal(1.0, m.DailyMae, 12);
    }
}
