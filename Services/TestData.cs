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
}
