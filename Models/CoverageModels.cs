namespace BugConvergenceTool.Models;

/// <summary>
/// パラメトリック NHPP モデルの基底クラス（擬似カバレッジ形状）
/// C(t) を時間ベースのカバレッジ様関数として m(t) = a · C(t) と定義
/// </summary>
/// <remarks>
/// <para>
/// <strong>学術的注記:</strong>
/// これらのモデルは「実測カバレッジデータなしの擬似カバレッジモデル」です。
/// 時間 t を説明変数としてカバレッジ的な振る舞いを近似します。
/// 実測カバレッジデータを使用する真のカバレッジベースSRGMとは区別されます。
/// </para>
/// <para>
/// 実測カバレッジデータがある場合は、それを直接説明変数として
/// 使用するモデルを推奨します。
/// </para>
/// </remarks>
public abstract class CoverageModelBase : ReliabilityGrowthModelBase
{
    /// <summary>
    /// カテゴリ名（「擬似カバレッジ」として明確化）
    /// </summary>
    public override string Category => "擬似Coverage";

    /// <summary>
    /// カバレッジ関数 C(t) を計算（0〜1）
    /// </summary>
    public abstract double Coverage(double t, double[] parameters);
}

/// <summary>
/// Weibull型擬似カバレッジ NHPP モデル
/// C(t) = 1 - exp(-(βt)^γ)
/// m(t) = a · C(t) = a(1 - exp(-(βt)^γ))
/// </summary>
/// <remarks>
/// <para>
/// Weibull分布に基づく擬似カバレッジ関数モデル。
/// 実測カバレッジデータなしで、時間を説明変数として使用します。
/// 数学的には一般化指数型 NHPP（Ohba型）と同等です。
/// </para>
/// <para>
/// - β: 時間スケールパラメータ（大きいほど立ち上がりが早い）
/// - γ: 形状パラメータ（γ&gt;1でS字型、γ=1で指数型、γ&lt;1で凸型）
/// </para>
/// </remarks>
public class WeibullCoverageModel : CoverageModelBase
{
    public override string Name => "Weibull型擬似Coverage";
    public override string Formula => "m(t) = a(1-e^(-(βt)^γ)), C(t) = 1-e^(-(βt)^γ)";
    public override string Description => "Weibull分布型擬似カバレッジ。γ>1でS字、γ<1で凸型。Ohba型と同等";
    public override string[] ParameterNames => new[] { "a", "β", "γ" };

    public override double Coverage(double t, double[] parameters)
    {
        double beta = parameters[1];
        double gamma = parameters[2];

        double x = beta * t;
        if (x < 0) x = 0;

        // C(t) = 1 - exp(-(βt)^γ)
        return 1.0 - Math.Exp(-Math.Pow(x, gamma));
    }

    public override double Calculate(double t, double[] parameters)
    {
        double a = parameters[0];
        return a * Coverage(t, parameters);
    }

    public override double[] GetInitialParameters(double[] tData, double[] yData)
    {
        double maxY = yData.Max();
        int n = tData.Length;

        // a: 収束度合いに応じて 1.2〜1.8×maxY
        double last = yData[^1];
        double prev = n > 1 ? yData[^2] : yData[^1];
        double increment = last - prev;
        double a0 = increment <= 1.0 ? maxY * 1.2 : maxY * 1.8;

        // β: 累積カバレッジ 50% 到達日から逆算（ざっくり）
        double day50 = FindDayForCumulativeRatio(yData, 0.5);
        double beta0 = day50 > 0 ? 1.0 / day50 : 1.0 / Math.Max(1, n / 2.0);

        // γ: まずは 1.0（指数型相当）から開始
        double gamma0 = 1.0;

        return new[] {
            a0,
            beta0,
            gamma0
        };
    }

