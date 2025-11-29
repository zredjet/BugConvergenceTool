namespace BugConvergenceTool.Optimizers;

/// <summary>
/// オプティマイザのファクトリクラス
/// </summary>
public static class OptimizerFactory
{
    /// <summary>
    /// 指定タイプのオプティマイザを作成
    /// </summary>
    public static IOptimizer Create(OptimizerType type)
    {
        return type switch
        {
            OptimizerType.GridSearchGradient => new GridSearchGradientOptimizer(),
            OptimizerType.PSO => new PSOOptimizer(),
            OptimizerType.DifferentialEvolution => new DEOptimizer(),
            OptimizerType.GWO => new GWOOptimizer(),
            OptimizerType.NelderMead => new NelderMeadOptimizer(),
            OptimizerType.CMAES => new CMAESOptimizer(),
            _ => new DEOptimizer() // デフォルトはDE
        };
    }
    
    /// <summary>
    /// 全オプティマイザを取得
    /// </summary>
    public static IEnumerable<IOptimizer> GetAllOptimizers()
    {
        yield return new GridSearchGradientOptimizer();
        yield return new PSOOptimizer();
        yield return new DEOptimizer();
        yield return new GWOOptimizer();
        yield return new NelderMeadOptimizer();
        yield return new CMAESOptimizer();
    }
    
    /// <summary>
    /// メタヒューリスティックオプティマイザのみ取得
    /// </summary>
    public static IEnumerable<IOptimizer> GetMetaheuristicOptimizers()
    {
        yield return new PSOOptimizer();
        yield return new DEOptimizer();
        yield return new GWOOptimizer();
        yield return new CMAESOptimizer();
    }
    
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
