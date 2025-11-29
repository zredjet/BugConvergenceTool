using BugConvergenceTool.Services;

namespace BugConvergenceTool.Models;

/// <summary>
/// 信頼度成長モデルのフィッティング結果
/// </summary>
public class FittingResult
{
    public string ModelName { get; set; } = "";
    public string Category { get; set; } = "基本";
    public Dictionary<string, double> Parameters { get; set; } = new();
    public double R2 { get; set; }
    public double MSE { get; set; }
    public double AIC { get; set; }
    
    /// <summary>
    /// AICc（小標本補正AIC）
    /// Burnham & Anderson (2002) の基準: AICc = AIC + 2k(k+1)/(n-k-1)
    /// </summary>
    public double AICc { get; set; }
    
    /// <summary>
    /// モデル選択に使用された評価基準 ("AIC", "AICc", または "Invalid")
    /// n/k < 40 の場合は AICc を使用（Burnham & Anderson の基準）
    /// </summary>
    public string ModelSelectionCriterion { get; set; } = "AIC";
    
    /// <summary>
    /// モデル選択スコア（ソート用）
    /// ModelSelectionCriterion に応じて AIC または AICc の値を返す
    /// </summary>
    public double SelectionScore => ModelSelectionCriterion == "AICc" ? AICc : AIC;
    
    public double[] PredictedValues { get; set; } = Array.Empty<double>();
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    
    // 収束予測
    public Dictionary<string, ConvergencePrediction> ConvergencePredictions { get; set; } = new();
    
    // 推定潜在バグ数（パラメータa）
    public double EstimatedTotalBugs => Parameters.GetValueOrDefault("a", 0);
    
    // 不完全デバッグ率（パラメータp）
    public double? ImperfectDebugRate => Parameters.ContainsKey("p") ? Parameters["p"] : null;
    
    // オプティマイザ情報
    public string OptimizerUsed { get; set; } = "";
    public long OptimizationTimeMs { get; set; }
    public int FunctionEvaluations { get; set; }
    
    // 信頼区間計算用：最適化時のパラメータベクトル（順序を保持）
    public double[] ParameterVector { get; set; } = Array.Empty<double>();
    
    // 信頼区間計算用：予測時刻（PredictedValues に対応する X 軸）
    public double[] PredictionTimes { get; set; } = Array.Empty<double>();
    
    // 95%信頼区間（ブートストラップ法で計算）
    public double[]? LowerConfidenceBounds { get; set; }
    public double[]? UpperConfidenceBounds { get; set; }
    
    // ホールドアウト検証結果（オプション）
    /// <summary>
    /// ホールドアウト検証の平均二乗誤差（MSE）
    /// </summary>
    public double? HoldoutMse { get; set; }
    
    /// <summary>
    /// ホールドアウト検証の平均絶対パーセント誤差（MAPE）%
    /// </summary>
    public double? HoldoutMape { get; set; }
    
    /// <summary>
    /// ホールドアウト検証の平均絶対誤差（MAE）
    /// </summary>
    public double? HoldoutMae { get; set; }
    
    /// <summary>
    /// 使用した損失関数タイプ
    /// </summary>
    public string LossFunctionUsed { get; set; } = "SSE";
    
    /// <summary>
    /// 警告メッセージのリスト
    /// </summary>
    public List<string> Warnings { get; set; } = new();
    
    /// <summary>
    /// 感度分析結果（推定総バグ数に対する感度）
    /// </summary>
    public Services.SensitivityReport? SensitivityAnalysis { get; set; }
    
    /// <summary>
    /// 変化点探索結果（変化点モデルの場合のみ）
    /// </summary>
    public Services.ChangePointSearchResult? ChangePointSearchResult { get; set; }
}

/// <summary>
/// 収束予測結果
/// </summary>
public class ConvergencePrediction
{
    public string Milestone { get; set; } = "";
    public double Ratio { get; set; }
    public double? PredictedDay { get; set; }
    public double? RemainingDays { get; set; }
    public DateTime? PredictedDate { get; set; }
    public double BugsAtPoint { get; set; }
    public bool AlreadyReached { get; set; }
}

