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
    /// <param name="yData">累積バグ発見数データ</param>
    /// <param name="model">信頼度成長モデル</param>
    /// <param name="parameters">モデルパラメータ</param>
    /// <param name="yFixedData">累積バグ修正数データ（FREモデル用、オプション）</param>
    /// <returns>損失値（最小化対象）</returns>
    double Evaluate(
        double[] tData, 
        double[] yData, 
        ReliabilityGrowthModelBase model, 
        double[] parameters,
        double[]? yFixedData = null);
    
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
    /// <param name="yData">累積バグ発見数データ</param>
    /// <param name="model">信頼度成長モデル</param>
    /// <param name="parameters">モデルパラメータ</param>
    /// <param name="yFixedData">累積バグ修正数データ（FREモデル用、オプション）</param>
    /// <returns>対数尤度 ln(L)</returns>
    double CalculateLogLikelihood(
        double[] tData, 
        double[] yData, 
        ReliabilityGrowthModelBase model, 
        double[] parameters,
        double[]? yFixedData = null);
    
    /// <summary>
    /// AICを計算
    /// </summary>
    /// <param name="tData">時間データ（日数）</param>
    /// <param name="yData">累積バグ発見数データ</param>
    /// <param name="model">信頼度成長モデル</param>
    /// <param name="parameters">モデルパラメータ</param>
    /// <param name="yFixedData">累積バグ修正数データ（FREモデル用、オプション）</param>
    /// <returns>AIC値</returns>
    double CalculateAIC(
        double[] tData, 
        double[] yData, 
        ReliabilityGrowthModelBase model, 
        double[] parameters,
        double[]? yFixedData = null);
    
    /// <summary>
    /// AICc（小標本補正AIC）を計算
    /// Burnham & Anderson (2002) に基づく補正: AICc = AIC + 2k(k+1)/(n-k-1)
    /// n/k < 40 の場合に推奨。n が小さく k が大きいモデルへの過学習ペナルティを強化。
    /// </summary>
    /// <param name="tData">時間データ（日数）</param>
    /// <param name="yData">累積バグ発見数データ</param>
    /// <param name="model">信頼度成長モデル</param>
    /// <param name="parameters">モデルパラメータ</param>
    /// <param name="yFixedData">累積バグ修正数データ（FREモデル用、オプション）</param>
    /// <returns>AICc値（n <= k+1 の場合は double.MaxValue）</returns>
    double CalculateAICc(
        double[] tData, 
        double[] yData, 
        ReliabilityGrowthModelBase model, 
        double[] parameters,
        double[]? yFixedData = null);
}

/// <summary>
/// 残差二乗和（SSE）損失関数
/// 従来の最小二乗法に対応
/// </summary>
public sealed class SseLossFunction : ILossFunction
{
    public string Name => "SSE (残差二乗和)";
    public string Description => "最小二乗法。累積バグ数に対する残差の二乗和を最小化。";
    
