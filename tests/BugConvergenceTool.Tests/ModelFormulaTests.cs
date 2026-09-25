using BugConvergenceTool.Models;

namespace BugConvergenceTool.Tests;

/// <summary>
/// モデル式の理論的な性質（境界条件・特殊ケースへの一致・識別可能性）を検証する
/// </summary>
public class ModelFormulaTests
{
    public static IEnumerable<object[]> AllModelKeys() =>
        TestHelpers.CreateAllModelInstances().Select(m => new object[] { TestHelpers.ModelKey(m) });

    [Theory]
    [MemberData(nameof(AllModelKeys))]
    public void MeanValueFunction_IsZeroAtTimeZero(string modelKey)
    {
        // NHPP の尤度は増分 m(tᵢ) - m(tᵢ₋₁) で決まるため、m(0) ≠ 0 だと観測されない質量が総数に含まれる
        var model = TestHelpers.CreateAllModelInstances().Single(m => TestHelpers.ModelKey(m) == modelKey);
        var data = TestHelpers.CreateGoelOkumotoData();
        TestHelpers.PrepareModel(model, data);

        foreach (var p in TestHelpers.SampleParameterPoints(model, data))
        {
            double m0 = model.Calculate(0, p);
            Assert.True(Math.Abs(m0) <= 1e-9 * Math.Max(1.0, Math.Abs(p[0])),
                $"{model.Name}: m(0)={m0} p=[{string.Join(", ", p)}]");
        }
    }

    [Fact]
    public void NoModelHasNonIdentifiableErrorGenerationRate()
    {
        // 定数バグ混入率 α のモデルは A=a/(1-α), B=b(1-α) の再パラメータ化と恒等的に等しく α を推定できないため削除した
        var withAlpha = TestHelpers.CreateAllModelInstances()
            .Where(m => m.ParameterNames.Any(n => n is "α" or "p"))
            .Select(m => m.Name)
            .ToList();
        Assert.Empty(withAlpha);
    }

    private static readonly double[] Times = { 0.5, 3, 7, 10, 10.0001, 15, 30, 80 };

    [Fact]
    public void ExponentialChangePoint_WithEqualRates_IsGoelOkumoto()
    {
        var cp = new ExponentialChangePointModel();
        var go = new ExponentialModel();
        foreach (double t in Times)
            Assert.Equal(go.Calculate(t, new[] { 120.0, 0.07 }), cp.Calculate(t, new[] { 120.0, 0.07, 0.07, 10.0 }), 9);
    }

    [Fact]
    public void DelayedSChangePoint_WithEqualRates_IsDelayedS()
    {
        var cp = new DelayedSChangePointModel();
        var ds = new DelayedSModel();
        foreach (double t in Times)
            Assert.Equal(ds.Calculate(t, new[] { 120.0, 0.15 }), cp.Calculate(t, new[] { 120.0, 0.15, 0.15, 10.0 }), 9);
    }

    [Fact]
    public void DelayedSChangePoint_IsContinuousAndKeepsDetectingAfterChangePoint()
    {
        // 以前は変化点で新しい遅延S字を立ち上げていたため、τ 直後の検出強度が 0 に落ちていた
        var cp = new DelayedSChangePointModel();
        var p = new[] { 120.0, 0.2, 0.05, 10.0 };
        const double h = 1e-6;

        double before = cp.Calculate(10.0 - h, p);
        double after = cp.Calculate(10.0 + h, p);
        Assert.Equal(before, after, 4);

        // τ 直後の検出強度は a·S(τ)·h₂(τ)（S(τ)=(1+b₁τ)e^(-b₁τ), h₂(τ)=b₂²τ/(1+b₂τ)）で、0 にはならない
        double expected = 120.0 * (1 + 0.2 * 10) * Math.Exp(-0.2 * 10) * (0.05 * 0.05 * 10 / (1 + 0.05 * 10));
        double intensityAfter = (cp.Calculate(10.0 + 2 * h, p) - cp.Calculate(10.0 + h, p)) / h;
        Assert.True(expected > 0.5);
        Assert.Equal(expected, intensityAfter, 3);
    }

