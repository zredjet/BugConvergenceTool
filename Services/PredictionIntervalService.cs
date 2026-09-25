using BugConvergenceTool.Models;
using MathNet.Numerics.Distributions;

namespace BugConvergenceTool.Services;

/// <summary>
/// 点推定と区間
/// </summary>
/// <param name="UpperIsBoundLimited">
/// 上限が探索範囲の上限で決まっており、実際の上限はこれ以上でありうる（表示は「≥」）
/// </param>
public sealed record IntervalEstimate(double Estimate, double Lower, double Upper, bool UpperIsBoundLimited = false);

/// <summary>
/// 収束マイルストーン（x% 発見日）の区間
/// </summary>
/// <param name="Ratio">発見率（0.9 など）</param>
/// <param name="EstimateDay">推定値 θ̂ での到達日</param>
/// <param name="LowerDay">区間の下限（日）</param>
/// <param name="UpperDay">区間の上限（日）。到達しない反復が上側の分位を超える場合は +∞</param>
/// <param name="UnreachableFraction">探索範囲内で到達しなかった反復の割合</param>
/// <param name="UpperIsBoundLimited">上限が探索範囲の上限（a の張り付き）で決まっており、実際はこれ以上でありうる</param>
public sealed record MilestoneInterval(double Ratio, double EstimateDay, double LowerDay, double UpperDay, double UnreachableFraction,
    bool UpperIsBoundLimited = false);

/// <summary>
/// 予測区間の計算結果
/// </summary>
public sealed class PredictionIntervalResult
{
    public double ConfidenceLevel { get; init; }

    /// <summary>要求したブートストラップ反復回数</summary>
    public int Requested { get; init; }

    /// <summary>再推定に成功した反復回数（区間の計算に使った数）</summary>
    public int Succeeded { get; init; }

    /// <summary>将来の時刻（観測最終日の翌日以降）</summary>
    public double[] FutureTimes { get; init; } = Array.Empty<double>();

    /// <summary>将来の累積発見数の点予測 y(T) + m(t) - m(T)</summary>
    public double[] PointForecast { get; init; } = Array.Empty<double>();

    /// <summary>将来の累積発見数の予測区間（下限）</summary>
    public double[] Lower { get; init; } = Array.Empty<double>();

    /// <summary>将来の累積発見数の予測区間（上限）</summary>
    public double[] Upper { get; init; } = Array.Empty<double>();

    /// <summary>各日の上限が探索範囲の上限（a の張り付き）で決まっているか</summary>
    public bool[] UpperIsBoundLimited { get; init; } = Array.Empty<bool>();

    /// <summary>推定潜在バグ総数 m(∞) の信頼区間（パラメータ不確実性）</summary>
    public IntervalEstimate? TotalBugs { get; init; }

    /// <summary>残りバグ数（今後発見される件数）の予測区間（パラメータ不確実性 + Poisson 変動）</summary>
    public IntervalEstimate? RemainingBugs { get; init; }

    /// <summary>収束マイルストーンの到達日の区間</summary>
    public List<MilestoneInterval> Milestones { get; init; } = new();

    public List<string> Warnings { get; init; } = new();
}

/// <summary>
/// パラメトリック・ブートストラップによる予測区間
/// </summary>
/// <remarks>
/// <para>
/// ブートストラップで得た各 θ* について、観測最終日 T 以降の日次発見数を Poisson(m(t;θ*) - m(t-1;θ*)) で生成し、
/// 観測済みの累積 y(T) に積み上げた経路の分位点を予測区間とする（パラメータの不確実性と Poisson 変動の両方を含む）。
/// 観測済みの部分は確定値なので、変動を加えるのは将来の増分だけである。
/// </para>
/// <para>
/// あわせて、m(∞;θ*) の分位点で推定潜在バグ総数の信頼区間、m(t;θ*) = r·m(∞;θ*) を満たす日の分位点で
/// 収束マイルストーン到達日の区間を求める。
/// </para>
/// </remarks>
public class PredictionIntervalService
{

    /// <summary>区間を求める収束マイルストーン（発見率）</summary>
    public static readonly double[] MilestoneRatios = { 0.90, 0.95, 0.99 };

