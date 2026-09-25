using BugConvergenceTool.Models;

namespace BugConvergenceTool.Services;

/// <summary>
/// ブートストラップによる平均値関数 m(t) の信頼区間（帯）と、総数・収束日の信頼区間
/// </summary>
public sealed class ConfidenceBandResult
{
    public double ConfidenceLevel { get; init; }
    
    /// <summary>要求したブートストラップ反復回数</summary>
    public int Requested { get; init; }
    
    /// <summary>再推定に成功した反復回数（区間の計算に使った数）</summary>
    public int Succeeded { get; init; }
    
    /// <summary>時刻（観測期間と将来の期間）</summary>
    public double[] Times { get; init; } = Array.Empty<double>();
    
    /// <summary>推定値での m(t)</summary>
    public double[] Estimate { get; init; } = Array.Empty<double>();
    
    /// <summary>m(t) の信頼区間の下限</summary>
    public double[] Lower { get; init; } = Array.Empty<double>();
    
    /// <summary>m(t) の信頼区間の上限</summary>
    public double[] Upper { get; init; } = Array.Empty<double>();
    
    /// <summary>各時刻の上限が探索範囲の上限（a の張り付き）で決まっているか</summary>
    public bool[] UpperIsBoundLimited { get; init; } = Array.Empty<bool>();
    
    /// <summary>推定潜在バグ総数 m(∞) の信頼区間</summary>
    public IntervalEstimate? TotalBugs { get; init; }
    
    /// <summary>収束マイルストーン到達日の信頼区間</summary>
    public List<MilestoneInterval> Milestones { get; init; } = new();
    
    public List<string> Warnings { get; init; } = new();
}

/// <summary>
/// パラメトリック・ブートストラップによる信頼区間（--ci）
/// </summary>
/// <remarks>
/// <para>
/// <see cref="ParametricBootstrap"/> で得た θ* の分布から、m(t)（観測期間と将来の期間）、m(∞)、
/// 収束マイルストーン到達日の信頼区間をパーセンタイル法で求める。パラメータの不確実性のみを含み、
/// Poisson 変動を含む将来の観測値の区間は <see cref="PredictionIntervalService"/>（--pi）で求める。
/// </para>
/// <para>
/// 以前の実装は、MLE で推定したモデルでも再推定を SSE で行い、失敗した反復を θ̂ で置き換え
/// （区間が不当に狭くなる）、区間を観測期間内の m(t) にしか付けていなかった。
/// また累積値の残差を入れ替える「残差リサンプリング」は、累積値が減少・非整数になり
/// Poisson 尤度と両立しないため廃止した。
/// </para>
/// </remarks>
public class ConfidenceIntervalService
{
    public ConfidenceBandResult Calculate(
        ReliabilityGrowthModelBase model,
        double[] estimate,
        ParametricBootstrapResult bootstrap,
        double[] times,
        double confidenceLevel = 0.95)
    {
        var replicates = bootstrap.Replicates;
        var warnings = new List<string>();
        if (replicates.Count == 0)
        {
            return new ConfidenceBandResult
            {
                ConfidenceLevel = confidenceLevel,
                Requested = bootstrap.Requested,
                Warnings = { "ブートストラップの再推定がすべて失敗したため、信頼区間を計算できません。" }
            };
        }
        if (bootstrap.SuccessRate < 0.8)
        {
            warnings.Add($"ブートストラップの再推定の成功が {bootstrap.Succeeded}/{bootstrap.Requested} 回と少なく、区間の精度が低い可能性があります。");
        }
        
        double qLow = (1 - confidenceLevel) / 2, qHigh = 1 - qLow;
        bool boundLimited = bootstrap.IsUpperLimitedByBound(qHigh);
        if (bootstrap.BoundWarning(qHigh) is { } boundWarning) warnings.Add(boundWarning);
        var lower = new double[times.Length];
        var upper = new double[times.Length];
        var upperLimited = new bool[times.Length];
        for (int i = 0; i < times.Length; i++)
        {
            var values = replicates.Select(p => model.Calculate(times[i], p)).ToList();
            var sorted = values.Where(double.IsFinite).OrderBy(v => v).ToList();
            lower[i] = ParametricBootstrap.Percentile(sorted, qLow);
            upper[i] = ParametricBootstrap.Percentile(sorted, qHigh);
            upperLimited[i] = boundLimited && PredictionIntervalService.AnyBoundReplicateInUpperTail(values, bootstrap.AtUpperBound, upper[i]);
        }
        
        var totals = replicates.Select(model.GetAsymptoticTotalBugs).Where(double.IsFinite).OrderBy(v => v).ToList();
        
        return new ConfidenceBandResult
        {
            ConfidenceLevel = confidenceLevel,
            Requested = bootstrap.Requested,
            Succeeded = replicates.Count,
            Times = times,
            Estimate = times.Select(t => model.Calculate(t, estimate)).ToArray(),
            Lower = lower,
            Upper = upper,
            UpperIsBoundLimited = upperLimited,
            TotalBugs = new IntervalEstimate(model.GetAsymptoticTotalBugs(estimate),
                ParametricBootstrap.Percentile(totals, qLow), ParametricBootstrap.Percentile(totals, qHigh), boundLimited),
            Milestones = PredictionIntervalService.MilestoneRatios
                .Select(ratio => PredictionIntervalService.CalculateMilestone(model, estimate, bootstrap, ratio, qLow, qHigh))
                .ToList(),
            Warnings = warnings
        };
    }
}

