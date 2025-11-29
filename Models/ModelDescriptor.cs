namespace BugConvergenceTool.Models;

/// <summary>
/// モデルのメタデータ（説明・推奨・注意事項）
/// </summary>
public sealed class ModelDescriptor
{
    /// <summary>
    /// モデル識別子
    /// </summary>
    public string Id { get; init; } = "";
    
    /// <summary>
    /// 表示名
    /// </summary>
    public string DisplayName { get; init; } = "";
    
    /// <summary>
    /// モデルの数式（簡略形）
    /// </summary>
    public string FormulaSummary { get; init; } = "";
    
    /// <summary>
    /// 挙動の特徴
    /// </summary>
    public string BehaviorComment { get; init; } = "";
    
    /// <summary>
    /// 推奨される使用場面
    /// </summary>
    public string RecommendedUse { get; init; } = "";
    
    /// <summary>
    /// 注意点
    /// </summary>
    public string Caution { get; init; } = "";
    
    /// <summary>
    /// MLEをサポートするか
    /// </summary>
    public bool SupportsMle { get; init; } = true;
}

/// <summary>
/// モデルメタデータのレジストリ
/// </summary>
public static class ModelDescriptorRegistry
{
    private static readonly Dictionary<string, ModelDescriptor> _descriptors = new(StringComparer.OrdinalIgnoreCase)
    {
        // 基本モデル
        ["指数型（Goel-Okumoto）"] = new ModelDescriptor
        {
            Id = "exponential",
            DisplayName = "指数型（Goel-Okumoto）",
            FormulaSummary = "m(t) = a(1 - e^(-bt))",
            BehaviorComment = "バグ発見率が一定。シンプルで初期から安定したテスト向け。",
            RecommendedUse = "テスト工程が比較的安定しており、初期から一定ペースでバグが検出される場合。",
            Caution = "テスト習熟期間がある場合は過大評価しやすい。",
            SupportsMle = true
        },
        
        ["遅延S字型"] = new ModelDescriptor
        {
            Id = "delayed-s",
            DisplayName = "遅延S字型",
            FormulaSummary = "m(t) = a(1 - (1+bt)e^(-bt))",
            BehaviorComment = "テスト初期の習熟期間を考慮。立ち上がりが緩やか。",
            RecommendedUse = "テスト初期にバグ発見が少なく、中盤から増加するパターン。",
            Caution = "習熟期間が短い場合は指数型の方が適切。",
            SupportsMle = true
        },
        
        ["ゴンペルツ"] = new ModelDescriptor
        {
            Id = "gompertz",
            DisplayName = "ゴンペルツ",
            FormulaSummary = "m(t) = a·e^(-b·e^(-ct))",
            BehaviorComment = "終盤の収束が急。S字カーブの非対称性を表現。",
            RecommendedUse = "テスト後半でバグ発見が急激に減少する傾向がある場合。",
            Caution = "パラメータcの推定が不安定になることがある。",
            SupportsMle = true
        },
        
        ["修正ゴンペルツ"] = new ModelDescriptor
        {
            Id = "modified-gompertz",
            DisplayName = "修正ゴンペルツ",
            FormulaSummary = "m(t) = a(c - e^(-bt))",
            BehaviorComment = "S字カーブの柔軟性向上版。漸近値の調整が可能。",
            RecommendedUse = "ゴンペルツ型で当てはまりが悪い場合の代替。",
            Caution = "パラメータ数が増えるため、データ数が少ないと過学習のリスク。",
            SupportsMle = true
        },
        
        ["ロジスティック"] = new ModelDescriptor
        {
            Id = "logistic",
            DisplayName = "ロジスティック",
            FormulaSummary = "m(t) = a / (1 + e^(-b(t-c)))",
            BehaviorComment = "対称S字カーブ。変曲点が明確。",
            RecommendedUse = "バグ発見のピークが明確で、前後対称的に推移する場合。",
            Caution = "非対称なデータには不向き。",
            SupportsMle = true
        },
        
        // 不完全デバッグモデル
        ["PNZ型"] = new ModelDescriptor
        {
            Id = "pnz",
            DisplayName = "PNZ型（Pham-Nordmann-Zhang）",
            FormulaSummary = "不完全デバッグ（デバッグ時のバグ混入を考慮）",
            BehaviorComment = "デバッグ時に新たなバグが混入する状況を表現。",
            RecommendedUse = "修正作業でデグレードが発生しやすい場合。",
            Caution = "パラメータpの推定が難しく、データ量が必要。",
            SupportsMle = false
        },
        
        ["Yamada遅延S字型"] = new ModelDescriptor
        {
            Id = "yamada-delayed-s",
            DisplayName = "Yamada遅延S字型",
            FormulaSummary = "遅延S字型 + 不完全デバッグ",
            BehaviorComment = "習熟期間と不完全デバッグの両方を考慮。",
            RecommendedUse = "複雑なテスト工程で、習熟とデグレードの両方がある場合。",
            Caution = "パラメータが多く、短期間のデータでは不安定。",
            SupportsMle = false
        },
        
        ["Pham-Zhang型"] = new ModelDescriptor
        {
            Id = "pham-zhang",
            DisplayName = "Pham-Zhang型",
            FormulaSummary = "依存性を持つ不完全デバッグモデル",
            BehaviorComment = "バグ間の依存性を考慮した高度なモデル。",
            RecommendedUse = "バグが相互に関連している場合や、修正の影響が大きい場合。",
            Caution = "データ量と品質が十分でないと過学習しやすい。",
            SupportsMle = false
        }
    };
    
    /// <summary>
    /// モデル名からディスクリプタを取得
    /// </summary>
    public static ModelDescriptor? GetDescriptor(string modelName)
    {
        return _descriptors.TryGetValue(modelName, out var descriptor) ? descriptor : null;
    }
    
    /// <summary>
    /// 全ディスクリプタを取得
    /// </summary>
    public static IReadOnlyDictionary<string, ModelDescriptor> GetAllDescriptors()
    {
        return _descriptors;
    }
    
    /// <summary>
    /// ディスクリプタを追加（拡張モデル用）
    /// </summary>
    public static void RegisterDescriptor(string modelName, ModelDescriptor descriptor)
    {
        _descriptors[modelName] = descriptor;
    }
}
