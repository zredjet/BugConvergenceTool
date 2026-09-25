using System.Diagnostics;
using System.Security.Cryptography;
using MathNet.Numerics.LinearAlgebra;

namespace BugConvergenceTool.Optimizers;

/// <summary>
/// CMA-ES（Covariance Matrix Adaptation Evolution Strategy）
/// 共分散行列適応進化戦略 - 非線形・マルチモーダル問題に強いグローバル最適化
/// ロジスティック変換により無拘束空間で最適化を実行
/// </summary>
/// <remarks>
/// <para>
/// IPOP-CMA-ES（Auger &amp; Hansen 2005）: 1回の探索が収束または停滞したら、集団サイズ λ を倍にして
/// 探索範囲内の別の点からやり直す。局所解に収束した探索が2回とも同じ値なら、それ以上やり直さない。
/// </para>
/// <para>
/// <see cref="OptimizationResult.Converged"/> は、最良解を出した探索がステップ幅（TolX）と
/// 適応度の幅（TolFun）の基準で止まった場合だけ true にする。
/// 以前は最良値が 51 世代更新されないだけで Converged=true とし、やり直しもしなかったため、
/// 局所解や途中で止まった解（データによって 100 回中数回〜十数回）をそのまま返していた。
/// </para>
/// </remarks>
public class CMAESOptimizer : IOptimizer
{
    public string Name => "CMA-ES";
    public string Description => "共分散行列適応進化戦略 - 地形の難しい問題に強いグローバル最適化";
    
    private readonly int _maxIterations;
    private readonly double _tolerance;
    private readonly double _initialSigmaU;  // u空間での初期σ
    private readonly int _maxRestarts;
    private readonly Random _random;
    
    /// <summary>初期点の u の絶対値の上限（境界上の初期点でシグモイドが飽和しないようにする）</summary>
    private const double MaxInitialAbsU = 4.0;
    
    /// <summary>
    /// コンストラクタ
    /// </summary>
    /// <param name="maxIterations">1回の探索の最大世代数（デフォルト: 500）。全体の評価回数の上限もこれから決まる</param>
    /// <param name="tolerance">適応度の幅の収束判定閾値（相対。デフォルト: 1e-10）</param>
    /// <param name="initialSigmaU">u空間での初期ステップサイズ（デフォルト: 0.5）</param>
    /// <param name="seed">乱数シード</param>
    /// <param name="maxRestarts">やり直しの最大回数（デフォルト: 8）</param>
    public CMAESOptimizer(
        int maxIterations = 500,
        double tolerance = 1e-10,
        double initialSigmaU = 0.5,
        int? seed = null,
        int maxRestarts = 8)
    {
        _maxIterations = maxIterations;
        _tolerance = tolerance;
        _initialSigmaU = initialSigmaU;
        _maxRestarts = Math.Max(0, maxRestarts);
        _random = seed.HasValue 
            ? new Random(seed.Value) 
            : new Random(RandomNumberGenerator.GetInt32(int.MaxValue));
    }
    
    /// <summary>
    /// 1回の探索の結果
    /// </summary>
    private sealed record RunOutcome(double[] BestX, double BestF, bool Converged, int Evaluations, int Generations, List<double> History);
    
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
            int n = lowerBounds.Length;  // 次元数
            var lower = (double[])lowerBounds.Clone();
            var upper = (double[])upperBounds.Clone();
            
            int baseLambda = Math.Max(4 + (int)Math.Floor(3 * Math.Log(n)), 6);  // 最低6個体
            // 全体の評価回数の上限（1回目の探索の最大世代数 × 4 倍の集団分）
            long budget = (long)_maxIterations * baseLambda * 4;
            
            // 1回目は初期推定値（なければ中央）から、2回目以降は探索範囲内の一様乱数の点から始める
            double[] xInit = new double[n];
            for (int i = 0; i < n; i++)
            {
                xInit[i] = initialGuess != null && initialGuess.Length == n
                    ? Math.Min(upper[i], Math.Max(lower[i], initialGuess[i]))
                    : (lower[i] + upper[i]) / 2.0;
            }
            double[] startU = TransformXToU(xInit, lower, upper);
            
            RunOutcome? best = null;
            int convergedAtBest = 0;
            int totalEvaluations = 0, totalGenerations = 0;
            int lambda = baseLambda;
            
