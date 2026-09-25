namespace BugConvergenceTool.Services;

/// <summary>
/// パラメータ名の表示用説明（CLI・レポートで共通使用）
/// </summary>
public static class ParameterDescriptions
{
    /// <summary>
    /// パラメータの説明（括弧付き）。不明な名前は空文字
    /// </summary>
    public static string Describe(string name) => name switch
    {
        "a" => "（潜在バグ総数の規模。TEF 組込モデル以外は m(∞) = a）",
        "b" => "（バグ発見率）",
        "c" => "（形状パラメータ）",
        "ψ" => "（変曲パラメータ。0 で指数型、大きいほど立ち上がりが遅い S 字）",
        "τ" => "（変化点）",
        "b₁" or "b1" => "（変化点前の発見率）",
        "b₂" or "b2" => "（変化点後の発見率）",
        "η" => "（欠陥除去効率）",
        "η₀" => "（初期の欠陥除去効率）",
        "η∞" => "（漸近的な欠陥除去効率）",
        "λ" => "（除去効率の学習速度）",
        _ when name.StartsWith("τ") => "（変化点）",
        _ when name.StartsWith("b") && int.TryParse(name[1..], out int k) => $"（第{k}区間の発見率）",
        _ when name.StartsWith("TEF_") => "（テスト工数関数のパラメータ）",
        _ => ""
    };

    /// <summary>
    /// 百分率でも表示するパラメータか（0〜1 の比率）
    /// </summary>
    public static bool IsRatio(string name) => name.StartsWith("η");

    /// <summary>
    /// 「名前 = 値 （説明）」形式の1行
    /// </summary>
    public static string FormatLine(string name, double value)
    {
        string ratio = IsRatio(name) ? $" ({value * 100:F1}%)" : "";
        return $"{name} = {value:F4}{ratio} {Describe(name)}";
    }
}
