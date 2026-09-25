using System.Diagnostics;
using System.Security.Cryptography;

namespace BugConvergenceTool.Optimizers;

/// <summary>
/// 差分進化（Differential Evolution）
/// DE/rand/1/bin 戦略を使用
/// </summary>
public class DEOptimizer : IOptimizer
{
    public string Name => "DE";
    public string Description => "差分進化 - 非微分可能・マルチモーダル関数に強い";
    
    private readonly int _populationSize;
    private readonly int _maxIterations;
    private readonly double _F;       // スケーリング係数
    private readonly double _CR;      // 交叉率
    private readonly double _tolerance;
    private readonly Random _random;
    
    /// <summary>
    /// コンストラクタ
    /// </summary>
    /// <param name="populationSize">個体数（デフォルト: 50）</param>
    /// <param name="maxIterations">最大反復回数（デフォルト: 500）</param>
    /// <param name="F">スケーリング係数（デフォルト: 0.8）</param>
    /// <param name="CR">交叉率（デフォルト: 0.9）</param>
    /// <param name="tolerance">収束判定閾値</param>
    /// <param name="seed">乱数シード</param>
    public DEOptimizer(
        int populationSize = 50,
        int maxIterations = 500,
        double F = 0.8,
        double CR = 0.9,
        double tolerance = 1e-10,
        int? seed = null)
    {
        _populationSize = populationSize;
        _maxIterations = maxIterations;
        _F = F;
        _CR = CR;
        _tolerance = tolerance;
        _random = seed.HasValue ? new Random(seed.Value) : new Random(RandomNumberGenerator.GetInt32(int.MaxValue));
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
            
            // 個体群の初期化
            var population = new double[_populationSize][];
            var fitness = new double[_populationSize];
            
            int bestIndex = 0;
            double bestFitness = double.MaxValue;
            
            for (int i = 0; i < _populationSize; i++)
            {
                population[i] = new double[dim];
                
                for (int d = 0; d < dim; d++)
                {
                    if (i == 0 && initialGuess != null)
                    {
                        population[i][d] = Math.Max(lowerBounds[d],
                            Math.Min(upperBounds[d], initialGuess[d]));
                    }
                    else
                    {
                        population[i][d] = lowerBounds[d] +
                            _random.NextDouble() * (upperBounds[d] - lowerBounds[d]);
                    }
                }
                
                fitness[i] = SafeEvaluate(objectiveFunction, population[i]);
                evaluations++;
                
                if (fitness[i] < bestFitness)
                {
                    bestFitness = fitness[i];
                    bestIndex = i;
                }
            }
            
            result.ConvergenceHistory.Add(bestFitness);
            
            // メインループ
            var trial = new double[dim];
            double previousBest = bestFitness;
            int stagnationCount = 0;
            
            for (int iter = 0; iter < _maxIterations; iter++)
            {
                // 適応的パラメータ
                double F = _F + 0.1 * (_random.NextDouble() - 0.5);
                
                for (int i = 0; i < _populationSize; i++)
                {
                    // 変異: DE/rand/1
                    int r1, r2, r3;
                    do { r1 = _random.Next(_populationSize); } while (r1 == i);
                    do { r2 = _random.Next(_populationSize); } while (r2 == i || r2 == r1);
                    do { r3 = _random.Next(_populationSize); } while (r3 == i || r3 == r1 || r3 == r2);
                    
                    // 交叉: 二項交叉
                    int jrand = _random.Next(dim);
                    
                    for (int d = 0; d < dim; d++)
                    {
                        if (_random.NextDouble() < _CR || d == jrand)
                        {
                            // 変異ベクトル
                            trial[d] = population[r1][d] + F * (population[r2][d] - population[r3][d]);
                            
                            // 境界処理（バウンス）
                            if (trial[d] < lowerBounds[d])
                            {
                                trial[d] = lowerBounds[d] + _random.NextDouble() * 
                                    (population[i][d] - lowerBounds[d]);
                            }
                            else if (trial[d] > upperBounds[d])
                            {
                                trial[d] = upperBounds[d] - _random.NextDouble() * 
                                    (upperBounds[d] - population[i][d]);
                            }
                        }
                        else
                        {
                            trial[d] = population[i][d];
                        }
                    }
                    
                    // 選択
                    double trialFitness = SafeEvaluate(objectiveFunction, trial);
                    evaluations++;
                    
                    if (trialFitness <= fitness[i])
                    {
                        Array.Copy(trial, population[i], dim);
                        fitness[i] = trialFitness;
                        
                        if (trialFitness < bestFitness)
                        {
                            bestFitness = trialFitness;
                            bestIndex = i;
                        }
                    }
                }
                
                result.ConvergenceHistory.Add(bestFitness);
                
                // 収束判定1: 個体群全体の目的関数値のばらつきが十分小さい（全個体が最良解の近くに集まった）
                // 最良値の停滞だけで判定すると、個体群が収束した後も 50 世代回り続けて遅い
                if (PopulationConverged(fitness, bestFitness))
                {
                    result.Converged = true;
                    result.Iterations = iter + 1;
                    break;
                }
                
                // 収束判定2: 最良値の相対変化が小さい状態が続く
                double relativeChange = Math.Abs(previousBest - bestFitness) / 
                                        (Math.Abs(previousBest) + 1e-10);
                if (relativeChange < _tolerance)
                {
                    stagnationCount++;
                    if (stagnationCount > 50)
                    {
                        result.Converged = true;
                        break;
                    }
                }
                else
                {
                    stagnationCount = 0;
                }
                previousBest = bestFitness;
                
                result.Iterations = iter + 1;
            }
            
            result.Parameters = (double[])population[bestIndex].Clone();
            result.ObjectiveValue = bestFitness;
            result.FunctionEvaluations = evaluations;
            result.Success = OptimizationResult.IsValidObjective(bestFitness);
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
    /// 個体群の目的関数値の範囲（最大 - 最小）が、最良値に対して十分小さいか
    /// </summary>
    /// <remarks>
    /// 範囲が 1e-8 × (1 + |最良値|) 以下なら収束とみなす。負の対数尤度（数十〜数百）では 1e-6 程度の差で、
    /// 推定値・AIC への影響は無視できる。
    /// </remarks>
    private static bool PopulationConverged(double[] fitness, double bestFitness)
    {
        double worst = double.MinValue;
        foreach (double f in fitness)
        {
            if (!OptimizationResult.IsValidObjective(f)) return false;
            if (f > worst) worst = f;
        }
        return worst - bestFitness <= 1e-8 * (1 + Math.Abs(bestFitness));
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
