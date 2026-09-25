using BugConvergenceTool.Models;

namespace BugConvergenceTool.Services;

/// <summary>
/// 情報量規準（AIC/AICc）で互いに比較できるモデルの組（比較グループ）
/// </summary>
/// <remarks>
/// AIC は「同じデータ」に対する「同じ尤度」で計算したモデル同士でしか比較できない。
/// FRE モデルは修正数、TEF モデルは工数データの尤度を含むため、
/// 発見数のみの尤度で推定したモデルとは AIC の値を比べられない。
/// また同じグループ内では AIC と AICc を混在させず、どちらか一方に揃える。
/// </remarks>
public static class ModelComparisonGroup
{
    /// <summary>日次バグ発見数のみの尤度</summary>
    public const string DetectionOnly = "発見数";

    /// <summary>発見数と工数データの結合尤度（TEF モデル）</summary>
    public const string DetectionAndEffort = "発見数+工数";

    /// <summary>発見数と修正数の結合尤度（FRE モデル）</summary>
    public const string DetectionAndCorrection = "発見数+修正数";

    /// <summary>表示・推奨モデル選択の優先順（先頭が推奨モデルを選ぶグループ）</summary>
    private static readonly string[] Priority = { DetectionOnly, DetectionAndEffort, DetectionAndCorrection };

    /// <summary>
    /// モデルの推定に使った尤度に含まれるデータから比較グループを決める
    /// </summary>
    /// <param name="model">モデル（TEF の場合は工数データ設定済みであること）</param>
    /// <param name="hasCorrectionData">修正数データを損失関数に渡しているか</param>
    public static string Determine(ReliabilityGrowthModelBase model, bool hasCorrectionData)
    {
        if (model is FaultRemovalEfficiencyModelBase && hasCorrectionData)
            return DetectionAndCorrection;
        if (model is TEFBasedModelBase tef && tef.ObservedEffortData != null)
            return DetectionAndEffort;
        return DetectionOnly;
    }

    /// <summary>
    /// グループの説明（表示用）
    /// </summary>
    public static string Describe(string group) => group switch
    {
        DetectionOnly => "日次バグ発見数のみの尤度",
        DetectionAndEffort => "発見数＋工数データの結合尤度（上のグループとは AIC を比較できません）",
        DetectionAndCorrection => "発見数＋修正数の結合尤度（上のグループとは AIC を比較できません）",
        _ => group
    };

    /// <summary>
    /// 情報量規準で比較可能な結果か（推定成功かつサンプルサイズ十分）
    /// </summary>
    public static bool IsComparable(FittingResult result)
    {
        return result.Success && !result.ModelSelectionCriterion.StartsWith("Invalid");
    }

    /// <summary>
    /// 比較可能な結果をグループごとに、優先順・グループ内は選択基準値の昇順で返す
    /// </summary>
    public static IReadOnlyList<(string Group, IReadOnlyList<FittingResult> Results)> GroupAndRank(
        IEnumerable<FittingResult> results)
    {
        return results
            .Where(IsComparable)
            .GroupBy(r => r.ComparisonGroup)
            .OrderBy(g => GroupOrder(g.Key))
            .Select(g => (g.Key, (IReadOnlyList<FittingResult>)g.OrderBy(r => r.SelectionScore).ToList()))
            .ToList();
    }

    /// <summary>
    /// 推奨モデルを選ぶグループ（優先順で最初に比較可能な結果があるグループ）
    /// </summary>
    public static string? SelectPrimaryGroup(IEnumerable<FittingResult> results)
    {
        return GroupAndRank(results).Select(g => g.Group).FirstOrDefault();
    }

    /// <summary>
    /// グループ内の選択基準を AIC か AICc のどちらかに揃える
    /// </summary>
    /// <remarks>
    /// 小標本補正の要否（n/k &lt; 40）はパラメータ数 k に依存するため、モデルごとに判定すると
    /// 同じ表で AIC と AICc が混在する。グループ内に1つでも AICc が必要なモデルがあれば全員 AICc とする
    /// （AICc は n が大きいと AIC に一致するため、揃えても不利なモデルは生じない）。
    /// </remarks>
    public static void HarmonizeCriterion(IEnumerable<FittingResult> results)
    {
        foreach (var group in results.Where(IsComparable).GroupBy(r => r.ComparisonGroup))
        {
            if (group.Any(r => r.ModelSelectionCriterion == "AICc"))
            {
                foreach (var r in group)
                    r.ModelSelectionCriterion = "AICc";
            }
        }
    }

    private static int GroupOrder(string group)
    {
        int index = Array.IndexOf(Priority, group);
        return index >= 0 ? index : Priority.Length;
    }
}