    public override (double[] lower, double[] upper) GetBounds(double[] tData, double[] yData)
    {
        double maxY = yData.Max();

        return (
            new[] {
                maxY,   // a: 下限は観測最大値
                1e-8,   // β: 正の小さな値
                0.1     // γ: 極端に小さな値は避ける
            },
            new[] {
                maxY * 100, // a: 上限は観測最大値の100倍
                10.0,       // β: 時間スケールに応じて調整
                5.0         // γ: 極端に大きな値は避ける
            }
        );
    }
}

/// <summary>
/// Logistic型擬似カバレッジ NHPP モデル
/// C(t) = 1 / (1 + exp(-β(t - τ)))
/// m(t) = a · C(t)
/// </summary>
/// <remarks>
/// <para>
/// ロジスティック関数に基づく擬似カバレッジモデル。
/// 実測カバレッジデータなしで、時間を説明変数として使用します。
/// S字型カーブが必要な場合に適します。
/// </para>
/// <para>
/// - β: 擬似カバレッジ立ち上がりの鋭さ（大きいほど急峻）
/// - τ: 擬似カバレッジが50%に達する時刻
/// </para>
/// </remarks>
public class LogisticCoverageModel : CoverageModelBase
{
    public override string Name => "Logistic型擬似Coverage";
    public override string Formula => "m(t) = a/(1+e^(-β(t-τ))), C(t) = 1/(1+e^(-β(t-τ)))";
    public override string Description => "Logistic型擬似カバレッジ。τで擬似Coverage50%、対称S字型";
    public override string[] ParameterNames => new[] { "a", "β", "τ" };

    public override double Coverage(double t, double[] parameters)
    {
        double beta = parameters[1];
        double tau = parameters[2];

        // C(t) = 1 / (1 + exp(-β(t - τ)))
        return 1.0 / (1.0 + Math.Exp(-beta * (t - tau)));
    }

    public override double Calculate(double t, double[] parameters)
    {
        double a = parameters[0];
        return a * Coverage(t, parameters);
    }

    public override double[] GetInitialParameters(double[] tData, double[] yData)
    {
        double maxY = yData.Max();
        int n = tData.Length;

        // a: 収束度合いに応じて 1.2〜1.8×maxY
        double last = yData[^1];
        double prev = n > 1 ? yData[^2] : yData[^1];
        double increment = last - prev;
        double a0 = increment <= 1.0 ? maxY * 1.2 : maxY * 1.8;

        // τ: 累積50%到達日をそのまま使用
        double tau0 = FindDayForCumulativeRatio(yData, 0.5);

        // β: 立ち上がりの鋭さ。平均増分で調整
        double avgSlope = EstimateAverageSlope(yData);
        double beta0 = avgSlope switch
        {
            <= 0.1 => 0.1,
            <= 0.5 => 0.3,
            <= 1.0 => 0.6,
            _ => 1.0
        };

        return new[] {
            a0,
            beta0,
            tau0
        };
    }

    public override (double[] lower, double[] upper) GetBounds(double[] tData, double[] yData)
    {
        double maxY = yData.Max();
        int n = tData.Length;

        return (
            new[] {
                maxY,       // a: 下限は観測最大値
                0.01,       // β: 正の小さな値
                1.0         // τ: 最小インデックス
            },
            new[] {
                maxY * 100, // a: 上限は観測最大値の100倍
                10.0,       // β: 急峻すぎると数値不安定
                (double)n   // τ: 最大インデックス
            }
        );
    }
}

/// <summary>
/// Gompertz型擬似カバレッジ NHPP モデル
/// C(t) = exp(-exp(-β(t - τ)))
/// m(t) = a · C(t)
/// </summary>
/// <remarks>
/// <para>
/// Gompertz曲線に基づく擬似カバレッジモデル。
/// 実測カバレッジデータなしで、時間を説明変数として使用します。
/// 非対称S字型で、テスト初期の遅い立ち上がりを表現します。
/// </para>
/// <para>
/// - β: 擬似カバレッジ増加率
/// - τ: 変曲点時刻（最大増加率の時点）
/// </para>
/// </remarks>
public class GompertzCoverageModel : CoverageModelBase
{
    public override string Name => "Gompertz型擬似Coverage";
    public override string Formula => "m(t) = a·exp(-e^(-β(t-τ))), C(t) = exp(-e^(-β(t-τ)))";
    public override string Description => "Gompertz型擬似カバレッジ。非対称S字型、初期は遅い立ち上がり";
    public override string[] ParameterNames => new[] { "a", "β", "τ" };

