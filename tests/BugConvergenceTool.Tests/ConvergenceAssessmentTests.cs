using BugConvergenceTool.Services;

namespace BugConvergenceTool.Tests;

public class ConvergenceAssessmentTests
{
    [Theory]
    [InlineData(0.0)]
    [InlineData(-5.0)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void InvalidTotal_IsUndetermined_NotConverged(double total)
    {
        // 以前は総数 0 で発見率 ∞ → ★★★ と判定されていた
        var result = ConvergenceAssessment.Evaluate(100, total);

        Assert.Equal(ConvergenceLevel.Undetermined, result.Level);
        Assert.Null(result.Ratio);
        Assert.DoesNotContain("★", result.Stars);
    }

    [Theory]
    [InlineData(99.0, ConvergenceLevel.Converged)]
    [InlineData(98.9, ConvergenceLevel.NearlyConverged)]
    [InlineData(95.0, ConvergenceLevel.NearlyConverged)]
    [InlineData(90.0, ConvergenceLevel.Converging)]
    [InlineData(89.9, ConvergenceLevel.NotConverged)]
    public void Thresholds(double found, ConvergenceLevel expected)
    {
        var result = ConvergenceAssessment.Evaluate(found, 100);
        Assert.Equal(expected, result.Level);
        Assert.Equal(found / 100, result.Ratio!.Value, 12);
        Assert.Null(result.Note);
    }

    [Fact]
    public void TotalBelowObserved_AddsNote()
    {
        var result = ConvergenceAssessment.Evaluate(120, 100);
        Assert.Equal(ConvergenceLevel.Converged, result.Level);
        Assert.NotNull(result.Note);
    }

    [Fact]
    public void Stars_UseLowerConfidenceBoundOfRatio()
    {
        // 点推定 99/100 = 99% でも、今後発見される件数の上限が 11 件なら発見率の信頼下限は 99/110 = 90% → ★☆☆
        var result = ConvergenceAssessment.Evaluate(99, 100, new IntervalEstimate(1, 0, 11), intervalSource: "テスト");
        Assert.Equal(ConvergenceLevel.Converging, result.Level);
        Assert.Equal(0.99, result.Ratio!.Value, 12);
        Assert.Equal(0.9, result.ConservativeRatio!.Value, 12);
        Assert.Contains("信頼下限", result.Basis);
    }

    [Fact]
    public void BoundLimitedUpper_CapsAtNoStars()
    {
        var result = ConvergenceAssessment.Evaluate(99, 100, new IntervalEstimate(1, 0, 1, UpperIsBoundLimited: true));
        Assert.Equal(ConvergenceLevel.NotConverged, result.Level);
        Assert.Contains("≥", result.Basis);
    }

    [Fact]
    public void UnstableEstimate_CapsAtNoStars()
    {
        var result = ConvergenceAssessment.Evaluate(99.5, 100, new IntervalEstimate(0.5, 0, 0), ConvergenceAssessment.UnstableAssessment);
        Assert.Equal(ConvergenceLevel.NotConverged, result.Level);
        Assert.Contains("不安定", result.Basis);
    }

    [Fact]
    public void WithoutInterval_UsesPointEstimateAndSaysSo()
    {
        var result = ConvergenceAssessment.Evaluate(96, 100);
        Assert.Equal(ConvergenceLevel.NearlyConverged, result.Level);
        Assert.Null(result.ConservativeRatio);
        Assert.Contains("点推定", result.Basis);
    }

    private static (int point, int bound) CountStars(double a, double b, ConvergenceLevel atLeast)
    {
        int point = 0, bound = 0;
        for (int seed = 0; seed < 100; seed++)
        {
            var data = TestHelpers.SimulateData(new BugConvergenceTool.Models.ExponentialModel(), new[] { a, b }, 40, 7000 + seed);
            var fitter = new ModelFitter(data, BugConvergenceTool.Optimizers.OptimizerType.NelderMead, seed: seed);
            var fit = fitter.FitModel(new BugConvergenceTool.Models.ExponentialModel());
            fitter.EnsureRemainingBugsIntervalForAssessment(fit);
            double observed = data.CurrentCumulativeBugs;
            if (ConvergenceAssessment.Evaluate(observed, fit.EstimatedTotalBugs).Level >= atLeast) point++;
            if (ConvergenceAssessment.Evaluate(observed, fit).Level >= atLeast) bound++;
        }
        return (point, bound);
    }

    [Fact]
    public void UnconvergedData_RarelyGetsStars()
    {
        // 真の発見率 83.5%（GO、a=60、40 日で m(40)/a = 0.835）の同じ条件のデータ 100 セット。
        // 点推定の発見率は 0.46〜0.96 に散らばり、点推定で判定すると約 1/4 に ★☆☆ 以上が付いていた
        var (point, bound) = CountStars(60, -Math.Log(1 - 0.835) / 40, ConvergenceLevel.Converging);
        Assert.True(point >= 10, $"点推定で ★☆☆ 以上: {point}/100（比較の前提）");
        Assert.True(bound <= 2, $"信頼下限で ★☆☆ 以上: {bound}/100（点推定 {point}/100）");
    }

    [Fact]
    public void ConvergedData_StillGetsStars()
    {
        // 真の発見率 99.97%（a=150、b=0.2）なら、信頼下限で判定しても ★★☆ 以上になる
        var (_, bound) = CountStars(150, 0.2, ConvergenceLevel.NearlyConverged);
        Assert.True(bound >= 90, $"信頼下限で ★★☆ 以上: {bound}/100");
    }
}