/// <summary>
/// Fisher情報行列に基づく解析的信頼区間計算サービス
/// </summary>
/// <remarks>
/// <para>
/// パラメータの漸近的な信頼区間を計算します。
/// ブートストラップ法より高速ですが、以下の仮定に依存します：
/// - 大標本近似（漸近正規性）
/// - モデルが正しく特定されている
/// - 観測値が独立
/// </para>
/// <para>
/// 小標本やモデル特定に不確実性がある場合は、
/// ブートストラップ法との比較を推奨します。
/// </para>
/// <para>
/// <strong>改善版（v2）</strong>
/// - NHPP（Poisson）仮定に基づく観測Fisher情報行列を追加
/// - プロファイル尤度に基づく信頼区間（より正確）
/// - 標準誤差の診断情報
/// </para>
/// </remarks>
public class FisherInformationService
{
    private readonly double _confidenceLevel;
    private readonly double _h; // 数値微分のステップサイズ
    
    /// <summary>
    /// コンストラクタ
    /// </summary>
    /// <param name="confidenceLevel">信頼水準（デフォルト: 0.95）</param>
    /// <param name="stepSize">数値微分のステップサイズ（デフォルト: 1e-5）</param>
    public FisherInformationService(double confidenceLevel = 0.95, double stepSize = 1e-5)
    {
        _confidenceLevel = confidenceLevel;
        _h = stepSize;
    }
    
    /// <summary>
    /// パラメータの漸近的標準誤差を計算（SSEベース）
    /// </summary>
    /// <param name="model">信頼度成長モデル</param>
    /// <param name="tData">時刻データ</param>
    /// <param name="yData">累積バグ数データ</param>
    /// <param name="parameters">推定パラメータ</param>
    /// <returns>各パラメータの標準誤差（計算失敗時はNaN）</returns>
    public double[] CalculateParameterStandardErrors(
        ReliabilityGrowthModelBase model,
        double[] tData,
        double[] yData,
        double[] parameters)
    {
        int n = tData.Length;
        int k = parameters.Length;
        
        // SSEベースの分散推定
        double sse = model.CalculateSSE(tData, yData, parameters);
        double sigma2 = sse / (n - k); // 残差分散
        
        // 数値ヘッセ行列（SSEの2階微分）の計算
        var hessian = CalculateHessian(
            p => model.CalculateSSE(tData, yData, p),
            parameters);
        
        // ヘッセ行列の逆行列 = 分散共分散行列の近似
        var covMatrix = InvertMatrix(hessian);
        
        if (covMatrix == null)
        {
            // 逆行列計算失敗
            return Enumerable.Repeat(double.NaN, k).ToArray();
        }
        
        // 対角成分から標準誤差を計算
        var standardErrors = new double[k];
        for (int i = 0; i < k; i++)
        {
            double variance = covMatrix[i, i] * sigma2 * 2; // SSEのヘッセ = 2 * I
            standardErrors[i] = variance > 0 ? Math.Sqrt(variance) : double.NaN;
        }
        
        return standardErrors;
    }
    
