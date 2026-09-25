using BugConvergenceTool.Services;
using BugConvergenceTool.Services.Diagnostics;

namespace BugConvergenceTool.Models;

/// <summary>
/// 信頼度成長モデルのフィッティング結果
/// </summary>
public class FittingResult
{
    public string ModelName { get; set; } = "";
    public string Category { get; set; } = "基本";
    
    /// <summary>
    /// 推定に使ったモデルのインスタンス（TEF の工数データなど推定時の状態を保持）
    /// </summary>
    public ReliabilityGrowthModelBase? Model { get; set; }
    
    /// <summary>
    /// 推奨モデルの選択対象から外す理由（null なら対象）
    /// 例: 潜在バグ総数が探索範囲の上限に張り付いている、変化点が尤度比検定で有意でない
    /// </summary>
    public string? SelectionExclusionReason { get; set; }
    
    /// <summary>
    /// 変化点の尤度比検定の結果（変化点モデルで検定を実行した場合）
    /// </summary>
    public Services.ChangePointLRTResult? ChangePointTest { get; set; }
    
    /// <summary>
    /// Fisher 情報行列による漸近的な標準誤差・信頼区間（--ci かつ MLE の場合）
    /// </summary>
    public Services.FisherInformationResult? FisherInformation { get; set; }
    
    /// <summary>
    /// Fisher 情報行列（デルタ法）による推定潜在バグ総数の信頼区間
    /// </summary>
    public Services.DerivedQuantityInterval? TotalBugsFisherInterval { get; set; }
    
    /// <summary>
    /// パラメトリック・ブートストラップによる予測区間（--pi の場合）
    /// </summary>
    public Services.PredictionIntervalResult? PredictionInterval { get; set; }
    
    /// <summary>
    /// マルチスタート最適化の開始点数（1 ならマルチスタートなし）
    /// </summary>
    public int OptimizationStarts { get; set; } = 1;
    
    /// <summary>
    /// マルチスタート最適化で最良解と同じ解に収束した開始点数
    /// </summary>
    public int StartsConvergedToBest { get; set; } = 1;
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

    /// <summary>
    /// 比較グループ（AIC を比較できるモデルの組。<see cref="Services.ModelComparisonGroup"/> 参照）
    /// </summary>
    public string ComparisonGroup { get; set; } = Services.ModelComparisonGroup.DetectionOnly;

    public double[] PredictedValues { get; set; } = Array.Empty<double>();
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    
    // 収束予測
    public Dictionary<string, ConvergencePrediction> ConvergencePredictions { get; set; } = new();
    
    /// <summary>
    /// 推定潜在バグ総数（漸近値 m(∞)）
    /// フィッティング時に <see cref="ReliabilityGrowthModelBase.GetAsymptoticTotalBugs"/> で設定する。
    /// パラメータ a は m(∞) と一致しないモデル（有限工数の TEF 組込モデル等）があるため直接使わないこと。
    /// </summary>
    public double EstimatedTotalBugs { get; set; }
    
    
    // オプティマイザ情報
    public string OptimizerUsed { get; set; } = "";
    public long OptimizationTimeMs { get; set; }
    public int FunctionEvaluations { get; set; }
    
    // 信頼区間計算用：最適化時のパラメータベクトル（順序を保持）
    public double[] ParameterVector { get; set; } = Array.Empty<double>();
    
    // 信頼区間計算用：予測時刻（PredictedValues に対応する X 軸）
    public double[] PredictionTimes { get; set; } = Array.Empty<double>();
    
    /// <summary>
    /// ブートストラップによる m(t)・総数・収束日の信頼区間（--ci の場合）
    /// </summary>
    public Services.ConfidenceBandResult? ConfidenceBand { get; set; }
    
    // ホールドアウト検証結果（オプション）
    // 検証用パラメータは訓練区間のみで別途推定したもの。最終結果（Parameters・AIC・収束予測）は全データで推定する。
    
    /// <summary>
    /// ホールドアウト検証の結果（未実施なら null）
    /// </summary>
    public Services.HoldoutValidationResult? Holdout { get; set; }
    
    /// <summary>
    /// ホールドアウト検証用に訓練区間のみで推定したパラメータ
    /// </summary>
    public double[]? HoldoutTrainParameters { get; set; }
    
    /// <summary>
    /// ホールドアウト期間の発見数の相対誤差（%、符号付き。正=過大予測）
    /// </summary>
    public double? HoldoutIncrementErrorPercent =>
        Holdout != null && double.IsFinite(Holdout.IncrementErrorPercent) ? Holdout.IncrementErrorPercent : null;
    
    /// <summary>
    /// ホールドアウト期間の日次増分の平均絶対誤差（件/日）
    /// </summary>
    public double? HoldoutDailyMae => Holdout?.DailyMae;
    
    /// <summary>
    /// ホールドアウト予測誤差の大きさ（モデル間の順位付け用。期間増分の相対誤差の絶対値）
    /// </summary>
    public double? HoldoutAbsIncrementErrorPercent =>
        HoldoutIncrementErrorPercent.HasValue ? Math.Abs(HoldoutIncrementErrorPercent.Value) : null;
    
    /// <summary>
    /// 使用した損失関数タイプ
    /// </summary>
    public string LossFunctionUsed { get; set; } = "SSE";
    
    /// <summary>
    /// 警告メッセージのリスト
    /// </summary>
    public List<string> Warnings { get; set; } = new();
    
    /// <summary>
    /// 推定の安定性（末尾の日を除いて推定し直したときの総数の変化。推奨モデルのみ）
    /// </summary>
    public Services.StabilityAnalysisResult? Stability { get; set; }
    
