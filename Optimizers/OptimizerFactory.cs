using BugConvergenceTool.Models;
using BugConvergenceTool.Services;

namespace BugConvergenceTool.Optimizers;

/// <summary>
/// オプティマイザのファクトリクラス
/// </summary>
public static class OptimizerFactory
{
    /// <summary>
    /// 指定タイプのオプティマイザを作成（設定を使用）
    /// </summary>
    public static IOptimizer Create(OptimizerType type)
    {
        var config = ConfigurationService.Current.Optimizers;
        
        return type switch
        {
            OptimizerType.GridSearchGradient => CreateGridSearchGradient(config.GridSearchGradient),
            OptimizerType.PSO => CreatePSO(config.PSO),
            OptimizerType.DifferentialEvolution => CreateDE(config.DE),
            OptimizerType.GWO => CreateGWO(config.GWO),
            OptimizerType.NelderMead => CreateNelderMead(config.NelderMead),
            OptimizerType.CMAES => CreateCMAES(config.CMAES),
            _ => CreateDE(config.DE) // デフォルトはDE
        };
    }
    
    /// <summary>
    /// 全オプティマイザを取得（設定を使用）
    /// </summary>
    public static IEnumerable<IOptimizer> GetAllOptimizers()
    {
        var config = ConfigurationService.Current.Optimizers;
        
        yield return CreateGridSearchGradient(config.GridSearchGradient);
        yield return CreatePSO(config.PSO);
        yield return CreateDE(config.DE);
        yield return CreateGWO(config.GWO);
        yield return CreateNelderMead(config.NelderMead);
        yield return CreateCMAES(config.CMAES);
    }
    
    /// <summary>
    /// メタヒューリスティックオプティマイザのみ取得（設定を使用）
    /// </summary>
    public static IEnumerable<IOptimizer> GetMetaheuristicOptimizers()
    {
        var config = ConfigurationService.Current.Optimizers;
        
        yield return CreatePSO(config.PSO);
        yield return CreateDE(config.DE);
        yield return CreateGWO(config.GWO);
        yield return CreateCMAES(config.CMAES);
    }
    
    #region ファクトリメソッド
    
    private static DEOptimizer CreateDE(DESettings settings)
    {
        return new DEOptimizer(
            populationSize: settings.PopulationSize,
            maxIterations: settings.MaxIterations,
            F: settings.F,
            CR: settings.CR,
            tolerance: settings.Tolerance
        );
    }
    
    private static PSOOptimizer CreatePSO(PSOSettings settings)
    {
        return new PSOOptimizer(
            swarmSize: settings.SwarmSize,
            maxIterations: settings.MaxIterations,
            w: settings.W,
            c1: settings.C1,
            c2: settings.C2,
            tolerance: settings.Tolerance
        );
    }
    
    private static CMAESOptimizer CreateCMAES(CMAESSettings settings)
    {
        return new CMAESOptimizer(
            maxIterations: settings.MaxIterations,
            tolerance: settings.Tolerance,
            initialSigmaU: settings.InitialSigmaU
        );
    }
    
    private static GWOOptimizer CreateGWO(GWOSettings settings)
    {
        return new GWOOptimizer(
            packSize: settings.PackSize,
            maxIterations: settings.MaxIterations,
            tolerance: settings.Tolerance
        );
    }
    
    private static NelderMeadOptimizer CreateNelderMead(NelderMeadSettings settings)
    {
        return new NelderMeadOptimizer(
            maxIterations: settings.MaxIterations,
            tolerance: settings.Tolerance,
            alpha: settings.Alpha,
            gamma: settings.Gamma,
            rho: settings.Rho,
            sigma: settings.Sigma
        );
    }
    
    private static GridSearchGradientOptimizer CreateGridSearchGradient(GridSearchGradientSettings settings)
    {
        return new GridSearchGradientOptimizer(
            gridSize: settings.GridSize,
            maxIterations: settings.MaxIterations,
            learningRate: settings.LearningRate,
            delta: settings.Delta
        );
    }
    
    #endregion
    
    /// <summary>
    /// 自動選択：全アルゴリズムで最適化し、最良の結果を返す
    /// </summary>
    public static OptimizationResult AutoOptimize(
        Func<double[], double> objectiveFunction,
        double[] lowerBounds,
        double[] upperBounds,
        double[]? initialGuess = null,
        bool verbose = false)
    {
        var results = new List<OptimizationResult>();
        
        foreach (var optimizer in GetAllOptimizers())
        {
            var result = optimizer.Optimize(objectiveFunction, lowerBounds, upperBounds, initialGuess);
            results.Add(result);
            
            if (verbose)
            {
                Console.WriteLine($"  {optimizer.Name}: SSE={result.ObjectiveValue:F6}, " +
                    $"Time={result.ElapsedMilliseconds}ms");
            }
        }
        
        // 成功した結果の中で最良のものを選択
        var bestResult = results
            .Where(r => r.Success)
            .OrderBy(r => r.ObjectiveValue)
            .FirstOrDefault();
        
        if (bestResult == null)
        {
            return new OptimizationResult
            {
                Success = false,
                ErrorMessage = "全アルゴリズムで最適化に失敗しました",
                AlgorithmName = "AutoSelect"
            };
        }
        
        bestResult.AlgorithmName = $"AutoSelect({bestResult.AlgorithmName})";
        return bestResult;
    }
    
