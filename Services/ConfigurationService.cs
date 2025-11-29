using System.Text.Json;
using BugConvergenceTool.Models;

namespace BugConvergenceTool.Services;

/// <summary>
/// 設定ファイルの読み込み・管理を行うサービス
/// </summary>
public class ConfigurationService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };
    
    /// <summary>
    /// 現在の設定（シングルトン）
    /// </summary>
    public static ToolConfiguration Current { get; private set; } = new();
    
    /// <summary>
    /// デフォルト設定ファイル名
    /// </summary>
    public const string DefaultConfigFileName = "config.json";
    
    /// <summary>
    /// テンプレート設定ファイル名
    /// </summary>
    public const string TemplateConfigFileName = "default-config.json";
    
    /// <summary>
    /// 設定ファイルを読み込む
    /// </summary>
    /// <param name="filePath">設定ファイルのパス（省略時はデフォルトパス）</param>
    /// <returns>読み込みが成功した場合は true</returns>
    public static bool Load(string? filePath = null)
    {
        var paths = GetConfigPaths(filePath);
        
        foreach (var path in paths)
        {
            if (File.Exists(path))
            {
                try
                {
                    var json = File.ReadAllText(path);
                    var config = JsonSerializer.Deserialize<ToolConfiguration>(json, JsonOptions);
                    
                    if (config != null)
                    {
                        Current = config;
                        Console.WriteLine($"設定ファイルを読み込みました: {path}");
                        return true;
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"警告: 設定ファイルの読み込みに失敗しました: {path}");
                    Console.WriteLine($"  エラー: {ex.Message}");
                    // 読み込み失敗時は次のパスを試す
                }
            }
        }
        
        // 設定ファイルが見つからない場合はデフォルト設定を使用
        Current = new ToolConfiguration();
        return false;
    }
    
    /// <summary>
    /// 設定ファイルを保存する
    /// </summary>
    /// <param name="filePath">保存先パス</param>
    public static void Save(string filePath)
    {
        var json = JsonSerializer.Serialize(Current, JsonOptions);
        var directory = Path.GetDirectoryName(filePath);
        
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }
        
        File.WriteAllText(filePath, json);
        Console.WriteLine($"設定ファイルを保存しました: {filePath}");
    }
    
    /// <summary>
    /// デフォルト設定ファイルのテンプレートを生成する
    /// </summary>
    /// <param name="outputPath">出力先パス</param>
    public static void GenerateTemplate(string outputPath)
    {
        var defaultConfig = new ToolConfiguration();
        var json = JsonSerializer.Serialize(defaultConfig, JsonOptions);
        
        var directory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }
        
        File.WriteAllText(outputPath, json);
        Console.WriteLine($"設定テンプレートを生成しました: {outputPath}");
    }
    
    /// <summary>
    /// 設定を検証する
    /// </summary>
    /// <returns>検証結果のリスト（空なら問題なし）</returns>
    public static List<string> Validate()
    {
        var errors = new List<string>();
        var config = Current;
        
        // オプティマイザ設定の検証
        ValidateOptimizerSettings(config.Optimizers, errors);
        
        // モデル初期化設定の検証
        ValidateModelInitializationSettings(config.ModelInitialization, errors);
        
        // 変化点設定の検証
        ValidateChangePointSettings(config.ChangePoint, errors);
        
        // 不完全デバッグ設定の検証
        ValidateImperfectDebugSettings(config.ImperfectDebug, errors);
        
        return errors;
    }
    
    /// <summary>
    /// 設定ファイルを探すパスの一覧を取得
    /// </summary>
    private static IEnumerable<string> GetConfigPaths(string? explicitPath)
    {
        // 明示的に指定されたパス
        if (!string.IsNullOrEmpty(explicitPath))
        {
            yield return explicitPath;
        }
        
        // カレントディレクトリ
        yield return Path.Combine(Directory.GetCurrentDirectory(), DefaultConfigFileName);
        
        // 実行ファイルと同じディレクトリ
        var exeDir = AppDomain.CurrentDomain.BaseDirectory;
        yield return Path.Combine(exeDir, DefaultConfigFileName);
        
        // Templatesフォルダ
        yield return Path.Combine(exeDir, "Templates", DefaultConfigFileName);
        yield return Path.Combine(exeDir, "Templates", TemplateConfigFileName);
    }
    
    #region 検証メソッド
    
    private static void ValidateOptimizerSettings(OptimizerSettings settings, List<string> errors)
    {
        // DE
        if (settings.DE.PopulationSize < 4)
            errors.Add("DE.PopulationSize は 4 以上である必要があります");
        if (settings.DE.MaxIterations < 1)
            errors.Add("DE.MaxIterations は 1 以上である必要があります");
        if (settings.DE.F <= 0 || settings.DE.F > 2)
            errors.Add("DE.F は 0 < F ≤ 2 の範囲である必要があります");
        if (settings.DE.CR < 0 || settings.DE.CR > 1)
            errors.Add("DE.CR は 0 ≤ CR ≤ 1 の範囲である必要があります");
        
        // PSO
        if (settings.PSO.SwarmSize < 2)
            errors.Add("PSO.SwarmSize は 2 以上である必要があります");
        if (settings.PSO.MaxIterations < 1)
            errors.Add("PSO.MaxIterations は 1 以上である必要があります");
        if (settings.PSO.W < 0)
            errors.Add("PSO.W は 0 以上である必要があります");
        if (settings.PSO.C1 < 0)
            errors.Add("PSO.C1 は 0 以上である必要があります");
        if (settings.PSO.C2 < 0)
            errors.Add("PSO.C2 は 0 以上である必要があります");
        
        // CMA-ES
        if (settings.CMAES.MaxIterations < 1)
            errors.Add("CMAES.MaxIterations は 1 以上である必要があります");
        if (settings.CMAES.InitialSigmaU <= 0)
            errors.Add("CMAES.InitialSigmaU は 0 より大きい必要があります");
        
        // GWO
        if (settings.GWO.PackSize < 4)
            errors.Add("GWO.PackSize は 4 以上である必要があります");
        if (settings.GWO.MaxIterations < 1)
            errors.Add("GWO.MaxIterations は 1 以上である必要があります");
        
        // NelderMead
        if (settings.NelderMead.MaxIterations < 1)
            errors.Add("NelderMead.MaxIterations は 1 以上である必要があります");
        if (settings.NelderMead.Alpha <= 0)
            errors.Add("NelderMead.Alpha は 0 より大きい必要があります");
        if (settings.NelderMead.Gamma <= 1)
            errors.Add("NelderMead.Gamma は 1 より大きい必要があります");
        if (settings.NelderMead.Rho <= 0 || settings.NelderMead.Rho >= 1)
            errors.Add("NelderMead.Rho は 0 < ρ < 1 の範囲である必要があります");
        if (settings.NelderMead.Sigma <= 0 || settings.NelderMead.Sigma >= 1)
            errors.Add("NelderMead.Sigma は 0 < σ < 1 の範囲である必要があります");
        
        // GridSearchGradient
        if (settings.GridSearchGradient.MaxIterations < 1)
            errors.Add("GridSearchGradient.MaxIterations は 1 以上である必要があります");
        if (settings.GridSearchGradient.LearningRate <= 0)
            errors.Add("GridSearchGradient.LearningRate は 0 より大きい必要があります");
        if (settings.GridSearchGradient.Delta <= 0)
            errors.Add("GridSearchGradient.Delta は 0 より大きい必要があります");
    }
    
    private static void ValidateModelInitializationSettings(ModelInitializationSettings settings, List<string> errors)
    {
        // スケール係数
        if (settings.ScaleFactorA.ConvergedMin <= 0)
            errors.Add("ScaleFactorA.ConvergedMin は 0 より大きい必要があります");
        if (settings.ScaleFactorA.ConvergedMax < settings.ScaleFactorA.ConvergedMin)
            errors.Add("ScaleFactorA.ConvergedMax は ConvergedMin 以上である必要があります");
        if (settings.ScaleFactorA.NotConvergedMin <= 0)
            errors.Add("ScaleFactorA.NotConvergedMin は 0 より大きい必要があります");
        if (settings.ScaleFactorA.NotConvergedMax < settings.ScaleFactorA.NotConvergedMin)
            errors.Add("ScaleFactorA.NotConvergedMax は NotConvergedMin 以上である必要があります");
        
        // 増分しきい値
        if (settings.IncrementThreshold.ConvergenceThreshold < 0)
            errors.Add("IncrementThreshold.ConvergenceThreshold は 0 以上である必要があります");
        
        // 平均傾き閾値
        if (settings.AverageSlopeThresholds.VeryLow < 0)
            errors.Add("AverageSlopeThresholds.VeryLow は 0 以上である必要があります");
        if (settings.AverageSlopeThresholds.Low < settings.AverageSlopeThresholds.VeryLow)
            errors.Add("AverageSlopeThresholds.Low は VeryLow 以上である必要があります");
        if (settings.AverageSlopeThresholds.Medium < settings.AverageSlopeThresholds.Low)
            errors.Add("AverageSlopeThresholds.Medium は Low 以上である必要があります");
    }
    
    private static void ValidateChangePointSettings(ChangePointSettings settings, List<string> errors)
    {
        if (settings.CumulativeRatio <= 0 || settings.CumulativeRatio >= 1)
            errors.Add("ChangePoint.CumulativeRatio は 0 < ratio < 1 の範囲である必要があります");
        
        // 複数変化点の比率が正しいか
        foreach (var ratio in settings.MultipleChangePoints.TwoPointRatios)
        {
            if (ratio <= 0 || ratio >= 1)
                errors.Add("MultipleChangePoints.TwoPointRatios の各値は 0 < ratio < 1 の範囲である必要があります");
        }
        
        foreach (var ratio in settings.MultipleChangePoints.ThreePointRatios)
        {
            if (ratio <= 0 || ratio >= 1)
                errors.Add("MultipleChangePoints.ThreePointRatios の各値は 0 < ratio < 1 の範囲である必要があります");
        }
    }
    
    private static void ValidateImperfectDebugSettings(ImperfectDebugDefaults settings, List<string> errors)
    {
        if (settings.P0 < -1 || settings.P0 >= 1)
            errors.Add("ImperfectDebug.P0 は -1 ≤ p < 1 の範囲である必要があります");
        
        if (settings.Eta0 <= 0 || settings.Eta0 > 1)
            errors.Add("ImperfectDebug.Eta0 は 0 < η₀ ≤ 1 の範囲である必要があります");
        
        if (settings.EtaInfinity <= 0 || settings.EtaInfinity > 1)
            errors.Add("ImperfectDebug.EtaInfinity は 0 < η∞ ≤ 1 の範囲である必要があります");
        
        if (settings.EtaInfinity < settings.Eta0)
            errors.Add("ImperfectDebug.EtaInfinity は Eta0 以上である必要があります（学習効果）");
        
        if (settings.Alpha0 < 0 || settings.Alpha0 >= 1)
            errors.Add("ImperfectDebug.Alpha0 は 0 ≤ α < 1 の範囲である必要があります");
        
        if (settings.GompertzB0 <= 0)
            errors.Add("ImperfectDebug.GompertzB0 は 0 より大きい必要があります");
    }
    
    #endregion
}