/// <summary>
/// 信頼度成長モデルの基底クラス
/// </summary>
public abstract class ReliabilityGrowthModelBase
{
    public abstract string Name { get; }
    public abstract string Category { get; }
    public abstract string Formula { get; }
    public abstract string Description { get; }
    public abstract string[] ParameterNames { get; }
    
    /// <summary>
    /// モデル関数 m(t) を計算
    /// </summary>
    public abstract double Calculate(double t, double[] parameters);
    
    /// <summary>
    /// パラメータの初期値を取得
    /// </summary>
    public abstract double[] GetInitialParameters(double[] tData, double[] yData);
    
    /// <summary>
    /// パラメータの下限・上限を取得
    /// </summary>
    public abstract (double[] lower, double[] upper) GetBounds(double[] tData, double[] yData);
    
    /// <summary>
    /// 漸近的総欠陥数（t→∞での極限値）を取得
    /// デフォルト実装は parameters[0] を返す（基本モデル用）
    /// 派生モデルで適切な極限値を計算する場合はオーバーライドする
    /// </summary>
    public virtual double GetAsymptoticTotalBugs(double[] parameters)
    {
        // 基本モデルの多くは parameters[0] (a) が総欠陥数
        return parameters[0];
    }

    #region 共通ヘルパ（初期値推定用）

    /// <summary>
    /// 観測値の累積系列を計算
    /// </summary>
    protected static double[] ComputeCumulative(double[] yData)
    {
        var cum = new double[yData.Length];
        double s = 0;
        for (int i = 0; i < yData.Length; i++)
        {
            s += yData[i];
            cum[i] = s;
        }
        return cum;
    }

    /// <summary>
    /// 累積系列が targetRatio に初めて達するインデックス（1始まりの日数を返す）
    /// 到達しない場合は最終日のインデックスを返す
    /// </summary>
    protected static double FindDayForCumulativeRatio(double[] yData, double targetRatio)
    {
        if (yData.Length == 0) return 1.0;

        var cum = ComputeCumulative(yData);
        double total = cum[^1];
        if (total <= 0) return 1.0;

        double target = total * targetRatio;
        for (int i = 0; i < cum.Length; i++)
        {
            if (cum[i] >= target)
                return i + 1.0; // 1始まりの日数に対応
        }

        return cum.Length;
    }

    /// <summary>
    /// 日次データから単純な平均勾配を推定
    /// </summary>
    protected static double EstimateAverageSlope(double[] yData)
    {
        if (yData.Length == 0) return 0;

        double min = yData.Min();
        double max = yData.Max();
        int n = yData.Length;
        if (n <= 1) return max - min;

        return (max - min) / (n - 1);
    }
    
    /// <summary>
    /// 設定から収束しきい値を取得
    /// </summary>
    protected static double GetConvergenceThreshold()
    {
        return ConfigurationService.Current.ModelInitialization.IncrementThreshold.ConvergenceThreshold;
    }
    
    /// <summary>
    /// 設定から a パラメータのスケール係数を取得（範囲内で調整）
    /// </summary>
    /// <param name="isConverged">収束済みかどうか</param>
    /// <param name="position">0.0～1.0の範囲でスケール位置を指定（0=Min, 1=Max）</param>
    protected static double GetScaleFactorAInRange(bool isConverged, double position)
    {
        var sf = ConfigurationService.Current.ModelInitialization.ScaleFactorA;
        
        if (isConverged)
        {
            return sf.ConvergedMin + (sf.ConvergedMax - sf.ConvergedMin) * position;
        }
        else
        {
            return sf.NotConvergedMin + (sf.NotConvergedMax - sf.NotConvergedMin) * position;
        }
    }
    
    /// <summary>
    /// 設定から b パラメータ（指数型）を取得
    /// </summary>
    protected static double GetBValueExponential(double avgSlope)
    {
        var thresholds = ConfigurationService.Current.ModelInitialization.AverageSlopeThresholds;
        var bValues = thresholds.BValuesExponential;
        
        return avgSlope switch
        {
            var s when s <= thresholds.VeryLow => bValues.VeryLow,
            var s when s <= thresholds.Low => bValues.Low,
            var s when s <= thresholds.Medium => bValues.Medium,
            _ => bValues.High
        };
    }
    