    public PredictionIntervalResult Calculate(
        ReliabilityGrowthModelBase model,
        double[] tData,
        double[] yData,
        double[] estimate,
        ParametricBootstrapResult bootstrap,
        int horizonDays,
        double confidenceLevel = 0.95,
        int? seed = null)
    {
        var warnings = new List<string>();
        var replicates = bootstrap.Replicates;
        if (replicates.Count == 0)
        {
            return new PredictionIntervalResult
            {
                ConfidenceLevel = confidenceLevel,
                Requested = bootstrap.Requested,
                Succeeded = 0,
                Warnings = { "ブートストラップの再推定がすべて失敗したため、予測区間を計算できません。" }
            };
        }
        if (bootstrap.SuccessRate < 0.8)
        {
            warnings.Add($"ブートストラップの再推定の成功が {bootstrap.Succeeded}/{bootstrap.Requested} 回と少なく、区間の精度が低い可能性があります。");
        }

        double qLow = (1 - confidenceLevel) / 2, qHigh = 1 - qLow;
        bool boundLimited = bootstrap.IsUpperLimitedByBound(qHigh);
        if (bootstrap.BoundWarning(qHigh) is { } boundWarning) warnings.Add(boundWarning);
        double tEnd = tData[^1];
        double yEnd = yData[^1];
        var futureTimes = Enumerable.Range(1, horizonDays).Select(d => tEnd + d).ToArray();

        // 将来の累積発見数の経路 [反復][日]
        // シード指定時もブートストラップ（ParametricBootstrap.Run）の合成データ生成と同じ乱数列にならないよう、
        // シードを別の系列に写す（同じ列を使うと θ* と将来の Poisson 変動が相関する）
        int baseSeed = seed.HasValue ? unchecked(seed.Value * -1640531535 + 0x5bd1e995) : Random.Shared.Next();
        var paths = new double[replicates.Count][];
        var remaining = new double[replicates.Count];
        Parallel.For(0, replicates.Count, r =>
        {
            var random = new Random(unchecked(baseSeed + r * 7919));
            var p = replicates[r];
            var path = new double[horizonDays];
            double prevM = model.Calculate(tEnd, p);
            double cumulative = yEnd;
            for (int d = 0; d < horizonDays; d++)
            {
                double m = model.Calculate(futureTimes[d], p);
                double lambda = m - prevM;
                cumulative += lambda > 0 ? Poisson.Sample(random, lambda) : 0;
                path[d] = cumulative;
                prevM = m;
            }
            paths[r] = path;

            double remainingMean = model.GetAsymptoticTotalBugs(p) - model.Calculate(tEnd, p);
            remaining[r] = remainingMean > 0 && double.IsFinite(remainingMean) ? Poisson.Sample(random, remainingMean) : 0;
        });

        var lower = new double[horizonDays];
        var upper = new double[horizonDays];
        var upperLimited = new bool[horizonDays];
        var point = new double[horizonDays];
        double mEnd = model.Calculate(tEnd, estimate);
        for (int d = 0; d < horizonDays; d++)
        {
            var sorted = paths.Select(path => path[d]).OrderBy(v => v).ToList();
            lower[d] = ParametricBootstrap.Percentile(sorted, qLow);
            upper[d] = ParametricBootstrap.Percentile(sorted, qHigh);
            upperLimited[d] = boundLimited && AnyBoundReplicateInUpperTail(paths.Select(path => path[d]).ToList(), bootstrap.AtUpperBound, upper[d]);
            point[d] = yEnd + model.Calculate(futureTimes[d], estimate) - mEnd;
        }

        var totals = replicates.Select(model.GetAsymptoticTotalBugs).Where(double.IsFinite).OrderBy(v => v).ToList();
        var remainingSorted = remaining.OrderBy(v => v).ToList();
        double estimateTotal = model.GetAsymptoticTotalBugs(estimate);

        return new PredictionIntervalResult
        {
            ConfidenceLevel = confidenceLevel,
            Requested = bootstrap.Requested,
            Succeeded = replicates.Count,
            FutureTimes = futureTimes,
            PointForecast = point,
            Lower = lower,
            Upper = upper,
            UpperIsBoundLimited = upperLimited,
            TotalBugs = new IntervalEstimate(estimateTotal,
                ParametricBootstrap.Percentile(totals, qLow), ParametricBootstrap.Percentile(totals, qHigh), boundLimited),
            RemainingBugs = new IntervalEstimate(Math.Max(0, estimateTotal - mEnd),
                ParametricBootstrap.Percentile(remainingSorted, qLow), ParametricBootstrap.Percentile(remainingSorted, qHigh), boundLimited),
            Milestones = MilestoneRatios
                .Select(ratio => CalculateMilestone(model, estimate, bootstrap, ratio, qLow, qHigh))
                .ToList(),
            Warnings = warnings
        };
    }

    /// <summary>
    /// ブートストラップの θ* から、発見率 ratio に到達する日の区間を求める
    /// </summary>
    public static MilestoneInterval CalculateMilestone(
        ReliabilityGrowthModelBase model, double[] estimate, ParametricBootstrapResult bootstrap,
        double ratio, double qLow, double qHigh)
    {
        // 到達しない反復は +∞ として分位点に含める（除外すると区間が楽観側に偏る）
        var unsorted = bootstrap.Replicates.Select(p => model.DayForRatio(ratio, p)).ToList();
        var days = unsorted.OrderBy(v => v).ToList();
        double unreachable = days.Count(double.IsPositiveInfinity) / (double)days.Count;
        double upper = QuantileWithInfinity(days, qHigh);
        // a が上限に張り付いた反復は総数が過小なので、到達日も過小（早すぎる）になりうる
        bool upperLimited = double.IsFinite(upper) && bootstrap.IsUpperLimitedByBound(qHigh)
                            && AnyBoundReplicateInUpperTail(unsorted, bootstrap.AtUpperBound, upper);
        return new MilestoneInterval(
            ratio,
            model.DayForRatio(ratio, estimate),
            QuantileWithInfinity(days, qLow),
            upper,
            unreachable,
            upperLimited);
    }

    /// <summary>
    /// a が上限に張り付いた反復のうち、値が上側の分位点以上のものがあるか
    /// （張り付いた反復が上側の裾にいなければ、その時点の上限は探索範囲の影響を受けていない）
    /// </summary>
    internal static bool AnyBoundReplicateInUpperTail(IReadOnlyList<double> values, IReadOnlyList<bool> atUpperBound, double upper)
    {
        for (int i = 0; i < values.Count && i < atUpperBound.Count; i++)
            if (atUpperBound[i] && values[i] >= upper) return true;
        return false;
    }

    /// <summary>
    /// +∞ を含む昇順リストの分位点（補間で ∞ に触れる場合は ∞）
    /// </summary>
    private static double QuantileWithInfinity(List<double> sortedDays, double q)
    {
        double pos = q * (sortedDays.Count - 1);
        int hi = Math.Min((int)Math.Ceiling(pos), sortedDays.Count - 1);
        if (double.IsPositiveInfinity(sortedDays[hi])) return double.PositiveInfinity;
        return ParametricBootstrap.Percentile(sortedDays, q);
    }
}
