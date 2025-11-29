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
                
                // 収束判定
                if (Math.Abs(previousBest - bestFitness) < _tolerance)
                {
                    stagnationCount++;
                    if (stagnationCount > 50)
                        break;
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
            result.Success = !double.IsNaN(bestFitness) && !double.IsInfinity(bestFitness);
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