    public override double Coverage(double t, double[] parameters)
    {
        double beta = parameters[1];
        double tau = parameters[2];

        // C(t) = exp(-exp(-β(t - τ)))
        return Math.Exp(-Math.Exp(-beta * (t - tau)));
    }

    public override double Calculate(double t, double[] parameters)
    {
        double a = parameters[0];
        return a * Coverage(t, parameters);
    }

    public override double[] GetInitialParameters(double[] tData, double[] yData)
    {
        double maxY = yData.Max();
        int n = tData.Length;

        // a: 収束度合いに応じて 1.2〜1.8×maxY
        double last = yData[^1];
        double prev = n > 1 ? yData[^2] : yData[^1];
        double increment = last - prev;
        double a0 = increment <= 1.0 ? maxY * 1.2 : maxY * 1.8;

        // τ: Gompertz は変曲点がやや前寄りなので、累積40〜60%あたりの中央値を意識
        double day40 = FindDayForCumulativeRatio(yData, 0.4);
        double day60 = FindDayForCumulativeRatio(yData, 0.6);
        double tau0 = (day40 + day60) / 2.0;

        // β: 平均増分で調整
        double avgSlope = EstimateAverageSlope(yData);
        double beta0 = avgSlope switch
        {
            <= 0.1 => 0.08,
            <= 0.5 => 0.15,
            <= 1.0 => 0.25,
            _ => 0.35
        };

        return new[] {
            a0,
            beta0,
            tau0
        };
    }

    public override (double[] lower, double[] upper) GetBounds(double[] tData, double[] yData)
    {
        double maxY = yData.Max();
        int n = tData.Length;

        return (
            new[] { maxY, 0.001, 1.0 },
            new[] { maxY * 100, 5.0, (double)n }
        );
    }
}

/// <summary>
/// Coverage モデルのファクトリ
/// </summary>
public static class CoverageModelFactory
{
    /// <summary>
    /// 全 Coverage モデルを取得
    /// </summary>
    public static IEnumerable<ReliabilityGrowthModelBase> GetAllCoverageModels()
    {
        // 注意: Weibull型擬似CoverageはOhba型（Weibull）と数学的に同等のため、
        // 基本モデルとの重複を避けるためS字型のみを返す
        yield return new LogisticCoverageModel();
        yield return new GompertzCoverageModel();
    }

    /// <summary>
    /// 推奨 Coverage モデルを取得（S字型のみ）
    /// </summary>
    /// <remarks>
    /// Weibull型擬似Coverageは基本モデルのOhba型（Weibull）と数学的に同等のため、
    /// 重複を避けるために除外しています。
    /// Logistic型とGompertz型は異なる形状を持つため利用価値があります。
    /// </remarks>
    public static IEnumerable<ReliabilityGrowthModelBase> GetRecommendedCoverageModels()
    {
        yield return new LogisticCoverageModel();
        yield return new GompertzCoverageModel();
    }

    /// <summary>
    /// S字型 Coverage モデルを取得
    /// </summary>
    public static IEnumerable<ReliabilityGrowthModelBase> GetSShapedCoverageModels()
    {
        yield return new LogisticCoverageModel();
        yield return new GompertzCoverageModel();
    }

    /// <summary>
    /// Weibull型を含む全Coverageモデルを取得（上級者向け）
    /// </summary>
    /// <remarks>
    /// Weibull型はOhba型と同等であることを理解した上で使用してください。
    /// </remarks>
    public static IEnumerable<ReliabilityGrowthModelBase> GetAllCoverageModelsIncludingWeibull()
    {
        yield return new WeibullCoverageModel();
        yield return new LogisticCoverageModel();
        yield return new GompertzCoverageModel();
    }
}