    /// <summary>
    /// NHPP（Poisson）仮定に基づくパラメータの標準誤差を計算
    /// </summary>
    /// <remarks>
    /// 観測Fisher情報行列を用いた標準誤差計算。
    /// Poisson-NHPP仮定が満たされる場合、SSEベースより正確。
    /// </remarks>
    /// <param name="fixedParameters">
    /// 固定して扱う（分散 0 とする）パラメータ。変化点 τ のように尤度が微分できないパラメータに使う
    /// （τ を固定した条件付きの共分散になるので、τ の不確実性は含まない）。null なら固定しない
    /// </param>
    public FisherInformationResult CalculateNHPPStandardErrors(
        ReliabilityGrowthModelBase model,
        double[] tData,
        double[] yData,
        double[] parameters,
        bool[]? fixedParameters = null)
    {
        int n = tData.Length;
        int k = parameters.Length;
        var free = Enumerable.Range(0, k).Where(i => fixedParameters == null || !fixedParameters[i]).ToArray();
        
        var result = new FisherInformationResult
        {
            ParameterNames = model.ParameterNames,
            Parameters = (double[])parameters.Clone(),
            FixedParameters = Enumerable.Range(0, k).Select(i => fixedParameters != null && fixedParameters[i]).ToArray()
        };
        
        try
        {
            // Fisher 情報行列はモデルが指定する座標 q で求める（ln ψ のように平らになりやすい座標を避ける）
            var q0 = model.ToFisherScale(parameters);
            
            // 負の対数尤度関数（Poisson-NHPP。固定しないパラメータだけの関数）
            Func<double[], double> negLogLik = qFree =>
            {
                var q = (double[])q0.Clone();
                for (int j = 0; j < free.Length; j++) q[free[j]] = qFree[j];
                var p = model.FromFisherScale(q);
                double logL = 0;
                double prevM = 0;
                
                for (int i = 0; i < n; i++)
                {
                    double mt = model.Calculate(tData[i], p);
                    double lambda = Math.Max(1e-10, mt - prevM);
                    double dailyBugs = (i == 0) ? yData[0] : yData[i] - yData[i - 1];
                    
                    // Poisson対数尤度: y*log(λ) - λ - log(y!)
                    logL += dailyBugs * Math.Log(lambda) - lambda;
                    prevM = mt;
                }
                
                return -logL; // 負の対数尤度を返す
            };
            
            // 観測Fisher情報行列（負の対数尤度のヘッセ行列、座標 q の固定しないパラメータ）
            var observedFisher = CalculateHessian(negLogLik, free.Select(i => q0[i]).ToArray());
            result.ObservedFisherMatrix = observedFisher;
            
            // 逆行列 = 分散共分散行列（固定したパラメータの行・列は 0）
            var covFree = InvertMatrix(observedFisher);
            double[,]? covQ = null;
            if (covFree != null)
            {
                covQ = new double[k, k];
                for (int a = 0; a < free.Length; a++)
                    for (int b = 0; b < free.Length; b++)
                        covQ[free[a], free[b]] = covFree[a, b];
            }
            
            if (covQ == null)
            {
                result.Success = false;
                result.ErrorMessage = "Fisher情報行列が特異または条件数が大きすぎます";
                result.StandardErrors = Enumerable.Repeat(double.NaN, k).ToArray();
                return result;
            }
            
            // 推定に使う座標 θ の共分散に戻す: Cov_θ = J Cov_q Jᵀ（J = ∂θ/∂q）
            var covMatrix = TransformCovariance(covQ, model.FromFisherScale, q0);
            
            result.CovarianceMatrix = covMatrix;
            
            // 標準誤差（固定したパラメータは NaN）
            var se = new double[k];
            for (int i = 0; i < k; i++)
            {
                se[i] = covMatrix[i, i] > 0 && free.Contains(i) ? Math.Sqrt(covMatrix[i, i]) : double.NaN;
            }
            result.StandardErrors = se;
            
            // 信頼区間
            double alpha = 1 - _confidenceLevel;
            double z = MathNet.Numerics.Distributions.Normal.InvCDF(0, 1, 1 - alpha / 2);
            
            result.LowerBounds = new double[k];
            result.UpperBounds = new double[k];
            
            for (int i = 0; i < k; i++)
            {
                if (!double.IsNaN(se[i]))
                {
                    result.LowerBounds[i] = parameters[i] - z * se[i];
                    result.UpperBounds[i] = parameters[i] + z * se[i];
                }
                else
                {
                    result.LowerBounds[i] = double.NaN;
                    result.UpperBounds[i] = double.NaN;
                }
            }
            
            // 相関行列
            result.CorrelationMatrix = new double[k, k];
            for (int i = 0; i < k; i++)
            {
                for (int j = 0; j < k; j++)
                {
                    if (se[i] > 0 && se[j] > 0)
                    {
                        result.CorrelationMatrix[i, j] = covMatrix[i, j] / (se[i] * se[j]);
                    }
                }
            }
            
            result.Success = true;
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.ErrorMessage = ex.Message;
            result.StandardErrors = Enumerable.Repeat(double.NaN, k).ToArray();
        }
        
        return result;
    }
    
