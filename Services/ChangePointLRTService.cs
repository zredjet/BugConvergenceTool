using BugConvergenceTool.Models;
using MathNet.Numerics.Distributions;

namespace BugConvergenceTool.Services;

/// <summary>
/// 変化点の存在検定（尤度比検定: Likelihood Ratio Test）
/// </summary>
/// <remarks>
/// <para>
/// 変化点モデルと変化点なしモデル（b₁ = b₂ の場合に一致する帰無モデル）を比較し、変化点の存在を検定する。
/// 帰無仮説 H0: 変化点なし、対立仮説 H1: 変化点あり。
/// 検定統計量: LR = -2 (ln L₀ - ln L₁)（Poisson-NHPP の対数尤度）。
/// </para>
/// <para>
/// 変化点 τ は帰無仮説の下では識別できない局外パラメータ（Davies 1987）のため、LR は χ² 分布に従わない。
/// 本実装は Davies の解析的な近似ではなく、帰無モデルの推定値から日次発見数を Poisson 再生成し、
/// 両モデルを推定し直して LR の帰無分布を求めるパラメトリック・ブートストラップで p 値を計算する。
/// </para>
/// <para>
/// 観測データとシミュレーションデータで同じ推定手順を使わないと LR の分布がずれるため、
/// 推定手順（再推定関数）は呼び出し側から受け取る。
/// </para>
/// </remarks>
public class ChangePointLRTService
{
    private readonly int _simulationIterations;
    private readonly int? _seed;
    private readonly bool _verbose;
    private readonly double _significanceLevel;

