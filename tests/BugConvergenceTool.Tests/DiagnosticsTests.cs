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
}
