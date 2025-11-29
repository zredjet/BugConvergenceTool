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
        LossType lossType = LossType.Sse,
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
        // 変化点固定版のモデルを返す
        return baseModel switch
        {
            ImperfectDebugExponentialChangePointModel => new FixedTauPnzChangePointModel(fixedTau),
            ExponentialChangePointModel => new FixedTauExponentialChangePointModel(fixedTau),
            DelayedSChangePointModel => new FixedTauDelayedSChangePointModel(fixedTau),
            _ => null
        };
    }
}

/// <summary>
/// τを固定したPNZ+変化点モデル
/// </summary>
internal class FixedTauPnzChangePointModel : ReliabilityGrowthModelBase
{
    private readonly int _fixedTau;
    
    public FixedTauPnzChangePointModel(int fixedTau)
    {
        _fixedTau = fixedTau;
    }
    
    public override string Name => $"PNZ型+変化点(τ={_fixedTau})";
    public override string Category => "変化点";
    public override string Formula => "m(t) = a(1-e^(-u(t)))/(1+p·e^(-u(t)))";
    public override string Description => $"変化点τ={_fixedTau}で固定したPNZ型モデル";
    public override string[] ParameterNames => new[] { "a", "b₁", "b₂", "p" };
    
    public override double GetAsymptoticTotalBugs(double[] parameters)
    {
        double a = parameters[0];
        double p = parameters[3];
        // p >= 1 の場合の発散を防ぐ
        if (p >= 1.0) return a * 100;
        return a / (1.0 + p);
    }
    
    public override double Calculate(double t, double[] p)
    {
        double a = p[0], b1 = p[1], b2 = p[2], pParam = p[3];
        double tau = _fixedTau;
        
        // 実効時間 u(t) の計算
        double u;
        if (t <= tau)
        {
            u = b1 * t;
        }
        else
        {
            u = b1 * tau + b2 * (t - tau);
        }
        
        double expU = Math.Exp(-u);
        double numerator = a * (1 - expU);
        double denominator = 1 + pParam * expU;
        
        if (denominator <= 0) return a;
        return numerator / denominator;
    }
    
    public override double[] GetInitialParameters(double[] tData, double[] yData)
    {
        double maxY = yData.Max();
        double avgSlope = EstimateAverageSlope(yData);
        double b0 = GetBValueExponential(avgSlope);
        double p0 = GetImperfectDebugP0();
        double a0 = maxY * GetScaleFactorAInRange(false, 0.5);
        
        return new[] { a0, b0, b0, p0 };
    }
    
    public override (double[] lower, double[] upper) GetBounds(double[] tData, double[] yData)
    {
        double maxY = yData.Max();
        
        return (
            new[] { maxY * 0.5, 1e-8, 1e-8, -0.5 },
            new[] { maxY * 100, 10.0, 10.0, 5.0 }
        );
    }
}

/// <summary>
/// τを固定した指数型+変化点モデル
/// </summary>
internal class FixedTauExponentialChangePointModel : ReliabilityGrowthModelBase
{
    private readonly int _fixedTau;
    
    public FixedTauExponentialChangePointModel(int fixedTau)
    {
        _fixedTau = fixedTau;
    }
    
    public override string Name => $"指数型+変化点(τ={_fixedTau})";
    public override string Category => "変化点";
    public override string Formula => "m(t) = a₁(1-e^(-b₁t)) [t≤τ], m₁(τ)+a₂(1-e^(-b₂(t-τ))) [t>τ]";
    public override string Description => $"変化点τ={_fixedTau}で固定した指数型モデル";
    public override string[] ParameterNames => new[] { "a₁", "b₁", "a₂", "b₂" };
    
    public override double GetAsymptoticTotalBugs(double[] parameters)
    {
        return parameters[0] + parameters[2]; // a₁ + a₂
    }
    
    public override double Calculate(double t, double[] p)
    {
        double a1 = p[0], b1 = p[1], a2 = p[2], b2 = p[3];
        double tau = _fixedTau;
        
        if (t <= tau)
        {
            return a1 * (1 - Math.Exp(-b1 * t));
        }
        else
        {
            double m_tau = a1 * (1 - Math.Exp(-b1 * tau));
            return m_tau + a2 * (1 - Math.Exp(-b2 * (t - tau)));
        }
    }
    
    public override double[] GetInitialParameters(double[] tData, double[] yData)
    {
        double maxY = yData.Max();
        double avgSlope = EstimateAverageSlope(yData);
        double b0 = GetBValueExponential(avgSlope);
        
        return new[] { maxY * 0.6, b0, maxY * 0.6, b0 };
    }
    
    public override (double[] lower, double[] upper) GetBounds(double[] tData, double[] yData)
    {
        double maxY = yData.Max();
        
        return (
            new[] { 1.0, 0.001, 1.0, 0.001 },
            new[] { maxY * 3, 1.0, maxY * 3, 1.0 }
        );
    }
}

/// <summary>
/// τを固定した遅延S字型+変化点モデル
/// </summary>
internal class FixedTauDelayedSChangePointModel : ReliabilityGrowthModelBase
{
    private readonly int _fixedTau;
    
    public FixedTauDelayedSChangePointModel(int fixedTau)
    {
        _fixedTau = fixedTau;
    }
    
    public override string Name => $"遅延S字型+変化点(τ={_fixedTau})";
    public override string Category => "変化点";
    public override string Formula => "m(t) = a₁(1-(1+b₁t)e^(-b₁t)) [t≤τ], m₁(τ)+a₂(...) [t>τ]";
    public override string Description => $"変化点τ={_fixedTau}で固定した遅延S字型モデル";
    public override string[] ParameterNames => new[] { "a₁", "b₁", "a₂", "b₂" };
    
    public override double GetAsymptoticTotalBugs(double[] parameters)
    {
        return parameters[0] + parameters[2]; // a₁ + a₂
    }
    
    public override double Calculate(double t, double[] p)
    {
        double a1 = p[0], b1 = p[1], a2 = p[2], b2 = p[3];
        double tau = _fixedTau;
        
        if (t <= tau)
        {
            return a1 * (1 - (1 + b1 * t) * Math.Exp(-b1 * t));
        }
        else
        {
            double m_tau = a1 * (1 - (1 + b1 * tau) * Math.Exp(-b1 * tau));
            double dt = t - tau;
            return m_tau + a2 * (1 - (1 + b2 * dt) * Math.Exp(-b2 * dt));
        }
    }
    
    public override double[] GetInitialParameters(double[] tData, double[] yData)
    {
        double maxY = yData.Max();
        double avgSlope = EstimateAverageSlope(yData);
        double b0 = GetBValueSCurve(avgSlope);
        
        return new[] { maxY * 0.6, b0, maxY * 0.6, b0 };
    }
    
    public override (double[] lower, double[] upper) GetBounds(double[] tData, double[] yData)
    {
        double maxY = yData.Max();
        
        return (
            new[] { 1.0, 0.001, 1.0, 0.001 },
            new[] { maxY * 3, 1.0, maxY * 3, 1.0 }
        );
    }
}
