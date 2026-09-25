using System.Collections.Concurrent;
using BugConvergenceTool.Models;
using BugConvergenceTool.Optimizers;

namespace BugConvergenceTool.Services;

/// <summary>
/// 変化点探索の結果を格納するクラス
/// </summary>
public class ChangePointSearchResult
{
    /// <summary>
    /// 最適な変化点（日数）
    /// </summary>
    public int BestTau { get; set; }
    
    /// <summary>
    /// 最適な変化点でのAICc
    /// </summary>
    public double BestAICc { get; set; }
    
    /// <summary>
    /// 最適な変化点でのAIC
    /// </summary>
    public double BestAIC { get; set; }
    
    /// <summary>
    /// 最適な変化点でのフィッティング結果
    /// </summary>
    public FittingResult? BestFittingResult { get; set; }
    
    /// <summary>
    /// 全候補のプロファイル尤度結果（τ → AICc のマッピング）
    /// </summary>
    public Dictionary<int, double> ProfileAICc { get; set; } = new();
    
    /// <summary>
    /// 探索成功フラグ
    /// </summary>
    public bool Success { get; set; }
    
    /// <summary>
    /// エラーメッセージ
    /// </summary>
    public string? ErrorMessage { get; set; }
    
    /// <summary>
    /// 探索に要した時間（ミリ秒）
    /// </summary>
    public long ElapsedMilliseconds { get; set; }
    
    /// <summary>
    /// 変化点の信頼性評価
    /// AICcの谷の深さに基づいて評価
    /// </summary>
    public string ChangePointReliability => EvaluateReliability();
    
    private string EvaluateReliability()
    {
        if (ProfileAICc.Count < 3) return "評価不可";
        
        var sortedAICc = ProfileAICc.Values.OrderBy(v => v).ToList();
        double minAICc = sortedAICc[0];
        double medianAICc = sortedAICc[sortedAICc.Count / 2];
        double maxAICc = sortedAICc[^1];
        
        // AICcの差（谷の深さ）で評価
        double depth = medianAICc - minAICc;
        
        return depth switch
        {
            > 10.0 => "高（明確な変化点）",
            > 4.0 => "中（変化点の可能性あり）",
            > 2.0 => "低（弱い変化点）",
            _ => "非常に低（変化点なしの可能性）"
        };
    }
}

/// <summary>
/// プロファイル尤度法による堅牢な変化点検出サービス
/// ヒューリスティックではなく、AICc最小化に基づいて最適な変化点を特定
/// </summary>
public class RobustChangePointDetector
{
    private readonly OptimizerType _optimizerType;
    private readonly LossType _lossType;
    private readonly bool _verbose;
    
    /// <summary>
    /// デフォルトの探索マージン（端点からの除外日数）
    /// </summary>
    public int SearchMargin { get; set; } = 3;
    
    /// <summary>
    /// 探索ステップサイズ（粗い探索の場合に使用）
    /// </summary>
    public int SearchStep { get; set; } = 1;
    
    /// <summary>
    /// 並列処理を使用するかどうか
    /// </summary>
    public bool UseParallel { get; set; } = true;
    
    public RobustChangePointDetector(
        OptimizerType optimizerType = OptimizerType.NelderMead,
        LossType lossType = LossType.Mle,
        bool verbose = false)
    {
        _optimizerType = optimizerType;
        _lossType = lossType;
        _verbose = verbose;
    }
    
    /// <summary>
    /// プロファイル尤度法により最適な変化点を探索する
    /// </summary>
    /// <param name="baseModel">変化点モデルの種類を決定するベースモデル</param>
    /// <param name="tData">時間データ</param>
    /// <param name="yData">累積バグ数データ</param>
    /// <param name="yFixedData">累積修正数データ（FREモデル用、オプション）</param>
    /// <returns>変化点探索結果</returns>
    public ChangePointSearchResult FindOptimalChangePoint(
        ChangePointModelBase baseModel,
        double[] tData,
        double[] yData,
        double[]? yFixedData = null)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var result = new ChangePointSearchResult();
        
        int n = tData.Length;
        
        // 探索範囲のバリデーション
        if (n < SearchMargin * 2 + 1)
        {
            result.Success = false;
            result.ErrorMessage = $"データ点数が不足しています（n={n}、最低{SearchMargin * 2 + 1}点必要）";
            return result;
        }
        
        int tauMin = SearchMargin;
        int tauMax = n - SearchMargin;
        
        if (_verbose)
        {
            Console.WriteLine($"  [変化点探索] τ ∈ [{tauMin}, {tauMax}] を探索中...");
        }
        
        // 候補となるτの値を生成
        var tauCandidates = Enumerable.Range(tauMin, tauMax - tauMin + 1)
            .Where(tau => (tau - tauMin) % SearchStep == 0)
            .ToList();
        
        var profileResults = new ConcurrentDictionary<int, (double AICc, FittingResult? Result)>();
        
        // 損失関数の取得
        var lossFunction = LossFunctionFactory.Create(_lossType);
        
