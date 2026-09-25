using BugConvergenceTool.Models;
using MathNet.Numerics.Distributions;

namespace BugConvergenceTool.Services;

/// <summary>
/// ブートストラップの再推定に渡す合成データ
/// </summary>
/// <param name="Y">累積発見数</param>
/// <param name="Effort">累積工数（TEF モデルで工数も生成し直した場合。null なら観測値を使う）</param>
public sealed record BootstrapSample(double[] Y, double[]? Effort);

/// <summary>
/// パラメトリック・ブートストラップの結果
/// </summary>
public sealed class ParametricBootstrapResult
{
    /// <summary>再推定に成功した反復のパラメータ（失敗した反復は含めない）</summary>
    public IReadOnlyList<double[]> Replicates { get; init; } = Array.Empty<double[]>();

    /// <summary>
    /// 各反復（<see cref="Replicates"/> と同じ順）で、規模パラメータ a が探索範囲の上限に張り付いたか
    /// </summary>
    public IReadOnlyList<bool> AtUpperBound { get; init; } = Array.Empty<bool>();

    /// <summary>要求した反復回数</summary>
    public int Requested { get; init; }

    /// <summary>工数も生成し直したか（TEF モデル）</summary>
    public bool EffortResimulated { get; init; }

    /// <summary>再推定に成功した反復回数</summary>
    public int Succeeded => Replicates.Count;

    /// <summary>成功率</summary>
    public double SuccessRate => Requested > 0 ? (double)Succeeded / Requested : 0;

    /// <summary>a が上限に張り付いた反復の数</summary>
    public int AtUpperBoundCount => AtUpperBound.Count(b => b);

    /// <summary>a が上限に張り付いた反復の割合</summary>
    public double AtUpperBoundFraction => Succeeded > 0 ? (double)AtUpperBoundCount / Succeeded : 0;

    /// <summary>
    /// 上側の分位点（qHigh）が探索範囲の上限で決まっているか
    /// </summary>
    /// <remarks>
    /// 張り付いた反復の「本当の」推定値は上限より大きい（尤度は a をさらに大きくすると改善する）。
    /// その割合が上側の裾 1 - qHigh を超えると、上側の分位点は探索範囲で決まった値にすぎず、
    /// 実際の上限はそれ以上になりうる。張り付いた反復を除くと区間が不当に狭くなるので除かない。
    /// </remarks>
    public bool IsUpperLimitedByBound(double qHigh) => AtUpperBoundFraction > 1 - qHigh + 1e-12;

    /// <summary>
    /// a が上限に張り付いた反復があれば、その注意（なければ null）
    /// </summary>
    public string? BoundWarning(double qHigh)
    {
        if (AtUpperBoundCount == 0) return null;
        return IsUpperLimitedByBound(qHigh)
            ? $"再推定の {AtUpperBoundCount}/{Succeeded} 回で潜在バグ総数の規模 a が探索範囲の上限（観測最大値の 5 倍）に張り付きました。" +
              "総数・残りバグ数・収束日の区間の上限は探索範囲で決まっており、実際の上限はこれより大きい可能性があります（「≥」で表示）。"
            : $"再推定の {AtUpperBoundCount}/{Succeeded} 回で a が探索範囲の上限に張り付きました（上側の裾 {1 - qHigh:P1} より少ないため区間の上限には影響しません）。";
    }
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
    /// TEF モデルの累積工数を再生成する（工数の尤度と同じ正規分布：Ŵ(tᵢ) + N(0, σ̂²)）
    /// </summary>
    /// <remarks>
    /// σ̂² は観測工数（正の値）の残差平方和 / 件数（<see cref="MleLossFunction"/> の工数尤度の最尤推定値）。
    /// 観測工数がない日は 0 のまま（尤度でも使わない）。
    /// </remarks>
    public static double[] SimulateEffort(TEFBasedModelBase model, double[] tData, double[] parameters, Random random)
    {
        var observed = model.ObservedEffortData!;
        int n = Math.Min(tData.Length, observed.Length);
        double sse = 0;
        int count = 0;
        for (int i = 0; i < n; i++)
        {
            if (observed[i] <= 0) continue;
            double r = observed[i] - model.CalculateEffort(tData[i], parameters);
            sse += r * r;
            count++;
        }
        double sigma = count > 0 ? Math.Sqrt(sse / count) : 0;
        var simulated = new double[observed.Length];
        for (int i = 0; i < n; i++)
        {
            if (observed[i] <= 0) continue;
            simulated[i] = model.CalculateEffort(tData[i], parameters) + (sigma > 0 ? Normal.Sample(random, 0, sigma) : 0);
        }
        return simulated;
    }

    /// <summary>
    /// パラメトリック・ブートストラップを実行（発見数だけを再生成する）
    /// </summary>
    public static ParametricBootstrapResult Run(
        ReliabilityGrowthModelBase model,
        double[] tData,
        double[] parameters,
        Func<double[], double[]?> refit,
        int iterations,
        int? seed = null)
        => Run(model, tData, parameters, sample => refit(sample.Y), iterations, seed, resimulateEffort: false);

    /// <summary>
    /// パラメトリック・ブートストラップを実行
    /// </summary>
    /// <param name="model">モデル</param>
    /// <param name="tData">時刻データ</param>
    /// <param name="parameters">推定値 θ̂</param>
    /// <param name="refit">合成データを受け取りパラメータを推定し直す関数（失敗時 null）</param>
    /// <param name="iterations">反復回数</param>
    /// <param name="seed">乱数シード（null なら毎回異なる）</param>
    /// <param name="resimulateEffort">
    /// TEF モデル（工数データあり）で工数も再生成するか。工数は発見数と同時に推定しているので、
    /// 固定すると工数の不確実性が区間に入らない
    /// </param>
    public static ParametricBootstrapResult Run(
        ReliabilityGrowthModelBase model,
        double[] tData,
        double[] parameters,
        Func<BootstrapSample, double[]?> refit,
        int iterations,
        int? seed = null,
        bool resimulateEffort = true)
    {
        var replicates = new double[]?[iterations];
        var atUpper = new bool[iterations];
        int baseSeed = seed ?? Random.Shared.Next();
        var tef = resimulateEffort && model is TEFBasedModelBase t && t.ObservedEffortData != null ? t : null;

        Parallel.For(0, iterations, iter =>
        {
            try
            {
                var random = new Random(unchecked(baseSeed + iter * 7919));
                var simY = SimulateCumulative(model, tData, parameters, random);
                var effort = tef != null ? SimulateEffort(tef, tData, parameters, random) : null;
                var p = refit(new BootstrapSample(simY, effort));
                if (p != null && p.All(double.IsFinite))
                {
                    replicates[iter] = p;
                    // 再推定と同じ探索範囲（合成データの最大値で決まる）の上限に a が張り付いたか
                    var (lower, upper) = model.GetBounds(tData, simY);
                    atUpper[iter] = p[0] >= upper[0] - 1e-3 * (upper[0] - lower[0]);
                }
            }
            catch
            {
                // 失敗した反復は除外する
            }
        });

        var succeeded = Enumerable.Range(0, iterations).Where(i => replicates[i] != null).ToList();
        return new ParametricBootstrapResult
        {
            Replicates = succeeded.Select(i => replicates[i]!).ToList(),
            AtUpperBound = succeeded.Select(i => atUpper[i]).ToList(),
            Requested = iterations,
            EffortResimulated = tef != null
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
