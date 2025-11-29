namespace BugConvergenceTool.Models;

/// <summary>
/// パラメトリック Coverage NHPP モデルの基底クラス
/// C(t) をテストカバレッジ関数として m(t) = a · C(t) と定義
/// 実測カバレッジデータなしで、時間 t からカバレッジを近似
/// </summary>
public abstract class CoverageModelBase : ReliabilityGrowthModelBase
{
    public override string Category => "Coverage";

    /// <summary>
    /// カバレッジ関数 C(t) を計算（0〜1）
    /// </summary>
    public abstract double Coverage(double t, double[] parameters);
}

/// <summary>
/// Weibull Coverage NHPP モデル
/// C(t) = 1 - exp(-(βt)^γ)
/// m(t) = a · C(t) = a(1 - exp(-(βt)^γ))
/// </summary>
/// <remarks>
/// - β: 時間スケールパラメータ（大きいほど立ち上がりが早い）
/// - γ: 形状パラメータ（γ>1でS字型、γ=1で指数型、γ&lt;1で凸型）
/// 一般化指数型 NHPP と同等だが、coverage として解釈できる
/// </remarks>
public class WeibullCoverageModel : CoverageModelBase
{
    public override string Name => "Weibull Coverage";
    public override string Formula => "m(t) = a(1-e^(-(βt)^γ)), C(t) = 1-e^(-(βt)^γ)";
    public override string Description => "Weibull分布ベースのカバレッジ関数モデル。γ>1でS字、γ<1で凸型";
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

        // β: 半分の時間でそこそこカバレッジが出るイメージ
        double beta0 = 1.0 / (n / 2.0);

        return new[] {
            maxY * 1.5, // a: 総欠陥数スケール
            beta0,      // β: 時間スケール
            1.0         // γ: 単調凸からスタート
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
/// Logistic Coverage NHPP モデル
/// C(t) = 1 / (1 + exp(-β(t - τ)))
/// m(t) = a · C(t)
/// </summary>
/// <remarks>
/// - β: カバレッジ立ち上がりの鋭さ（大きいほど急峻）
/// - τ: カバレッジが50%に達する時刻
/// S字型カーブが必要な場合に適する
/// </remarks>
public class LogisticCoverageModel : CoverageModelBase
{
    public override string Name => "Logistic Coverage";
    public override string Formula => "m(t) = a/(1+e^(-β(t-τ))), C(t) = 1/(1+e^(-β(t-τ)))";
    public override string Description => "ロジスティック関数ベースのカバレッジモデル。τでカバレッジ50%";
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

        // τ: 中央時刻付近でカバレッジ50%
        double midT = n / 2.0;

        return new[] {
            maxY * 1.5, // a: 総欠陥数スケール
            0.2,        // β: 立ち上がりの鋭さ
            midT        // τ: カバレッジ50%時刻
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
/// Gompertz Coverage NHPP モデル
/// C(t) = exp(-exp(-β(t - τ)))
/// m(t) = a · C(t)
/// </summary>
/// <remarks>
/// - β: カバレッジ増加率
/// - τ: 変曲点時刻（最大増加率の時点）
/// 非対称S字型で、テスト初期の遅い立ち上がりを表現
/// </remarks>
public class GompertzCoverageModel : CoverageModelBase
{
    public override string Name => "Gompertz Coverage";
    public override string Formula => "m(t) = a·exp(-e^(-β(t-τ))), C(t) = exp(-e^(-β(t-τ)))";
    public override string Description => "Gompertz曲線ベースのカバレッジモデル。非対称S字型";
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

        double tau0 = n / 3.0; // 変曲点は前半に

        return new[] {
            maxY * 1.5, // a
            0.15,       // β
            tau0        // τ
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
        yield return new WeibullCoverageModel();
        yield return new LogisticCoverageModel();
        yield return new GompertzCoverageModel();
    }

    /// <summary>
    /// 推奨 Coverage モデルを取得（Weibull のみ）
    /// </summary>
    public static IEnumerable<ReliabilityGrowthModelBase> GetRecommendedCoverageModels()
    {
        yield return new WeibullCoverageModel();
    }

    /// <summary>
    /// S字型 Coverage モデルを取得
    /// </summary>
    public static IEnumerable<ReliabilityGrowthModelBase> GetSShapedCoverageModels()
    {
        yield return new LogisticCoverageModel();
        yield return new GompertzCoverageModel();
    }
}
