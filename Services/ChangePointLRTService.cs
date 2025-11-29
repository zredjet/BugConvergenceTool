using BugConvergenceTool.Models;
using BugConvergenceTool.Optimizers;

namespace BugConvergenceTool.Services;

/// <summary>
/// 変化点の存在検定（尤度比検定: Likelihood Ratio Test）
/// </summary>
/// <remarks>
/// <para>
/// 変化点モデルと変化点なしモデルを比較し、変化点の存在を統計的に検定します。
/// </para>
/// <para>
/// 帰無仮説 H0: 変化点なし（単純モデル）
/// 対立仮説 H1: 変化点あり（変化点モデル）
/// </para>
/// <para>
/// 検定統計量: LR = -2 * (ln(L_0) - ln(L_1)) = -2 * ln(L_0 / L_1)
/// 漸近的に χ²(df) 分布に従う。df = 変化点モデルのパラメータ数 - 単純モデルのパラメータ数
/// </para>
/// <para>
/// <strong>注意:</strong>
/// 変化点の位置τが探索範囲内のどこでも取り得る場合、標準的なχ²分布は適用できません。
/// これは「境界上のパラメータ」問題として知られ、Davies (1987) のアプローチや
/// シミュレーションベースのp値計算が必要です。本実装ではシミュレーション法を使用します。
/// </para>
/// </remarks>
public class ChangePointLRTService
{
    private readonly OptimizerType _optimizerType;
    private readonly LossType _lossType;
    private readonly bool _verbose;
    private readonly int _simulationIterations;

    /// <summary>
    /// コンストラクタ
    /// </summary>
    /// <param name="optimizerType">最適化アルゴリズム</param>
    /// <param name="lossType">損失関数タイプ</param>
    /// <param name="verbose">詳細出力</param>
    /// <param name="simulationIterations">p値シミュレーション反復回数（デフォルト: 500）</param>
    public ChangePointLRTService(
        OptimizerType optimizerType = OptimizerType.NelderMead,
        LossType lossType = LossType.Mle,
        bool verbose = false,
        int simulationIterations = 500)
    {
        _optimizerType = optimizerType;
        _lossType = lossType;
        _verbose = verbose;
        _simulationIterations = simulationIterations;
    }

    /// <summary>
    /// 変化点の存在を検定
    /// </summary>
    /// <param name="tData">時刻データ</param>
    /// <param name="yData">累積バグ数データ</param>
    /// <param name="nullModel">帰無仮説モデル（変化点なし）</param>
    /// <param name="alternativeModel">対立仮説モデル（変化点あり）</param>
    /// <param name="nullParams">帰無モデルの推定パラメータ</param>
    /// <param name="altParams">対立モデルの推定パラメータ</param>
    /// <returns>検定結果</returns>
    public ChangePointLRTResult Test(
        double[] tData,
        double[] yData,
        ReliabilityGrowthModelBase nullModel,
        ChangePointModelBase alternativeModel,
        double[] nullParams,
        double[] altParams)
    {
        var result = new ChangePointLRTResult();
        
        try
        {
            int n = tData.Length;
            
            // 損失関数を取得
            var lossFunction = LossFunctionFactory.Create(_lossType);
            
            // 対数尤度を計算（MLE使用時）または擬似尤度（SSE使用時）
            double logL0, logL1;
            
            if (_lossType == LossType.Mle)
            {
                logL0 = lossFunction.CalculateLogLikelihood(tData, yData, nullModel, nullParams);
                logL1 = lossFunction.CalculateLogLikelihood(tData, yData, alternativeModel, altParams);
            }
            else
            {
                // SSEの場合、正規仮定での擬似対数尤度
                // ln(L) ≈ -n/2 * ln(SSE/n)
                double sse0 = nullModel.CalculateSSE(tData, yData, nullParams);
                double sse1 = alternativeModel.CalculateSSE(tData, yData, altParams);
                logL0 = -n / 2.0 * Math.Log(sse0 / n);
                logL1 = -n / 2.0 * Math.Log(sse1 / n);
            }
            
            // 尤度比統計量
            double lrStatistic = -2.0 * (logL0 - logL1);
            result.LRStatistic = lrStatistic;
            result.LogLikelihoodNull = logL0;
            result.LogLikelihoodAlternative = logL1;
            
            // 自由度（パラメータ数の差）
            int df = altParams.Length - nullParams.Length;
            result.DegreesOfFreedom = df;
            
            // 標準的なχ²近似p値（参考値）
            if (df > 0 && lrStatistic >= 0)
            {
                result.ChiSquarePValue = 1.0 - MathNet.Numerics.Distributions.ChiSquared.CDF(df, lrStatistic);
            }
            else
            {
                result.ChiSquarePValue = 1.0;
            }
            
            // シミュレーションベースのp値（変化点問題では必須）
            result.SimulatedPValue = CalculateSimulatedPValue(
                tData, yData, nullModel, alternativeModel, nullParams, lrStatistic);
            
            // 判定
            result.IsChangePointSignificant = result.SimulatedPValue < 0.05;
            
            // メッセージ生成
            result.Interpretation = GenerateInterpretation(result);
            result.Success = true;
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.ErrorMessage = ex.Message;
        }
        
        return result;
    }