            for (int run = 0; run <= _maxRestarts && totalEvaluations < budget; run++)
            {
                for (int i = 0; i < n; i++)
                    startU[i] = Math.Clamp(startU[i], -MaxInitialAbsU, MaxInitialAbsU);
                
                var outcome = RunOnce(objectiveFunction, lower, upper, startU, lambda, budget - totalEvaluations);
                totalEvaluations += outcome.Evaluations;
                totalGenerations += outcome.Generations;
                result.ConvergenceHistory.AddRange(outcome.History);
                
                if (best == null || outcome.BestF < best.BestF - SameValueTolerance(best.BestF))
                {
                    // 明らかに良い解が見つかった
                    best = outcome;
                    convergedAtBest = outcome.Converged ? 1 : 0;
                }
                else if (Math.Abs(outcome.BestF - best.BestF) <= SameValueTolerance(best.BestF))
                {
                    // 同じ値に到達した（収束したほうを優先する）
                    if (outcome.Converged) convergedAtBest++;
                    if (outcome.BestF < best.BestF || (outcome.Converged && !best.Converged))
                        best = outcome with { Converged = best.Converged || outcome.Converged };
                }
                
                // 2回の探索が同じ最良値に収束したら、それ以上やり直さない
                if (convergedAtBest >= 2) break;
                
                // IPOP: 集団サイズを倍にして、探索範囲内のランダムな点からやり直す
                lambda *= 2;
                for (int i = 0; i < n; i++)
                    startU[i] = Logit(0.05 + 0.9 * _random.NextDouble());
            }
            
            result.Parameters = best?.BestX ?? xInit;
            result.ObjectiveValue = best?.BestF ?? double.MaxValue;
            result.Converged = best?.Converged ?? false;
            result.FunctionEvaluations = totalEvaluations;
            result.Iterations = totalGenerations;
            result.Success = OptimizationResult.IsValidObjective(result.ObjectiveValue);
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
    /// 2つの目的関数値を同じとみなす許容差
    /// </summary>
    private static double SameValueTolerance(double f) => 1e-9 * (1 + Math.Abs(f));
    
