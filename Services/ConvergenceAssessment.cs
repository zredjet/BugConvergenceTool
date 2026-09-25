namespace BugConvergenceTool.Services;

/// <summary>
/// 収束判断のレベル
/// </summary>
public enum ConvergenceLevel
{
    /// <summary>推定総バグ数が不正（0 以下・非有限）で判定できない</summary>
    Undetermined,
    NotConverged,
    Converging,
    NearlyConverged,
    Converged
}

/// <summary>
/// 収束判断の結果
/// </summary>
/// <param name="Level">判定レベル</param>
/// <param name="Ratio">現在の発見率の点推定（累積発見数 / 推定総バグ数）。判定不能時は null</param>
/// <param name="Stars">★表記</param>
/// <param name="Message">判定メッセージ</param>
/// <param name="Note">補足（推定総バグ数が累積発見数を下回る場合など）。なければ null</param>
/// <param name="ConservativeRatio">
/// 発見率の信頼下限（累積発見数 / (累積発見数 + 今後発見される件数の予測区間の上限)）。区間がなければ null
/// </param>
/// <param name="Basis">★ の判定根拠（信頼下限で判定したか、点推定で判定したか、☆☆☆ 止まりにした理由）</param>
public sealed record ConvergenceAssessmentResult(
    ConvergenceLevel Level,
    double? Ratio,
    string Stars,
    string Message,
    string? Note,
    double? ConservativeRatio = null,
    string? Basis = null);

/// <summary>
/// 現在の発見率から収束状況を判定する（CLI・レポートで共通使用）
/// </summary>
/// <remarks>
/// <para>
/// ★ は発見率の点推定ではなく信頼下限 = 累積発見数 / (累積発見数 + 今後発見される件数の予測区間の上限) で決める。
/// 点推定は推定のばらつきが大きく、真の発見率 83.5% の同じ条件のデータで 0.46〜0.96 に散らばり、
/// 点推定だけで ★ を決めると未収束のデータの約 1/4 に ★☆☆ 以上を付けていた。
/// </para>
/// <para>
/// 総数 m(∞) = a の信頼区間の上限で割らないのは、a が総数の Poisson 分布の平均であり、ほぼすべて発見済みでも
/// 区間の幅が √a 程度残るため（真の発見率 99.97% でも信頼下限が 86% 程度になり ★ が付かない）。
/// 発見率に効くのは「まだ見つかっていない件数」で、その予測区間はパラメータの不確実性と Poisson 変動を含む。
/// </para>
/// <para>
/// 今後発見される件数の上限が探索範囲で決まっている（「≥」）場合や、推定の安定性分析が「不安定」の場合は、
/// 発見率の下限が定まらないので ☆☆☆ 止まりにする。
/// </para>
/// </remarks>
public static class ConvergenceAssessment
{
    /// <summary>安定性分析の「不安定」</summary>
    public const string UnstableAssessment = "不安定";

    /// <summary>
    /// 収束状況を判定する
    /// </summary>
    /// <param name="observedCumulative">現在の累積バグ発見数</param>
    /// <param name="estimatedTotalBugs">推定潜在バグ総数（m(∞)）</param>
    /// <param name="remainingInterval">今後発見される件数の予測区間（なければ点推定で判定する）</param>
    /// <param name="stabilityAssessment">推定の安定性分析の判定（"不安定" なら ☆☆☆ 止まり）</param>
    /// <param name="intervalSource">区間の求め方（表示用。例: "ブートストラップ 95%予測区間"）</param>
    public static ConvergenceAssessmentResult Evaluate(
        double observedCumulative,
        double estimatedTotalBugs,
        IntervalEstimate? remainingInterval = null,
        string? stabilityAssessment = null,
        string? intervalSource = null)
    {
        if (!double.IsFinite(estimatedTotalBugs) || estimatedTotalBugs <= 0)
        {
            return new ConvergenceAssessmentResult(
                ConvergenceLevel.Undetermined, null, "－－－",
                "推定潜在バグ総数を算出できないため、収束判断できません。",
                null);
        }

        double ratio = observedCumulative / estimatedTotalBugs;

        string? note = ratio > 1.0 + 1e-9
            ? "推定潜在バグ総数が累積発見数を下回っています。モデルがデータに適合していない可能性があります。"
            : null;

        double? conservative = null;
        var reasons = new List<string>();
        bool capped = false;
        string basis;
        if (remainingInterval != null && double.IsFinite(remainingInterval.Upper) && remainingInterval.Upper >= 0
            && observedCumulative > 0)
        {
            double remainingUpper = remainingInterval.Upper;
            conservative = Math.Min(ratio, observedCumulative / (observedCumulative + remainingUpper));
            string source = intervalSource != null ? $"、{intervalSource}" : "";
            basis = $"発見率の信頼下限 {conservative.Value * 100:F1}%（累積 {observedCumulative:F0} / (累積 + 今後発見される件数の上限 " +
                    $"{(remainingInterval.UpperIsBoundLimited ? "≥" : "")}{remainingUpper:F0}){source}）で判定";
            if (remainingInterval.UpperIsBoundLimited)
            {
                capped = true;
                reasons.Add("今後発見される件数の上限が探索範囲で決まっており（≥）、発見率の下限が定まらない");
            }
        }
        else
        {
            basis = "今後発見される件数の予測区間がないため点推定で判定";
        }

        if (stabilityAssessment == UnstableAssessment)
        {
            capped = true;
            reasons.Add("末尾の数日を除くと推定総数が大きく変わる（安定性分析が「不安定」）");
        }
        if (capped)
        {
            basis += $"。{string.Join("、", reasons)}ため ☆☆☆ 止まり";
        }

        var level = capped ? ConvergenceLevel.NotConverged : LevelFor(conservative ?? ratio);
        var (stars, message) = level switch
        {
            ConvergenceLevel.Converged => ("★★★", "十分に収束しています。リリース可能な状態です。"),
            ConvergenceLevel.NearlyConverged => ("★★☆", "ほぼ収束しています。ベータリリースに適した状態です。"),
            ConvergenceLevel.Converging => ("★☆☆", "収束傾向にあります。継続的なテストが推奨されます。"),
            _ => ("☆☆☆", capped
                ? "収束していると判断できません。テスト継続が必要です。"
                : "まだ収束していません。テスト継続が必要です。")
        };
        return new ConvergenceAssessmentResult(level, ratio, stars, message, note, conservative, basis);
    }

    /// <summary>
    /// 推定結果から収束状況を判定する（今後発見される件数の予測区間は <see cref="Models.FittingResult.RemainingBugsInterval"/>）
    /// </summary>
    public static ConvergenceAssessmentResult Evaluate(double observedCumulative, Models.FittingResult result)
        => Evaluate(observedCumulative, result.EstimatedTotalBugs, result.RemainingBugsInterval,
            result.Stability?.Assessment, result.RemainingBugsIntervalSource);

    private static ConvergenceLevel LevelFor(double ratio) => ratio switch
    {
        >= 0.99 => ConvergenceLevel.Converged,
        >= 0.95 => ConvergenceLevel.NearlyConverged,
        >= 0.90 => ConvergenceLevel.Converging,
        _ => ConvergenceLevel.NotConverged
    };

    /// <summary>
    /// 発見率を表示用文字列に整形（判定不能時は "-"）
    /// </summary>
    public static string FormatRatio(double? ratio) => ratio.HasValue ? $"{ratio.Value * 100:F1}%" : "-";
}