    /// <param name="simulationIterations">p値シミュレーション反復回数</param>
    /// <param name="seed">乱数シード（null なら毎回異なる）</param>
    /// <param name="verbose">詳細出力</param>
    /// <param name="significanceLevel">有意水準</param>
    public ChangePointLRTService(
        int simulationIterations = 99,
        int? seed = null,
        bool verbose = false,
        double significanceLevel = 0.05)
    {
        _simulationIterations = simulationIterations;
        _seed = seed;
        _verbose = verbose;
        _significanceLevel = significanceLevel;
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
    /// <param name="refitNull">累積データを受け取り帰無モデルを推定し直す関数（失敗時 null）</param>
    /// <param name="refitAlternative">累積データを受け取り対立モデルを推定し直す関数（失敗時 null）</param>
    public ChangePointLRTResult Test(
        double[] tData,
        double[] yData,
        ReliabilityGrowthModelBase nullModel,
        ReliabilityGrowthModelBase alternativeModel,
        double[] nullParams,
        double[] altParams,
        Func<double[], double[]?> refitNull,
        Func<double[], double[]?> refitAlternative)
    {
        var result = new ChangePointLRTResult
        {
            NullModelName = nullModel.Name,
            RequestedSimulations = _simulationIterations,
            SignificanceLevel = _significanceLevel
        };

        try
        {
            double lrStatistic = CalculateLR(tData, yData, nullModel, alternativeModel, nullParams, altParams);
            result.LRStatistic = lrStatistic;
            result.DegreesOfFreedom = altParams.Length - nullParams.Length;

            // 標準的なχ²近似p値（参考値。変化点問題では正しくない）
            result.ChiSquarePValue = result.DegreesOfFreedom > 0 && lrStatistic >= 0
                ? 1.0 - ChiSquared.CDF(result.DegreesOfFreedom, lrStatistic)
                : 1.0;

            var (exceed, valid) = SimulateNullDistribution(
                tData, nullModel, alternativeModel, nullParams, lrStatistic, refitNull, refitAlternative);
            result.ValidSimulations = valid;

            if (valid == 0)
            {
                result.Success = false;
                result.ErrorMessage = "シミュレーションでの再推定がすべて失敗しました";
                return result;
            }

            // p 値 = (観測値以上の統計量の数 + 1) / (有効なシミュレーション数 + 1)
            // 失敗したシミュレーションを「超過しなかった」と数えると p 値が小さく偏るため、分母は有効数とする
            result.SimulatedPValue = (exceed + 1.0) / (valid + 1.0);
            result.IsChangePointSignificant = result.SimulatedPValue < _significanceLevel;

            if (valid < _simulationIterations / 2)
            {
                result.Warning = $"シミュレーションの再推定の成功が {valid}/{_simulationIterations} 回と少なく、p 値の精度が低い可能性があります。";
            }

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
    /// Poisson-NHPP 対数尤度による尤度比統計量
    /// </summary>
    private static double CalculateLR(
        double[] tData, double[] yData,
        ReliabilityGrowthModelBase nullModel, ReliabilityGrowthModelBase alternativeModel,
        double[] nullParams, double[] altParams)
    {
        var mle = LossFunctionFactory.Create(LossType.Mle);
        double logL0 = mle.CalculateLogLikelihood(tData, yData, nullModel, nullParams);
        double logL1 = mle.CalculateLogLikelihood(tData, yData, alternativeModel, altParams);
        return -2.0 * (logL0 - logL1);
    }

    /// <summary>
    /// 帰無モデルからデータを再生成し、LR の帰無分布を求める
    /// </summary>
    /// <returns>(観測 LR 以上になった回数, 再推定に成功した回数)</returns>
    private (int exceed, int valid) SimulateNullDistribution(
        double[] tData,
        ReliabilityGrowthModelBase nullModel,
        ReliabilityGrowthModelBase alternativeModel,
        double[] nullParams,
        double observedLR,
        Func<double[], double[]?> refitNull,
        Func<double[], double[]?> refitAlternative)
    {
        int exceed = 0, valid = 0;
        int baseSeed = _seed ?? Random.Shared.Next();

        if (_verbose)
        {
            Console.WriteLine($"  変化点LRT（{alternativeModel.Name}）: シミュレーション {_simulationIterations} 回...");
        }

        Parallel.For(0, _simulationIterations, iter =>
        {
            try
            {
                var random = new Random(unchecked(baseSeed + iter * 7919));
                var simY = ParametricBootstrap.SimulateCumulative(nullModel, tData, nullParams, random);

                var p0 = refitNull(simY);
                var p1 = refitAlternative(simY);
                if (p0 == null || p1 == null) return;

                double lrSim = CalculateLR(tData, simY, nullModel, alternativeModel, p0, p1);
                if (!double.IsFinite(lrSim)) return;

                Interlocked.Increment(ref valid);
                if (lrSim >= observedLR)
                {
                    Interlocked.Increment(ref exceed);
                }
            }
            catch
            {
                // 失敗したシミュレーションは有効数に含めない
            }
        });

        if (_verbose)
        {
            Console.WriteLine($"    観測LR={observedLR:F2}, 超過={exceed}/{valid}（有効）");
        }

        return (exceed, valid);
    }

    private static string GenerateInterpretation(ChangePointLRTResult result)
    {
        var sb = new System.Text.StringBuilder();
        sb.Append($"LR = {result.LRStatistic:F2} (df={result.DegreesOfFreedom}), ");
        sb.Append($"シミュレーションp値 = {result.SimulatedPValue:F3}（{result.ValidSimulations}回）");
        sb.Append(result.IsChangePointSignificant
            ? $" → 有意水準{result.SignificanceLevel:P0}で変化点ありと判断"
            : $" → 変化点の証拠は不十分（変化点なしの {result.NullModelName} で十分）");
        return sb.ToString();
    }
}

/// <summary>
/// 変化点尤度比検定の結果
/// </summary>
public class ChangePointLRTResult
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }

    /// <summary>帰無モデル（変化点なし）の名前</summary>
    public string NullModelName { get; set; } = "";

    /// <summary>尤度比統計量 LR = -2(ln L₀ - ln L₁)</summary>
    public double LRStatistic { get; set; }

    /// <summary>パラメータ数の差</summary>
    public int DegreesOfFreedom { get; set; }

    /// <summary>χ² 近似の p 値（参考値。変化点問題では正しくない）</summary>
    public double ChiSquarePValue { get; set; }

    /// <summary>パラメトリック・ブートストラップによる p 値</summary>
    public double SimulatedPValue { get; set; } = 1.0;

    /// <summary>要求したシミュレーション回数</summary>
    public int RequestedSimulations { get; set; }

    /// <summary>再推定に成功したシミュレーション回数（p 値の分母）</summary>
    public int ValidSimulations { get; set; }

    /// <summary>有意水準</summary>
    public double SignificanceLevel { get; set; } = 0.05;

    /// <summary>変化点が有意か</summary>
    public bool IsChangePointSignificant { get; set; }

    /// <summary>注意事項（シミュレーション成功数が少ない等）</summary>
    public string? Warning { get; set; }

    public string Interpretation { get; set; } = "";
}