        if (UseParallel)
        {
            // 並列処理で全候補を計算
            Parallel.ForEach(tauCandidates, tau =>
            {
                var fitResult = FitWithFixedTau(baseModel, tData, yData, tau, lossFunction, yFixedData);
                if (fitResult != null && fitResult.Success)
                {
                    profileResults[tau] = (fitResult.AICc, fitResult);
                }
            });
        }
        else
        {
            // 逐次処理
            foreach (var tau in tauCandidates)
            {
                var fitResult = FitWithFixedTau(baseModel, tData, yData, tau, lossFunction, yFixedData);
                if (fitResult != null && fitResult.Success)
                {
                    profileResults[tau] = (fitResult.AICc, fitResult);
                    
                    if (_verbose)
                    {
                        Console.WriteLine($"    τ={tau}: AICc={fitResult.AICc:F2}");
                    }
                }
            }
        }
        
        if (profileResults.IsEmpty)
        {
            result.Success = false;
            result.ErrorMessage = "すべての変化点候補でフィッティングに失敗しました";
            stopwatch.Stop();
            result.ElapsedMilliseconds = stopwatch.ElapsedMilliseconds;
            return result;
        }
        
        // プロファイルAICcを結果に格納
        foreach (var kvp in profileResults)
        {
            result.ProfileAICc[kvp.Key] = kvp.Value.AICc;
        }
        
        // 最良の結果を選択
        var best = profileResults.OrderBy(x => x.Value.AICc).First();
        result.BestTau = best.Key;
        result.BestAICc = best.Value.AICc;
        result.BestFittingResult = best.Value.Result;
        result.BestAIC = best.Value.Result?.AIC ?? double.MaxValue;
        result.Success = true;
        
        stopwatch.Stop();
        result.ElapsedMilliseconds = stopwatch.ElapsedMilliseconds;
        
        if (_verbose)
        {
            Console.WriteLine($"  [変化点探索] 最適 τ={result.BestTau} (AICc={result.BestAICc:F2})");
            Console.WriteLine($"  [変化点探索] 信頼性: {result.ChangePointReliability}");
            Console.WriteLine($"  [変化点探索] 探索時間: {result.ElapsedMilliseconds}ms");
        }
        
        return result;
    }
    
    /// <summary>
    /// 固定されたτでモデルをフィッティング
    /// </summary>
    private FittingResult? FitWithFixedTau(
        ChangePointModelBase baseModel,
        double[] tData,
        double[] yData,
        int fixedTau,
        ILossFunction lossFunction,
        double[]? yFixedData)
    {
        try
        {
            // 変化点を固定したモデルを作成
            var fixedModel = CreateFixedTauModel(baseModel, fixedTau);
            if (fixedModel == null) return null;
            
            // パラメータの境界と初期値を取得
            var (lower, upper) = fixedModel.GetBounds(tData, yData);
            var initial = fixedModel.GetInitialParameters(tData, yData);
            
            // 目的関数
            Func<double[], double> objective = p => lossFunction.Evaluate(tData, yData, fixedModel, p, yFixedData);
            
            // 最適化を実行
            var optimizer = OptimizerFactory.Create(_optimizerType);
            var optResult = optimizer.Optimize(objective, lower, upper, initial);
            
            if (!optResult.Success) return null;
            
            var parameters = optResult.Parameters;
            
            // フィッティング結果を構築
            var result = new FittingResult
            {
                ModelName = fixedModel.Name,
                Category = fixedModel.Category,
                Success = true,
                ParameterVector = parameters,
                EstimatedTotalBugs = fixedModel.GetAsymptoticTotalBugs(parameters),
                PredictedValues = tData.Select(t => fixedModel.Calculate(t, parameters)).ToArray(),
                PredictionTimes = (double[])tData.Clone(),
                R2 = fixedModel.CalculateR2(tData, yData, parameters),
                MSE = fixedModel.CalculateSSE(tData, yData, parameters) / tData.Length,
                LossFunctionUsed = _lossType == LossType.Mle ? "MLE" : "SSE"
            };
            
            // パラメータを辞書に格納
            for (int i = 0; i < fixedModel.ParameterNames.Length && i < parameters.Length; i++)
            {
                result.Parameters[fixedModel.ParameterNames[i]] = parameters[i];
            }
            
            // AIC と AICc を計算
            result.AIC = lossFunction.CalculateAIC(tData, yData, fixedModel, parameters, yFixedData);
            result.AICc = lossFunction.CalculateAICc(tData, yData, fixedModel, parameters, yFixedData);
            
            // モデル選択基準の決定
            int n = tData.Length;
            int k = parameters.Length;
            if (n <= k + 1)
            {
                result.ModelSelectionCriterion = "Invalid (n <= k+1)";
            }
            else if ((double)n / k < 40.0)
            {
                result.ModelSelectionCriterion = "AICc";
            }
            else
            {
                result.ModelSelectionCriterion = "AIC";
            }
            
            return result;
        }
        catch
        {
            return null;
        }
    }
    
    /// <summary>
    /// 変化点を固定したモデルを作成
    /// </summary>
    private ReliabilityGrowthModelBase? CreateFixedTauModel(ChangePointModelBase baseModel, int fixedTau)
    {
        // τ を最後のパラメータに1つだけ持つ変化点モデルのみ対応（複数変化点は対象外）
        return FixedTauChangePointModel.Supports(baseModel)
            ? new FixedTauChangePointModel(baseModel, fixedTau)
            : null;
    }
}