    /// <summary>
    /// 変化点探索結果（変化点モデルの場合のみ）
    /// </summary>
    public Services.ChangePointSearchResult? ChangePointSearchResult { get; set; }
    
    // ========================================
    // 統計診断結果（Phase 1-2）
    // ========================================
    
    /// <summary>
    /// 残差診断結果
    /// </summary>
    public DiagnosticReport? Diagnostics { get; set; }
    
    /// <summary>
    /// 適合度検定結果
    /// </summary>
    public GoodnessOfFitResult? GoodnessOfFit { get; set; }
    
    /// <summary>
    /// 診断の総合評価グレード
    /// </summary>
    public DiagnosticGrade? DiagnosticGrade => Diagnostics?.OverallGrade;
    
    /// <summary>
    /// 診断の総合評価スコア（0-100）
    /// </summary>
    public int? DiagnosticScore => Diagnostics?.OverallScore;
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

    /// <summary>
    /// 発見数の平均値関数 m(t)（<see cref="Calculate"/>）に効くパラメータの数
    /// </summary>
    /// <remarks>
    /// 発見数だけを使う検定（χ² 適合度検定など）の自由度に使う。FRE モデルの η・D などの修正数にしか効かない
    /// パラメータは含めない。
    /// </remarks>
    public virtual int DetectionParameterCount => ParameterNames.Length;

    /// <summary>
    /// m(∞)（<see cref="GetAsymptoticTotalBugs"/>）の表示名
    /// </summary>
    public virtual string TotalBugsLabel => "推定潜在バグ総数";

    /// <summary>
    /// パラメータ index の境界 bound が、別のモデルに一致するなどの「自然な境界」か
    /// </summary>
    /// <remarks>
    /// 自然な境界に張り付いた推定値は、データがそのモデルを支持しているだけなので注意しない。
    /// 既定は 0 の境界（変化の大きさが 0 など）と、欠陥除去効率 η の上限 1（完全除去）。
    /// </remarks>
    /// <param name="index">パラメータの位置</param>
    /// <param name="upper">上限なら true、下限なら false</param>
    /// <param name="bound">境界の値</param>
    public virtual bool IsNaturalBound(int index, bool upper, double bound)
    {
        string name = index < ParameterNames.Length ? ParameterNames[index] : "";
        return bound == 0 || (upper && name.StartsWith("η") && bound == 1.0);
    }

    /// <summary>
    /// Fisher 情報行列を計算する座標に変換する（既定は恒等変換）
    /// </summary>
    /// <remarks>
    /// 探索しやすさのために対数などで持っているパラメータは、自然な境界の近くで尤度がほとんど平らになり
    /// （例: ln ψ → -∞ で ψ → 0）、その座標のヘッセ行列は特異に近くなる。その場合は元の座標
    /// （ψ）で Fisher 情報行列を求め、ヤコビアンで推定に使う座標の共分散に戻す。
    /// </remarks>
    public virtual double[] ToFisherScale(double[] parameters) => (double[])parameters.Clone();

    /// <summary>
    /// <see cref="ToFisherScale"/> の逆変換
    /// </summary>
    public virtual double[] FromFisherScale(double[] fisherParameters) => (double[])fisherParameters.Clone();

    /// <summary>
    /// パラメータから導かれる、解釈しやすい量（変曲点など）
    /// </summary>
    public virtual IEnumerable<(string Name, double Value, string Description)> GetDerivedQuantities(double[] parameters)
        => Enumerable.Empty<(string, double, string)>();

    #region 共通ヘルパ（初期値推定用）

    /// <summary>
    /// 累積系列が最終値の targetRatio 倍に初めて達する日（1始まりの日数を返す）
    /// 到達しない場合は最終日を返す
    /// </summary>
    /// <param name="cumulative">累積バグ数（GetInitialParameters に渡される yData はすでに累積値）</param>
    /// <remarks>
    /// 以前は累積値をさらに累積してから比率を求めていたため、到達日が大きく後ろにずれていた
    /// （例: 50% 到達日 15 日が 30 日と算出される）。
    /// </remarks>
    protected static double FindDayForCumulativeRatio(double[] cumulative, double targetRatio)
    {
        if (cumulative.Length == 0) return 1.0;

        double total = cumulative[^1];
        if (total <= 0) return 1.0;

        double target = total * targetRatio;
        for (int i = 0; i < cumulative.Length; i++)
        {
            if (cumulative[i] >= target)
                return i + 1.0; // 1始まりの日数に対応
        }

        return cumulative.Length;
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
    /// m(t) = ratio × m(∞) となる日（t=0 から二分法）。到達しなければ +∞
    /// </summary>
    /// <remarks>
    /// 収束予測・予測区間・モデル平均化で共通に使う。以前は収束予測だけ別実装（現在日から探索し、
    /// モデル上すでに到達していると null を返す）で、観測値が未到達・モデル値が到達済みのとき「予測不可」と表示されていた。
    /// </remarks>
    public double DayForRatio(double ratio, double[] parameters)
    {
        double target = GetAsymptoticTotalBugs(parameters) * ratio;
        if (!double.IsFinite(target) || target <= 0) return double.PositiveInfinity;
        
        double lo = 0, hi = 1;
        while (Calculate(hi, parameters) < target)
        {
            hi *= 2;
            if (hi > MaxMilestoneSearchDay) return double.PositiveInfinity;
        }
        for (int i = 0; i < 100 && hi - lo > 1e-6; i++)
        {
            double mid = (lo + hi) / 2;
            if (Calculate(mid, parameters) < target) lo = mid; else hi = mid;
        }
        return (lo + hi) / 2;
    }
    
    /// <summary>マイルストーン到達日を探す最大日数</summary>
    private const double MaxMilestoneSearchDay = 1e5;
}