    /// <summary>
    /// シミュレーションによるp値計算
    /// </summary>
    /// <remarks>
    /// 帰無仮説（変化点なし）の下でデータを再生成し、
    /// 尤度比統計量の分布をシミュレーションで求めます。
    /// </remarks>
    private double CalculateSimulatedPValue(
        double[] tData,
        double[] yData,
        ReliabilityGrowthModelBase nullModel,
        ChangePointModelBase alternativeModel,
        double[] nullParams,
        double observedLR)
    {
        int n = tData.Length;
        int exceedCount = 0;
        var random = new Random();
        
        // 帰無仮説の下での日次期待値を計算
        var dailyExpected = new double[n];
        dailyExpected[0] = nullModel.Calculate(tData[0], nullParams);
        for (int i = 1; i < n; i++)
        {
            double prevM = nullModel.Calculate(tData[i - 1], nullParams);
            double currM = nullModel.Calculate(tData[i], nullParams);
            dailyExpected[i] = Math.Max(0.01, currM - prevM); // 最小値を設定
        }
        
        if (_verbose)
        {
            Console.WriteLine($"  変化点LRT: シミュレーション {_simulationIterations} 回...");
        }
        
        var lossFunction = LossFunctionFactory.Create(_lossType);
        var optimizer = OptimizerFactory.Create(_optimizerType);
        
        for (int iter = 0; iter < _simulationIterations; iter++)
        {
            try
            {
                // 帰無仮説の下でデータを再生成（Poisson）
                var simDaily = new double[n];
                for (int i = 0; i < n; i++)
                {
                    simDaily[i] = SamplePoisson(random, dailyExpected[i]);
                }
                
                // 累積化
                var simY = new double[n];
                simY[0] = simDaily[0];
                for (int i = 1; i < n; i++)
                {
                    simY[i] = simY[i - 1] + simDaily[i];
                }
                
                // 帰無モデルを再フィット
                var (lower0, upper0) = nullModel.GetBounds(tData, simY);
                var initial0 = nullModel.GetInitialParameters(tData, simY);
                var result0 = optimizer.Optimize(
                    p => lossFunction.Evaluate(tData, simY, nullModel, p),
                    lower0, upper0, initial0);
                
                if (!result0.Success) continue;
                
                // 対立モデル（変化点あり）を再フィット
                var (lower1, upper1) = alternativeModel.GetBounds(tData, simY);
                var initial1 = alternativeModel.GetInitialParameters(tData, simY);
                var result1 = optimizer.Optimize(
                    p => lossFunction.Evaluate(tData, simY, alternativeModel, p),
                    lower1, upper1, initial1);
                
                if (!result1.Success) continue;
                
                // LR統計量を計算
                double logL0Sim, logL1Sim;
                if (_lossType == LossType.Mle)
                {
                    logL0Sim = lossFunction.CalculateLogLikelihood(tData, simY, nullModel, result0.Parameters);
                    logL1Sim = lossFunction.CalculateLogLikelihood(tData, simY, alternativeModel, result1.Parameters);
                }
                else
                {
                    double sse0Sim = nullModel.CalculateSSE(tData, simY, result0.Parameters);
                    double sse1Sim = alternativeModel.CalculateSSE(tData, simY, result1.Parameters);
                    logL0Sim = -n / 2.0 * Math.Log(sse0Sim / n);
                    logL1Sim = -n / 2.0 * Math.Log(sse1Sim / n);
                }
                
                double lrSim = -2.0 * (logL0Sim - logL1Sim);
                
                if (lrSim >= observedLR)
                {
                    exceedCount++;
                }
            }
            catch
            {
                // シミュレーション失敗は無視
            }
        }
        
        // p値 = 観測値以上の統計量が出現した割合
        double pValue = (double)(exceedCount + 1) / (_simulationIterations + 1); // +1 は保守的調整
        
        if (_verbose)
        {
            Console.WriteLine($"    シミュレーション完了: 観測LR={observedLR:F2}, 超過回数={exceedCount}, p={pValue:F4}");
        }
        
        return pValue;
    }
    
