namespace BugConvergenceTool.Services;

/// <summary>
/// 区間推定の結果を表示用の1行に整形する（CLI・テキストレポートで共通使用）
/// </summary>
public static class IntervalFormatter
{
    /// <summary>
    /// 収束マイルストーン到達日の区間（例: "90%発見: 62.4日目  [41.8日目, 101.3日目]"）
    /// </summary>
    public static string MilestoneLine(MilestoneInterval milestone)
    {
        static string FormatDay(double d) => double.IsPositiveInfinity(d) ? "到達せず" : $"{d:F1}日目";
        string note = milestone.UnreachableFraction > 0 ? $"（{milestone.UnreachableFraction:P0} の反復で到達せず）" : "";
        return $"{milestone.Ratio * 100:F0}%発見: {FormatDay(milestone.EstimateDay)}  [{FormatDay(milestone.LowerDay)}, {FormatDay(milestone.UpperDay)}]{note}";
    }
    
    /// <summary>
    /// Fisher 情報行列によるパラメータ i の推定値・標準誤差・信頼区間
    /// </summary>
    public static string FisherParameterLine(FisherInformationResult fisher, int i)
    {
        return $"{fisher.ParameterNames[i],-4} = {fisher.Parameters[i],10:G5}  SE={fisher.StandardErrors[i],10:G4}  [{fisher.LowerBounds[i]:G5}, {fisher.UpperBounds[i]:G5}]";
    }
    
    /// <summary>
    /// デルタ法による推定潜在バグ総数の信頼区間
    /// </summary>
    public static string FisherTotalBugsLine(DerivedQuantityInterval total)
    {
        return $"推定潜在バグ総数: {total.Estimate:F1} 件  {total.ConfidenceLevel:P0}区間 [{total.Lower:F1}, {total.Upper:F1}]（デルタ法{(total.LogScale ? "・対数スケール" : "")}）";
    }
}
