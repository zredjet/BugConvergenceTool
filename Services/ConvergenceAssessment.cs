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
/// <param name="Ratio">現在の発見率（累積発見数 / 推定総バグ数）。判定不能時は null</param>
/// <param name="Stars">★表記</param>
/// <param name="Message">判定メッセージ</param>
/// <param name="Note">補足（推定総バグ数が累積発見数を下回る場合など）。なければ null</param>
public sealed record ConvergenceAssessmentResult(
    ConvergenceLevel Level,
    double? Ratio,
    string Stars,
    string Message,
    string? Note);

/// <summary>
/// 現在の発見率から収束状況を判定する（CLI・レポートで共通使用）
/// </summary>
public static class ConvergenceAssessment
{
    /// <summary>
    /// 収束状況を判定する
    /// </summary>
    /// <param name="observedCumulative">現在の累積バグ発見数</param>
    /// <param name="estimatedTotalBugs">推定潜在バグ総数（m(∞)）</param>
    public static ConvergenceAssessmentResult Evaluate(double observedCumulative, double estimatedTotalBugs)
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

        return ratio switch
        {
            >= 0.99 => new ConvergenceAssessmentResult(ConvergenceLevel.Converged, ratio, "★★★",
                "十分に収束しています。リリース可能な状態です。", note),
            >= 0.95 => new ConvergenceAssessmentResult(ConvergenceLevel.NearlyConverged, ratio, "★★☆",
                "ほぼ収束しています。ベータリリースに適した状態です。", note),
            >= 0.90 => new ConvergenceAssessmentResult(ConvergenceLevel.Converging, ratio, "★☆☆",
                "収束傾向にあります。継続的なテストが推奨されます。", note),
            _ => new ConvergenceAssessmentResult(ConvergenceLevel.NotConverged, ratio, "☆☆☆",
                "まだ収束していません。テスト継続が必要です。", note)
        };
    }

    /// <summary>
    /// 発見率を表示用文字列に整形（判定不能時は "-"）
    /// </summary>
    public static string FormatRatio(double? ratio) => ratio.HasValue ? $"{ratio.Value * 100:F1}%" : "-";
}
