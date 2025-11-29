using System.Diagnostics;

namespace BugConvergenceTool.Optimizers;

/// <summary>
/// Nelder-Mead法（単体法 / Downhill Simplex法）
/// 勾配不要の直接探索法で、低次元問題に効果的
/// </summary>
public class NelderMeadOptimizer : IOptimizer
{
    public string Name => "NelderMead";
    public string Description => "Nelder-Mead法 - 勾配不要・低次元に強い局所最適化";
    
    private readonly int _maxIterations;
    private readonly double _tolerance;
    private readonly double _alpha;   // 反射係数
    private readonly double _gamma;   // 拡大係数
    private readonly double _rho;     // 収縮係数
    private readonly double _sigma;   // 縮小係数
    
    /// <summary>
    /// コンストラクタ
    /// </summary>
    /// <param name="maxIterations">最大反復回数（デフォルト: 1000）</param>
    /// <param name="tolerance">収束判定閾値（デフォルト: 1e-10）</param>
    /// <param name="alpha">反射係数（デフォルト: 1.0）</param>
    /// <param name="gamma">拡大係数（デフォルト: 2.0）</param>
    /// <param name="rho">収縮係数（デフォルト: 0.5）</param>
    /// <param name="sigma">縮小係数（デフォルト: 0.5）</param>
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
            
            // 単体（simplex）の初期化: n+1 個の頂点
            var simplex = new double[dim + 1][];
            var values = new double[dim + 1];
            
            // 初期推定値を設定（なければ中央値）
            double[] x0 = new double[dim];
            if (initialGuess != null)
            {
                for (int i = 0; i < dim; i++)
                {
                    x0[i] = Clip(initialGuess[i], lowerBounds[i], upperBounds[i]);
                }
            }
            else
            {
                for (int i = 0; i < dim; i++)
                {
                    x0[i] = (lowerBounds[i] + upperBounds[i]) / 2.0;
                }
            }
            
            // 最初の頂点は初期推定値
            simplex[0] = (double[])x0.Clone();
            values[0] = SafeEvaluate(objectiveFunction, simplex[0]);
            evaluations++;
            
            // 残りの頂点を初期推定値の周りに配置
            for (int i = 0; i < dim; i++)
            {
                simplex[i + 1] = (double[])x0.Clone();
                
                // 各次元方向に少しずらす（範囲の5%程度）
                double step = (upperBounds[i] - lowerBounds[i]) * 0.05;
                if (step < 1e-8) step = 1e-8;
                
                // 上限に近い場合は下方向、それ以外は上方向
                if (x0[i] + step > upperBounds[i])
                {
                    simplex[i + 1][i] = x0[i] - step;
                }
                else
                {
                    simplex[i + 1][i] = x0[i] + step;
                }
                
                simplex[i + 1][i] = Clip(simplex[i + 1][i], lowerBounds[i], upperBounds[i]);
                values[i + 1] = SafeEvaluate(objectiveFunction, simplex[i + 1]);
                evaluations++;
            }
            
            // 初期の最良値を記録
            int bestIdx = Array.IndexOf(values, values.Min());
            result.ConvergenceHistory.Add(values[bestIdx]);
            
            // メインループ
            int stagnationCount = 0;
            double previousBest = values[bestIdx];
            