    [Fact]
    public void InflectionS_WithZeroPsi_IsGoelOkumoto()
    {
        var inflection = new InflectionSModel();
        var go = new ExponentialModel();
        foreach (double t in Times)
            Assert.Equal(go.Calculate(t, new[] { 120.0, 0.07 }), inflection.Calculate(t, new[] { 120.0, 0.07, 0.0 }), 9);
    }

    [Fact]
    public void InflectionS_IsTruncatedLogistic()
    {
        // 切断ロジスティック (L(t)-L(0))/(1-L(0)) は ψ = e^(bc) の変曲S字型と恒等的に等しい（別モデルとして持たない理由）
        var inflection = new InflectionSModel();
        double a = 120, b = 0.2, c = 15;
        double L(double t) => 1 / (1 + Math.Exp(-b * (t - c)));
        foreach (double t in Times)
        {
            double truncatedLogistic = a * (L(t) - L(0)) / (1 - L(0));
            Assert.Equal(truncatedLogistic, inflection.Calculate(t, new[] { a, b, Math.Exp(b * c) }), 9);
        }
    }

    [Fact]
    public void InflectionSChangePoint_WithEqualRates_IsInflectionS()
    {
        var cp = new InflectionSChangePointModel();
        var inflection = new InflectionSModel();
        foreach (double t in Times)
            Assert.Equal(inflection.Calculate(t, new[] { 120.0, 0.07, 3.0 }), cp.Calculate(t, new[] { 120.0, 0.07, 0.07, 3.0, 10.0 }), 9);
    }

    [Fact]
    public void MultipleChangePoint_WithEqualRates_IsGoelOkumoto()
    {
        var cp = new MultipleChangePointModel(2);
        var go = new ExponentialModel();
        // a, b1, b2, b3, τ1, τ2
        var p = new[] { 120.0, 0.07, 0.07, 0.07, 8.0, 20.0 };
        foreach (double t in Times)
            Assert.Equal(go.Calculate(t, new[] { 120.0, 0.07 }), cp.Calculate(t, p), 9);
    }

    [Fact]
    public void FixedTauWrapper_MatchesBaseModel()
    {
        var baseModel = new InflectionSChangePointModel();
        var fixedModel = new FixedTauChangePointModel(baseModel, 12);
        var p = new[] { 120.0, 0.05, 0.1, 2.0 };

        Assert.Equal(new[] { "a", "b₁", "b₂", "ψ" }, fixedModel.ParameterNames);
        foreach (double t in Times)
            Assert.Equal(baseModel.Calculate(t, new[] { 120.0, 0.05, 0.1, 2.0, 12.0 }), fixedModel.Calculate(t, p), 12);
        Assert.False(FixedTauChangePointModel.Supports(new MultipleChangePointModel(2)));
    }

    [Fact]
    public void TruncatedGompertz_ApproachesPlainGompertzForLargeB()
    {
        // b が大きいと G(0)=e^(-b)≈0 となり、切断・正規化の影響は無視できる
        var model = new GompertzModel();
        double a = 100, b = 15, c = 0.2;
        foreach (double t in Times)
        {
            double plain = a * Math.Exp(-b * Math.Exp(-c * t));
            Assert.True(Math.Abs(plain - model.Calculate(t, new[] { a, b, c })) <= a * Math.Exp(-b) * 1.01);
        }
    }

    [Fact]
    public void FindDayForCumulativeRatio_TreatsInputAsCumulative()
    {
        // 累積 [2,4,...,30]（15日）の 50% = 15 に初めて達するのは 8 日目。
        // 以前は累積値をさらに累積していたため大きく後ろにずれていた
        var cumulative = Enumerable.Range(1, 15).Select(i => 2.0 * i).ToArray();
        Assert.Equal(8.0, RatioProbe.Find(cumulative, 0.5));
        Assert.Equal(15.0, RatioProbe.Find(cumulative, 1.0));
    }

    private sealed class RatioProbe : ExponentialModel
    {
        public static double Find(double[] cumulative, double ratio) => FindDayForCumulativeRatio(cumulative, ratio);
    }
}
