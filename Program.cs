using BugConvergenceTool.Models;
using BugConvergenceTool.Optimizers;
using BugConvergenceTool.Services;

namespace BugConvergenceTool;

class Program
{
    static int Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        
        PrintHeader();
        
        // 引数解析
        var options = ParseArguments(args);
        
        if (options.ShowHelp || string.IsNullOrEmpty(options.InputFile))
        {
            PrintUsage();
            return options.ShowHelp ? 0 : 1;
        }
        
        try
        {
            return RunAnalysis(options);
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"\nエラー: {ex.Message}");
            Console.ResetColor();
            
            if (options.Verbose)
            {
                Console.WriteLine($"\n詳細:\n{ex.StackTrace}");
            }
            
            return 1;
        }
    }
    
    static int RunAnalysis(CommandOptions options)
    {
        // 1. 入力ファイルの読み込み
        Console.WriteLine($"入力ファイル: {options.InputFile}");
        
        var reader = new ExcelReader();
        var testData = reader.ReadFromExcel(options.InputFile);
        
        Console.WriteLine($"プロジェクト: {testData.ProjectName}");
        Console.WriteLine($"データ件数: {testData.DayCount} 日分");
        if (testData.StartDate.HasValue)
            Console.WriteLine($"テスト開始日: {testData.StartDate.Value:yyyy/MM/dd}");
        Console.WriteLine($"オプティマイザ: {options.Optimizer}");
        
        // 使用モデルの表示
        var modelTypes = new List<string> { "基本" };
        if (options.IncludeImperfectDebug) modelTypes.Add("不完全デバッグ");
        if (options.IncludeChangePoint) modelTypes.Add("変化点");
        if (options.IncludeTEF) modelTypes.Add("TEF組込");
        if (options.IncludeFRE) modelTypes.Add("FRE");
        Console.WriteLine($"モデル: {string.Join(", ", modelTypes)}");
        Console.WriteLine();
        
        // 2. モデルフィッティング
        Console.WriteLine("モデルフィッティング中...");
        var fitter = new ModelFitter(testData, options.Optimizer, options.Verbose);
        
        List<FittingResult> results;
        if (options.AllExtended || options.IncludeChangePoint || options.IncludeTEF || options.IncludeFRE)
        {
            // 拡張モデルを使用
            var models = ModelFactory.GetAllExtendedModels(
                options.IncludeChangePoint,
                options.IncludeTEF,
                options.IncludeFRE);
            results = models.Select(m => fitter.FitModel(m)).ToList();
        }
        else
        {
            results = fitter.FitAllModels(options.IncludeImperfectDebug);
        }
        
        var bestResult = fitter.GetBestModel(results);
        if (bestResult == null)
        {
            Console.WriteLine("エラー: フィッティングに成功したモデルがありません。");
            return 1;
        }
        
        // 3. 結果表示
        PrintResults(results, bestResult, options.Verbose);
        
        // 4. 出力ディレクトリ作成
        string outputDir = options.OutputDir ?? Path.GetDirectoryName(options.InputFile) ?? ".";
        string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        string outputBase = Path.Combine(outputDir, $"Result_{timestamp}");
        
        Directory.CreateDirectory(outputDir);
        
        // 5. Excel出力
        string templatePath = FindTemplatePath();
        string excelPath = $"{outputBase}.xlsx";
        
        var writer = new ExcelWriter(testData);
        writer.WriteResults(templatePath, excelPath, results, bestResult);
        Console.WriteLine($"\nExcel出力: {excelPath}");
        
        // 6. テキストレポート出力
        string reportPath = $"{outputBase}.txt";
        var reportGen = new ReportGenerator(testData);
        reportGen.SaveReport(reportPath, results, bestResult);
        Console.WriteLine($"レポート出力: {reportPath}");
        
        // 7. グラフ画像出力
        string chartsDir = Path.Combine(outputDir, $"Charts_{timestamp}");
        var chartGen = new ChartGenerator(testData);
        chartGen.GenerateAllCharts(chartsDir, bestResult);
        Console.WriteLine($"グラフ出力: {chartsDir}/");
        
        Console.WriteLine("\n処理完了！");
        
        return 0;
    }
    
    static void PrintResults(List<FittingResult> results, FittingResult bestResult, bool verbose = false)
    {
        Console.WriteLine("\n=== モデル比較結果 ===\n");
        
        // ヘッダー
        if (verbose)
            Console.WriteLine($"{"モデル名",-25} {"カテゴリ",-12} {"R²",10} {"AIC",10} {"潜在バグ",10} {"時間(ms)",10}");
        else
            Console.WriteLine($"{"モデル名",-25} {"カテゴリ",-12} {"R²",10} {"AIC",10} {"潜在バグ",10}");
        Console.WriteLine(new string('-', verbose ? 85 : 70));
        
        foreach (var result in results.Where(r => r.Success).OrderBy(r => r.AIC))
        {
            bool isBest = result.ModelName == bestResult.ModelName;
            
            if (isBest)
                Console.ForegroundColor = ConsoleColor.Green;
            else if (result.Category == "不完全デバッグ")
                Console.ForegroundColor = ConsoleColor.Yellow;
            
            string marker = isBest ? " *" : "";
            if (verbose)
                Console.WriteLine($"{result.ModelName + marker,-25} {result.Category,-12} {result.R2,10:F4} {result.AIC,10:F2} {result.EstimatedTotalBugs,10:F1} {result.OptimizationTimeMs,10}");
            else
                Console.WriteLine($"{result.ModelName + marker,-25} {result.Category,-12} {result.R2,10:F4} {result.AIC,10:F2} {result.EstimatedTotalBugs,10:F1}");
            
            Console.ResetColor();
        }
        
        Console.WriteLine("\n* = 推奨モデル（AIC最小）");
        
        // 収束予測
        Console.WriteLine($"\n=== 収束予測（{bestResult.ModelName}）===\n");
        
        Console.WriteLine($"推定潜在バグ総数: {bestResult.EstimatedTotalBugs:F1} 件");
        Console.WriteLine($"残り推定バグ数: {bestResult.EstimatedTotalBugs - bestResult.PredictedValues.Last():F1} 件");
        Console.WriteLine();
        
        foreach (var (name, pred) in bestResult.ConvergencePredictions)
        {
            if (pred.AlreadyReached)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"  {name}: 到達済み");
            }
            else if (pred.PredictedDay.HasValue)
            {
                Console.WriteLine($"  {name}: {pred.PredictedDay:F1}日目 (残り{pred.RemainingDays:F1}日" +
                    (pred.PredictedDate.HasValue ? $", {pred.PredictedDate:yyyy/MM/dd}" : "") + ")");
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.WriteLine($"  {name}: 予測不可");
            }
            Console.ResetColor();
        }
    }
    
    static string FindTemplatePath()
    {
        // 実行ファイルと同じディレクトリ
        string exeDir = AppDomain.CurrentDomain.BaseDirectory;
        string[] searchPaths = new[]
        {
            Path.Combine(exeDir, "Templates", "Template.xlsx"),
            Path.Combine(exeDir, "Template.xlsx"),
            Path.Combine("Templates", "Template.xlsx"),
            "Template.xlsx",
        };
        
        foreach (var path in searchPaths)
        {
            if (File.Exists(path))
                return path;
        }
        
        throw new FileNotFoundException(
            "テンプレートファイル（Template.xlsx）が見つかりません。\n" +
            "Templatesフォルダにテンプレートを配置してください。");
    }
    
    static CommandOptions ParseArguments(string[] args)
    {
        var options = new CommandOptions();
        
        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "-h":
                case "--help":
                    options.ShowHelp = true;
                    break;
                    
                case "-o":
                case "--output":
                    if (i + 1 < args.Length)
                        options.OutputDir = args[++i];
                    break;
                    
                case "--basic-only":
                    options.IncludeImperfectDebug = false;
                    break;
                    
                case "-v":
                case "--verbose":
                    options.Verbose = true;
                    break;
                
                case "--optimizer":
                    if (i + 1 < args.Length)
                    {
                        string opt = args[++i].ToLower();
                        options.Optimizer = opt switch
                        {
                            "pso" => OptimizerType.PSO,
                            "de" => OptimizerType.DifferentialEvolution,
                            "gwo" => OptimizerType.GWO,
                            "grid" => OptimizerType.GridSearchGradient,
                            "auto" => OptimizerType.AutoSelect,
                            _ => OptimizerType.DifferentialEvolution
                        };
                    }
                    break;
                
                // 拡張モデルオプション
                case "--change-point":
                    options.IncludeChangePoint = true;
                    break;
                    
                case "--tef":
                    options.IncludeTEF = true;
                    break;
                    
                case "--fre":
                    options.IncludeFRE = true;
                    break;
                    
                case "--all-extended":
                    options.AllExtended = true;
                    options.IncludeChangePoint = true;
                    options.IncludeTEF = true;
                    options.IncludeFRE = true;
                    break;
                    
                default:
                    if (!args[i].StartsWith("-"))
                        options.InputFile = args[i];
                    break;
            }
        }
        
        return options;
    }
    
    static void PrintHeader()
    {
        Console.WriteLine();
        Console.WriteLine("┌─────────────────────────────────────────────────────────┐");
        Console.WriteLine("│         バグ収束推定ツール (Bug Convergence Tool)       │");
        Console.WriteLine("│              信頼度成長モデルによる品質分析              │");
        Console.WriteLine("└─────────────────────────────────────────────────────────┘");
        Console.WriteLine();
    }
    
    static void PrintUsage()
    {
        Console.WriteLine("使用方法:");
        Console.WriteLine("  BugConvergenceTool <入力Excel> [オプション]");
        Console.WriteLine();
        Console.WriteLine("オプション:");
        Console.WriteLine("  -h, --help            ヘルプを表示");
        Console.WriteLine("  -o, --output DIR      出力ディレクトリを指定");
        Console.WriteLine("  --basic-only          基本モデルのみ使用（不完全デバッグモデルを除外）");
        Console.WriteLine("  -v, --verbose         詳細出力");
        Console.WriteLine();
        Console.WriteLine("  --optimizer TYPE      最適化アルゴリズムを指定:");
        Console.WriteLine("                          de   - 差分進化（デフォルト、推奨）");
        Console.WriteLine("                          pso  - 粒子群最適化");
        Console.WriteLine("                          gwo  - Grey Wolf Optimizer");
        Console.WriteLine("                          grid - グリッドサーチ+勾配降下法（従来手法）");
        Console.WriteLine("                          auto - 全アルゴリズムで比較し最良を選択");
        Console.WriteLine();
        Console.WriteLine("拡張モデルオプション:");
        Console.WriteLine("  --change-point        変化点モデルを含める");
        Console.WriteLine("  --tef                 テスト工数関数モデルを含める");
        Console.WriteLine("  --fre                 欠陥除去効率モデルを含める");
        Console.WriteLine("  --all-extended        全拡張モデルを含める");
        Console.WriteLine();
        Console.WriteLine("使用例:");
        Console.WriteLine("  BugConvergenceTool TestData.xlsx");
        Console.WriteLine("  BugConvergenceTool TestData.xlsx -o ./output");
        Console.WriteLine("  BugConvergenceTool TestData.xlsx --optimizer pso");
        Console.WriteLine("  BugConvergenceTool TestData.xlsx --change-point --fre");
        Console.WriteLine("  BugConvergenceTool TestData.xlsx --all-extended -v");
        Console.WriteLine();
        Console.WriteLine("入力Excelの形式:");
        Console.WriteLine("  「データ入力」シートに以下の形式でデータを配置:");
        Console.WriteLine("       |  B  |  C  |  D  | ...");
        Console.WriteLine("  -----+-----+-----+-----+----");
        Console.WriteLine("  行6  | 日付| 日付| 日付| ...");
        Console.WriteLine("  行7  | 予定消化（日次）");
        Console.WriteLine("  行8  | 実績消化（日次）");
        Console.WriteLine("  行9  | バグ発生（日次）");
        Console.WriteLine("  行10 | バグ修正（日次）");
        Console.WriteLine();
    }
}

class CommandOptions
{
    public string? InputFile { get; set; }
    public string? OutputDir { get; set; }
    public bool ShowHelp { get; set; }
    public bool IncludeImperfectDebug { get; set; } = true;
    public bool Verbose { get; set; }
    public OptimizerType Optimizer { get; set; } = OptimizerType.DifferentialEvolution;
    
    // 拡張モデルオプション
    public bool IncludeChangePoint { get; set; } = false;
    public bool IncludeTEF { get; set; } = false;
    public bool IncludeFRE { get; set; } = false;
    public bool AllExtended { get; set; } = false;
}