    /// <summary>
    /// CMA-ES の探索を1回行う
    /// </summary>
    /// <param name="startU">開始点（u空間）</param>
    /// <param name="lambda">集団サイズ</param>
    /// <param name="maxEvaluations">この探索で使える評価回数</param>
    private RunOutcome RunOnce(
        Func<double[], double> objectiveFunction,
        double[] lower,
        double[] upper,
        double[] startU,
        int lambda,
        long maxEvaluations)
    {
        int n = lower.Length;
        int evaluations = 0;
        var history = new List<double>();
        
        var m_u = Vector<double>.Build.DenseOfArray((double[])startU.Clone());
        double sigma_u = _initialSigmaU;
        var C = Matrix<double>.Build.DenseIdentity(n);
        var p_c = Vector<double>.Build.Dense(n, 0.0);
        var p_sigma = Vector<double>.Build.Dense(n, 0.0);
        var B = Matrix<double>.Build.DenseIdentity(n);
        var D = Vector<double>.Build.Dense(n, 1.0);
        
        int mu = lambda / 2;
        
        // 重み w_i（対数スケーリング）
        double[] weights = new double[mu];
        double sumW = 0;
        for (int i = 0; i < mu; i++)
        {
            weights[i] = Math.Log(mu + 0.5) - Math.Log(i + 1);
            sumW += weights[i];
        }
        for (int i = 0; i < mu; i++)
            weights[i] /= sumW;
        
        double sumW2 = 0;
        for (int i = 0; i < mu; i++)
            sumW2 += weights[i] * weights[i];
        double mu_eff = 1.0 / sumW2;
        
        // アルゴリズムパラメータ（標準値）
        double c_sigma = (mu_eff + 2.0) / (n + mu_eff + 5.0);
        double d_sigma = 1.0 + 2.0 * Math.Max(0, Math.Sqrt((mu_eff - 1.0) / (n + 1.0)) - 1.0) + c_sigma;
        double c_c = (4.0 + mu_eff / n) / (n + 4.0 + 2.0 * mu_eff / n);
        double c1 = 2.0 / ((n + 1.3) * (n + 1.3) + mu_eff);
        double c_mu = Math.Min(1.0 - c1, 2.0 * (mu_eff - 2.0 + 1.0 / mu_eff) / ((n + 2.0) * (n + 2.0) + mu_eff));
        double chi_n = Math.Sqrt(n) * (1.0 - 1.0 / (4.0 * n) + 1.0 / (21.0 * n * n));
        
        double[] bestX = TransformUToX(m_u.ToArray(), lower, upper);
        double bestFitness = SafeEvaluate(objectiveFunction, bestX);
        evaluations++;
        history.Add(bestFitness);
        
        // 停滞の判定: 直近の世代の最良値（Hansen の TolFun 用の履歴長）
        int historyLength = 10 + (int)Math.Ceiling(30.0 * n / lambda);
        var generationBests = new Queue<double>();
        // 最良値が更新されない世代数の上限（停滞したら収束ではなくやり直し）
        int stagnationLimit = 20 + (int)Math.Ceiling(50.0 * n / lambda);
        int sinceImprovement = 0;
        bool converged = false;
        int generations = 0;
        
        while (generations < _maxIterations && evaluations + lambda <= maxEvaluations)
        {
            generations++;
            var population = new List<(Vector<double> y_u, Vector<double> z, double[] x, double f)>(lambda);
            bool improved = false;
            
            for (int k = 0; k < lambda; k++)
            {
                var z = SampleStandardNormalVector(n);
                var y_u = B * D.PointwiseMultiply(z);
                var u_k = m_u + sigma_u * y_u;
                double[] x_k = TransformUToX(u_k.ToArray(), lower, upper);
                double f_k = SafeEvaluate(objectiveFunction, x_k);
                evaluations++;
                population.Add((y_u, z, x_k, f_k));
                
                if (f_k < bestFitness)
                {
                    improved |= f_k < bestFitness - SameValueTolerance(bestFitness);
                    bestFitness = f_k;
                    bestX = (double[])x_k.Clone();
                }
            }
            
            population.Sort((a, b) => a.f.CompareTo(b.f));
            
            // m_u の更新
            var y_w = Vector<double>.Build.Dense(n, 0.0);
            var z_w = Vector<double>.Build.Dense(n, 0.0);
            for (int i = 0; i < mu; i++)
            {
                y_w += weights[i] * population[i].y_u;
                z_w += weights[i] * population[i].z;
            }
            m_u += sigma_u * y_w;
            
            // p_sigma・σ_u の更新（白色化空間で管理：B を掛けない）
            p_sigma = (1.0 - c_sigma) * p_sigma + Math.Sqrt(c_sigma * (2.0 - c_sigma) * mu_eff) * z_w;
            double p_sigma_norm = p_sigma.L2Norm();
            sigma_u *= Math.Exp((c_sigma / d_sigma) * (p_sigma_norm / chi_n - 1.0));
            sigma_u = Math.Min(10.0, sigma_u);  // 発散防止
            
            // p_c・C の更新（rank-1 + rank-μ）
            double expectedNorm = Math.Max(1e-10, Math.Sqrt(1.0 - Math.Pow(1.0 - c_sigma, 2.0 * generations)));
            bool h_sigma = (p_sigma_norm / expectedNorm) < (1.4 + 2.0 / (n + 1.0)) * chi_n;
            p_c = h_sigma
                ? (1.0 - c_c) * p_c + Math.Sqrt(c_c * (2.0 - c_c) * mu_eff) * y_w
                : (1.0 - c_c) * p_c;
            double delta_h = h_sigma ? 0.0 : c_c * (2.0 - c_c);
            var rankMu = Matrix<double>.Build.Dense(n, n, 0.0);
            for (int i = 0; i < mu; i++)
                rankMu += weights[i] * population[i].y_u.OuterProduct(population[i].y_u);
            C = (1.0 - c1 - c_mu + delta_h * c1) * C + c1 * p_c.OuterProduct(p_c) + c_mu * rankMu;
            
            // C の固有値分解。失敗したら（数値的に破綻した探索なので）この探索を終えてやり直す
            C = (C + C.Transpose()) / 2.0;
            try
            {
                var evd = C.Evd();
                var eigenValues = evd.EigenValues.Real();
                if (eigenValues.Any(v => !(v > 0) || !double.IsFinite(v))) break;
                B = evd.EigenVectors;
                for (int i = 0; i < n; i++)
                    D[i] = Math.Sqrt(eigenValues[i]);
            }
            catch
            {
                break;
            }
            
            history.Add(bestFitness);
            
            // TolFun: 直近の世代の最良値と今の世代の適応度の幅が十分小さい
            generationBests.Enqueue(population[0].f);
            if (generationBests.Count > historyLength) generationBests.Dequeue();
            double scale = _tolerance * (1 + Math.Abs(bestFitness));
            bool flatGeneration = population[^1].f - population[0].f <= scale;
            bool flatHistory = generationBests.Count == historyLength && generationBests.Max() - generationBests.Min() <= scale;
            // TolX: u空間の探索幅が十分小さい
            bool smallStep = sigma_u * Math.Max(D.Maximum(), p_c.AbsoluteMaximum()) < 1e-9;
            if (smallStep || (flatGeneration && flatHistory))
            {
                // 評価がすべて失敗している（適合度が一律 MaxValue）場合は「幅 0」でも収束ではない
                converged = OptimizationResult.IsValidObjective(bestFitness);
                break;
            }
            
            // 停滞（最良値が長く更新されない）: 収束ではないので Converged にしない
            sinceImprovement = improved ? 0 : sinceImprovement + 1;
            if (sinceImprovement > stagnationLimit) break;
        }
        
        return new RunOutcome(bestX, bestFitness, converged, evaluations, generations, history);
    }
    
