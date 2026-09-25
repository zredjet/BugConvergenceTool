using BugConvergenceTool.Models;
using BugConvergenceTool.Services;
using BugConvergenceTool.Services.Diagnostics;

namespace BugConvergenceTool.Tests;

/// <summary>
/// 統計診断（適合度検定・正規性検定）の検定の大きさ（正しいモデルでの棄却率）と検出力の検証
/// </summary>
public class DiagnosticsTests
{
    private static readonly double[] Times = Enumerable.Range(1, 40).Select(i => (double)i).ToArray();

    [Fact]
    public void ChiSquare_DegreesOfFreedomAreBinsMinusParameters()
    {
        // 各ビンの件数は独立な Poisson 変数（合計は固定されない）ので df = ビン数 - k（以前は -1 余分だった）
        var model = new ExponentialModel();
        var p = new[] { 150.0, 0.05 };
        var y = ParametricBootstrap.SimulateCumulative(model, Times, p, new Random(1));

        var (_, df, _, bins) = new GoodnessOfFitTest().ChiSquareTest(model, Times, y, p);

        Assert.Equal(bins - p.Length, df);
    }

    [Fact]
    public void CvMAndNormality_HaveNominalSize_WithTrueParameters()
    {
        // 以前の CvM は正規分布のパラメータ推定用（Case 3）の表を使い、正しいモデルでも約半数を棄却していた。
        // 正規性検定は Pearson 残差に適用していたため正しいモデルでも棄却されやすかった
        var model = new ExponentialModel();
        var p = new[] { 150.0, 0.05 };
        var gof = new GoodnessOfFitTest();
        var diagnostics = new DiagnosticReportGenerator();
        int reps = 200, cvm = 0, jb = 0;
        for (int r = 0; r < reps; r++)
        {
            var y = ParametricBootstrap.SimulateCumulative(model, Times, p, new Random(1000 + r));
            if (gof.CramerVonMisesTest(model, Times, y, p).pValue < 0.05) cvm++;
            if (diagnostics.Generate(model, Times, y, p).NormalityTest!.JarqueBeraPValue < 0.05) jb++;
        }

        Assert.InRange(cvm / (double)reps, 0.01, 0.10);
        Assert.InRange(jb / (double)reps, 0.01, 0.10);
    }

    [Fact]
    public void BootstrapEdfPValues_DetectMisspecifiedModel()
    {
        // 遅延S字型のデータに指数型を当てはめると、パラメータ推定を考慮した KS・CvM で棄却される
        var truth = new DelayedSModel();
        var data = new TestData { StartDate = new DateTime(2025, 1, 6) };
        double prev = 0;
        for (int i = 0; i < 40; i++)
        {
            double m = Math.Round(truth.Calculate(i + 1, new[] { 130.0, 0.12 }));
            data.Dates.Add(data.StartDate.Value.AddDays(i));
            data.PlannedDaily.Add(0);
            data.ActualDaily.Add(0);
            data.BugsFoundDaily.Add(m - prev);
            data.BugsFixedDaily.Add(0);
            prev = m;
        }
        var fitter = new ModelFitter(data);
        var fit = fitter.FitModel(new ExponentialModel());

        var result = new GoodnessOfFitTest().Test(fit.Model!, Times, data.GetCumulativeBugsFound(), fit.ParameterVector,
            fitter.CreateRefitFunction(fit.Model!), bootstrapIterations: 99, seed: 5);

        Assert.Contains("ブートストラップ", result.EdfPValueMethod);
        Assert.True(result.CramerVonMisesPValue < 0.05, $"CvM p={result.CramerVonMisesPValue}");
        Assert.True(result.KsPValue < 0.05, $"KS p={result.KsPValue}");
    }

    /// <summary>
    /// 正しいモデル（GO データに GO を最尤推定）の診断を n 回繰り返す
    /// </summary>
    private static List<DiagnosticReport> CorrectModelReports(int count, double a = 150)
    {
        var generator = new DiagnosticReportGenerator();
        var reports = new List<DiagnosticReport>();
        for (int seed = 0; seed < count; seed++)
        {
            var data = TestHelpers.SimulateData(new ExponentialModel(), new[] { a, 0.05 }, 40, 5000 + seed);
            var model = new ExponentialModel();
            var fit = new ModelFitter(data, BugConvergenceTool.Optimizers.OptimizerType.NelderMead, seed: seed).FitModel(model);
            reports.Add(generator.Generate(model, Times, data.GetCumulativeBugsFound(), fit.ParameterVector));
        }
        return reports;
    }

