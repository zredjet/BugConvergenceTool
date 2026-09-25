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
}