    public double Evaluate(
        double[] tData, 
        double[] yData, 
        ReliabilityGrowthModelBase model, 
        double[] parameters,
        double[]? yFixedData = null)
    {
        double sse = 0;
        
        // 発見数のSSE
        for (int i = 0; i < tData.Length; i++)
        {
            double predicted = model.Calculate(tData[i], parameters);
            double residual = yData[i] - predicted;
            sse += residual * residual;
        }
        
        // FREモデルの場合、修正数のSSEも追加
        if (model is FaultRemovalEfficiencyModelBase freModel && yFixedData != null)
        {
            for (int i = 0; i < tData.Length; i++)
            {
                double predictedCorrected = freModel.CalculateCorrected(tData[i], parameters);
                double residual = yFixedData[i] - predictedCorrected;
                sse += residual * residual;
            }
        }

        // TEFモデルの場合、工数データのSSEも追加（工数曲線のフィッティング）
        if (model is TEFBasedModelBase tefModel && tefModel.ObservedEffortData != null)
        {
            var effortData = tefModel.ObservedEffortData;
            int len = Math.Min(tData.Length, effortData.Length);

            // --- スケーリングの導入 ---
            // バグ数と工数でスケールが大きく異なる場合、工数SSEが支配的にならないよう、
            // 各系列の分散で正規化した上で重み付けを行う簡易的な手法を用いる。

            // バグ数系列の分散（0の場合は1にフォールバック）
            double meanBugs = 0.0;
            for (int i = 0; i < tData.Length; i++)
            {
                meanBugs += yData[i];
            }
            meanBugs /= Math.Max(1, tData.Length);

            double varBugs = 0.0;
            for (int i = 0; i < tData.Length; i++)
            {
                double diff = yData[i] - meanBugs;
                varBugs += diff * diff;
            }
            varBugs /= Math.Max(1, tData.Length);
            if (varBugs <= 0) varBugs = 1.0;

            // 工数系列の分散（0の場合は1にフォールバック）
            double meanEffort = 0.0;
            int effortCount = 0;
            for (int i = 0; i < len; i++)
            {
                if (effortData[i] <= 0) continue;
                meanEffort += effortData[i];
                effortCount++;
            }
            if (effortCount > 0)
            {
                meanEffort /= effortCount;
            }

            double varEffort = 0.0;
            if (effortCount > 0)
            {
                for (int i = 0; i < len; i++)
                {
                    if (effortData[i] <= 0) continue;
                    double diff = effortData[i] - meanEffort;
                    varEffort += diff * diff;
                }
                varEffort /= effortCount;
            }
            if (varEffort <= 0) varEffort = 1.0;

            // 工数SSEに掛ける重み: バグ系列と同程度のスケールになるように調整
            // weightEffort ≈ varBugs / varEffort
            double weightEffort = varBugs / varEffort;

            for (int i = 0; i < len; i++)
            {
                // 工数データが0の場合はスキップ（未入力とみなす）
                if (effortData[i] <= 0) continue;

                double predictedEffort = tefModel.CalculateEffort(tData[i], parameters);
                double residual = effortData[i] - predictedEffort;
                sse += residual * residual * weightEffort;
            }
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
    /// FREモデルの場合は発見+修正の結合尤度
    /// </summary>
    public double CalculateLogLikelihood(
        double[] tData, 
        double[] yData, 
        ReliabilityGrowthModelBase model, 
        double[] parameters,
        double[]? yFixedData = null)
    {
        // FREモデルの場合、発見と修正それぞれの対数尤度を計算して合計
        if (model is FaultRemovalEfficiencyModelBase freModel && yFixedData != null)
        {
            int n = tData.Length;
            
            // 発見数のSSE
            double sseDetected = 0;
            for (int i = 0; i < n; i++)
            {
                double predicted = freModel.CalculateDetected(tData[i], parameters);
                double residual = yData[i] - predicted;
                sseDetected += residual * residual;
            }
            
            // 修正数のSSE
            double sseCorrected = 0;
            for (int i = 0; i < n; i++)
            {
                double predicted = freModel.CalculateCorrected(tData[i], parameters);
                double residual = yFixedData[i] - predicted;
                sseCorrected += residual * residual;
            }
            
            // TEFモデルの工数SSEも考慮（もしあれば）
            double sseEffort = 0;
            if (model is TEFBasedModelBase tefModel && tefModel.ObservedEffortData != null)
            {
                var effortData = tefModel.ObservedEffortData;
                int len = Math.Min(n, effortData.Length);
                for (int i = 0; i < len; i++)
                {
                    if (effortData[i] <= 0) continue;
                    double predictedEffort = tefModel.CalculateEffort(tData[i], parameters);
                    double residual = effortData[i] - predictedEffort;
                    sseEffort += residual * residual;
                }
            }
            
            if (sseDetected <= 0 || sseCorrected <= 0 || n == 0) 
                return double.NegativeInfinity;
            
            // 各プロセスの対数尤度
            double sigma2D = sseDetected / n;
            double sigma2C = sseCorrected / n;
            
            double logLD = -n / 2.0 * (Math.Log(2 * Math.PI) + Math.Log(sigma2D) + 1);
            double logLC = -n / 2.0 * (Math.Log(2 * Math.PI) + Math.Log(sigma2C) + 1);
            
            double logLE = 0;
            if (sseEffort > 0)
            {
                double sigma2E = sseEffort / n;
                logLE = -n / 2.0 * (Math.Log(2 * Math.PI) + Math.Log(sigma2E) + 1);
            }
            
            return logLD + logLC + logLE;
        }
        
        // 通常モデル
        int nPoints = tData.Length;
        double sse = Evaluate(tData, yData, model, parameters); // Evaluate now includes TEF effort SSE
        
        if (sse <= 0 || nPoints == 0) return double.NegativeInfinity;
        
        double sigma2 = sse / nPoints;
        double logL = -nPoints / 2.0 * (Math.Log(2 * Math.PI) + Math.Log(sigma2) + 1);
        return logL;
    }
    
    /// <summary>
    /// SSEベースのAIC計算
    /// 正規分布仮定での最尤推定に対応
    /// AIC = n * ln(SSE/n) + 2k （定数項を無視した形式、従来互換）
    /// FREモデルの場合は 2k - 2ln(L_total)
    /// </summary>
    public double CalculateAIC(
        double[] tData, 
        double[] yData, 
        ReliabilityGrowthModelBase model, 
        double[] parameters,
        double[]? yFixedData = null)
    {
        int k = parameters.Length;
        
        // FREモデルの場合、結合対数尤度からAICを計算
        if (model is FaultRemovalEfficiencyModelBase && yFixedData != null)
        {
            double logL = CalculateLogLikelihood(tData, yData, model, parameters, yFixedData);
            if (double.IsNegativeInfinity(logL) || double.IsNaN(logL))
                return double.MaxValue;
            return 2 * k - 2 * logL;
        }
        
        // 通常モデル: 従来の公式
        int n = tData.Length;
        double sse = Evaluate(tData, yData, model, parameters);
        
        if (sse <= 0) return double.MaxValue;
        
        return n * Math.Log(sse / n) + 2 * k;
    }
    
    /// <summary>
    /// SSEベースのAICc計算（小標本補正付きAIC）
    /// AICc = AIC + 2k(k+1)/(n-k-1)
    /// Burnham & Anderson (2002) の基準に基づく
    /// </summary>
    public double CalculateAICc(
        double[] tData, 
        double[] yData, 
        ReliabilityGrowthModelBase model, 
        double[] parameters,
        double[]? yFixedData = null)
    {
        // 1. 通常の AIC を計算
        double aic = CalculateAIC(tData, yData, model, parameters, yFixedData);
        
        // AIC が計算不能ならそのまま返す
        if (aic >= double.MaxValue || double.IsInfinity(aic) || double.IsNaN(aic))
            return aic;

        int n = tData.Length;
        int k = parameters.Length;

        // 2. 補正項の分母チェック (n <= k + 1 の場合は計算不能)
        double denominator = n - k - 1.0;
        if (denominator <= 0) 
        {
            // サンプルサイズ不足でモデルとして不適
            return double.MaxValue;
        }

        // 3. AICc = AIC + (2k(k+1) / (n-k-1))
        double correction = (2.0 * k * (k + 1.0)) / denominator;
        return aic + correction;
    }
}

/// <summary>
/// 最尤推定（MLE）損失関数
/// Poisson-NHPP（非斉次ポアソン過程）を前提とした負の対数尤度を最小化
/// 全モデルでサポート（NHPP前提）
/// </summary>
public sealed class MleLossFunction : ILossFunction
{
    public string Name => "MLE (最尤推定)";
    public string Description => "Poisson-NHPPに基づく最尤推定。日次バグ数がポアソン分布に従うと仮定。";
    
    /// <summary>
    /// 負の対数尤度を計算（最小化対象）
    /// FREモデルの場合は発見+修正の結合尤度
    /// </summary>
    public double Evaluate(
        double[] tData, 
        double[] yData, 
        ReliabilityGrowthModelBase model, 
        double[] parameters,
        double[]? yFixedData = null)
    {
        return -CalculateLogLikelihood(tData, yData, model, parameters, yFixedData);
    }
    
    /// <summary>
    /// 全モデルでMLEをサポート
    /// NHPPとして定義されたモデルに対してMLEは数学的に自然なアプローチ
    /// 注意: 計算の安定性のため、強力なオプティマイザ（DE, CMA-ES）の使用を推奨
    /// </summary>
    public bool IsSupported(ReliabilityGrowthModelBase model)
    {
        // 全モデルでMLEをサポート
        return true;
    }
    
    /// <summary>
    /// Poisson-NHPPの対数尤度を計算
    /// ln(L) = Σ[y_i * ln(λ_i) - λ_i - ln(y_i!)]
    /// ここで λ_i = m(t_i) - m(t_{i-1}) は期間 [t_{i-1}, t_i] の期待バグ数
    /// FREモデルの場合は発見と修正の同時最尤推定（結合尤度）
    /// </summary>
    public double CalculateLogLikelihood(
        double[] tData, 
        double[] yData, 
        ReliabilityGrowthModelBase model, 
        double[] parameters,
        double[]? yFixedData = null)
    {
        // 発見数の対数尤度
        double logLDetected = CalculateDetectedLogLikelihood(tData, yData, model, parameters);
        
        // FREモデルの場合、修正数の対数尤度を加算（同時最尤推定）
        if (model is FaultRemovalEfficiencyModelBase freModel && yFixedData != null)
        {
            double logLCorrected = CalculateCorrectedLogLikelihood(tData, yFixedData, freModel, parameters);
            
            // TEFモデルの工数尤度も考慮（もしあれば）
            double logLEffort = 0;
            if (model is TEFBasedModelBase tefModel && tefModel.ObservedEffortData != null)
            {
                logLEffort = CalculateEffortLogLikelihood(tData, tefModel, parameters);
            }
            
            return logLDetected + logLCorrected + logLEffort;
        }
        
        // TEFモデルの工数尤度も考慮（通常モデルの場合）
        if (model is TEFBasedModelBase tefModel2 && tefModel2.ObservedEffortData != null)
        {
            double logLEffort = CalculateEffortLogLikelihood(tData, tefModel2, parameters);
            return logLDetected + logLEffort;
        }
        
        return logLDetected;
    }
    
    /// <summary>
    /// 工数の対数尤度を計算（TEFモデル用、正規分布仮定）
    /// </summary>
    private double CalculateEffortLogLikelihood(
        double[] tData, 
        TEFBasedModelBase model, 
        double[] parameters)
    {
        var effortData = model.ObservedEffortData;
        if (effortData == null) return 0;
        
        int n = Math.Min(tData.Length, effortData.Length);
        double sse = 0;
        int count = 0;
        
        for (int i = 0; i < n; i++)
        {
            if (effortData[i] <= 0) continue;
            
            double predicted = model.CalculateEffort(tData[i], parameters);
            double residual = effortData[i] - predicted;
            sse += residual * residual;
            count++;
        }
        
        if (sse <= 0 || count == 0) return 0; // 完全一致またはデータなし
        
        // 正規分布の対数尤度: -n/2 * ln(2πσ²) - SSE/(2σ²)
        // 最尤推定では σ² = SSE/n となるため
        // ln(L) = -n/2 * (ln(2π) + ln(SSE/n) + 1)
        
        double sigma2 = sse / count;
        return -count / 2.0 * (Math.Log(2 * Math.PI) + Math.Log(sigma2) + 1);
    }

    /// <summary>
    /// 発見数の対数尤度を計算
    /// </summary>
    private double CalculateDetectedLogLikelihood(
        double[] tData, 
        double[] yData, 
        ReliabilityGrowthModelBase model, 
        double[] parameters)
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
    /// 修正数の対数尤度を計算（FREモデル用）
    /// </summary>
    private double CalculateCorrectedLogLikelihood(
        double[] tData, 
        double[] yFixedData, 
        FaultRemovalEfficiencyModelBase model, 
        double[] parameters)
    {
        double logL = 0.0;
        double prevT = 0.0;
        
        for (int i = 0; i < tData.Length; i++)
        {
            double t = tData[i];
            
            // 日次修正数（実測値）
            double dailyFixed = (i == 0) ? yFixedData[i] : yFixedData[i] - yFixedData[i - 1];
            
            // 期待される修正数: m_c(t_i) - m_c(t_{i-1})
            double mc = model.CalculateCorrected(t, parameters);
            double mcPrev = model.CalculateCorrected(prevT, parameters);
            double lambda = Math.Max(mc - mcPrev, 1e-9); // 0除算防止
            
            // Poisson対数尤度加算
            logL += dailyFixed * Math.Log(lambda) - lambda - LogFactorial(dailyFixed);
            
            prevT = t;
        }
        
        return logL;
    }
    
    /// <summary>
    /// Poisson-NHPPベースのAIC計算
    /// AIC = 2k - 2ln(L)
    /// FREモデルの場合は結合尤度 L_total = L_d × L_c を使用
    /// </summary>
    public double CalculateAIC(
        double[] tData, 
        double[] yData, 
        ReliabilityGrowthModelBase model, 
        double[] parameters,
        double[]? yFixedData = null)
    {
        int k = parameters.Length;
        double logL = CalculateLogLikelihood(tData, yData, model, parameters, yFixedData);
        
        if (double.IsNegativeInfinity(logL) || double.IsNaN(logL))
            return double.MaxValue;
        
        // AIC = 2k - 2ln(L)
        return 2 * k - 2 * logL;
    }
    
    /// <summary>
    /// Poisson-NHPPベースのAICc計算（小標本補正付きAIC）
    /// AICc = AIC + 2k(k+1)/(n-k-1)
    /// Burnham & Anderson (2002) の基準に基づく
    /// </summary>
    public double CalculateAICc(
        double[] tData, 
        double[] yData, 
        ReliabilityGrowthModelBase model, 
        double[] parameters,
        double[]? yFixedData = null)
    {
        // 1. 通常の AIC を計算
        double aic = CalculateAIC(tData, yData, model, parameters, yFixedData);
        
        // AIC が計算不能ならそのまま返す
        if (aic >= double.MaxValue || double.IsInfinity(aic) || double.IsNaN(aic))
            return aic;

        int n = tData.Length;
        int k = parameters.Length;

        // 2. 補正項の分母チェック (n <= k + 1 の場合は計算不能)
        double denominator = n - k - 1.0;
        if (denominator <= 0) 
        {
            // サンプルサイズ不足でモデルとして不適
            return double.MaxValue;
        }

        // 3. AICc = AIC + (2k(k+1) / (n-k-1))
        double correction = (2.0 * k * (k + 1.0)) / denominator;
        return aic + correction;
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
    /// 全モデルでMLE/SSE両方をサポート
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
        
        // 全モデルでサポート（フォールバックなし）
        actualType = requestedType;
        fallbackWarning = null;
        return lossFunction;
    }
}
