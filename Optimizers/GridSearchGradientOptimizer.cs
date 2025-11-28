using System.Diagnostics;

namespace BugConvergenceTool.Optimizers;

/// <summary>
/// グリッドサーチ + 勾配降下法（既存手法）
/// </summary>
public class GridSearchGradientOptimizer : IOptimizer
{
    public string Name => "GridSearch+GD";
    public string Description => "グリッドサーチ + 勾配降下法 - 従来手法";
    
    private readonly int _gridSize;
    private readonly int _maxIterations;
    private readonly double _learningRate;
    private readonly double _delta;
    
    /// <summary>
    /// コンストラクタ
    /// </summary>
    /// <param name="gridSize">グリッドサイズ（デフォルト: 自動）</param>
    /// <param name="maxIterations">勾配降下法の最大反復回数</param>
    /// <param name="learningRate">学習率</param>
    /// <param name="delta">数値微分のデルタ</param>
    public GridSearchGradientOptimizer(
        int gridSize = 0,
        int maxIterations = 2000,
        double learningRate = 0.00005,
        double delta = 0.0001)
    {
        _gridSize = gridSize;
        _maxIterations = maxIterations;
        _learningRate = learningRate;
        _delta = delta;
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
            
            // グリッドサイズを決定
            int gridSize = _gridSize > 0 ? _gridSize : 
                (dim <= 2 ? 20 : (dim == 3 ? 10 : 8));
            
            // グリッドサーチ
            double bestSSE = double.MaxValue;
            double[]? bestParams = null;
            
            void GridSearch(double[] current, int depth)
            {
                if (depth == dim)
                {
                    double sse = SafeEvaluate(objectiveFunction, current);
                    evaluations++;
                    
                    if (sse < bestSSE)
                    {
                        bestSSE = sse;
                        bestParams = (double[])current.Clone();
                    }
                    return;
                }
                
                double step = (upperBounds[depth] - lowerBounds[depth]) / gridSize;
                for (int i = 1; i <= gridSize; i++)
                {
                    current[depth] = lowerBounds[depth] + step * i;
                    GridSearch(current, depth + 1);
                }
            }
            
            GridSearch(new double[dim], 0);
            
            if (bestParams == null)
            {
                result.Success = false;
                result.ErrorMessage = "グリッドサーチで有効な解が見つかりませんでした";
                return result;
            }
            
            result.ConvergenceHistory.Add(bestSSE);
            
            // 勾配降下法で精緻化
            double[] p = (double[])bestParams.Clone();
            
            for (int iter = 0; iter < _maxIterations; iter++)
            {
                double[] gradient = new double[dim];
                
                for (int i = 0; i < dim; i++)
                {
                    double[] pPlus = (double[])p.Clone();
                    double[] pMinus = (double[])p.Clone();
                    pPlus[i] += _delta;
                    pMinus[i] -= _delta;
                    
                    double ssePlus = SafeEvaluate(objectiveFunction, pPlus);
                    double sseMinus = SafeEvaluate(objectiveFunction, pMinus);
                    evaluations += 2;
                    
                    gradient[i] = (ssePlus - sseMinus) / (2 * _delta);
                    
                    if (double.IsNaN(gradient[i]) || double.IsInfinity(gradient[i]))
                        gradient[i] = 0;
                }
                
                // パラメータ更新
                for (int i = 0; i < dim; i++)
                {
                    p[i] -= _learningRate * gradient[i];
                    p[i] = Math.Max(lowerBounds[i], Math.Min(upperBounds[i], p[i]));
                }
                
                double currentSSE = SafeEvaluate(objectiveFunction, p);
                evaluations++;
                
                if (currentSSE < bestSSE)
                {
                    bestSSE = currentSSE;
                    bestParams = (double[])p.Clone();
                }
                
                if (iter % 100 == 0)
                    result.ConvergenceHistory.Add(bestSSE);
                
                result.Iterations = iter + 1;
            }
            
            result.Parameters = bestParams;
            result.ObjectiveValue = bestSSE;
            result.FunctionEvaluations = evaluations;
            result.Success = !double.IsNaN(bestSSE) && !double.IsInfinity(bestSSE);
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
