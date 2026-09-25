using BugConvergenceTool.Models;
using BugConvergenceTool.Services;

namespace BugConvergenceTool.Tests;

/// <summary>
/// FRE モデルの修正遅れ（発見から修正までの遅れを指数分布で畳み込む）
/// </summary>
public class CorrectionDelayTests
{
    private static readonly double[] Times = Enumerable.Range(0, 81).Select(i => i * 0.5).ToArray();

    [Theory]
    [InlineData(3.0)]
    [InlineData(7.0)]
    public void ConstantFre_IsUnbiased_WhenCorrectionIsDelayed(double meanDelay)
    {
        // 以前の m_c = η·m_d は修正の遅れを b の小ささで説明し、a が過大・b が過小に偏っていた
        // （40 日のデータで 3 日遅れ: a +11%・b -16%、7 日遅れ: a +31%・b -36%）
        var a = new List<double>();
        var b = new List<double>();
        var d = new List<double>();
        for (int seed = 0; seed < 50; seed++)
        {
            var data = TestHelpers.SimulateDetectionAndCorrection(200, 0.04, 0.9, meanDelay, 40, seed);
            var fit = new ModelFitter(data, seed: seed).FitModel(new ConstantFREModel());
            Assert.True(fit.Success);
            a.Add(fit.ParameterVector[0]);
            b.Add(fit.ParameterVector[1]);
            d.Add(fit.Parameters["D"]);
        }

        Assert.InRange(a.Average(), 190, 210);
        Assert.InRange(b.Average(), 0.036, 0.044);
        Assert.InRange(d.Average(), meanDelay * 0.8, meanDelay * 1.2);
    }

    [Fact]
    public void ConstantFre_WithoutDelay_EstimatesSmallDelay()
    {
        // 遅れのないデータでは D ≈ 0 と推定され、a は発見数だけの GO とほぼ同じになる
        var d = new List<double>();
        var a = new List<double>();
        var goA = new List<double>();
        for (int seed = 0; seed < 40; seed++)
        {
            var data = TestHelpers.SimulateDetectionAndCorrection(200, 0.04, 0.9, 0, 40, seed);
            var fitter = new ModelFitter(data, seed: seed);
            var fit = fitter.FitModel(new ConstantFREModel());
            d.Add(fit.Parameters["D"]);
            a.Add(fit.ParameterVector[0]);
            goA.Add(fitter.FitModel(new ExponentialModel()).ParameterVector[0]);
            // D = 0 は自然な境界なので張り付いても注意しない
            Assert.DoesNotContain(fit.Warnings, w => w.Contains("パラメータ D"));
        }
        Assert.True(d.Average() < 0.5, $"D の平均 {d.Average()}");
        Assert.InRange(a.Average() / goA.Average(), 0.97, 1.03);
    }

    [Fact]
    public void ZeroDelay_ReducesToEtaTimesDetected()
    {
        var constant = new ConstantFREModel();
        var changePoint = new FREChangePointModel();
        foreach (double t in Times)
        {
            var p = new[] { 150.0, 0.06, 0.85, 0.0 };
            Assert.Equal(0.85 * constant.CalculateDetected(t, p), constant.CalculateCorrected(t, p), 10);
            var q = new[] { 150.0, 0.06, 0.1, 0.85, 0.0, 12.0 };
            Assert.Equal(0.85 * changePoint.CalculateDetected(t, q), changePoint.CalculateCorrected(t, q), 10);
        }
    }

    /// <summary>
    /// m_c(t) = ∫₀ᵗ η(s)·m_d'(s)·(1 - e^(-(t-s)/D)) ds の数値積分（台形則・細かい格子）
    /// </summary>
    private static double NumericCorrected(FaultRemovalEfficiencyModelBase model, double[] p, double meanDelay, double t)
    {
        if (t <= 0) return 0;
        const int steps = 20000;
        double h = t / steps, sum = 0;
        for (int i = 0; i <= steps; i++)
        {
            double s = i * h;
            double density = (model.CalculateDetected(s + 1e-6, p) - model.CalculateDetected(Math.Max(0, s - 1e-6), p)) / (s + 1e-6 - Math.Max(0, s - 1e-6));
            double f = model.GetFaultRemovalEfficiency(s, p) * density * (1 - Math.Exp(-(t - s) / meanDelay));
            sum += (i == 0 || i == steps ? 0.5 : 1) * f;
        }
        return sum * h;
    }

    [Fact]
    public void ClosedForms_MatchNumericConvolution()
    {
        var cases = new (FaultRemovalEfficiencyModelBase model, double[] p, double delay)[]
        {
            (new ConstantFREModel(), new[] { 150.0, 0.06, 0.85, 4.0 }, 4.0),
            (new ConstantFREModel(), new[] { 150.0, 0.25, 0.85, 4.0 }, 4.0),   // b = 1/D（桁落ちしやすい）
            (new LearningFREModel(), new[] { 150.0, 0.06, 0.4, 0.95, 0.1, 3.0 }, 3.0),
            (new FREChangePointModel(), new[] { 150.0, 0.03, 0.12, 0.85, 5.0, 12.0 }, 5.0),
        };
        foreach (var (model, p, delay) in cases)
            foreach (double t in new[] { 0.5, 5.0, 12.0, 13.0, 30.0, 40.0 })
            {
                double expected = NumericCorrected(model, p, delay, t);
                Assert.Equal(expected, model.CalculateCorrected(t, p), 1e-4 * (1 + expected));
            }
    }

    [Fact]
    public void LearningFre_WithoutDelay_MatchesPreviousIntegral()
    {
        // D = 0 で以前の m_c = ∫₀ᵗ η(s)·m_d'(s) ds に一致する
        var model = new LearningFREModel();
        var p = new[] { 150.0, 0.06, 0.4, 0.95, 0.1, 0.0 };
        foreach (double t in Times.Where(t => t > 0))
        {
            const int steps = 20000;
            double h = t / steps, sum = 0;
            for (int i = 0; i <= steps; i++)
            {
                double s = i * h;
                double f = model.GetFaultRemovalEfficiency(s, p) * 150 * 0.06 * Math.Exp(-0.06 * s);
                sum += (i == 0 || i == steps ? 0.5 : 1) * f;
            }
            Assert.Equal(sum * h, model.CalculateCorrected(t, p), 1e-6 * (1 + sum * h));
        }
    }
}
