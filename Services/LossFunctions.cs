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
    
    public double Evaluate(double[] tData, double[] yData, ReliabilityGrowthModelBase model, double[] parameters)
    {
        // 累積データから日次データを計算
        double nll = 0.0;
        double prevT = 0.0;
        double prevY = 0.0;
        
        for (int i = 0; i < tData.Length; i++)
        {
            double t = tData[i];
            
            // 日次バグ数（累積の差分）
            double dailyBugs = (i == 0) ? yData[i] : yData[i] - yData[i - 1];
            
            // 期待値 λ_i = m(t_i) - m(t_{i-1})
            double mt = model.Calculate(t, parameters);
            double mtPrev = model.Calculate(prevT, parameters);
            double lambda = Math.Max(mt - mtPrev, 1e-9); // 負やゼロを避ける
            
            // Poisson NLL: λ - y * log(λ)
            // 注: log(y!) 項は定数なので省略（最適化には影響なし）
            nll += lambda - dailyBugs * Math.Log(lambda);
            
            prevT = t;
            prevY = yData[i];
        }
        
        return nll;
    }
    
    public bool IsSupported(ReliabilityGrowthModelBase model)
    {
        return SupportedModelNames.Contains(model.Name);
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
