namespace BugConvergenceTool.Services;

/// <summary>
/// 末尾の日を除いて推定し直した結果
/// </summary>
/// <param name="RemovedDays">除いた末尾の日数</param>
/// <param name="UsedDays">推定に使った日数</param>
/// <param name="TotalBugs">推定潜在バグ総数（推定に失敗した場合は null）</param>
/// <param name="AtUpperBound">潜在バグ総数の規模 a が探索範囲の上限に張り付いたか（総数を推定できていない）</param>
public sealed record StabilityPoint(int RemovedDays, int UsedDays, double? TotalBugs, bool AtUpperBound);

/// <summary>
/// 推定の安定性（データの打ち切りに対する感度）
/// </summary>
/// <remarks>
/// 推奨モデルを、末尾の 1〜K 日を除いたデータで推定し直し、推定潜在バグ総数の変化を見る。
/// 直近の数日のデータで総数が大きく変わる場合、推定は安定しておらず、収束予測の信頼性は低い。
/// 以前の感度分析はパラメータを 1% 動かしたときの m(∞) の弾力性で、多くのモデルでは m(∞)=a なので
/// ほぼ常に 1 となり、データに対する推定の安定性は測れていなかった。
/// </remarks>
public sealed class StabilityAnalysisResult
{
    /// <summary>全データでの推定潜在バグ総数</summary>
    public double BaseTotalBugs { get; init; }
    
    public List<StabilityPoint> Points { get; init; } = new();
    
    /// <summary>
    /// 総数の相対的な変動幅 (最大 - 最小) / 全データでの総数（全データの値を含む。計算できなければ null）
    /// </summary>
    public double? RelativeRange { get; init; }
    
    /// <summary>判定（安定 / やや不安定 / 不安定 / 判定不能）</summary>
    public string Assessment { get; init; } = "";
    
    public List<string> Warnings { get; init; } = new();
    
    /// <summary>相対変動幅がこれ未満なら「安定」</summary>
    public const double StableThreshold = 0.10;
    
    /// <summary>相対変動幅がこれ未満なら「やや不安定」、以上なら「不安定」</summary>
    public const double UnstableThreshold = 0.25;
}