    [Theory]
    [InlineData(150)]
    [InlineData(60)]
    public void Autocorrelation_CorrectModel_RarelyFlagged(double a)
    {
        // 以前は Pearson 残差に DW 帯・Ljung-Box・10 ラグの個別判定の OR をとり、正しいモデルでも 28〜35% を自己相関ありとしていた
        var reports = CorrectModelReports(200, a);
        double rate = reports.Count(r => r.AutocorrelationTest!.HasSignificantAutocorrelation) / (double)reports.Count;
        Assert.True(rate <= 0.10, $"自己相関ありの割合 {rate:P1}");
    }

    [Fact]
    public void ResidualPenalties_UseRandomizedQuantileResiduals()
    {
        // 以前の歪度・尖度・外れ値の減点は Pearson 残差で、期待値の小さい日次発見数では正しいモデルでも
        // |歪度| > 0.5 が 5〜8 割になり、減点されやすかった
        var reports = CorrectModelReports(200, 60);
        Assert.All(reports, r => Assert.Equal(ResidualType.RandomizedQuantile, r.ScoringResiduals!.Type));
        double penalized = reports.Count(r => r.OverallScore < 85) / (double)reports.Count;
        Assert.True(penalized <= 0.12, $"優良（85 点以上）にならない割合 {penalized:P1}");
    }

    [Fact]
    public void ChiSquare_FreModel_CountsOnlyDetectionParameters()
    {
        // 検定に使うのは発見数だけなので、修正数にしか効かない η・D は自由度から引かない
        var model = new ConstantFREModel();
        var p = new[] { 150.0, 0.05, 0.9, 2.0 };
        var y = ParametricBootstrap.SimulateCumulative(model, Times, p, new Random(1));

        var (_, df, pValue, bins) = new GoodnessOfFitTest().ChiSquareTest(model, Times, y, p);

        Assert.Equal(bins - 2, df);
        Assert.True(double.IsFinite(pValue));
    }

    [Fact]
    public void ChiSquare_WithoutDegreesOfFreedom_IsNotTested()
    {
        // ビン数 ≤ パラメータ数なら検定できない（以前は Max(1, B-k) で自由度 1 をでっち上げていた）
        var model = new MultipleChangePointModel(3);   // 8 パラメータ
        var p = new[] { 150.0, 0.05, 0.05, 0.05, 0.05, 8.0, 16.0, 24.0 };
        var y = ParametricBootstrap.SimulateCumulative(model, Times, p, new Random(1));

        var (_, df, pValue, bins) = new GoodnessOfFitTest().ChiSquareTest(model, Times, y, p);

        Assert.True(bins <= 8);
        Assert.True(df <= 0);
        Assert.True(double.IsNaN(pValue));
    }

    [Fact]
    public void Adequacy_WithoutCalibratedEdfTests_UsesChiSquareOnly()
    {
        // FRE モデルでは KS・CvM をブートストラップで較正できない（refit なし）。漸近 p 値は正しいモデルでほぼ棄却されず、
        // 以前は多数決に入れていたため、χ² で棄却されても「適合」になっていた
        var model = new DelayedSModel();
        var y = ParametricBootstrap.SimulateCumulative(model, Times, new[] { 150.0, 0.1 }, new Random(3));
        var go = new ExponentialModel();
        var fit = new ModelFitter(TestHelpers.FromCumulative(y), seed: 1).FitModel(go);

        var result = new GoodnessOfFitTest().Test(go, Times, y, fit.ParameterVector, refit: null);

        Assert.False(result.EdfPValuesCalibrated);
        Assert.True(result.AdequacyDetermined);
        Assert.Equal(result.ChiSquarePValue >= 0.05, result.IsModelAdequate);
        Assert.Contains("χ²検定", result.OverallAssessment);
    }
}
