namespace BugConvergenceTool.Models;

/// <summary>
/// ツール全体の設定を表すルートクラス
/// </summary>
public class ToolConfiguration
{
    /// <summary>
    /// オプティマイザ設定
    /// </summary>
    public OptimizerSettings Optimizers { get; set; } = new();
    
    /// <summary>
    /// モデル初期値推定の設定
    /// </summary>
    public ModelInitializationSettings ModelInitialization { get; set; } = new();
    
    /// <summary>
    /// 変化点モデルの設定
    /// </summary>
    public ChangePointSettings ChangePoint { get; set; } = new();
    
    /// <summary>
    /// 不完全デバッグ・FRE系のデフォルト設定
    /// </summary>
    public ImperfectDebugDefaults ImperfectDebug { get; set; } = new();
    
    /// <summary>
    /// ブートストラップ信頼区間の設定
    /// </summary>
    public BootstrapSettings Bootstrap { get; set; } = new();
}

#region ブートストラップ設定

/// <summary>
/// ブートストラップ法による信頼区間計算の設定
/// </summary>
public class BootstrapSettings
{
    /// <summary>
    /// ブートストラップ反復回数（デフォルト: 200）
    /// </summary>
    public int Iterations { get; set; } = 200;
    
    /// <summary>
    /// 信頼水準（デフォルト: 0.95 = 95%信頼区間）
    /// </summary>
    public double ConfidenceLevel { get; set; } = 0.95;
    
    /// <summary>
    /// ブートストラップ用オプティマイザの最大反復回数（デフォルト: 80）
    /// </summary>
    public int OptimizerMaxIterations { get; set; } = 80;
    
    /// <summary>
    /// ブートストラップ用オプティマイザの収束判定閾値（デフォルト: 1e-6）
    /// </summary>
    public double OptimizerTolerance { get; set; } = 1e-6;
    
    /// <summary>
    /// SSE が元の何倍以上悪い場合に破棄するか（デフォルト: 10.0）
    /// 0以下で無効化
    /// </summary>
    public double SSEThresholdMultiplier { get; set; } = 10.0;
    
    /// <summary>
    /// ランダムシード（null で自動生成）
    /// </summary>
    public int? RandomSeed { get; set; }
}

#endregion

#region オプティマイザ設定

/// <summary>
/// 各オプティマイザのハイパーパラメータ設定
/// </summary>
public class OptimizerSettings
{
    /// <summary>
    /// 差分進化（DE）の設定
    /// </summary>
    public DESettings DE { get; set; } = new();
    
    /// <summary>
    /// 粒子群最適化（PSO）の設定
    /// </summary>
    public PSOSettings PSO { get; set; } = new();
    
    /// <summary>
    /// CMA-ES の設定
    /// </summary>
    public CMAESSettings CMAES { get; set; } = new();
    
    /// <summary>
    /// Grey Wolf Optimizer の設定
    /// </summary>
    public GWOSettings GWO { get; set; } = new();
    
    /// <summary>
    /// Nelder-Mead法の設定
    /// </summary>
    public NelderMeadSettings NelderMead { get; set; } = new();
    
    /// <summary>
    /// グリッドサーチ+勾配降下法の設定
    /// </summary>
    public GridSearchGradientSettings GridSearchGradient { get; set; } = new();
}

/// <summary>
/// 差分進化（DE）の設定
/// </summary>
public class DESettings
{
    /// <summary>
    /// 個体数（デフォルト: 50）
    /// </summary>
    public int PopulationSize { get; set; } = 50;
    
    /// <summary>
    /// 最大反復回数（デフォルト: 500）
    /// </summary>
    public int MaxIterations { get; set; } = 500;
    
    /// <summary>
    /// スケーリング係数 F（デフォルト: 0.8）
    /// </summary>
    public double F { get; set; } = 0.8;
    
    /// <summary>
    /// 交叉率 CR（デフォルト: 0.9）
    /// </summary>
    public double CR { get; set; } = 0.9;
    
    /// <summary>
    /// 収束判定閾値（デフォルト: 1e-10）
    /// </summary>
    public double Tolerance { get; set; } = 1e-10;
}

/// <summary>
/// 粒子群最適化（PSO）の設定
/// </summary>
public class PSOSettings
{
    /// <summary>
    /// 粒子数（デフォルト: 30）
    /// </summary>
    public int SwarmSize { get; set; } = 30;
    
    /// <summary>
    /// 最大反復回数（デフォルト: 500）
    /// </summary>
    public int MaxIterations { get; set; } = 500;
    
    /// <summary>
    /// 慣性重み w（デフォルト: 0.729）
    /// </summary>
    public double W { get; set; } = 0.729;
    
    /// <summary>
    /// 認知係数 c1（デフォルト: 1.49445）
    /// </summary>
    public double C1 { get; set; } = 1.49445;
    
