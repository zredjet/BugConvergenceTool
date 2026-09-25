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
    /// <param name="type">アルゴリズム</param>
    /// <param name="seed">乱数シード（null なら毎回異なる。乱数を使わない手法では無視）</param>
    public static IOptimizer Create(OptimizerType type, int? seed = null)
    {
        var config = ConfigurationService.Current.Optimizers;
        
        return type switch
        {
            OptimizerType.GridSearchGradient => CreateGridSearchGradient(config.GridSearchGradient),
            OptimizerType.PSO => CreatePSO(config.PSO, seed),
            OptimizerType.DifferentialEvolution => CreateDE(config.DE, seed),
            OptimizerType.GWO => CreateGWO(config.GWO, seed),
            OptimizerType.NelderMead => CreateNelderMead(config.NelderMead),
            OptimizerType.CMAES => CreateCMAES(config.CMAES, seed),
            _ => CreateDE(config.DE, seed) // デフォルトはDE
        };
    }
    
    /// <summary>
    /// 全オプティマイザを取得（設定を使用）
    /// </summary>
    /// <param name="seed">乱数シード（手法ごとに別の系列に写す）</param>
    public static IEnumerable<IOptimizer> GetAllOptimizers(int? seed = null)
    {
        var config = ConfigurationService.Current.Optimizers;
        
        yield return CreateGridSearchGradient(config.GridSearchGradient);
        yield return CreatePSO(config.PSO, DeriveSeed(seed, 1));
        yield return CreateDE(config.DE, DeriveSeed(seed, 2));
        yield return CreateGWO(config.GWO, DeriveSeed(seed, 3));
        yield return CreateNelderMead(config.NelderMead);
        yield return CreateCMAES(config.CMAES, DeriveSeed(seed, 4));
    }
    
    /// <summary>
    /// メタヒューリスティックオプティマイザのみ取得（設定を使用）
    /// </summary>
    public static IEnumerable<IOptimizer> GetMetaheuristicOptimizers(int? seed = null)
    {
        var config = ConfigurationService.Current.Optimizers;
        
        yield return CreatePSO(config.PSO, DeriveSeed(seed, 1));
        yield return CreateDE(config.DE, DeriveSeed(seed, 2));
        yield return CreateGWO(config.GWO, DeriveSeed(seed, 3));
        yield return CreateCMAES(config.CMAES, DeriveSeed(seed, 4));
    }
    
    /// <summary>
    /// 基準シードから、用途ごとに別の系列のシードを決定的に作る（基準が null なら null）
    /// </summary>
    public static int? DeriveSeed(int? seed, int stream)
    {
        if (!seed.HasValue) return null;
        unchecked
        {
            // SplitMix 風の混合（近いシード同士でも系列が似ないようにする）
            ulong z = (ulong)(uint)seed.Value * 0x9E3779B97F4A7C15UL + (ulong)(uint)stream * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            z ^= z >> 31;
            return (int)(z & 0x7FFFFFFF);
        }
    }
    
    #region ファクトリメソッド
    
    private static DEOptimizer CreateDE(DESettings settings, int? seed)
    {
        return new DEOptimizer(
            populationSize: settings.PopulationSize,
            maxIterations: settings.MaxIterations,
            F: settings.F,
            CR: settings.CR,
            tolerance: settings.Tolerance,
            seed: seed
        );
    }
    
    private static PSOOptimizer CreatePSO(PSOSettings settings, int? seed)
    {
        return new PSOOptimizer(
            swarmSize: settings.SwarmSize,
            maxIterations: settings.MaxIterations,
            w: settings.W,
            c1: settings.C1,
            c2: settings.C2,
            tolerance: settings.Tolerance,
            seed: seed
        );
    }
    
    private static CMAESOptimizer CreateCMAES(CMAESSettings settings, int? seed)
    {
        return new CMAESOptimizer(
            maxIterations: settings.MaxIterations,
            tolerance: settings.Tolerance,
            initialSigmaU: settings.InitialSigmaU,
            seed: seed
        );
    }
    
    private static GWOOptimizer CreateGWO(GWOSettings settings, int? seed)
    {
        return new GWOOptimizer(
            packSize: settings.PackSize,
            maxIterations: settings.MaxIterations,
            tolerance: settings.Tolerance,
            seed: seed
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
        bool verbose = false,
        int? seed = null)
    {
        var results = new List<OptimizationResult>();
        
        foreach (var optimizer in GetAllOptimizers(seed))
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
    /// マルチスタート最適化（Latin Hypercube Sampling による複数の開始点から並列に探索し最良解を返す）
    /// </summary>
    /// <param name="objectiveFunction">目的関数（スレッドセーフであること）</param>
    /// <param name="lowerBounds">下限</param>
    /// <param name="upperBounds">上限</param>
    /// <param name="initialGuess">初期推定値（開始点の1つに含める）</param>
    /// <param name="optimizerFactory">
    /// 最適化器の生成関数（引数は開始点の番号）。最適化器は乱数などの内部状態を持つため、開始点ごとに新しいインスタンスを使う。
    /// 再現性が必要なら、番号から開始点ごとに別のシードを作って渡すこと
    /// </param>
    /// <param name="numStarts">開始点の数</param>
    /// <param name="verbose">詳細出力</param>
    /// <param name="seed">開始点の生成に使う乱数シード（null なら毎回異なる）</param>
    /// <remarks>
    /// 戻り値の <see cref="OptimizationResult.StartsConvergedToBest"/> は最良解と同じ目的関数値
    /// （相対差 1e-6 以内）に到達した開始点の数で、少なければ解が初期点に依存している可能性がある。
    /// </remarks>
    public static OptimizationResult MultiStartOptimize(
        Func<double[], double> objectiveFunction,
        double[] lowerBounds,
        double[] upperBounds,
        double[]? initialGuess = null,
        Func<int, IOptimizer>? optimizerFactory = null,
        int numStarts = 5,
        bool verbose = false,
        int? seed = null)
    {
        // 開始点ごとに別のシード（同じシードだと DE の初期集団が初期点以外すべて同じになり、開始点を変える意味がない）
        optimizerFactory ??= start => Create(OptimizerType.DifferentialEvolution, DeriveSeed(seed, start));
        string optimizerName = optimizerFactory(0).Name;
        
        var startPoints = GenerateStartPoints(lowerBounds, upperBounds, initialGuess, numStarts, DeriveSeed(seed, -1));
        var results = new OptimizationResult?[startPoints.Count];
        
        if (verbose)
        {
            Console.WriteLine($"  マルチスタート最適化: {startPoints.Count}点から{optimizerName}で探索...");
        }
        
        Parallel.For(0, startPoints.Count, idx =>
        {
            try
            {
                var result = optimizerFactory(idx).Optimize(objectiveFunction, lowerBounds, upperBounds, startPoints[idx]);
                if (result.Success && double.IsFinite(result.ObjectiveValue))
                    results[idx] = result;
            }
            catch
            {
                // 個別の最適化失敗は無視
            }
        });
        
        var succeeded = results.Where(r => r != null).Select(r => r!).ToList();
        if (succeeded.Count == 0)
        {
            return new OptimizationResult
            {
                Success = false,
                ErrorMessage = "全ての開始点で最適化に失敗しました",
                AlgorithmName = $"MultiStart({optimizerName})",
                StartsAttempted = startPoints.Count
            };
        }
        
        var bestResult = succeeded.OrderBy(r => r.ObjectiveValue).First();
        double tolerance = 1e-6 * Math.Max(1.0, Math.Abs(bestResult.ObjectiveValue));
        bestResult.AlgorithmName = $"MultiStart({optimizerName})";
        bestResult.StartsAttempted = startPoints.Count;
        bestResult.StartsSucceeded = succeeded.Count;
        bestResult.StartsConvergedToBest = succeeded.Count(r => r.ObjectiveValue - bestResult.ObjectiveValue <= tolerance);
        bestResult.FunctionEvaluations = succeeded.Sum(r => r.FunctionEvaluations);
        
        if (verbose)
        {
            Console.WriteLine($"    最良値={bestResult.ObjectiveValue:E4}, 同じ解に収束: {bestResult.StartsConvergedToBest}/{bestResult.StartsSucceeded}");
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
        int numStarts,
        int? seed)
    {
        var points = new List<double[]>();
        int dim = lowerBounds.Length;
        var random = seed.HasValue ? new Random(seed.Value) : new Random();
        
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
