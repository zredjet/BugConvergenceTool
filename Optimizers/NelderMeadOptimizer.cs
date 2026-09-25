using System.Diagnostics;

namespace BugConvergenceTool.Optimizers;

/// <summary>
/// Nelder-Mead 法（単体法）
/// </summary>
/// <remarks>
/// <para>
/// 勾配を使わない局所最適化法。境界制約はロジスティック変換 x = l + (u - l)·σ(z) で
/// 制約なしの空間 z に写して扱う。以前は各点を境界に切り詰めていたため、単体の頂点が境界面に
/// 集まって退化し（全頂点で同じ座標になり、その次元に動けなくなる）、a が下限 maxY に張り付くなどの
/// 早期収束を起こしていた。
/// </para>
/// <para>
/// 収束後は最良点から単体を作り直して再開する（再開で改善しなくなるまで。最大 <c>MaxRestarts</c> 回）。
/// 単体法は平坦な方向で早期に縮退しやすく、再開は標準的な対策である。
/// </para>
/// </remarks>
public class NelderMeadOptimizer : IOptimizer
{
    public string Name => "NelderMead";
    public string Description => "Nelder-Mead法 - 勾配不要・低次元に強い局所最適化";

    /// <summary>収束後に単体を作り直して再開する最大回数</summary>
    private const int MaxRestarts = 4;

    /// <summary>初期単体の辺の長さ（変換後の空間 z での値。σ'(0)=1/4 なので範囲の約 1/8 に相当）</summary>
    private const double InitialStep = 0.5;

    /// <summary>変換後の空間で端点に近づきすぎないための上限（σ(±30) は 1 - 1e-13 程度）</summary>
    private const double MaxZ = 30.0;

    private readonly int _maxIterations;
    private readonly double _tolerance;
    private readonly double _alpha;   // 反射係数
    private readonly double _gamma;   // 拡大係数
    private readonly double _rho;     // 収縮係数
    private readonly double _sigma;   // 縮小係数

    /// <param name="maxIterations">最大反復回数（再開を含む合計）</param>
    /// <param name="tolerance">収束判定の許容誤差（関数値の範囲・単体の大きさ）</param>
    public NelderMeadOptimizer(
        int maxIterations = 1000,
        double tolerance = 1e-10,
        double alpha = 1.0,
        double gamma = 2.0,
        double rho = 0.5,
        double sigma = 0.5)
    {
        _maxIterations = maxIterations;
        _tolerance = tolerance;
        _alpha = alpha;
        _gamma = gamma;
        _rho = rho;
        _sigma = sigma;
    }

    public OptimizationResult Optimize(
        Func<double[], double> objectiveFunction,
        double[] lowerBounds,
        double[] upperBounds,
        double[]? initialGuess = null)
    {
        var stopwatch = Stopwatch.StartNew();
        var result = new OptimizationResult { AlgorithmName = Name };

        try
        {
            int dim = lowerBounds.Length;
            int evaluations = 0;

            double Evaluate(double[] z)
            {
                evaluations++;
                return SafeEvaluate(objectiveFunction, ToBounded(z, lowerBounds, upperBounds));
            }

            // 初期点（なければ中央 = z=0）
            var z0 = new double[dim];
            if (initialGuess != null)
            {
                for (int i = 0; i < dim; i++)
                    z0[i] = ToUnbounded(initialGuess[i], lowerBounds[i], upperBounds[i]);
            }

            double[] bestZ = z0;
            double bestValue = Evaluate(z0);
            result.ConvergenceHistory.Add(bestValue);
            int iterationsLeft = _maxIterations;
            bool converged = false;

            for (int restart = 0; restart <= MaxRestarts && iterationsLeft > 0; restart++)
            {
                var (z, value, iterations, runConverged) = RunSimplex(Evaluate, bestZ, bestValue, dim, iterationsLeft, result.ConvergenceHistory);
                iterationsLeft -= iterations;
                converged = runConverged;

                bool improved = value < bestValue - _tolerance * (1 + Math.Abs(bestValue));
                if (value < bestValue)
                {
                    bestValue = value;
                    bestZ = z;
                }

                // 再開しても改善しなければ終了（初回は必ず1回再開して確認する）
                if (restart > 0 && !improved) break;
            }

            result.Parameters = ToBounded(bestZ, lowerBounds, upperBounds);
            result.ObjectiveValue = bestValue;
            result.FunctionEvaluations = evaluations;
            result.Iterations = _maxIterations - iterationsLeft;
            result.Converged = converged;
            result.Success = OptimizationResult.IsValidObjective(bestValue);
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.ErrorMessage = ex.Message;
        }

        stopwatch.Stop();
        result.ElapsedMilliseconds = stopwatch.ElapsedMilliseconds;

        return result;
    }

