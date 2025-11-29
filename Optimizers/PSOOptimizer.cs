using System.Diagnostics;
using System.Security.Cryptography;

namespace BugConvergenceTool.Optimizers;

/// <summary>
/// 粒子群最適化（Particle Swarm Optimization）
/// </summary>
public class PSOOptimizer : IOptimizer
{
    public string Name => "PSO";
    public string Description => "粒子群最適化 - 群知能に基づく大域的最適化";
    
    // PSO パラメータ
    private readonly int _swarmSize;
    private readonly int _maxIterations;
    private readonly double _w;      // 慣性重み
    private readonly double _c1;     // 認知係数
    private readonly double _c2;     // 社会係数
    private readonly double _tolerance;
    private readonly Random _random;
    
    /// <summary>
    /// コンストラクタ
    /// </summary>
    /// <param name="swarmSize">粒子数（デフォルト: 30）</param>
    /// <param name="maxIterations">最大反復回数（デフォルト: 500）</param>
    /// <param name="w">慣性重み（デフォルト: 0.729）</param>
    /// <param name="c1">認知係数（デフォルト: 1.49445）</param>
    /// <param name="c2">社会係数（デフォルト: 1.49445）</param>
    /// <param name="tolerance">収束判定閾値</param>
    /// <param name="seed">乱数シード（null=時刻ベース）</param>
    public PSOOptimizer(
        int swarmSize = 30,
        int maxIterations = 500,
        double w = 0.729,
        double c1 = 1.49445,
        double c2 = 1.49445,
        double tolerance = 1e-10,
        int? seed = null)
    {
        _swarmSize = swarmSize;
        _maxIterations = maxIterations;
        _w = w;
        _c1 = c1;
        _c2 = c2;
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
            
            // 粒子の初期化
            var particles = new Particle[_swarmSize];
            var globalBestPosition = new double[dim];
            double globalBestFitness = double.MaxValue;
            
            for (int i = 0; i < _swarmSize; i++)
            {
                particles[i] = new Particle(dim);
                
                // 位置の初期化（初期推定値がある場合は一部利用）
                for (int d = 0; d < dim; d++)
                {
                    if (i == 0 && initialGuess != null)
                    {
                        // 最初の粒子は初期推定値を使用
                        particles[i].Position[d] = Math.Max(lowerBounds[d], 
                            Math.Min(upperBounds[d], initialGuess[d]));
                    }
                    else
                    {
                        particles[i].Position[d] = lowerBounds[d] + 
                            _random.NextDouble() * (upperBounds[d] - lowerBounds[d]);
                    }
                    
                    // 速度の初期化
                    double range = upperBounds[d] - lowerBounds[d];
                    particles[i].Velocity[d] = (_random.NextDouble() - 0.5) * range * 0.1;
                }
                
                // 適合度を評価
                double fitness = SafeEvaluate(objectiveFunction, particles[i].Position);
                evaluations++;
                particles[i].BestFitness = fitness;
                Array.Copy(particles[i].Position, particles[i].BestPosition, dim);
                
                if (fitness < globalBestFitness)
                {
                    globalBestFitness = fitness;
                    Array.Copy(particles[i].Position, globalBestPosition, dim);
                }
            }
            
            result.ConvergenceHistory.Add(globalBestFitness);
            
            // メインループ
            double previousBest = globalBestFitness;
            int stagnationCount = 0;
            
            for (int iter = 0; iter < _maxIterations; iter++)
            {
                // 適応的慣性重み（線形減少）
                double w = _w - (_w - 0.4) * iter / _maxIterations;
                
                for (int i = 0; i < _swarmSize; i++)
                {
                    var p = particles[i];
                    
                    for (int d = 0; d < dim; d++)
                    {
                        double r1 = _random.NextDouble();
                        double r2 = _random.NextDouble();
                        
                        // 速度更新
                        p.Velocity[d] = w * p.Velocity[d]
                            + _c1 * r1 * (p.BestPosition[d] - p.Position[d])
                            + _c2 * r2 * (globalBestPosition[d] - p.Position[d]);
                        
                        // 速度制限
                        double vMax = (upperBounds[d] - lowerBounds[d]) * 0.2;
                        p.Velocity[d] = Math.Max(-vMax, Math.Min(vMax, p.Velocity[d]));
                        
                        // 位置更新
                        p.Position[d] += p.Velocity[d];
                        
                        // 境界処理（反射）
                        if (p.Position[d] < lowerBounds[d])
                        {
                            p.Position[d] = lowerBounds[d];
                            p.Velocity[d] *= -0.5;
                        }
                        else if (p.Position[d] > upperBounds[d])
                        {
                            p.Position[d] = upperBounds[d];
                            p.Velocity[d] *= -0.5;
                        }
                    }
                    
                    // 適合度を評価
                    double fitness = SafeEvaluate(objectiveFunction, p.Position);
                    evaluations++;
                    
                    // 個人最良を更新
                    if (fitness < p.BestFitness)
                    {
                        p.BestFitness = fitness;
                        Array.Copy(p.Position, p.BestPosition, dim);
                        
                        // 全体最良を更新
                        if (fitness < globalBestFitness)
                        {
                            globalBestFitness = fitness;
                            Array.Copy(p.Position, globalBestPosition, dim);
                        }
                    }
                }
                
                result.ConvergenceHistory.Add(globalBestFitness);
                
                // 収束判定（相対収束）
                double relativeChange = Math.Abs(previousBest - globalBestFitness) / 
                                        (Math.Abs(previousBest) + 1e-10);
                if (relativeChange < _tolerance)
                {
                    stagnationCount++;
                    if (stagnationCount > 50)
                        break;
                }
                else
                {
                    stagnationCount = 0;
                }
                previousBest = globalBestFitness;
                
                result.Iterations = iter + 1;
            }
            
            result.Parameters = globalBestPosition;
            result.ObjectiveValue = globalBestFitness;
            result.FunctionEvaluations = evaluations;
            result.Success = !double.IsNaN(globalBestFitness) && !double.IsInfinity(globalBestFitness);
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
    
    /// <summary>
    /// 粒子クラス
    /// </summary>
    private class Particle
    {
        public double[] Position { get; }
        public double[] Velocity { get; }
        public double[] BestPosition { get; }
        public double BestFitness { get; set; } = double.MaxValue;
        
        public Particle(int dim)
        {
            Position = new double[dim];
            Velocity = new double[dim];
            BestPosition = new double[dim];
        }
    }
}