    /// <summary>
    /// 社会係数 c2（デフォルト: 1.49445）
    /// </summary>
    public double C2 { get; set; } = 1.49445;
    
    /// <summary>
    /// 収束判定閾値（デフォルト: 1e-10）
    /// </summary>
    public double Tolerance { get; set; } = 1e-10;
}

/// <summary>
/// CMA-ES の設定
/// </summary>
public class CMAESSettings
{
    /// <summary>
    /// 最大反復回数（デフォルト: 500）
    /// </summary>
    public int MaxIterations { get; set; } = 500;
    
    /// <summary>
    /// u空間での初期ステップサイズ σ_u（デフォルト: 0.5）
    /// </summary>
    public double InitialSigmaU { get; set; } = 0.5;
    
    /// <summary>
    /// 収束判定閾値（デフォルト: 1e-10）
    /// </summary>
    public double Tolerance { get; set; } = 1e-10;
}

/// <summary>
/// Grey Wolf Optimizer の設定
/// </summary>
public class GWOSettings
{
    /// <summary>
    /// 群れサイズ（デフォルト: 30）
    /// </summary>
    public int PackSize { get; set; } = 30;
    
    /// <summary>
    /// 最大反復回数（デフォルト: 500）
    /// </summary>
    public int MaxIterations { get; set; } = 500;
    
    /// <summary>
    /// 収束判定閾値（デフォルト: 1e-10）
    /// </summary>
    public double Tolerance { get; set; } = 1e-10;
}

/// <summary>
/// Nelder-Mead法の設定
/// </summary>
public class NelderMeadSettings
{
    /// <summary>
    /// 最大反復回数（デフォルト: 1000）
    /// </summary>
    public int MaxIterations { get; set; } = 1000;
    
    /// <summary>
    /// 収束判定閾値（デフォルト: 1e-10）
    /// </summary>
    public double Tolerance { get; set; } = 1e-10;
    
    /// <summary>
    /// 反射係数 α（デフォルト: 1.0）
    /// </summary>
    public double Alpha { get; set; } = 1.0;
    
    /// <summary>
    /// 拡大係数 γ（デフォルト: 2.0）
    /// </summary>
    public double Gamma { get; set; } = 2.0;
    
    /// <summary>
    /// 収縮係数 ρ（デフォルト: 0.5）
    /// </summary>
    public double Rho { get; set; } = 0.5;
    
    /// <summary>
    /// 縮小係数 σ（デフォルト: 0.5）
    /// </summary>
    public double Sigma { get; set; } = 0.5;
}

/// <summary>
/// グリッドサーチ+勾配降下法の設定
/// </summary>
public class GridSearchGradientSettings
{
    /// <summary>
    /// グリッドサイズ（0=自動）
    /// </summary>
    public int GridSize { get; set; } = 0;
    
    /// <summary>
    /// 勾配降下法の最大反復回数（デフォルト: 2000）
    /// </summary>
    public int MaxIterations { get; set; } = 2000;
    
    /// <summary>
    /// 学習率（デフォルト: 0.00005）
    /// </summary>
    public double LearningRate { get; set; } = 0.00005;
    
    /// <summary>
    /// 数値微分のデルタ（デフォルト: 0.0001）
    /// </summary>
    public double Delta { get; set; } = 0.0001;
}

#endregion

#region モデル初期値推定設定

/// <summary>
/// モデルの初期パラメータ推定に関する設定
/// </summary>
public class ModelInitializationSettings
{
    /// <summary>
    /// パラメータ a のスケール係数設定
    /// </summary>
    public ScaleFactorSettings ScaleFactorA { get; set; } = new();
    
    /// <summary>
    /// 終盤の増分しきい値設定
    /// </summary>
    public IncrementThresholdSettings IncrementThreshold { get; set; } = new();
    
    /// <summary>
    /// 平均増分による b 推定の区切り
    /// </summary>
    public AverageSlopeThresholds AverageSlopeThresholds { get; set; } = new();
}

/// <summary>
/// パラメータ a のスケール係数設定
/// 収束度合いに応じて a = maxY × スケール係数
/// </summary>
public class ScaleFactorSettings
{
    /// <summary>
    /// 収束時（増分が閾値以下）の最小スケール係数（デフォルト: 1.1）
    /// 指数型などの基本モデル向け
    /// </summary>
    public double ConvergedMin { get; set; } = 1.1;
    
    /// <summary>
    /// 収束時（増分が閾値以下）の最大スケール係数（デフォルト: 1.4）
    /// S字型や不完全デバッグモデル向け
    /// </summary>
    public double ConvergedMax { get; set; } = 1.4;
    
    /// <summary>
    /// 未収束時（増分が閾値超過）の最小スケール係数（デフォルト: 1.5）
    /// </summary>
    public double NotConvergedMin { get; set; } = 1.5;
    
