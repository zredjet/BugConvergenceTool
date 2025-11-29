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
        
        // 設定ファイルの読み込み
        if (!string.IsNullOrEmpty(options.ConfigFile))
        {
            ConfigurationService.Load(options.ConfigFile);
        }
        else
        {
            ConfigurationService.Load();
        }
        
        // 設定の検証
        var validationErrors = ConfigurationService.Validate();
        if (validationErrors.Count > 0)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("設定ファイルに警告があります:");
            foreach (var error in validationErrors)
            {
                Console.WriteLine($"  - {error}");
            }
            Console.ResetColor();
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
        Console.WriteLine($"損失関数: {(options.LossFunction == LossType.Mle ? "MLE (最尤推定)" : "SSE (残差二乗和)")}");
        
        // ホールドアウト検証の表示
        if (options.HoldoutDays > 0)
        {
            Console.WriteLine($"ホールドアウト検証: 末尾 {options.HoldoutDays} 日");
        }
        
        // 使用モデルの表示
        var modelTypes = new List<string> { "基本" };
        if (options.IncludeImperfectDebug) modelTypes.Add("不完全デバッグ");
        if (options.IncludeChangePoint) modelTypes.Add("変化点");
        if (options.IncludeTEF) modelTypes.Add("TEF組込");
        if (options.IncludeFRE) modelTypes.Add("FRE");
        if (options.IncludeCoverage) modelTypes.Add("Coverage");
        Console.WriteLine($"モデル: {string.Join(", ", modelTypes)}");
        Console.WriteLine();
        
        // 2. モデルフィッティング
        Console.WriteLine("モデルフィッティング中...");
        var fitter = new ModelFitter(
            testData, 
            options.Optimizer, 
            options.Verbose,
            options.LossFunction,
            options.HoldoutDays);
        
        List<FittingResult> results;
        if (options.AllExtended || options.IncludeChangePoint || options.IncludeTEF || options.IncludeFRE || options.IncludeCoverage)
        {
            // 拡張モデルを使用
            var models = ModelFactory.GetAllExtendedModels(
                options.IncludeChangePoint,
                options.IncludeTEF,
                options.IncludeFRE,
                options.IncludeCoverage);
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
        
        // 2.5. 信頼区間の計算（オプション指定時）
        if (options.CalculateConfidenceInterval)
        {
            // 設定から反復回数を取得（CLIオプションで上書き可能）
            var bootstrapSettings = ConfigurationService.Current.Bootstrap;
            int iterations = options.BootstrapIterations > 0 
                ? options.BootstrapIterations 
                : bootstrapSettings.Iterations;
            
            // CLIで指定された場合は設定を上書き
            if (options.BootstrapIterations > 0)
            {
                bootstrapSettings = new BootstrapSettings
                {
                    Iterations = iterations,
                    ConfidenceLevel = bootstrapSettings.ConfidenceLevel,
                    OptimizerMaxIterations = bootstrapSettings.OptimizerMaxIterations,
                    OptimizerTolerance = bootstrapSettings.OptimizerTolerance,
                    SSEThresholdMultiplier = bootstrapSettings.SSEThresholdMultiplier
                };
            }
            
            Console.WriteLine($"{bootstrapSettings.ConfidenceLevel * 100:F0}%信頼区間をブートストラップで計算中（{iterations}回）...");
            
            // ベストモデルのインスタンスを取得
            var bestModel = GetModelByName(bestResult.ModelName);
            if (bestModel != null)
            {
                var ciService = new ConfidenceIntervalService(bootstrapSettings, options.Verbose);
                
                try
                {
                    ciService.CalculateIntervals(
                        bestModel,
                        testData.GetTimeData(),
                        testData.GetCumulativeBugsFound(),
                        bestResult
                    );
                    Console.WriteLine("  信頼区間の計算完了");
                }
                catch (Exception ex)
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine($"  信頼区間の計算に失敗: {ex.Message}");
                    Console.ResetColor();
                }
            }
        }
        
        // 3. 結果表示
        PrintResults(results, bestResult, testData, options.Verbose);
        
        // 3.5. 警告メッセージの生成と表示
        var warnings = WarningService.GenerateAllWarnings(
            bestResult,
            results,
            testData.DayCount,
            testData.GetCumulativeBugsFound().LastOrDefault());
        WarningService.PrintWarnings(warnings);
        
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
        reportGen.SaveReport(reportPath, results, bestResult, warnings);
        Console.WriteLine($"レポート出力: {reportPath}");
        
        // 7. グラフ画像出力
        string chartsDir = Path.Combine(outputDir, $"Charts_{timestamp}");
        var chartGen = new ChartGenerator(testData);
        chartGen.GenerateAllCharts(chartsDir, bestResult);
        Console.WriteLine($"グラフ出力: {chartsDir}/");
        
        Console.WriteLine("\n処理完了！");
        
        return 0;
    }
    
    /// <summary>
    /// 文字列の表示幅を計算（全角文字は2、半角文字は1）
    /// </summary>
    static int GetDisplayWidth(string s)
    {
        if (string.IsNullOrEmpty(s)) return 0;
        int width = 0;
        foreach (char c in s)
        {
            // 全角文字（日本語、中国語、記号等）は幅2、それ以外は幅1
            width += IsFullWidth(c) ? 2 : 1;
        }
        return width;
    }
    
    /// <summary>
    /// 全角文字かどうかを判定
    /// </summary>
    static bool IsFullWidth(char c)
    {
        // CJK文字、全角記号、ひらがな、カタカナなど
        return (c >= 0x1100 && c <= 0x115F) ||  // Hangul Jamo
               (c >= 0x2E80 && c <= 0x9FFF) ||  // CJK
               (c >= 0xAC00 && c <= 0xD7A3) ||  // Hangul Syllables
               (c >= 0xF900 && c <= 0xFAFF) ||  // CJK Compatibility Ideographs
               (c >= 0xFE10 && c <= 0xFE1F) ||  // Vertical Forms
               (c >= 0xFE30 && c <= 0xFE6F) ||  // CJK Compatibility Forms
               (c >= 0xFF00 && c <= 0xFF60) ||  // Fullwidth Forms
               (c >= 0xFFE0 && c <= 0xFFE6) ||  // Fullwidth Forms
               (c >= 0x3000 && c <= 0x303F) ||  // CJK Symbols and Punctuation
               (c >= 0x3040 && c <= 0x309F) ||  // Hiragana
               (c >= 0x30A0 && c <= 0x30FF) ||  // Katakana
               (c >= 0x31F0 && c <= 0x31FF);    // Katakana Phonetic Extensions
    }
    
    /// <summary>
    /// 文字列を指定幅に左寄せでパディング
    /// </summary>
    static string PadRightByWidth(string s, int totalWidth)
    {
        int currentWidth = GetDisplayWidth(s);
        int padding = totalWidth - currentWidth;
        return padding > 0 ? s + new string(' ', padding) : s;
    }
    
    /// <summary>
    /// 文字列を指定幅に右寄せでパディング
    /// </summary>
    static string PadLeftByWidth(string s, int totalWidth)
    {
        int currentWidth = GetDisplayWidth(s);
        int padding = totalWidth - currentWidth;
        return padding > 0 ? new string(' ', padding) + s : s;
    }
    
    static void PrintResults(List<FittingResult> results, FittingResult bestResult, TestData testData, bool verbose = false)
    {
        Console.WriteLine("\n=== モデル比較結果 ===\n");
        
        // ホールドアウト検証の有無を判定
        bool hasHoldout = results.Any(r => r.HoldoutMse.HasValue);
        
        // カラム幅の定義
        const int colModel = 28;
        const int colCategory = 14;
        const int colNum = 10;
        
        // ヘッダー
        if (hasHoldout)
        {
            if (verbose)
                Console.WriteLine($"{PadRightByWidth("モデル名", colModel)} {PadRightByWidth("カテゴリ", colCategory)} {"R²",colNum} {"AIC",colNum} {"MAPE(%)",colNum} {"潜在バグ",colNum} {"時間(ms)",colNum}");
            else
                Console.WriteLine($"{PadRightByWidth("モデル名", colModel)} {PadRightByWidth("カテゴリ", colCategory)} {"R²",colNum} {"AIC",colNum} {"MAPE(%)",colNum} {"潜在バグ",colNum}");
            Console.WriteLine(new string('-', verbose ? 105 : 92));
        }
        else
        {
            if (verbose)
                Console.WriteLine($"{PadRightByWidth("モデル名", colModel)} {PadRightByWidth("カテゴリ", colCategory)} {"R²",colNum} {"AIC",colNum} {"潜在バグ",colNum} {"時間(ms)",colNum}");
            else
                Console.WriteLine($"{PadRightByWidth("モデル名", colModel)} {PadRightByWidth("カテゴリ", colCategory)} {"R²",colNum} {"AIC",colNum} {"潜在バグ",colNum}");
            Console.WriteLine(new string('-', verbose ? 93 : 80));
        }
        
        foreach (var result in results.Where(r => r.Success && !r.ModelSelectionCriterion.StartsWith("Invalid")).OrderBy(r => r.SelectionScore))
        {
            bool isBest = result.ModelName == bestResult.ModelName;
            
            if (isBest)
                Console.ForegroundColor = ConsoleColor.Green;
            else if (result.Category == "不完全デバッグ")
                Console.ForegroundColor = ConsoleColor.Yellow;
            
            string marker = isBest ? " *" : "";
            string modelNameWithMarker = result.ModelName + marker;
            string mapeStr = result.HoldoutMape.HasValue ? $"{result.HoldoutMape:F2}" : "-";
            
            if (hasHoldout)
            {
                if (verbose)
                    Console.WriteLine($"{PadRightByWidth(modelNameWithMarker, colModel)} {PadRightByWidth(result.Category, colCategory)} {result.R2,colNum:F4} {result.AIC,colNum:F2} {mapeStr,colNum} {result.EstimatedTotalBugs,colNum:F1} {result.OptimizationTimeMs,colNum}");
                else
                    Console.WriteLine($"{PadRightByWidth(modelNameWithMarker, colModel)} {PadRightByWidth(result.Category, colCategory)} {result.R2,colNum:F4} {result.AIC,colNum:F2} {mapeStr,colNum} {result.EstimatedTotalBugs,colNum:F1}");
            }
            else
            {
                if (verbose)
                    Console.WriteLine($"{PadRightByWidth(modelNameWithMarker, colModel)} {PadRightByWidth(result.Category, colCategory)} {result.R2,colNum:F4} {result.AIC,colNum:F2} {result.EstimatedTotalBugs,colNum:F1} {result.OptimizationTimeMs,colNum}");
                else
                    Console.WriteLine($"{PadRightByWidth(modelNameWithMarker, colModel)} {PadRightByWidth(result.Category, colCategory)} {result.R2,colNum:F4} {result.AIC,colNum:F2} {result.EstimatedTotalBugs,colNum:F1}");
            }
            
            Console.ResetColor();
        }
        
        // 使用された評価基準を表示
        var criterion = bestResult.ModelSelectionCriterion;
        Console.WriteLine($"\n* = 推奨モデル（{criterion}最小）");
        if (criterion == "AICc")
        {
            int n = testData.DayCount;
            int k = bestResult.ParameterVector.Length;
            Console.WriteLine($"  （小標本補正適用: n={n}, k={k}, n/k={n/(double)k:F1} < 40）");
        }
        
        // ホールドアウト検証サマリー
        if (hasHoldout)
        {
            Console.WriteLine("\n=== ホールドアウト検証結果 ===\n");
            var bestByMape = results.Where(r => r.Success && r.HoldoutMape.HasValue)
                                    .OrderBy(r => r.HoldoutMape!.Value)
                                    .FirstOrDefault();
            if (bestByMape != null)
            {
                Console.WriteLine($"予測精度最良モデル（MAPE最小）: {bestByMape.ModelName} (MAPE={bestByMape.HoldoutMape:F2}%)");
            }
            
            // 警告の表示
            var modelsWithHighMape = results.Where(r => r.Success && r.HoldoutMape > 50).ToList();
            if (modelsWithHighMape.Any())
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"注意: {modelsWithHighMape.Count}個のモデルでMAPE > 50%（予測精度が低い可能性）");
                Console.ResetColor();
            }
        }
        
        // 収束予測
        Console.WriteLine($"\n=== 収束予測（{bestResult.ModelName}）===\n");
        
        Console.WriteLine($"推定潜在バグ総数: {bestResult.EstimatedTotalBugs:F1} 件");
        Console.WriteLine($"残り推定バグ数: {bestResult.EstimatedTotalBugs - bestResult.PredictedValues.Last():F1} 件");
        Console.WriteLine($"使用損失関数: {bestResult.LossFunctionUsed}");
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
    
    /// <summary>
    /// モデル名からモデルインスタンスを取得
    /// </summary>
    static ReliabilityGrowthModelBase? GetModelByName(string modelName)
    {
        // 全拡張モデルから検索
        var allModels = ModelFactory.GetAllExtendedModels(
            includeChangePoint: true,
            includeTEF: true,
            includeFRE: true,
            includeCoverage: true);
        
        return allModels.FirstOrDefault(m => m.Name == modelName)
            ?? ModelFactory.GetAllModels().FirstOrDefault(m => m.Name == modelName);
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
                            "nm" => OptimizerType.NelderMead,
                            "cmaes" => OptimizerType.CMAES,
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

                case "--coverage":
                    options.IncludeCoverage = true;
                    break;

                case "--all-extended":
                    options.AllExtended = true;
                    options.IncludeChangePoint = true;
                    options.IncludeTEF = true;
                    options.IncludeFRE = true;
                    options.IncludeCoverage = true;
                    break;
                
                case "-c":
                case "--config":
                    if (i + 1 < args.Length)
                        options.ConfigFile = args[++i];
                    break;
                
                case "--ci":
                case "--confidence-interval":
                    options.CalculateConfidenceInterval = true;
                    break;
                
                case "--bootstrap":
                    if (i + 1 < args.Length && int.TryParse(args[++i], out int bootIter))
                        options.BootstrapIterations = Math.Max(50, bootIter);
                    break;
                
                // 損失関数オプション
                case "--loss":
                    if (i + 1 < args.Length)
                    {
                        string loss = args[++i].ToLower();
                        options.LossFunction = loss switch
                        {
                            "mle" => LossType.Mle,
                            "sse" => LossType.Sse,
                            _ => LossType.Sse
                        };
                    }
                    break;
                
                // ホールドアウト検証オプション
                case "--holdout-days":
                case "--holdout":
                    if (i + 1 < args.Length && int.TryParse(args[++i], out int holdout))
                        options.HoldoutDays = Math.Max(0, holdout);
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
        Console.WriteLine("│              信頼度成長モデルによる品質分析             │");
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
        Console.WriteLine("                          de    - 差分進化（デフォルト、推奨）");
        Console.WriteLine("                          pso   - 粒子群最適化");
        Console.WriteLine("                          gwo   - Grey Wolf Optimizer");
        Console.WriteLine("                          cmaes - CMA-ES（共分散行列適応進化戦略）");
        Console.WriteLine("                          nm    - Nelder-Mead法（局所最適化）");
        Console.WriteLine("                          grid  - グリッドサーチ+勾配降下法（従来手法）");
        Console.WriteLine("                          auto  - 全アルゴリズムで比較し最良を選択");
        Console.WriteLine();
        Console.WriteLine("拡張モデルオプション:");
        Console.WriteLine("  --change-point        変化点モデルを含める");
        Console.WriteLine("  --tef                 テスト工数関数モデルを含める");
        Console.WriteLine("  --fre                 欠陥除去効率モデルを含める");
        Console.WriteLine("  --coverage            Coverageモデルを含める");
        Console.WriteLine("  --all-extended        全拡張モデルを含める");
        Console.WriteLine();
        Console.WriteLine("設定オプション:");
        Console.WriteLine("  -c, --config FILE     設定ファイルを指定");
        Console.WriteLine();
        Console.WriteLine("信頼区間オプション:");
        Console.WriteLine("  --ci, --confidence-interval");
        Console.WriteLine("                        95%信頼区間を計算（ブートストラップ法）");
        Console.WriteLine("  --bootstrap N         ブートストラップ反復回数（デフォルト: 200）");
        Console.WriteLine();
        Console.WriteLine("推定・検証オプション:");
        Console.WriteLine("  --loss TYPE           損失関数を指定:");
        Console.WriteLine("                          sse - 残差二乗和（デフォルト）");
        Console.WriteLine("                          mle - 最尤推定（Poisson-NHPP）");
        Console.WriteLine("  --holdout-days N      末尾N日をホールドアウト検証に使用");
        Console.WriteLine("                        （訓練データで推定し、テストデータで予測精度を評価）");
        Console.WriteLine();
        Console.WriteLine("使用例:");
        Console.WriteLine("  BugConvergenceTool TestData.xlsx");
        Console.WriteLine("  BugConvergenceTool TestData.xlsx -o ./output");
        Console.WriteLine("  BugConvergenceTool TestData.xlsx --optimizer pso");
        Console.WriteLine("  BugConvergenceTool TestData.xlsx --change-point --fre");
        Console.WriteLine("  BugConvergenceTool TestData.xlsx --all-extended -v");
        Console.WriteLine("  BugConvergenceTool TestData.xlsx --loss mle --holdout-days 5");
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
        Console.WriteLine("設定ファイル:");
        Console.WriteLine("  config.json を実行ディレクトリまたは Templates フォルダに配置すると");
        Console.WriteLine("  オプティマイザやモデルのパラメータをカスタマイズできます。");
        Console.WriteLine("  --config オプションで明示的に指定することも可能です。");
        Console.WriteLine();
    }
}

class CommandOptions
{
    public string? InputFile { get; set; }
    public string? OutputDir { get; set; }
    public string? ConfigFile { get; set; }
    public bool ShowHelp { get; set; }
    public bool IncludeImperfectDebug { get; set; } = true;
    public bool Verbose { get; set; }
    public OptimizerType Optimizer { get; set; } = OptimizerType.DifferentialEvolution;
    
    // 拡張モデルオプション
    public bool IncludeChangePoint { get; set; } = false;
    public bool IncludeTEF { get; set; } = false;
    public bool IncludeFRE { get; set; } = false;
    public bool IncludeCoverage { get; set; } = false;
    public bool AllExtended { get; set; } = false;
    
    // 信頼区間オプション
    public bool CalculateConfidenceInterval { get; set; } = false;
    public int BootstrapIterations { get; set; } = 200;
    
    // 損失関数オプション
    public LossType LossFunction { get; set; } = LossType.Sse;
    
    // ホールドアウト検証オプション
    public int HoldoutDays { get; set; } = 0;
}
