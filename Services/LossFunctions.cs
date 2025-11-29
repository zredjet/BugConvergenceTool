using BugConvergenceTool.Models;

namespace BugConvergenceTool.Services;

/// <summary>
/// 損失関数のタイプ
/// </summary>
public enum LossType
{
    /// <summary>
    /// 残差二乗和（Sum of Squared Errors）- デフォルト
    /// </summary>
    Sse,
    
    /// <summary>
    /// 最尤推定（Maximum Likelihood Estimation）- Poisson-NHPP
    /// </summary>
    Mle
}

/// <summary>
/// 損失関数のインターフェース
/// </summary>
public interface ILossFunction
{
    /// <summary>
    /// 損失関数の名前
    /// </summary>
    string Name { get; }
    
    /// <summary>
    /// 損失関数の説明
    /// </summary>
    string Description { get; }
    
    /// <summary>
    /// 損失関数を評価
    /// </summary>
    /// <param name="tData">時間データ（日数）</param>
    /// <param name="yData">累積バグ数データ</param>
    /// <param name="model">信頼度成長モデル</param>
    /// <param name="parameters">モデルパラメータ</param>
    /// <returns>損失値（最小化対象）</returns>
    double Evaluate(double[] tData, double[] yData, ReliabilityGrowthModelBase model, double[] parameters);
    
    /// <summary>
    /// このモデルに対してこの損失関数がサポートされているか
    /// </summary>
    /// <param name="model">信頼度成長モデル</param>
    /// <returns>サポート状況</returns>
    bool IsSupported(ReliabilityGrowthModelBase model);
    
    /// <summary>
    /// 対数尤度を計算（AIC計算用）
    /// </summary>
    /// <param name="tData">時間データ（日数）</param>
    /// <param name="yData">累積バグ数データ</param>
    /// <param name="model">信頼度成長モデル</param>
    /// <param name="parameters">モデルパラメータ</param>
    /// <returns>対数尤度 ln(L)</returns>
    double CalculateLogLikelihood(double[] tData, double[] yData, ReliabilityGrowthModelBase model, double[] parameters);
    
    /// <summary>
    /// AICを計算
    /// </summary>
    /// <param name="tData">時間データ（日数）</param>
    /// <param name="yData">累積バグ数データ</param>
    /// <param name="model">信頼度成長モデル</param>
    /// <param name="parameters">モデルパラメータ</param>
    /// <returns>AIC値</returns>
    double CalculateAIC(double[] tData, double[] yData, ReliabilityGrowthModelBase model, double[] parameters);
}

/// <summary>
/// 残差二乗和（SSE）損失関数
/// 従来の最小二乗法に対応
/// </summary>
public sealed class SseLossFunction : ILossFunction
{
    public string Name => "SSE (残差二乗和)";
    public string Description => "最小二乗法。累積バグ数に対する残差の二乗和を最小化。";
    
    public double Evaluate(double[] tData, double[] yData, ReliabilityGrowthModelBase model, double[] parameters)
    {
        double sse = 0;
        for (int i = 0; i < tData.Length; i++)
        {
            double predicted = model.Calculate(tData[i], parameters);
            double residual = yData[i] - predicted;
            sse += residual * residual;
        }
        return sse;
    }
    
    public bool IsSupported(ReliabilityGrowthModelBase model)
    {
        // SSEは全モデルでサポート
        return true;
    }
    
    /// <summary>
    /// SSEベースの対数尤度（正規分布仮定）
    /// ln(L) = -n/2 * ln(2π) - n/2 * ln(σ²) - SSE/(2σ²)
    /// σ² = SSE/n として計算
    /// </summary>
    public double CalculateLogLikelihood(double[] tData, double[] yData, ReliabilityGrowthModelBase model, double[] parameters)
    {
        int n = tData.Length;
        double sse = Evaluate(tData, yData, model, parameters);
        
        if (sse <= 0 || n == 0) return double.NegativeInfinity;
        
        // 最尤推定における分散 σ² = SSE/n
        double sigma2 = sse / n;
        
        // ln(L) = -n/2 * ln(2π) - n/2 * ln(σ²) - n/2
        //       = -n/2 * (ln(2π) + ln(σ²) + 1)
        double logL = -n / 2.0 * (Math.Log(2 * Math.PI) + Math.Log(sigma2) + 1);
        return logL;
    }
    
    /// <summary>
    /// SSEベースのAIC計算
    /// 正規分布仮定での最尤推定に対応
    /// AIC = n * ln(SSE/n) + 2k （定数項を無視した形式、従来互換）
    /// </summary>
    public double CalculateAIC(double[] tData, double[] yData, ReliabilityGrowthModelBase model, double[] parameters)
    {
        int n = tData.Length;
        int k = parameters.Length;
        double sse = Evaluate(tData, yData, model, parameters);
        
        if (sse <= 0) return double.MaxValue;
        
        // 従来の公式: n * ln(SSE/n) + 2k
        // これは AIC = 2k - 2ln(L) の定数項を無視した近似形式
        return n * Math.Log(sse / n) + 2 * k;
    }
}

/// <summary>
/// 最尤推定（MLE）損失関数
/// Poisson-NHPP（非斉次ポアソン過程）を前提とした負の対数尤度を最小化
/// </summary>
public sealed class MleLossFunction : ILossFunction
{
    public string Name => "MLE (最尤推定)";
    public string Description => "Poisson-NHPPに基づく最尤推定。日次バグ数がポアソン分布に従うと仮定。";
    
