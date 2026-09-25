namespace BugConvergenceTool.Services;

/// <summary>
/// テストデータ
/// </summary>
public class TestData
{
    /// <summary>プロジェクト名</summary>
    public string ProjectName { get; set; } = "";
    
    /// <summary>総テストケース数</summary>
    public int TotalTestCases { get; set; }
    
    /// <summary>テスト開始日</summary>
    public DateTime? StartDate { get; set; }
    
    /// <summary>日付リスト</summary>
    public List<DateTime> Dates { get; set; } = new();
    
    /// <summary>予定消化数（日次）</summary>
    public List<double> PlannedDaily { get; set; } = new();
    
    /// <summary>実績消化数（日次）</summary>
    public List<double> ActualDaily { get; set; } = new();
    
    /// <summary>バグ発生件数（日次）</summary>
    public List<double> BugsFoundDaily { get; set; } = new();
    
    /// <summary>バグ修正件数（日次）</summary>
    public List<double> BugsFixedDaily { get; set; } = new();
    
    /// <summary>データ日数</summary>
    public int DayCount => Dates.Count;
    
    /// <summary>時間データ（1, 2, 3, ...）</summary>
    public double[] GetTimeData()
    {
        return Enumerable.Range(1, DayCount).Select(i => (double)i).ToArray();
    }
    
    /// <summary>累積バグ発生数</summary>
    public double[] GetCumulativeBugsFound()
    {
        var cumulative = new double[DayCount];
        double sum = 0;
        for (int i = 0; i < DayCount; i++)
        {
            sum += BugsFoundDaily[i];
            cumulative[i] = sum;
        }
        return cumulative;
    }
    
    /// <summary>累積バグ修正数</summary>
    public double[] GetCumulativeBugsFixed()
    {
        var cumulative = new double[DayCount];
        double sum = 0;
        for (int i = 0; i < DayCount; i++)
        {
            sum += BugsFixedDaily[i];
            cumulative[i] = sum;
        }
        return cumulative;
    }
    
    /// <summary>累積予定消化数</summary>
    public double[] GetCumulativePlanned()
    {
        var cumulative = new double[DayCount];
        double sum = 0;
        for (int i = 0; i < DayCount; i++)
        {
            sum += PlannedDaily[i];
            cumulative[i] = sum;
        }
        return cumulative;
    }
    
    /// <summary>累積実績消化数</summary>
    public double[] GetCumulativeActual()
    {
        var cumulative = new double[DayCount];
        double sum = 0;
        for (int i = 0; i < DayCount; i++)
        {
            sum += ActualDaily[i];
            cumulative[i] = sum;
        }
        return cumulative;
    }
    
    /// <summary>残存バグ数</summary>
    public double[] GetRemainingBugs()
    {
        var found = GetCumulativeBugsFound();
        var fixeda = GetCumulativeBugsFixed();
        var remaining = new double[DayCount];
        for (int i = 0; i < DayCount; i++)
        {
            remaining[i] = found[i] - fixeda[i];
        }
        return remaining;
    }
    
    /// <summary>現在の累積バグ発見数</summary>
    public double CurrentCumulativeBugs => GetCumulativeBugsFound().LastOrDefault();
    
    /// <summary>
    /// 読み込み時の注意（未入力の日を除外した、空欄を 0 件とした など）
    /// </summary>
    public List<string> Warnings { get; } = new();
    
    /// <summary>
    /// 観測日が営業日（土日を除く）で記録されているか
    /// </summary>
    /// <remarks>
    /// 観測日に土日が1日もなく、期間が1週間以上ある場合に営業日とみなす。祝日は考慮しない。
    /// </remarks>
    public bool UsesBusinessDays =>
        Dates.Count >= 2
        && (Dates[^1] - Dates[0]).TotalDays >= 7
        && !Dates.Any(d => d.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday);
    
    /// <summary>
    /// テスト日数 day（1 始まり。小数なら ceil した日）に対応するカレンダー上の日付
    /// </summary>
    /// <remarks>
    /// <para>
    /// モデルの時間はテスト実施日の通し番号（1, 2, 3, …）なので、観測期間内は実際の日付を返す。
    /// 観測期間後は、観測日が営業日なら土日を除いて、そうでなければ暦日で延ばす。
    /// </para>
    /// <para>
    /// 以前は開始日に日数を足していたため、土日を除いたデータでは予測日付がずれていた。
    /// </para>
    /// </remarks>
    public DateTime? DateForDay(double day)
    {
        int index = Math.Max(0, (int)Math.Ceiling(day) - 1);
        if (Dates.Count == 0)
            return StartDate?.AddDays(index);
        if (index < Dates.Count)
            return Dates[index];
        
        var date = Dates[^1];
        int extra = index - (Dates.Count - 1);
        if (!UsesBusinessDays)
            return date.AddDays(extra);
        
        while (extra > 0)
        {
            date = date.AddDays(1);
            if (date.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday))
                extra--;
        }
        return date;
    }
}
