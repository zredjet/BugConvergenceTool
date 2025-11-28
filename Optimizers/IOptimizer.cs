namespace BugConvergenceTool.Optimizers;

/// <summary>
/// 最適化アルゴリズムのインターフェース
/// </summary>
public interface IOptimizer
{
    /// <summary>
    /// アルゴリズム名
    /// </summary>
    string Name { get; }
    
    /// <summary>
    /// アルゴリズムの説明
    /// </summary>
    string Description { get; }
    
    /// <summary>
    /// 最適化を実行
    /// </summary>
    /// <param name="objectiveFunction">最小化する目的関数</param>
    /// <param name="lowerBounds">パラメータの下限</param>
    /// <param name="upperBounds">パラメータの上限</param>
    /// <param name="initialGuess">初期推定値（オプション）</param>
    /// <returns>最適化結果</returns>
    OptimizationResult Optimize(
        Func<double[], double> objectiveFunction,
        double[] lowerBounds,
        double[] upperBounds,
        double[]? initialGuess = null);
}

/// <summary>
/// 最適化結果
/// </summary>
public class OptimizationResult
{
    /// <summary>
    /// 最適パラメータ
    /// </summary>
    public double[] Parameters { get; set; } = Array.Empty<double>();
    
    /// <summary>
    /// 目的関数の最終値（SSE等）
    /// </summary>
    public double ObjectiveValue { get; set; } = double.MaxValue;
    
    /// <summary>
    /// 成功フラグ
    /// </summary>
    public bool Success { get; set; }
    
    /// <summary>
    /// エラーメッセージ
    /// </summary>
    public string? ErrorMessage { get; set; }
    
    /// <summary>
    /// 反復回数
    /// </summary>
    public int Iterations { get; set; }
    
    /// <summary>
    /// 評価回数
    /// </summary>
    public int FunctionEvaluations { get; set; }
    
    /// <summary>
    /// 収束履歴（各世代の最良値）
    /// </summary>
    public List<double> ConvergenceHistory { get; set; } = new();
    
    /// <summary>
    /// 使用したアルゴリズム名
    /// </summary>
    public string AlgorithmName { get; set; } = "";
    
    /// <summary>
    /// 計算時間（ミリ秒）
    /// </summary>
    public long ElapsedMilliseconds { get; set; }
}

/// <summary>
/// 利用可能なオプティマイザの種類
/// </summary>
public enum OptimizerType
{
    /// <summary>
    /// グリッドサーチ + 勾配降下法（既存）
    /// </summary>
    GridSearchGradient,
    
    /// <summary>
    /// 粒子群最適化
    /// </summary>
    PSO,
    
    /// <summary>
    /// 差分進化
    /// </summary>
    DifferentialEvolution,
    
    /// <summary>
    /// Grey Wolf Optimizer
    /// </summary>
    GWO,
    
    /// <summary>
    /// 全アルゴリズムで比較し最良を選択
    /// </summary>
    AutoSelect
}