    /// <summary>
    /// 未収束時（増分が閾値超過）の最大スケール係数（デフォルト: 1.9）
    /// </summary>
    public double NotConvergedMax { get; set; } = 1.9;
}

/// <summary>
/// 終盤の増分（直近2点の差）の判定しきい値設定
/// </summary>
public class IncrementThresholdSettings
{
    /// <summary>
    /// 収束と判定する増分の閾値（デフォルト: 1.0）
    /// 直近2点の累積バグ数差がこの値以下なら「ほぼ収束」と判定
    /// </summary>
    public double ConvergenceThreshold { get; set; } = 1.0;
}

/// <summary>
/// 平均増分（傾き）に基づく b パラメータ推定の区切り
/// </summary>
public class AverageSlopeThresholds
{
    /// <summary>
    /// 非常に緩やかな増分の閾値（デフォルト: 0.1）
    /// </summary>
    public double VeryLow { get; set; } = 0.1;
    
    /// <summary>
    /// 緩やかな増分の閾値（デフォルト: 0.5）
    /// </summary>
    public double Low { get; set; } = 0.5;
    
    /// <summary>
    /// 中程度の増分の閾値（デフォルト: 1.0）
    /// </summary>
    public double Medium { get; set; } = 1.0;
    
    /// <summary>
    /// 各閾値に対応する b の初期値（指数型用）
    /// </summary>
    public BParameterValues BValuesExponential { get; set; } = new()
    {
        VeryLow = 0.05,
        Low = 0.1,
        Medium = 0.2,
        High = 0.3
    };
    
    /// <summary>
    /// 各閾値に対応する b の初期値（S字型用）
    /// </summary>
    public BParameterValues BValuesSCurve { get; set; } = new()
    {
        VeryLow = 0.08,
        Low = 0.15,
        Medium = 0.25,
        High = 0.35
    };
}

/// <summary>
/// 傾き区分ごとの b パラメータ値
/// </summary>
public class BParameterValues
{
    /// <summary>
    /// 非常に緩やか（slope ≤ VeryLow）の場合の b 値
    /// </summary>
    public double VeryLow { get; set; }
    
    /// <summary>
    /// 緩やか（VeryLow < slope ≤ Low）の場合の b 値
    /// </summary>
    public double Low { get; set; }
    
    /// <summary>
    /// 中程度（Low < slope ≤ Medium）の場合の b 値
    /// </summary>
    public double Medium { get; set; }
    
    /// <summary>
    /// 急（slope > Medium）の場合の b 値
    /// </summary>
    public double High { get; set; }
}

#endregion

#region 変化点設定

/// <summary>
/// 変化点モデルの設定
/// </summary>
public class ChangePointSettings
{
    /// <summary>
    /// 変化点 τ を決める累積比率（デフォルト: 0.5）
    /// 累積バグ数がこの比率に達する日を変化点候補とする
    /// </summary>
    public double CumulativeRatio { get; set; } = 0.5;
    
    /// <summary>
    /// 複数変化点モデルの分割比設定
    /// </summary>
    public MultipleChangePointSettings MultipleChangePoints { get; set; } = new();
}

/// <summary>
/// 複数変化点モデルの設定
/// </summary>
public class MultipleChangePointSettings
{
    /// <summary>
    /// 2変化点モデルの累積比率リスト（デフォルト: [0.33, 0.67]）
    /// </summary>
    public double[] TwoPointRatios { get; set; } = new[] { 0.33, 0.67 };
    
    /// <summary>
    /// 3変化点モデルの累積比率リスト（デフォルト: [0.25, 0.5, 0.75]）
    /// </summary>
    public double[] ThreePointRatios { get; set; } = new[] { 0.25, 0.5, 0.75 };
}

#endregion

#region 不完全デバッグ・FRE設定

/// <summary>
/// 不完全デバッグ・欠陥除去効率（FRE）系モデルのデフォルト設定
/// </summary>
public class ImperfectDebugDefaults
{
    /// <summary>
    /// 不完全デバッグ係数 p の初期値（デフォルト: 0.1）
    /// 修正時に新たなバグが混入する割合
    /// </summary>
    public double P0 { get; set; } = 0.1;
    
    /// <summary>
    /// 初期欠陥除去効率 η₀（デフォルト: 0.8）
    /// </summary>
    public double Eta0 { get; set; } = 0.8;
    
    /// <summary>
    /// 漸近欠陥除去効率 η∞（デフォルト: 0.95）
    /// </summary>
    public double EtaInfinity { get; set; } = 0.95;
    
    /// <summary>
    /// バグ混入率 α の初期値（デフォルト: 0.1）
    /// </summary>
    public double Alpha0 { get; set; } = 0.1;
    
    /// <summary>
    /// ゴンペルツモデルの初期遅延係数 b（デフォルト: 2.0）
    /// </summary>
    public double GompertzB0 { get; set; } = 2.0;
}

#endregion
