using BugConvergenceTool.Models;
using MathNet.Numerics.Distributions;

namespace BugConvergenceTool.Services;

/// <summary>
/// パラメトリック・ブートストラップの結果
/// </summary>
public sealed class ParametricBootstrapResult
{
    /// <summary>再推定に成功した反復のパラメータ（失敗した反復は含めない）</summary>
    public IReadOnlyList<double[]> Replicates { get; init; } = Array.Empty<double[]>();

    /// <summary>要求した反復回数</summary>
    public int Requested { get; init; }

    /// <summary>再推定に成功した反復回数</summary>
    public int Succeeded => Replicates.Count;

    /// <summary>成功率</summary>
    public double SuccessRate => Requested > 0 ? (double)Succeeded / Requested : 0;
}

/// <summary>
/// NHPP に整合したパラメトリック・ブートストラップ
/// </summary>
/// <remarks>
/// 推定値 θ̂ のモデルから日次発見数を Poisson(m(tᵢ) - m(tᵢ₋₁)) で再生成し、
/// 呼び出し側が渡す推定手順（本推定と同じ損失関数・最適化手法）で推定し直す。
/// 推定に失敗した反復は θ̂ で置き換えず除外する（置き換えると区間が不当に狭くなる）。
/// </remarks>
public static class ParametricBootstrap
{
    /// <summary>
    /// モデルから累積発見数を Poisson 再生成する（m(0)=0 を前提）
    /// </summary>
    public static double[] SimulateCumulative(
        ReliabilityGrowthModelBase model, double[] tData, double[] parameters, Random random)
    {
        var cumulative = new double[tData.Length];
        double prevM = 0, sum = 0;
        for (int i = 0; i < tData.Length; i++)
        {
            double m = model.Calculate(tData[i], parameters);
            double lambda = m - prevM;
            sum += lambda > 0 ? Poisson.Sample(random, lambda) : 0;
            cumulative[i] = sum;
            prevM = m;
        }
        return cumulative;
    }

    /// <summary>
    /// パラメトリック・ブートストラップを実行
    /// </summary>
    /// <param name="model">モデル</param>
    /// <param name="tData">時刻データ</param>
    /// <param name="parameters">推定値 θ̂</param>
    /// <param name="refit">累積データを受け取りパラメータを推定し直す関数（失敗時 null）</param>
    /// <param name="iterations">反復回数</param>
    /// <param name="seed">乱数シード（null なら毎回異なる）</param>
    public static ParametricBootstrapResult Run(
        ReliabilityGrowthModelBase model,
        double[] tData,
        double[] parameters,
        Func<double[], double[]?> refit,
        int iterations,
        int? seed = null)
    {
        var replicates = new double[]?[iterations];
        int baseSeed = seed ?? Random.Shared.Next();

        Parallel.For(0, iterations, iter =>
        {
            try
            {
                var random = new Random(unchecked(baseSeed + iter * 7919));
                var simY = SimulateCumulative(model, tData, parameters, random);
                var p = refit(simY);
                if (p != null && p.All(double.IsFinite))
                    replicates[iter] = p;
            }
            catch
            {
                // 失敗した反復は除外する
            }
        });

        return new ParametricBootstrapResult
        {
            Replicates = replicates.Where(p => p != null).Select(p => p!).ToList(),
            Requested = iterations
        };
    }

    /// <summary>
    /// パーセンタイル（線形補間）
    /// </summary>
    public static double Percentile(IReadOnlyList<double> sorted, double q)
    {
        if (sorted.Count == 0) return double.NaN;
        double pos = q * (sorted.Count - 1);
        int lo = (int)Math.Floor(pos);
        int hi = Math.Min(lo + 1, sorted.Count - 1);
        double frac = pos - lo;
        return sorted[lo] + (sorted[hi] - sorted[lo]) * frac;
    }
}
