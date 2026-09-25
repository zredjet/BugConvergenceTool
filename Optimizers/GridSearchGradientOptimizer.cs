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
    
    /// <summary>
    /// コンストラクタ
    /// </summary>
    /// <param name="gridSize">グリッドサイズ（0 なら次元数に応じて自動）</param>
    /// <param name="maxIterations">勾配降下の最大反復回数</param>
    /// <param name="learningRate">未使用（旧実装の固定学習率。設定ファイルとの互換性のため受け取る）</param>
    /// <param name="delta">未使用（旧実装の固定差分幅。設定ファイルとの互換性のため受け取る）</param>
    public GridSearchGradientOptimizer(
        int gridSize = 0,
        int maxIterations = 2000,
        double learningRate = 0.00005,
        double delta = 0.0001)
    {
        _gridSize = gridSize;
        _maxIterations = maxIterations;
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
            // 各パラメータを探索範囲で [0,1] に正規化した空間で、中心差分の勾配と Armijo 条件の
            // バックトラッキングで歩幅を決める射影勾配法を行う。
            // （以前は学習率 5e-5・差分幅 1e-4 を全パラメータ共通の絶対値で使っていたため、
            //   a ≈ 100 と b ≈ 0.05 のようにスケールの違うパラメータがほとんど動かず、グリッドの点のままだった）
            var range = new double[dim];
            for (int i = 0; i < dim; i++) range[i] = upperBounds[i] - lowerBounds[i];
            
            double[] ToParams(double[] u)
            {
                var x = new double[dim];
                for (int i = 0; i < dim; i++) x[i] = lowerBounds[i] + range[i] * Math.Clamp(u[i], 0, 1);
                return x;
            }
            
            var uCurrent = new double[dim];
            for (int i = 0; i < dim; i++) uCurrent[i] = range[i] > 0 ? (bestParams[i] - lowerBounds[i]) / range[i] : 0;
            double fCurrent = bestSSE;
            double stepSize = 0.1;
            const double h = 1e-6;
            
            for (int iter = 0; iter < _maxIterations; iter++)
            {
                var gradient = new double[dim];
                for (int i = 0; i < dim; i++)
                {
                    var uPlus = (double[])uCurrent.Clone();
                    var uMinus = (double[])uCurrent.Clone();
                    uPlus[i] = Math.Min(1, uCurrent[i] + h);
                    uMinus[i] = Math.Max(0, uCurrent[i] - h);
                    double width = uPlus[i] - uMinus[i];
                    double fPlus = SafeEvaluate(objectiveFunction, ToParams(uPlus));
                    double fMinus = SafeEvaluate(objectiveFunction, ToParams(uMinus));
                    evaluations += 2;
                    gradient[i] = width > 0 && OptimizationResult.IsValidObjective(fPlus) && OptimizationResult.IsValidObjective(fMinus)
                        ? (fPlus - fMinus) / width
                        : 0;
                }
                
                double gradientNorm = Math.Sqrt(gradient.Sum(g => g * g));
                if (gradientNorm < 1e-6 * (1 + Math.Abs(fCurrent)))
                {
                    result.Converged = true;
                    break;
                }
                
                // Armijo 条件を満たすまで歩幅を半分にする（射影付き）
                bool accepted = false;
                for (int trial = 0; trial < 40; trial++)
                {
                    var uNew = new double[dim];
                    double decrease = 0;
                    for (int i = 0; i < dim; i++)
                    {
                        uNew[i] = Math.Clamp(uCurrent[i] - stepSize * gradient[i] / gradientNorm, 0, 1);
                        decrease += gradient[i] * (uCurrent[i] - uNew[i]);
                    }
                    double fNew = SafeEvaluate(objectiveFunction, ToParams(uNew));
                    evaluations++;
                    if (OptimizationResult.IsValidObjective(fNew) && fNew <= fCurrent - 1e-4 * decrease && fNew < fCurrent)
                    {
                        uCurrent = uNew;
                        fCurrent = fNew;
                        stepSize = Math.Min(0.5, stepSize * 2);
                        accepted = true;
                        break;
                    }
                    stepSize /= 2;
                }
                
                result.ConvergenceHistory.Add(fCurrent);
                result.Iterations = iter + 1;
                
                if (!accepted || stepSize < 1e-12)
                {
                    // これ以上下がる方向がない（射影勾配の意味で停留点）
                    result.Converged = true;
                    break;
                }
            }
            
            if (fCurrent < bestSSE)
            {
                bestSSE = fCurrent;
                bestParams = ToParams(uCurrent);
            }
            
            result.Parameters = bestParams;
            result.ObjectiveValue = bestSSE;
            result.FunctionEvaluations = evaluations;
            result.Success = OptimizationResult.IsValidObjective(bestSSE);
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
