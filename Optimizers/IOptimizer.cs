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
    
    #region 収束診断情報
    
    /// <summary>
    /// 収束診断結果
    /// </summary>
    public ConvergenceDiagnostics? Diagnostics { get; set; }
    
    #endregion
}

/// <summary>
/// 収束診断情報
/// </summary>
public class ConvergenceDiagnostics
{
    /// <summary>
    /// 勾配ノルムの近似値（数値微分ベース）
    /// 小さいほど局所最適解に近い
    /// </summary>
    public double ApproximateGradientNorm { get; set; } = double.NaN;
    
    /// <summary>
    /// 最終世代でのパラメータ変動幅（最大成分）
    /// </summary>
    public double ParameterChangeNorm { get; set; } = double.NaN;
    
    /// <summary>
    /// 最終N世代での目的関数値の変動幅
    /// </summary>
    public double ObjectiveChangeRate { get; set; } = double.NaN;
    
    /// <summary>
    /// 収束品質の評価
    /// </summary>
    public ConvergenceQuality Quality { get; set; } = ConvergenceQuality.Unknown;
    
    /// <summary>
    /// 診断メッセージ
    /// </summary>
    public string Message { get; set; } = "";
    
    /// <summary>
    /// パラメータが境界値に張り付いているか
    /// </summary>
    public bool[] AtBoundary { get; set; } = Array.Empty<bool>();
    
    /// <summary>
    /// 収束診断を実行
    /// </summary>
    public static ConvergenceDiagnostics Evaluate(
        Func<double[], double> objective,
        double[] parameters,
        double[] lowerBounds,
        double[] upperBounds,
        List<double> history,
        double tolerance = 1e-6)
    {
        var diag = new ConvergenceDiagnostics();
        int dim = parameters.Length;
        
        // 1. 数値微分による勾配ノルム近似
        double gradNorm = 0;
        double h = 1e-7;
        for (int i = 0; i < dim; i++)
        {
            var pPlus = (double[])parameters.Clone();
            var pMinus = (double[])parameters.Clone();
            
            double step = Math.Max(h, Math.Abs(parameters[i]) * h);
            pPlus[i] = Math.Min(upperBounds[i], parameters[i] + step);
            pMinus[i] = Math.Max(lowerBounds[i], parameters[i] - step);
            
            double fPlus = objective(pPlus);
            double fMinus = objective(pMinus);
            double grad = (fPlus - fMinus) / (pPlus[i] - pMinus[i]);
            gradNorm += grad * grad;
        }
        diag.ApproximateGradientNorm = Math.Sqrt(gradNorm);
        
        // 2. 境界チェック
        diag.AtBoundary = new bool[dim];
        int boundaryCount = 0;
        for (int i = 0; i < dim; i++)
        {
            double range = upperBounds[i] - lowerBounds[i];
            bool atLower = (parameters[i] - lowerBounds[i]) < range * 0.001;
            bool atUpper = (upperBounds[i] - parameters[i]) < range * 0.001;
            diag.AtBoundary[i] = atLower || atUpper;
            if (diag.AtBoundary[i]) boundaryCount++;
        }
        
        // 3. 収束履歴からの変動評価
        if (history.Count >= 10)
        {
            int lastN = Math.Min(10, history.Count);
            var recent = history.Skip(history.Count - lastN).ToList();
            double maxVal = recent.Max();
            double minVal = recent.Min();
            diag.ObjectiveChangeRate = maxVal > 0 ? (maxVal - minVal) / maxVal : 0;
        }
        
        // 4. 品質評価
        bool gradOk = diag.ApproximateGradientNorm < tolerance * 1000;
        bool changeOk = diag.ObjectiveChangeRate < tolerance * 100;
        bool boundaryOk = boundaryCount == 0;
        
        if (gradOk && changeOk && boundaryOk)
        {
            diag.Quality = ConvergenceQuality.Good;
            diag.Message = "収束状態良好";
        }
        else if (gradOk && changeOk)
        {
            diag.Quality = ConvergenceQuality.Acceptable;
            diag.Message = boundaryCount > 0 
                ? $"収束済みだが{boundaryCount}個のパラメータが境界に張り付き" 
                : "収束状態は許容範囲内";
        }
        else if (!gradOk && boundaryCount > 0)
        {
            diag.Quality = ConvergenceQuality.Poor;
            diag.Message = $"パラメータが境界値で停止。探索範囲の見直しを推奨";
        }
        else
        {
            diag.Quality = ConvergenceQuality.Questionable;
            diag.Message = "収束が不完全な可能性。反復回数増加または別アルゴリズムを検討";
        }
        
        return diag;
    }
}

/// <summary>
/// 収束品質の評価
/// </summary>
public enum ConvergenceQuality
{
    /// <summary>不明</summary>
    Unknown,
    /// <summary>良好（勾配小、変動小、境界から離れている）</summary>
    Good,
    /// <summary>許容可能（収束しているが境界付近）</summary>
    Acceptable,
    /// <summary>疑問あり（収束不完全の可能性）</summary>
    Questionable,
    /// <summary>不良（明確な問題あり）</summary>
    Poor
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
    /// Nelder-Mead法（単体法）
    /// </summary>
    NelderMead,
    
    /// <summary>
    /// CMA-ES（共分散行列適応進化戦略）
    /// </summary>
    CMAES,
    
    /// <summary>
    /// 全アルゴリズムで比較し最良を選択
    /// </summary>
    AutoSelect
}