    // MLEをサポートするモデル名のリスト（段階的に拡張）
    private static readonly HashSet<string> SupportedModelNames = new(StringComparer.OrdinalIgnoreCase)
    {
        // 基本モデル（Phase 1）
        "指数型（Goel-Okumoto）",
        "遅延S字型",
        "ゴンペルツ",
        "修正ゴンペルツ",
        "ロジスティック",
        
        // 不完全デバッグモデル（Phase 2）- 後で追加
        // "PNZ型", "Yamada遅延S字型", "Pham-Zhang型"
    };
    
    /// <summary>
    /// 負の対数尤度を計算（最小化対象）
    /// </summary>
    public double Evaluate(double[] tData, double[] yData, ReliabilityGrowthModelBase model, double[] parameters)
    {
        return -CalculateLogLikelihood(tData, yData, model, parameters);
    }
    
    public bool IsSupported(ReliabilityGrowthModelBase model)
    {
        return SupportedModelNames.Contains(model.Name);
    }
    
    /// <summary>
    /// Poisson-NHPPの対数尤度を計算
    /// ln(L) = Σ[y_i * ln(λ_i) - λ_i - ln(y_i!)]
    /// ここで λ_i = m(t_i) - m(t_{i-1}) は期間 [t_{i-1}, t_i] の期待バグ数
    /// </summary>
    public double CalculateLogLikelihood(double[] tData, double[] yData, ReliabilityGrowthModelBase model, double[] parameters)
    {
        double logL = 0.0;
        double prevT = 0.0;
        
        for (int i = 0; i < tData.Length; i++)
        {
            double t = tData[i];
            
            // 日次バグ数（累積の差分）
            double dailyBugs = (i == 0) ? yData[i] : yData[i] - yData[i - 1];
            
            // 期待値 λ_i = m(t_i) - m(t_{i-1})
            double mt = model.Calculate(t, parameters);
            double mtPrev = model.Calculate(prevT, parameters);
            double lambda = Math.Max(mt - mtPrev, 1e-9); // 負やゼロを避ける
            
            // Poisson対数尤度: y * ln(λ) - λ - ln(y!)
            logL += dailyBugs * Math.Log(lambda) - lambda - LogFactorial(dailyBugs);
            
            prevT = t;
        }
        
        return logL;
    }
    
    /// <summary>
    /// Poisson-NHPPベースのAIC計算
    /// AIC = 2k - 2ln(L)
    /// </summary>
    public double CalculateAIC(double[] tData, double[] yData, ReliabilityGrowthModelBase model, double[] parameters)
    {
        int k = parameters.Length;
        double logL = CalculateLogLikelihood(tData, yData, model, parameters);
        
        if (double.IsNegativeInfinity(logL) || double.IsNaN(logL))
            return double.MaxValue;
        
        // AIC = 2k - 2ln(L)
        return 2 * k - 2 * logL;
    }
    
    /// <summary>
    /// ln(n!) をスターリング近似で計算
    /// n が小さい場合は直接計算
    /// </summary>
    private static double LogFactorial(double n)
    {
        if (n < 0) return 0;
        if (n <= 1) return 0;
        
        int nInt = (int)Math.Round(n);
        if (nInt <= 20)
        {
            // 小さいnは直接計算
            double result = 0;
            for (int i = 2; i <= nInt; i++)
                result += Math.Log(i);
            return result;
        }
        
        // スターリング近似: ln(n!) ≈ n*ln(n) - n + 0.5*ln(2πn)
        return n * Math.Log(n) - n + 0.5 * Math.Log(2 * Math.PI * n);
    }
    
    /// <summary>
    /// モデルをMLEサポート対象に追加（拡張用）
    /// </summary>
    public static void AddSupportedModel(string modelName)
    {
        SupportedModelNames.Add(modelName);
    }
}

/// <summary>
/// 損失関数のファクトリ
/// </summary>
public static class LossFunctionFactory
{
    private static readonly SseLossFunction _sseLoss = new();
    private static readonly MleLossFunction _mleLoss = new();
    
    /// <summary>
    /// 指定タイプの損失関数を取得
    /// </summary>
    public static ILossFunction Create(LossType type)
    {
        return type switch
        {
            LossType.Sse => _sseLoss,
            LossType.Mle => _mleLoss,
            _ => _sseLoss
        };
    }
    
    /// <summary>
    /// モデルに対して適切な損失関数を取得
    /// MLEが指定されてもサポートされていない場合はSSEにフォールバック
    /// </summary>
    /// <param name="requestedType">要求された損失関数タイプ</param>
    /// <param name="model">対象モデル</param>
    /// <param name="actualType">実際に使用される損失関数タイプ（out）</param>
    /// <param name="fallbackWarning">フォールバックが発生した場合の警告メッセージ（out）</param>
    /// <returns>損失関数インスタンス</returns>
    public static ILossFunction GetForModel(
        LossType requestedType, 
        ReliabilityGrowthModelBase model,
        out LossType actualType,
        out string? fallbackWarning)
    {
        var lossFunction = Create(requestedType);
        
        if (lossFunction.IsSupported(model))
        {
            actualType = requestedType;
            fallbackWarning = null;
            return lossFunction;
        }
        
        // MLEがサポートされていない場合はSSEにフォールバック
        actualType = LossType.Sse;
        fallbackWarning = $"モデル '{model.Name}' は MLE をサポートしていないため、SSE にフォールバックしました。";
        return _sseLoss;
    }
}