    /// <summary>
    /// 発見数だけの Poisson-NHPP 尤度の Fisher 情報行列で固定するパラメータのマスク
    /// </summary>
    /// <remarks>
    /// 変化点 τ（名前が τ で始まる。尤度が τ について微分できない）と、発見数に効かないパラメータ
    /// （FRE モデルの η・D など。曲率が 0 でヘッセ行列が特異になる）を固定する。
    /// </remarks>
    public static bool[] DetectionLikelihoodMask(ReliabilityGrowthModelBase model)
        => model.ParameterNames.Select((name, i) => name.StartsWith("τ") || !model.IsDetectionParameter(i)).ToArray();
    
    /// <summary>
    /// パラメータの関数 g(θ) の漸近信頼区間（デルタ法）
    /// </summary>
    /// <param name="g">パラメータの関数（例: 推定潜在バグ総数 m(∞)）</param>
    /// <param name="parameters">推定値</param>
    /// <param name="covariance">パラメータの分散共分散行列（Fisher 情報行列の逆行列）</param>
    /// <param name="logScale">
    /// true なら ln g の区間を求めて指数変換する（正の量で下限が負にならず、右に裾の長い分布に合う）
    /// </param>
    public DerivedQuantityInterval CalculateDerivedInterval(
        Func<double[], double> g, double[] parameters, double[,] covariance, bool logScale = true)
    {
        double estimate = g(parameters);
        Func<double[], double> h = logScale ? p => Math.Log(g(p)) : g;
        var gradient = CalculateGradient(h, parameters);
        
        int k = parameters.Length;
        double variance = 0;
        for (int i = 0; i < k; i++)
            for (int j = 0; j < k; j++)
                variance += gradient[i] * covariance[i, j] * gradient[j];
        
        if (!(variance >= 0) || !double.IsFinite(variance) || (logScale && !(estimate > 0)))
        {
            return new DerivedQuantityInterval(estimate, double.NaN, double.NaN, double.NaN, _confidenceLevel, logScale);
        }
        
        double se = Math.Sqrt(variance);
        double z = MathNet.Numerics.Distributions.Normal.InvCDF(0, 1, 1 - (1 - _confidenceLevel) / 2);
        double center = h(parameters);
        double lower = center - z * se, upper = center + z * se;
        return logScale
            ? new DerivedQuantityInterval(estimate, se * estimate, Math.Exp(lower), Math.Exp(upper), _confidenceLevel, true)
            : new DerivedQuantityInterval(estimate, se, lower, upper, _confidenceLevel, false);
    }

    /// <summary>
    /// パラメータの信頼区間を計算
    /// </summary>
    public (double[] lower, double[] upper) CalculateParameterConfidenceIntervals(
        ReliabilityGrowthModelBase model,
        double[] tData,
        double[] yData,
        double[] parameters)
    {
        int k = parameters.Length;
        var se = CalculateParameterStandardErrors(model, tData, yData, parameters);
        
        // 正規分布の分位点（大標本近似）
        double alpha = 1 - _confidenceLevel;
        double z = MathNet.Numerics.Distributions.Normal.InvCDF(0, 1, 1 - alpha / 2);
        
        var lower = new double[k];
        var upper = new double[k];
        
        for (int i = 0; i < k; i++)
        {
            if (double.IsNaN(se[i]))
            {
                lower[i] = double.NaN;
                upper[i] = double.NaN;
            }
            else
            {
                lower[i] = parameters[i] - z * se[i];
                upper[i] = parameters[i] + z * se[i];
            }
        }
        
        return (lower, upper);
    }
    