    /// <summary>
    /// マルチスタート最適化：複数の初期点から最適化を行い、最良の結果を返す
    /// </summary>
    /// <param name="objectiveFunction">目的関数</param>
    /// <param name="lowerBounds">パラメータ下限</param>
    /// <param name="upperBounds">パラメータ上限</param>
    /// <param name="initialGuess">初期推定値（基準点として使用）</param>
    /// <param name="optimizer">使用するオプティマイザ</param>
    /// <param name="numStarts">開始点の数（デフォルト: 5）</param>
    /// <param name="verbose">詳細出力</param>
    /// <returns>最良の最適化結果</returns>
    /// <remarks>
    /// <para>
    /// マルチスタート法は局所最適解の問題を軽減するために複数の初期点から最適化を実行します。
    /// 初期点の生成方法：
    /// 1. 指定された初期推定値（あれば）
    /// 2. ラテン超方格サンプリング（LHS）による分散配置
    /// 3. 境界付近のサンプリング（パラメータが境界に張り付くケースに対応）
    /// </para>
    /// </remarks>
    public static OptimizationResult MultiStartOptimize(
        Func<double[], double> objectiveFunction,
        double[] lowerBounds,
        double[] upperBounds,
        double[]? initialGuess = null,
        IOptimizer? optimizer = null,
        int numStarts = 5,
        bool verbose = false)
    {
        optimizer ??= Create(OptimizerType.DifferentialEvolution);
        int dim = lowerBounds.Length;
        
        // 初期点を生成
        var startPoints = GenerateStartPoints(lowerBounds, upperBounds, initialGuess, numStarts);
        
        var results = new List<OptimizationResult>();
        
        if (verbose)
        {
            Console.WriteLine($"  マルチスタート最適化: {numStarts}点から{optimizer.Name}で探索...");
        }
        
        int startIdx = 0;
        foreach (var startPoint in startPoints)
        {
            try
            {
                var result = optimizer.Optimize(objectiveFunction, lowerBounds, upperBounds, startPoint);
                
                if (result.Success)
                {
                    results.Add(result);
                    
                    if (verbose)
                    {
                        Console.WriteLine($"    開始点{startIdx + 1}: 目的関数値={result.ObjectiveValue:E4}");
                    }
                }
            }
            catch
            {
                // 個別の最適化失敗は無視
            }
            
            startIdx++;
        }
        
        if (results.Count == 0)
        {
            return new OptimizationResult
            {
                Success = false,
                ErrorMessage = "全ての開始点で最適化に失敗しました",
                AlgorithmName = $"MultiStart({optimizer.Name})"
            };
        }
        
        // 最良結果を選択
        var bestResult = results.OrderBy(r => r.ObjectiveValue).First();
        bestResult.AlgorithmName = $"MultiStart({optimizer.Name})";
        
        if (verbose)
        {
            Console.WriteLine($"    最良結果: 目的関数値={bestResult.ObjectiveValue:E4}");
        }
        
        return bestResult;
    }
    
    /// <summary>
    /// マルチスタート用の初期点を生成（LHS + 境界サンプリング）
    /// </summary>
    private static List<double[]> GenerateStartPoints(
        double[] lowerBounds,
        double[] upperBounds,
        double[]? initialGuess,
        int numStarts)
    {
        var points = new List<double[]>();
        int dim = lowerBounds.Length;
        var random = new Random();
        
        // 1. 初期推定値を追加（あれば）
        if (initialGuess != null && initialGuess.Length == dim)
        {
            points.Add((double[])initialGuess.Clone());
        }
        
        // 2. ラテン超方格サンプリング（簡易版LHS）
        int lhsCount = Math.Max(1, numStarts - points.Count - 1);
        var lhsPoints = GenerateLHSPoints(lowerBounds, upperBounds, lhsCount, random);
        points.AddRange(lhsPoints);
        
        // 3. 中央点を追加
        if (points.Count < numStarts)
        {
            var centerPoint = new double[dim];
            for (int i = 0; i < dim; i++)
            {
                centerPoint[i] = (lowerBounds[i] + upperBounds[i]) / 2.0;
            }
            points.Add(centerPoint);
        }
        
        // 4. 境界付近の点を追加（残りの枠がある場合）
        while (points.Count < numStarts)
        {
            var boundaryPoint = new double[dim];
            for (int i = 0; i < dim; i++)
            {
                double range = upperBounds[i] - lowerBounds[i];
                // 10%または90%の位置にランダムに配置
                double position = random.NextDouble() < 0.5 ? 0.1 : 0.9;
                boundaryPoint[i] = lowerBounds[i] + range * position;
            }
            points.Add(boundaryPoint);
        }
        
        return points.Take(numStarts).ToList();
    }
    