    /// <summary>
    /// Poisson分布からサンプリング
    /// </summary>
    private static int SamplePoisson(Random random, double lambda)
    {
        if (lambda <= 0) return 0;
        
        if (lambda <= 30)
        {
            double L = Math.Exp(-lambda);
            int k = 0;
            double p = 1.0;
            
            do
            {
                k++;
                p *= random.NextDouble();
            } while (p > L);
            
            return k - 1;
        }
        else
        {
            // 正規近似
            double u1 = random.NextDouble();
            double u2 = random.NextDouble();
            double z = Math.Sqrt(-2 * Math.Log(u1)) * Math.Cos(2 * Math.PI * u2);
            int result = (int)Math.Round(lambda + Math.Sqrt(lambda) * z);
            return Math.Max(0, result);
        }
    }
    
    /// <summary>
    /// 検定結果の解釈を生成
    /// </summary>
    private static string GenerateInterpretation(ChangePointLRTResult result)
    {
        if (!result.Success)
            return $"検定に失敗: {result.ErrorMessage}";
        
        var sb = new System.Text.StringBuilder();
        
        sb.Append($"尤度比統計量 LR = {result.LRStatistic:F2}");
        sb.Append($" (df={result.DegreesOfFreedom})");
        sb.AppendLine();
        
        sb.Append($"シミュレーションp値 = {result.SimulatedPValue:F4}");
        sb.Append($" (χ²近似p値 = {result.ChiSquarePValue:F4})");
        sb.AppendLine();
        
        if (result.SimulatedPValue < 0.01)
        {
            sb.Append("判定: 変化点が高度に有意 (p < 0.01)。変化点モデルを推奨。");
        }
        else if (result.SimulatedPValue < 0.05)
        {
            sb.Append("判定: 変化点が有意 (p < 0.05)。変化点モデルを検討。");
        }
        else if (result.SimulatedPValue < 0.10)
        {
            sb.Append("判定: 変化点が弱く示唆される (p < 0.10)。追加データで再検討を推奨。");
        }
        else
        {
            sb.Append("判定: 変化点の証拠なし (p ≥ 0.10)。単純モデルを推奨。");
        }
        
        return sb.ToString();
    }
}

/// <summary>
/// 変化点尤度比検定の結果
/// </summary>
public class ChangePointLRTResult
{
    /// <summary>検定成功フラグ</summary>
    public bool Success { get; set; }
    
    /// <summary>エラーメッセージ</summary>
    public string? ErrorMessage { get; set; }
    
    /// <summary>尤度比統計量 LR = -2 * ln(L0/L1)</summary>
    public double LRStatistic { get; set; }
    
    /// <summary>帰無モデルの対数尤度</summary>
    public double LogLikelihoodNull { get; set; }
    
    /// <summary>対立モデルの対数尤度</summary>
    public double LogLikelihoodAlternative { get; set; }
    
    /// <summary>自由度（パラメータ数の差）</summary>
    public int DegreesOfFreedom { get; set; }
    
    /// <summary>χ²分布に基づくp値（参考値）</summary>
    public double ChiSquarePValue { get; set; }
    
    /// <summary>シミュレーションベースのp値（推奨）</summary>
    public double SimulatedPValue { get; set; }
    
    /// <summary>変化点が統計的に有意か（p < 0.05）</summary>
    public bool IsChangePointSignificant { get; set; }
    
    /// <summary>検定結果の解釈</summary>
    public string Interpretation { get; set; } = "";
}