    /// <summary>
    /// 予測値の信頼区間を計算（デルタ法）
    /// </summary>
    public (double[] lower, double[] upper) CalculatePredictionConfidenceIntervals(
        ReliabilityGrowthModelBase model,
        double[] tData,
        double[] yData,
        double[] parameters,
        double[] predictionTimes)
    {
        int n = tData.Length;
        int k = parameters.Length;
        int nPred = predictionTimes.Length;
        
        // 残差分散
        double sse = model.CalculateSSE(tData, yData, parameters);
        double sigma2 = sse / (n - k);
        
        // ヘッセ行列の逆行列
        var hessian = CalculateHessian(
            p => model.CalculateSSE(tData, yData, p),
            parameters);
        var covMatrix = InvertMatrix(hessian);
        
        if (covMatrix == null)
        {
            return (
                Enumerable.Repeat(double.NaN, nPred).ToArray(),
                Enumerable.Repeat(double.NaN, nPred).ToArray()
            );
        }
        
        // 分位点
        double alpha = 1 - _confidenceLevel;
        double z = MathNet.Numerics.Distributions.Normal.InvCDF(0, 1, 1 - alpha / 2);
        
        var lower = new double[nPred];
        var upper = new double[nPred];
        
        for (int t = 0; t < nPred; t++)
        {
            double time = predictionTimes[t];
            
            // 勾配ベクトル ∂m/∂θ
            var gradient = CalculateGradient(
                p => model.Calculate(time, p),
                parameters);
            
            // 予測分散 = σ² * g' * (H⁻¹) * g
            double variance = 0;
            for (int i = 0; i < k; i++)
            {
                for (int j = 0; j < k; j++)
                {
                    variance += gradient[i] * covMatrix[i, j] * gradient[j] * sigma2 * 2;
                }
            }
            
            double se = variance > 0 ? Math.Sqrt(variance) : 0;
            double pred = model.Calculate(time, parameters);
            
            lower[t] = pred - z * se;
            upper[t] = pred + z * se;
        }
        
        return (lower, upper);
    }
    
    /// <summary>
    /// 数値微分による勾配計算
    /// </summary>
    private double[] CalculateGradient(Func<double[], double> f, double[] x)
    {
        int n = x.Length;
        var grad = new double[n];
        
        for (int i = 0; i < n; i++)
        {
            var xPlus = (double[])x.Clone();
            var xMinus = (double[])x.Clone();
            
            double step = Math.Max(_h, Math.Abs(x[i]) * _h);
            xPlus[i] += step;
            xMinus[i] -= step;
            
            grad[i] = (f(xPlus) - f(xMinus)) / (2 * step);
        }
        
        return grad;
    }
    
    /// <summary>
    /// 座標 q の共分散を θ = f(q) の共分散に変換する（数値ヤコビアン。恒等変換ならそのまま）
    /// </summary>
    private double[,] TransformCovariance(double[,] covQ, Func<double[], double[]> f, double[] q0)
    {
        int k = q0.Length;
        var theta0 = f(q0);
        var jacobian = new double[k, k];
        bool identity = true;
        for (int j = 0; j < k; j++)
        {
            double step = Math.Max(_h, Math.Abs(q0[j]) * _h);
            // 下側が 0 以下になる座標（ψ など）は前進差分にする
            double lowerStep = q0[j] - step > 0 || q0[j] <= 0 ? step : 0;
            var qp = (double[])q0.Clone(); qp[j] += step;
            var qm = (double[])q0.Clone(); qm[j] -= lowerStep;
            var tp = f(qp);
            var tm = lowerStep > 0 ? f(qm) : theta0;
            for (int i = 0; i < k; i++)
            {
                jacobian[i, j] = (tp[i] - tm[i]) / (step + lowerStep);
                if (Math.Abs(jacobian[i, j] - (i == j ? 1.0 : 0.0)) > 1e-6) identity = false;
            }
        }
        if (identity) return covQ;
        
        var cov = new double[k, k];
        for (int a = 0; a < k; a++)
            for (int b = 0; b < k; b++)
            {
                double sum = 0;
                for (int i = 0; i < k; i++)
                    for (int j = 0; j < k; j++)
                        sum += jacobian[a, i] * covQ[i, j] * jacobian[b, j];
                cov[a, b] = sum;
            }
        return cov;
    }
    
    /// <summary>
    /// 数値微分によるヘッセ行列計算
    /// </summary>
    private double[,] CalculateHessian(Func<double[], double> f, double[] x)
    {
        int n = x.Length;
        var hessian = new double[n, n];
        
        for (int i = 0; i < n; i++)
        {
            for (int j = i; j < n; j++)
            {
                double step_i = Math.Max(_h, Math.Abs(x[i]) * _h);
                double step_j = Math.Max(_h, Math.Abs(x[j]) * _h);
                
                var x_pp = (double[])x.Clone(); x_pp[i] += step_i; x_pp[j] += step_j;
                var x_pm = (double[])x.Clone(); x_pm[i] += step_i; x_pm[j] -= step_j;
                var x_mp = (double[])x.Clone(); x_mp[i] -= step_i; x_mp[j] += step_j;
                var x_mm = (double[])x.Clone(); x_mm[i] -= step_i; x_mm[j] -= step_j;
                
                double d2f = (f(x_pp) - f(x_pm) - f(x_mp) + f(x_mm)) / (4 * step_i * step_j);
                
                hessian[i, j] = d2f;
                hessian[j, i] = d2f; // 対称行列
            }
        }
        
        return hessian;
    }
    