            for (int iter = 0; iter < _maxIterations; iter++)
            {
                // 頂点をソート（最良、2番目に悪い、最悪の順序を取得）
                var indices = Enumerable.Range(0, dim + 1)
                    .OrderBy(i => values[i])
                    .ToArray();
                
                int best = indices[0];
                int worst = indices[dim];
                int secondWorst = indices[dim - 1];
                
                double bestValue = values[best];
                double worstValue = values[worst];
                double secondWorstValue = values[secondWorst];
                
                // 収束判定：単体のサイズが十分小さいか
                double maxDiff = 0;
                for (int i = 1; i <= dim; i++)
                {
                    double diff = Math.Abs(values[indices[i]] - bestValue);
                    if (diff > maxDiff) maxDiff = diff;
                }
                
                if (maxDiff < _tolerance)
                {
                    break;
                }
                
                // 重心を計算（最悪点を除く）
                double[] centroid = new double[dim];
                for (int i = 0; i < dim; i++)
                {
                    double sum = 0;
                    for (int j = 0; j <= dim; j++)
                    {
                        if (j != worst)
                            sum += simplex[j][i];
                    }
                    centroid[i] = sum / dim;
                }
                
                // 1. 反射（Reflection）
                double[] reflected = new double[dim];
                for (int i = 0; i < dim; i++)
                {
                    reflected[i] = Clip(
                        centroid[i] + _alpha * (centroid[i] - simplex[worst][i]),
                        lowerBounds[i], upperBounds[i]);
                }
                double reflectedValue = SafeEvaluate(objectiveFunction, reflected);
                evaluations++;
                
                if (reflectedValue >= bestValue && reflectedValue < secondWorstValue)
                {
                    // 反射点を採用
                    simplex[worst] = reflected;
                    values[worst] = reflectedValue;
                }
                else if (reflectedValue < bestValue)
                {
                    // 2. 拡大（Expansion）
                    double[] expanded = new double[dim];
                    for (int i = 0; i < dim; i++)
                    {
                        expanded[i] = Clip(
                            centroid[i] + _gamma * (reflected[i] - centroid[i]),
                            lowerBounds[i], upperBounds[i]);
                    }
                    double expandedValue = SafeEvaluate(objectiveFunction, expanded);
                    evaluations++;
                    
                    if (expandedValue < reflectedValue)
                    {
                        simplex[worst] = expanded;
                        values[worst] = expandedValue;
                    }
                    else
                    {
                        simplex[worst] = reflected;
                        values[worst] = reflectedValue;
                    }
                }
                else
                {
                    // 3. 収縮（Contraction）
                    double[] contracted;
                    double contractedValue;
                    
                    if (reflectedValue < worstValue)
                    {
                        // Outside contraction
                        contracted = new double[dim];
                        for (int i = 0; i < dim; i++)
                        {
                            contracted[i] = Clip(
                                centroid[i] + _rho * (reflected[i] - centroid[i]),
                                lowerBounds[i], upperBounds[i]);
                        }
                        contractedValue = SafeEvaluate(objectiveFunction, contracted);
                        evaluations++;
                        
                        if (contractedValue <= reflectedValue)
                        {
                            simplex[worst] = contracted;
                            values[worst] = contractedValue;
                        }
                        else
                        {
                            // 4. 縮小（Shrink）
                            Shrink(simplex, values, best, dim, lowerBounds, upperBounds,
                                objectiveFunction, ref evaluations);
                        }
                    }
                    else
                    {
                        // Inside contraction
                        contracted = new double[dim];
                        for (int i = 0; i < dim; i++)
                        {
                            contracted[i] = Clip(
                                centroid[i] - _rho * (centroid[i] - simplex[worst][i]),
                                lowerBounds[i], upperBounds[i]);
                        }
                        contractedValue = SafeEvaluate(objectiveFunction, contracted);
                        evaluations++;
                        
                        if (contractedValue < worstValue)
                        {
                            simplex[worst] = contracted;
                            values[worst] = contractedValue;
                        }
                        else
                        {
                            // 4. 縮小（Shrink）
                            Shrink(simplex, values, best, dim, lowerBounds, upperBounds,
                                objectiveFunction, ref evaluations);
                        }
                    }
                }
                
                // 現在の最良値を取得
                bestIdx = 0;
                double currentBest = values[0];
                for (int i = 1; i <= dim; i++)
                {
                    if (values[i] < currentBest)
                    {
                        currentBest = values[i];
                        bestIdx = i;
                    }
                }
                
                result.ConvergenceHistory.Add(currentBest);
                
                // 停滞判定
                if (Math.Abs(previousBest - currentBest) < _tolerance)
                {
                    stagnationCount++;
                    if (stagnationCount > 100)
                        break;
                }
                else
                {
                    stagnationCount = 0;
                }
                previousBest = currentBest;
                
                result.Iterations = iter + 1;
            }
            
            // 最良の頂点を結果として返す
            bestIdx = 0;
            double bestValue2 = values[0];
            for (int i = 1; i <= dim; i++)
            {
                if (values[i] < bestValue2)
                {
                    bestValue2 = values[i];
                    bestIdx = i;
                }
            }
            
            result.Parameters = (double[])simplex[bestIdx].Clone();
            result.ObjectiveValue = bestValue2;
            result.FunctionEvaluations = evaluations;
            result.Success = !double.IsNaN(bestValue2) && !double.IsInfinity(bestValue2);
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
    /// 縮小操作：最良点に向かって全頂点を縮小
    /// </summary>
    private void Shrink(
        double[][] simplex, double[] values, int best, int dim,
        double[] lowerBounds, double[] upperBounds,
        Func<double[], double> objectiveFunction, ref int evaluations)
    {
        for (int i = 0; i <= dim; i++)
        {
            if (i != best)
            {
                for (int j = 0; j < dim; j++)
                {
                    simplex[i][j] = Clip(
                        simplex[best][j] + _sigma * (simplex[i][j] - simplex[best][j]),
                        lowerBounds[j], upperBounds[j]);
                }
                values[i] = SafeEvaluate(objectiveFunction, simplex[i]);
                evaluations++;
            }
        }
    }
    
    /// <summary>
    /// 値を境界内にクリップ
    /// </summary>
    private static double Clip(double value, double lower, double upper)
    {
        return Math.Max(lower, Math.Min(upper, value));
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