    /// <summary>
    /// ラテン超方格サンプリング（簡易版）
    /// </summary>
    private static List<double[]> GenerateLHSPoints(
        double[] lowerBounds,
        double[] upperBounds,
        int numPoints,
        Random random)
    {
        int dim = lowerBounds.Length;
        var points = new List<double[]>();
        
        // 各次元でnumPoints個の区間に分割
        var permutations = new int[dim][];
        for (int d = 0; d < dim; d++)
        {
            permutations[d] = Enumerable.Range(0, numPoints).OrderBy(_ => random.Next()).ToArray();
        }
        
        for (int i = 0; i < numPoints; i++)
        {
            var point = new double[dim];
            for (int d = 0; d < dim; d++)
            {
                double range = upperBounds[d] - lowerBounds[d];
                double intervalSize = range / numPoints;
                int interval = permutations[d][i];
                
                // 区間内でランダムに配置
                point[d] = lowerBounds[d] + intervalSize * (interval + random.NextDouble());
            }
            points.Add(point);
        }
        
        return points;
    }
    
    /// <summary>
    /// アルゴリズム比較レポートを生成
    /// </summary>
    public static OptimizerComparisonReport CompareOptimizers(
        Func<double[], double> objectiveFunction,
        double[] lowerBounds,
        double[] upperBounds,
        double[]? initialGuess = null,
        int trials = 3)
    {
        var report = new OptimizerComparisonReport();
        
        foreach (var optimizer in GetAllOptimizers())
        {
            var summary = new OptimizerSummary { Name = optimizer.Name };
            
            for (int t = 0; t < trials; t++)
            {
                var result = optimizer.Optimize(objectiveFunction, lowerBounds, upperBounds, initialGuess);
                
                if (result.Success)
                {
                    summary.SuccessCount++;
                    summary.ObjectiveValues.Add(result.ObjectiveValue);
                    summary.ElapsedTimes.Add(result.ElapsedMilliseconds);
                    summary.Evaluations.Add(result.FunctionEvaluations);
                }
            }
            
            if (summary.SuccessCount > 0)
            {
                summary.BestObjective = summary.ObjectiveValues.Min();
                summary.AvgObjective = summary.ObjectiveValues.Average();
                summary.AvgTime = summary.ElapsedTimes.Average();
                summary.AvgEvaluations = summary.Evaluations.Average();
            }
            
            report.Summaries.Add(summary);
        }
        
        // 最良アルゴリズムを特定
        report.BestAlgorithm = report.Summaries
            .Where(s => s.SuccessCount > 0)
            .OrderBy(s => s.BestObjective)
            .FirstOrDefault()?.Name ?? "N/A";
        
        return report;
    }
}

/// <summary>
/// オプティマイザ比較レポート
/// </summary>
public class OptimizerComparisonReport
{
    public List<OptimizerSummary> Summaries { get; set; } = new();
    public string BestAlgorithm { get; set; } = "";
    
    public override string ToString()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("=== オプティマイザ比較 ===");
        sb.AppendLine($"{"アルゴリズム",-15} {"成功率",-10} {"最良SSE",-15} {"平均SSE",-15} {"平均時間(ms)",-12}");
        sb.AppendLine(new string('-', 70));
        
        foreach (var s in Summaries)
        {
            string successRate = s.SuccessCount > 0 ? $"{s.SuccessCount}/3" : "0/3";
            string bestObj = s.SuccessCount > 0 ? s.BestObjective.ToString("E4") : "N/A";
            string avgObj = s.SuccessCount > 0 ? s.AvgObjective.ToString("E4") : "N/A";
            string avgTime = s.SuccessCount > 0 ? s.AvgTime.ToString("F0") : "N/A";
            
            sb.AppendLine($"{s.Name,-15} {successRate,-10} {bestObj,-15} {avgObj,-15} {avgTime,-12}");
        }
        
        sb.AppendLine();
        sb.AppendLine($"推奨アルゴリズム: {BestAlgorithm}");
        
        return sb.ToString();
    }
}

/// <summary>
/// オプティマイザサマリ
/// </summary>
public class OptimizerSummary
{
    public string Name { get; set; } = "";
    public int SuccessCount { get; set; }
    public List<double> ObjectiveValues { get; set; } = new();
    public List<long> ElapsedTimes { get; set; } = new();
    public List<int> Evaluations { get; set; } = new();
    
    public double BestObjective { get; set; }
    public double AvgObjective { get; set; }
    public double AvgTime { get; set; }
    public double AvgEvaluations { get; set; }
}
