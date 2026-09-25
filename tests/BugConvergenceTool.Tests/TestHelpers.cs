using BugConvergenceTool.Models;
using BugConvergenceTool.Services;

namespace BugConvergenceTool.Tests;

/// <summary>
/// テスト用の合成データと、全モデルインスタンスの列挙
/// </summary>
internal static class TestHelpers
{
    /// <summary>
    /// Goel-Okumoto（a=150, b=0.05）に従う決定的な合成データ（40日）
    /// </summary>
    public static TestData CreateGoelOkumotoData(int days = 40, double a = 150, double b = 0.05)
    {
        var data = new TestData
        {
            ProjectName = "合成データ",
            TotalTestCases = 800,
            StartDate = new DateTime(2025, 1, 6)
        };

        double Mean(double t) => a * (1 - Math.Exp(-b * t));
        double prevRounded = 0;
        for (int i = 0; i < days; i++)
        {
            double rounded = Math.Round(Mean(i + 1));
            data.Dates.Add(data.StartDate.Value.AddDays(i));
            data.PlannedDaily.Add(20);
            data.ActualDaily.Add(20);
            data.BugsFoundDaily.Add(rounded - prevRounded);
            data.BugsFixedDaily.Add(Math.Max(0, rounded - prevRounded - 1));
            prevRounded = rounded;
        }
        return data;
    }

    /// <summary>
    /// アセンブリ内の全具象モデルを、コンストラクタ引数のバリエーション込みでインスタンス化する
    /// </summary>
    public static IEnumerable<ReliabilityGrowthModelBase> CreateAllModelInstances()
    {
        // 引数の既定値と明示値で同じモデルができる場合があるため、キーで重複を除く
        return EnumerateModelInstances().DistinctBy(ModelKey);
    }

    /// <summary>
    /// モデルを一意に識別するキー（型名 + 表示名）
    /// </summary>
    public static string ModelKey(ReliabilityGrowthModelBase model) => model.GetType().Name + "|" + model.Name;

    private static IEnumerable<ReliabilityGrowthModelBase> EnumerateModelInstances()
    {
        var modelTypes = typeof(ReliabilityGrowthModelBase).Assembly.GetTypes()
            .Where(t => !t.IsAbstract && t.IsSubclassOf(typeof(ReliabilityGrowthModelBase)))
            .OrderBy(t => t.FullName);

        foreach (var type in modelTypes)
        {
            var ctor = type.GetConstructors().Single();
            var ps = ctor.GetParameters();

            if (ps.Length == 0 || ps.All(p => p.IsOptional))
            {
                yield return (ReliabilityGrowthModelBase)ctor.Invoke(ps.Select(p => p.DefaultValue).ToArray());
            }

            if (type == typeof(FixedTauChangePointModel))
            {
                // τ を最後に1つ持つ変化点モデルを τ=15 で固定したもの
                foreach (var baseModel in ChangePointModelFactory.GetAllChangePointModels()
                             .OfType<ChangePointModelBase>().Where(FixedTauChangePointModel.Supports))
                    yield return new FixedTauChangePointModel(baseModel, 15);
            }
            else if (ps.Length == 1 && ps[0].ParameterType == typeof(ITestEffortFunction))
            {
                foreach (var tef in TEFFactory.GetAllTEFs())
                    yield return (ReliabilityGrowthModelBase)ctor.Invoke(new object[] { tef });
            }
            else if (ps.Length == 1 && ps[0].ParameterType == typeof(int))
            {
                // 複数変化点の数 / 固定変化点 τ
                foreach (int value in new[] { 1, 2, 3, 15 })
                    yield return (ReliabilityGrowthModelBase)ctor.Invoke(new object[] { value });
            }
            else if (ps.Length > 0 && !ps.All(p => p.IsOptional))
            {
                throw new InvalidOperationException(
                    $"{type.Name} のコンストラクタ引数に対応していません。TestHelpers を更新してください。");
            }
        }
    }

    /// <summary>
    /// TEF モデルには工数データを設定する（初期値・境界の計算に必要）
    /// </summary>
    public static void PrepareModel(ReliabilityGrowthModelBase model, TestData data)
    {
        if (model is TEFBasedModelBase tef)
        {
            tef.ObservedEffortData = data.GetCumulativeActual();
        }
    }

    /// <summary>
    /// 初期値と、境界内の一様乱数点（シード固定）を返す
    /// </summary>
    public static IEnumerable<double[]> SampleParameterPoints(
        ReliabilityGrowthModelBase model, TestData data, int randomCount = 20, int seed = 12345)
    {
        var t = data.GetTimeData();
        var y = data.GetCumulativeBugsFound();
        var (lower, upper) = model.GetBounds(t, y);

        yield return model.GetInitialParameters(t, y);

        var rnd = new Random(seed);
        for (int i = 0; i < randomCount; i++)
        {
            var p = new double[lower.Length];
            for (int j = 0; j < p.Length; j++)
                p[j] = lower[j] + (upper[j] - lower[j]) * rnd.NextDouble();
            yield return p;
        }
    }

    /// <summary>
    /// W(∞)=∞ の TEF を使うモデルか（t を大きくしても m(t) が漸近値に近づくのが極めて遅い）
    /// </summary>
    public static bool UsesInfiniteEffort(ReliabilityGrowthModelBase model, double[] parameters)
    {
        return model is TEFBasedModelBase && model.Name.Contains("べき乗則TEF");
    }
}