    #region 変換ヘルパー（u ↔ x）
    
    /// <summary>
    /// シグモイド関数（オーバーフロー対策込み）
    /// </summary>
    private static double Sigmoid(double u)
    {
        if (u >= 0)
        {
            double e = Math.Exp(-u);
            return 1.0 / (1.0 + e);
        }
        else
        {
            double e = Math.Exp(u);
            return e / (1.0 + e);
        }
    }
    
    /// <summary>
    /// ロジット関数（逆シグモイド）
    /// </summary>
    private static double Logit(double p)
    {
        const double eps = 1e-12;
        double pp = Math.Min(1.0 - eps, Math.Max(eps, p));
        return Math.Log(pp / (1.0 - pp));
    }
    
    /// <summary>
    /// u空間からx空間への変換
    /// x_i = lower_i + (upper_i - lower_i) * sigmoid(u_i)
    /// </summary>
    private static double[] TransformUToX(double[] u, double[] lower, double[] upper)
    {
        var x = new double[u.Length];
        for (int i = 0; i < u.Length; i++)
        {
            double range = upper[i] - lower[i];
            if (range <= 0)
            {
                x[i] = lower[i];  // 固定パラメータ
                continue;
            }
            double s = Sigmoid(u[i]);  // 0〜1
            x[i] = lower[i] + range * s;
        }
        return x;
    }
    
    /// <summary>
    /// x空間からu空間への変換
    /// u_i = logit((x_i - lower_i) / (upper_i - lower_i))
    /// </summary>
    private static double[] TransformXToU(double[] x, double[] lower, double[] upper)
    {
        var u = new double[x.Length];
        for (int i = 0; i < x.Length; i++)
        {
            double range = upper[i] - lower[i];
            if (range <= 0)
            {
                u[i] = 0.0;  // 固定パラメータ扱い
                continue;
            }
            double p = (x[i] - lower[i]) / range;  // 0〜1
            u[i] = Logit(p);
        }
        return u;
    }
    
    #endregion
    
    /// <summary>
    /// 標準正規分布からn次元ベクトルをサンプリング（Box-Muller法）
    /// </summary>
    private Vector<double> SampleStandardNormalVector(int n)
    {
        var z = new double[n];
        
        for (int i = 0; i < n; i += 2)
        {
            // Box-Muller 変換
            double u1 = _random.NextDouble();
            double u2 = _random.NextDouble();
            
            // u1 が 0 にならないようにする
            while (u1 < 1e-10)
                u1 = _random.NextDouble();
            
            double r = Math.Sqrt(-2.0 * Math.Log(u1));
            double theta = 2.0 * Math.PI * u2;
            
            z[i] = r * Math.Cos(theta);
            if (i + 1 < n)
                z[i + 1] = r * Math.Sin(theta);
        }
        
        return Vector<double>.Build.DenseOfArray(z);
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