    /// <summary>
    /// 行列の逆行列を計算（LU分解）
    /// </summary>
    private static double[,]? InvertMatrix(double[,] matrix)
    {
        try
        {
            int n = matrix.GetLength(0);
            var m = MathNet.Numerics.LinearAlgebra.Matrix<double>.Build.DenseOfArray(matrix);
            
            // 条件数チェック（特異に近い場合は失敗）
            var svd = m.Svd();
            double conditionNumber = svd.S[0] / svd.S[n - 1];
            if (conditionNumber > 1e10 || double.IsNaN(conditionNumber))
            {
                return null;
            }
            
            var inv = m.Inverse();
            return inv.ToArray();
        }
        catch
        {
            return null;
        }
    }
}

/// <summary>
/// デルタ法による派生量の信頼区間
/// </summary>
/// <param name="Estimate">推定値 g(θ̂)</param>
/// <param name="StandardError">標準誤差（対数スケールの場合は g(θ̂)·SE[ln g] で近似）</param>
/// <param name="Lower">下限</param>
/// <param name="Upper">上限</param>
/// <param name="ConfidenceLevel">信頼水準</param>
/// <param name="LogScale">対数スケールで計算したか</param>
public sealed record DerivedQuantityInterval(
    double Estimate, double StandardError, double Lower, double Upper, double ConfidenceLevel, bool LogScale)
{
    public bool IsValid => double.IsFinite(Lower) && double.IsFinite(Upper);
}

/// <summary>
/// Fisher情報行列による信頼区間計算結果
/// </summary>
public class FisherInformationResult
{
    /// <summary>計算成功フラグ</summary>
    public bool Success { get; set; }
    
    /// <summary>エラーメッセージ</summary>
    public string? ErrorMessage { get; set; }
    
    /// <summary>パラメータ名</summary>
    public string[] ParameterNames { get; set; } = Array.Empty<string>();
    
    /// <summary>推定パラメータ値</summary>
    public double[] Parameters { get; set; } = Array.Empty<double>();
    
    /// <summary>固定して計算したパラメータ（変化点 τ など。標準誤差・区間は求めない）</summary>
    public bool[] FixedParameters { get; set; } = Array.Empty<bool>();
    
    /// <summary>パラメータの標準誤差</summary>
    public double[] StandardErrors { get; set; } = Array.Empty<double>();
    
    /// <summary>信頼区間下限</summary>
    public double[] LowerBounds { get; set; } = Array.Empty<double>();
    
    /// <summary>信頼区間上限</summary>
    public double[] UpperBounds { get; set; } = Array.Empty<double>();
    
    /// <summary>観測Fisher情報行列</summary>
    public double[,]? ObservedFisherMatrix { get; set; }
    
    /// <summary>分散共分散行列</summary>
    public double[,]? CovarianceMatrix { get; set; }
    
    /// <summary>相関行列</summary>
    public double[,]? CorrelationMatrix { get; set; }
    
    /// <summary>
    /// 結果を文字列で表示
    /// </summary>
    public override string ToString()
    {
        if (!Success)
            return $"計算失敗: {ErrorMessage}";
        
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("=== Fisher情報行列による解析的信頼区間 ===");
        sb.AppendLine($"{"パラメータ",-15} {"推定値",-12} {"標準誤差",-12} {"95%CI下限",-12} {"95%CI上限",-12}");
        sb.AppendLine(new string('-', 65));
        
        for (int i = 0; i < ParameterNames.Length; i++)
        {
            string name = ParameterNames[i];
            string value = double.IsNaN(Parameters[i]) ? "N/A" : Parameters[i].ToString("G4");
            string se = double.IsNaN(StandardErrors[i]) ? "N/A" : StandardErrors[i].ToString("G4");
            string lower = double.IsNaN(LowerBounds[i]) ? "N/A" : LowerBounds[i].ToString("G4");
            string upper = double.IsNaN(UpperBounds[i]) ? "N/A" : UpperBounds[i].ToString("G4");
            
            sb.AppendLine($"{name,-15} {value,-12} {se,-12} {lower,-12} {upper,-12}");
        }
        
        return sb.ToString();
    }
}
