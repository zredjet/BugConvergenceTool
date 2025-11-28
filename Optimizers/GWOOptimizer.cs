using System.Diagnostics;

namespace BugConvergenceTool.Optimizers;

/// <summary>
/// Grey Wolf Optimizer（灰色オオカミ最適化）
/// オオカミの群れ行動に基づく最適化アルゴリズム
/// </summary>
public class GWOOptimizer : IOptimizer
{
    public string Name => "GWO";
    public string Description => "Grey Wolf Optimizer - オオカミの群れ行動に基づく最適化";
    
    private readonly int _packSize;
    private readonly int _maxIterations;
    private readonly double _tolerance;
    private readonly Random _random;
    
    /// <summary>
    /// コンストラクタ
    /// </summary>
    /// <param name="packSize">群れサイズ（デフォルト: 30）</param>
    /// <param name="maxIterations">最大反復回数（デフォルト: 500）</param>
    /// <param name="tolerance">収束判定閾値</param>
    /// <param name="seed">乱数シード</param>
    public GWOOptimizer(
        int packSize = 30,
        int maxIterations = 500,
        double tolerance = 1e-10,
        int? seed = null)
    {
        _packSize = packSize;
        _maxIterations = maxIterations;
        _tolerance = tolerance;
        _random = seed.HasValue ? new Random(seed.Value) : new Random();
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
            
            // 群れの初期化
            var wolves = new double[_packSize][];
            var fitness = new double[_packSize];
            
            // α, β, δ（上位3頭）の位置と適合度
            var alpha = new double[dim];
            var beta = new double[dim];
            var delta = new double[dim];
            double alphaFitness = double.MaxValue;
            double betaFitness = double.MaxValue;
            double deltaFitness = double.MaxValue;
            
            // 初期化
            for (int i = 0; i < _packSize; i++)
            {
                wolves[i] = new double[dim];
                
                for (int d = 0; d < dim; d++)
                {
                    if (i == 0 && initialGuess != null)
                    {
                        wolves[i][d] = Math.Max(lowerBounds[d],
                            Math.Min(upperBounds[d], initialGuess[d]));
                    }
                    else
                    {
                        wolves[i][d] = lowerBounds[d] +
                            _random.NextDouble() * (upperBounds[d] - lowerBounds[d]);
                    }
                }
                
                fitness[i] = SafeEvaluate(objectiveFunction, wolves[i]);
                evaluations++;
                
                // α, β, δ を更新
                UpdateLeaders(wolves[i], fitness[i], ref alpha, ref beta, ref delta,
                    ref alphaFitness, ref betaFitness, ref deltaFitness);
            }
            
            result.ConvergenceHistory.Add(alphaFitness);
            
            // メインループ
            double previousBest = alphaFitness;
            int stagnationCount = 0;
            
            for (int iter = 0; iter < _maxIterations; iter++)
            {
                // a は 2 から 0 に線形減少
                double a = 2.0 - iter * (2.0 / _maxIterations);
                
                for (int i = 0; i < _packSize; i++)
                {
                    for (int d = 0; d < dim; d++)
                    {
                        // α に向かう移動
                        double r1 = _random.NextDouble();
                        double r2 = _random.NextDouble();
                        double A1 = 2 * a * r1 - a;
                        double C1 = 2 * r2;
                        double D_alpha = Math.Abs(C1 * alpha[d] - wolves[i][d]);
                        double X1 = alpha[d] - A1 * D_alpha;
                        
                        // β に向かう移動
                        r1 = _random.NextDouble();
                        r2 = _random.NextDouble();
                        double A2 = 2 * a * r1 - a;
                        double C2 = 2 * r2;
                        double D_beta = Math.Abs(C2 * beta[d] - wolves[i][d]);
                        double X2 = beta[d] - A2 * D_beta;
                        
                        // δ に向かう移動
                        r1 = _random.NextDouble();
                        r2 = _random.NextDouble();
                        double A3 = 2 * a * r1 - a;
                        double C3 = 2 * r2;
                        double D_delta = Math.Abs(C3 * delta[d] - wolves[i][d]);
                        double X3 = delta[d] - A3 * D_delta;
                        
                        // 新しい位置（3頭の平均）
                        wolves[i][d] = (X1 + X2 + X3) / 3.0;
                        
                        // 境界処理
                        wolves[i][d] = Math.Max(lowerBounds[d], 
                            Math.Min(upperBounds[d], wolves[i][d]));
                    }
                    
                    // 適合度を評価
                    fitness[i] = SafeEvaluate(objectiveFunction, wolves[i]);
                    evaluations++;
                    
                    // α, β, δ を更新
                    UpdateLeaders(wolves[i], fitness[i], ref alpha, ref beta, ref delta,
                        ref alphaFitness, ref betaFitness, ref deltaFitness);
                }
                
                result.ConvergenceHistory.Add(alphaFitness);
                
                // 収束判定
                if (Math.Abs(previousBest - alphaFitness) < _tolerance)
                {
                    stagnationCount++;
                    if (stagnationCount > 50)
                        break;
                }
                else
                {
                    stagnationCount = 0;
                }
                previousBest = alphaFitness;
                
                result.Iterations = iter + 1;
            }
            
            result.Parameters = (double[])alpha.Clone();
            result.ObjectiveValue = alphaFitness;
            result.FunctionEvaluations = evaluations;
            result.Success = !double.IsNaN(alphaFitness) && !double.IsInfinity(alphaFitness);
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
    /// α, β, δ（上位3頭）を更新
    /// </summary>
    private void UpdateLeaders(
        double[] position, double fitness,
        ref double[] alpha, ref double[] beta, ref double[] delta,
        ref double alphaFitness, ref double betaFitness, ref double deltaFitness)
    {
        if (fitness < alphaFitness)
        {
            // 新しい α
            deltaFitness = betaFitness;
            Array.Copy(beta, delta, beta.Length);
            
            betaFitness = alphaFitness;
            Array.Copy(alpha, beta, alpha.Length);
            
            alphaFitness = fitness;
            Array.Copy(position, alpha, position.Length);
        }
        else if (fitness < betaFitness)
        {
            // 新しい β
            deltaFitness = betaFitness;
            Array.Copy(beta, delta, beta.Length);
            
            betaFitness = fitness;
            Array.Copy(position, beta, position.Length);
        }
        else if (fitness < deltaFitness)
        {
            // 新しい δ
            deltaFitness = fitness;
            Array.Copy(position, delta, position.Length);
        }
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
