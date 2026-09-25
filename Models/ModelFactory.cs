namespace BugConvergenceTool.Models;

/// <summary>
/// モデル一覧を取得するファクトリ
/// </summary>
public static class ModelFactory
{
    /// <summary>
    /// 拡張なしの全モデルを取得（基本モデル）
    /// </summary>
    public static IEnumerable<ReliabilityGrowthModelBase> GetAllModels()
    {
        return GetBasicModels();
    }
    
    /// <summary>
    /// 拡張モデルを含む全モデルを取得
    /// </summary>
    public static IEnumerable<ReliabilityGrowthModelBase> GetAllExtendedModels(
        bool includeChangePoint = true,
        bool includeTEF = true,
        bool includeFRE = true)
    {
        // 基本モデル
        foreach (var m in GetBasicModels())
            yield return m;

        // 変化点モデル
        if (includeChangePoint)
        {
            foreach (var m in ChangePointModelFactory.GetBasicChangePointModels())
                yield return m;
        }

        // TEF組込モデル（推奨のWeibull TEFのみ）
        if (includeTEF)
        {
            foreach (var m in TEFModelFactory.GetRecommendedTEFModels())
                yield return m;
        }

        // 欠陥除去効率モデル
        if (includeFRE)
        {
            foreach (var m in FREModelFactory.GetBasicFREModels())
                yield return m;
        }
    }
    
    /// <summary>
    /// 基本モデルのみ取得
    /// </summary>
    public static IEnumerable<ReliabilityGrowthModelBase> GetBasicModels()
    {
        yield return new ExponentialModel();
        yield return new DelayedSModel();
        yield return new GompertzModel();
        yield return new GeneralizedGoelOkumotoModel();
        yield return new InflectionSModel();
    }
    
    /// <summary>
    /// モデルカテゴリ一覧を取得
    /// </summary>
    public static IEnumerable<string> GetCategories()
    {
        yield return "基本";
        yield return "変化点";
        yield return "TEF組込";
        yield return "欠陥除去効率";
    }
}