    /// <summary>
    /// 設定から b パラメータ（S字型）を取得
    /// </summary>
    protected static double GetBValueSCurve(double avgSlope)
    {
        var thresholds = ConfigurationService.Current.ModelInitialization.AverageSlopeThresholds;
        var bValues = thresholds.BValuesSCurve;
        
        return avgSlope switch
        {
            var s when s <= thresholds.VeryLow => bValues.VeryLow,
            var s when s <= thresholds.Low => bValues.Low,
            var s when s <= thresholds.Medium => bValues.Medium,
            _ => bValues.High
        };
    }
    
    /// <summary>
    /// 設定から変化点比率を取得
    /// </summary>
    protected static double GetChangePointRatio()
    {
        return ConfigurationService.Current.ChangePoint.CumulativeRatio;
    }
    
    /// <summary>
    /// 設定から不完全デバッグ係数 p の初期値を取得
    /// </summary>
    protected static double GetImperfectDebugP0()
    {
        return ConfigurationService.Current.ImperfectDebug.P0;
    }
    
    /// <summary>
    /// 設定から初期欠陥除去効率 η₀ を取得
    /// </summary>
    protected static double GetEta0()
    {
        return ConfigurationService.Current.ImperfectDebug.Eta0;
    }
    
    /// <summary>
    /// 設定から漸近欠陥除去効率 η∞ を取得
    /// </summary>
    protected static double GetEtaInfinity()
    {
        return ConfigurationService.Current.ImperfectDebug.EtaInfinity;
    }
    
    /// <summary>
    /// 設定からバグ混入率 α の初期値を取得
    /// </summary>
    protected static double GetAlpha0()
    {
        return ConfigurationService.Current.ImperfectDebug.Alpha0;
    }
    
    /// <summary>
    /// 設定からゴンペルツの初期遅延係数を取得
    /// </summary>
    protected static double GetGompertzB0()
    {
        return ConfigurationService.Current.ImperfectDebug.GompertzB0;
    }

    #endregion
    
    /// <summary>
    /// 残差二乗和を計算
    /// </summary>
    public double CalculateSSE(double[] tData, double[] yData, double[] parameters)
    {
        double sse = 0;
        for (int i = 0; i < tData.Length; i++)
        {
            double predicted = Calculate(tData[i], parameters);
            double residual = yData[i] - predicted;
            sse += residual * residual;
        }
        return sse;
    }
    
    /// <summary>
    /// 決定係数 R² を計算
    /// </summary>
    public double CalculateR2(double[] tData, double[] yData, double[] parameters)
    {
        double yMean = yData.Average();
        double ssTot = yData.Sum(y => (y - yMean) * (y - yMean));
        double ssRes = CalculateSSE(tData, yData, parameters);
        
        if (ssTot == 0) return 0;
        return 1 - (ssRes / ssTot);
    }
    
    /// <summary>
    /// AIC（赤池情報量規準）を計算
    /// </summary>
    public double CalculateAIC(double[] tData, double[] yData, double[] parameters)
    {
        int n = tData.Length;
        int k = parameters.Length;
        double sse = CalculateSSE(tData, yData, parameters);
        
        if (sse <= 0) return double.MaxValue;
        return n * Math.Log(sse / n) + 2 * k;
    }
    
    /// <summary>
    /// 指定割合に到達する日を予測
    /// </summary>
    public double? PredictDayForRatio(double ratio, double[] parameters, int currentDay)
    {
        double totalBugs = GetAsymptoticTotalBugs(parameters);
        double target = totalBugs * ratio;
        double currentValue = Calculate(currentDay, parameters);
        
        // 既に到達済み
        if (currentValue >= target)
            return null;
        
        // 二分法で探索
        double tLow = currentDay;
        double tHigh = currentDay * 10;
        
        // 上限を拡大
        while (Calculate(tHigh, parameters) < target && tHigh < 10000)
            tHigh *= 2;
        
        if (Calculate(tHigh, parameters) < target)
            return double.PositiveInfinity;
        
        // 二分法
        for (int i = 0; i < 100; i++)
        {
            double tMid = (tLow + tHigh) / 2;
            double valMid = Calculate(tMid, parameters);
            
            if (Math.Abs(valMid - target) < 0.01)
                return tMid;
            
            if (valMid < target)
                tLow = tMid;
            else
                tHigh = tMid;
        }
        
        return (tLow + tHigh) / 2;
    }
}