    /// <summary>
    /// start を頂点の1つとする単体から Nelder-Mead を1回実行する
    /// </summary>
    private (double[] z, double value, int iterations, bool converged) RunSimplex(
        Func<double[], double> evaluate, double[] start, double startValue, int dim, int maxIterations, List<double> history)
    {
        var simplex = new double[dim + 1][];
        var values = new double[dim + 1];
        simplex[0] = (double[])start.Clone();
        values[0] = startValue;
        for (int i = 0; i < dim; i++)
        {
            simplex[i + 1] = (double[])start.Clone();
            // 端に近い場合は内側に向けて辺を張る
            simplex[i + 1][i] += start[i] > MaxZ / 2 ? -InitialStep : InitialStep;
            values[i + 1] = evaluate(simplex[i + 1]);
        }

        int iter = 0;
        bool converged = false;
        var order = new int[dim + 1];
        var centroid = new double[dim];

        for (; iter < maxIterations; iter++)
        {
            for (int i = 0; i <= dim; i++) order[i] = i;
            Array.Sort(order, (x, y) => values[x].CompareTo(values[y]));
            int best = order[0], worst = order[dim], secondWorst = order[dim - 1];

            // 収束判定: 関数値の範囲と単体の大きさ（z 空間）の両方が小さい
            double valueRange = values[worst] - values[best];
            double size = 0;
            for (int i = 0; i <= dim; i++)
                for (int d = 0; d < dim; d++)
                    size = Math.Max(size, Math.Abs(simplex[i][d] - simplex[best][d]));
            if (OptimizationResult.IsValidObjective(values[worst])
                && valueRange <= _tolerance * (1 + Math.Abs(values[best]))
                && size <= 1e-8)
            {
                converged = true;
                break;
            }

            // 重心（最悪点を除く）
            Array.Clear(centroid);
            for (int j = 0; j <= dim; j++)
            {
                if (j == worst) continue;
                for (int d = 0; d < dim; d++) centroid[d] += simplex[j][d] / dim;
            }

            // 反射
            var reflected = Combine(centroid, simplex[worst], -_alpha);
            double reflectedValue = evaluate(reflected);

            if (reflectedValue < values[best])
            {
                // 拡大
                var expanded = Combine(centroid, simplex[worst], -_alpha * _gamma);
                double expandedValue = evaluate(expanded);
                if (expandedValue < reflectedValue)
                    Replace(simplex, values, worst, expanded, expandedValue);
                else
                    Replace(simplex, values, worst, reflected, reflectedValue);
            }
            else if (reflectedValue < values[secondWorst])
            {
                Replace(simplex, values, worst, reflected, reflectedValue);
            }
            else
            {
                // 収縮（反射点が最悪点より良ければ外側、そうでなければ内側）
                bool outside = reflectedValue < values[worst];
                var contracted = outside
                    ? Combine(centroid, simplex[worst], -_alpha * _rho)
                    : Combine(centroid, simplex[worst], _rho);
                double contractedValue = evaluate(contracted);
                double reference = outside ? reflectedValue : values[worst];

                if (contractedValue < reference)
                {
                    Replace(simplex, values, worst, contracted, contractedValue);
                }
                else
                {
                    // 縮小: 最良点に向かって全頂点を縮める
                    for (int i = 0; i <= dim; i++)
                    {
                        if (i == best) continue;
                        for (int d = 0; d < dim; d++)
                            simplex[i][d] = simplex[best][d] + _sigma * (simplex[i][d] - simplex[best][d]);
                        values[i] = evaluate(simplex[i]);
                    }
                }
            }

            history.Add(values.Min());
        }

        int bestIndex = Array.IndexOf(values, values.Min());
        return (simplex[bestIndex], values[bestIndex], Math.Max(1, iter), converged);
    }

    /// <summary>
    /// centroid + coefficient × (point - centroid)（z 空間。|z| は MaxZ で抑える）
    /// </summary>
    private static double[] Combine(double[] centroid, double[] point, double coefficient)
    {
        var result = new double[centroid.Length];
        for (int d = 0; d < centroid.Length; d++)
            result[d] = Math.Clamp(centroid[d] + coefficient * (point[d] - centroid[d]), -MaxZ, MaxZ);
        return result;
    }

    private static void Replace(double[][] simplex, double[] values, int index, double[] point, double value)
    {
        simplex[index] = point;
        values[index] = value;
    }

    /// <summary>
    /// 制約なしの z から境界内の x へ: x = l + (u - l)·σ(z)
    /// </summary>
    private static double[] ToBounded(double[] z, double[] lower, double[] upper)
    {
        var x = new double[z.Length];
        for (int i = 0; i < z.Length; i++)
            x[i] = lower[i] + (upper[i] - lower[i]) / (1 + Math.Exp(-z[i]));
        return x;
    }

    /// <summary>
    /// 境界内の x から z へ（境界上の点は内側にわずかにずらす）
    /// </summary>
    private static double ToUnbounded(double x, double lower, double upper)
    {
        double range = upper - lower;
        if (range <= 0) return 0;
        double p = Math.Clamp((x - lower) / range, 1e-9, 1 - 1e-9);
        return Math.Log(p / (1 - p));
    }

    private static double SafeEvaluate(Func<double[], double> f, double[] x)
    {
        try
        {
            double val = f(x);
            return double.IsNaN(val) || double.IsInfinity(val) ? double.MaxValue : val;
        }
        catch
        {
            return double.MaxValue;
        }
    }
}
