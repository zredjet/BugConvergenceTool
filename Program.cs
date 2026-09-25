using BugConvergenceTool.Models;
using BugConvergenceTool.Optimizers;
using BugConvergenceTool.Services;
using BugConvergenceTool.Services.Diagnostics;

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
        
        if (options.Notices.Count > 0)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            foreach (var notice in options.Notices)
                Console.WriteLine($"注意: {notice}");
            Console.ResetColor();
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
        if (options.IncludeChangePoint) modelTypes.Add("変化点");
        if (options.IncludeTEF) modelTypes.Add("TEF組込");
        if (options.IncludeFRE) modelTypes.Add("FRE");
        Console.WriteLine($"モデル: {string.Join(", ", modelTypes)}");
        Console.WriteLine();
        
        // 2. モデルフィッティング
        Console.WriteLine("モデルフィッティング中...");
        var fitter = new ModelFitter(
            testData, 
            options.Optimizer, 
            options.Verbose,
            options.LossFunction,
            options.HoldoutDays,
            options.MultiStarts);
        
        if (options.MultiStarts > 1 && options.Optimizer == OptimizerType.AutoSelect)
        {
            Console.WriteLine("注意: --optimizer auto では全アルゴリズムを比較するため、--multi-start は使用しません。");
        }
        
        List<FittingResult> results;
        if (options.AllExtended || options.IncludeChangePoint || options.IncludeTEF || options.IncludeFRE)
        {
            // 拡張モデルを使用
            var models = ModelFactory.GetAllExtendedModels(
                options.IncludeChangePoint,
                options.IncludeTEF,
                options.IncludeFRE);
            results = fitter.FitModels(models);
        }
        else
        {
            results = fitter.FitAllModels();
        }
        
        // 2.3. 変化点の尤度比検定（AIC は変化点 τ を過小に罰するため、有意でない変化点モデルは推奨しない）
        if (results.Any(r => r.Success && r.Model is ChangePointModelBase))
        {
            if (options.LrtIterations > 0)
            {
                Console.WriteLine($"変化点の尤度比検定中（シミュレーション {options.LrtIterations} 回）...");
                var stopwatch = System.Diagnostics.Stopwatch.StartNew();
                int tested = fitter.TestChangePoints(results, options.LrtIterations);
                Console.WriteLine(tested > 0
                    ? $"  {tested} モデルを検定しました（{stopwatch.Elapsed.TotalSeconds:F1}秒）"
                    : "  検定は不要でした（変化点モデルより AIC の小さい変化点なしのモデルがあるため）");
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("注意: --lrt-iterations 0 のため変化点の尤度比検定を省略します。AIC は変化点モデルを過大に有利にするため、変化点モデルが推奨された場合は注意してください。");
                Console.ResetColor();
            }
        }
        
        var bestResult = fitter.GetBestModel(results);
        if (bestResult == null)
        {
            Console.WriteLine("エラー: フィッティングに成功したモデルがありません。");
            return 1;
        }
        var bestModel = bestResult.Model!;
        var tData = testData.GetTimeData();
        var yData = testData.GetCumulativeBugsFound();
        
        // 2.5. 信頼区間・予測区間（パラメトリック・ブートストラップは両者で共有する）
        if (options.CalculateConfidenceInterval || options.CalculatePredictionInterval)
        {
            CalculateIntervals(options, fitter, bestResult, tData, yData, testData.DayCount);
        }
        
        // 3. 結果表示
        PrintResults(results, bestResult, testData, options.Verbose);
        PrintConfidenceBand(bestResult);
        PrintFisherIntervals(bestResult);
        PrintPredictionIntervals(bestResult, testData);
        
        // 3.3. 統計診断の実行（Phase 1-2）
        if (options.RunDiagnostics)
        {
            Console.WriteLine("\n統計診断を実行中...");
            var diagnosticGenerator = new DiagnosticReportGenerator();
            var gofTest = new GoodnessOfFitTest();
            
            // ベストモデルの診断
            {
                try
                {
                    // 残差診断
                    bestResult.Diagnostics = diagnosticGenerator.Generate(
                        bestModel, tData, yData, bestResult.ParameterVector);
                    
                    // 適合度検定
                    bestResult.GoodnessOfFit = gofTest.Test(
                        bestModel, tData, yData, bestResult.ParameterVector);
                    
                    // 診断レポートを表示
                    Console.WriteLine(DiagnosticReportGenerator.FormatReport(bestResult.Diagnostics));
                    
                    // 適合度検定結果を表示
                    Console.WriteLine($"【適合度検定】{bestResult.GoodnessOfFit.OverallAssessment}");
                    Console.WriteLine($"  χ²検定: χ²={bestResult.GoodnessOfFit.ChiSquareStatistic:F2} (df={bestResult.GoodnessOfFit.ChiSquareDegreesOfFreedom}, p={bestResult.GoodnessOfFit.ChiSquarePValue:F4})");
                    Console.WriteLine($"  KS検定: D={bestResult.GoodnessOfFit.KsStatistic:F4} (p={bestResult.GoodnessOfFit.KsPValue:F4})");
                    Console.WriteLine($"  CvM検定: W²={bestResult.GoodnessOfFit.CramerVonMisesStatistic:F4} (p={bestResult.GoodnessOfFit.CramerVonMisesPValue:F4})");
                    Console.WriteLine();
                }
                catch (Exception ex)
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine($"  診断の実行に失敗: {ex.Message}");
                    Console.ResetColor();
                }
            }
        }
        
        // 3.4. モデル平均化（Phase 4）
        ModelAveragingResult? averagingResult = null;
        if (options.UseModelAveraging)
        {
            Console.WriteLine("\nモデル平均化を実行中...");
            var averagingService = new ModelAveragingService();
            // 推定に使ったモデルのインスタンス（TEF の工数データ等を保持）を使う
            var models = results
                .Where(r => r.Success && r.Model != null)
                .GroupBy(r => r.ModelName)
                .ToDictionary(g => g.Key, g => g.First().Model!);
            var predictionTimes = tData;
            
            averagingResult = averagingService.Average(
                results, models, predictionTimes, testData.DayCount);
            
            // 結果を表示
            Console.WriteLine(ModelAveragingService.FormatResult(averagingResult));
        }
        
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
        // 現在の状況を表示
        PrintCurrentStatus(testData);
        
        Console.WriteLine("\n=== モデル比較結果 ===\n");
        
        // ホールドアウト検証の有無を判定
        bool hasHoldout = results.Any(r => r.Holdout != null);
        
        // カラム幅の定義
        const int colModel = 28;
        const int colCategory = 14;
        const int colNum = 10;
        int lineWidth = colModel + colCategory + colNum * (4 + (hasHoldout ? 1 : 0) + (verbose ? 1 : 0)) + 6;
        
        // AIC は同じデータ・同じ尤度のモデル同士でしか比較できないため、比較グループごとに表示する
        foreach (var (group, groupResults) in ModelComparisonGroup.GroupAndRank(results))
        {
            string criterionName = groupResults[0].ModelSelectionCriterion;
            double minScore = groupResults[0].SelectionScore;
            
            Console.WriteLine($"【比較グループ: {group}】{ModelComparisonGroup.Describe(group)}");
            
            string header = $"{PadRightByWidth("モデル名", colModel)} {PadRightByWidth("カテゴリ", colCategory)} {"R²",colNum} {criterionName,colNum} {"Δ" + criterionName,colNum}";
            if (hasHoldout) header += $" {"HO誤差(%)",colNum}";
            header += $" {"潜在バグ",colNum}";
            if (verbose) header += $" {"時間(ms)",colNum}";
            Console.WriteLine(header);
            Console.WriteLine(new string('-', lineWidth));
            
            foreach (var result in groupResults)
            {
                bool isBest = result.ModelName == bestResult.ModelName;
                
                if (isBest)
                    Console.ForegroundColor = ConsoleColor.Green;
                
                string modelNameWithMarker = result.ModelName + (isBest ? " *" : "") + (result.SelectionExclusionReason != null ? " †" : "");
                string line = $"{PadRightByWidth(modelNameWithMarker, colModel)} {PadRightByWidth(result.Category, colCategory)} {result.R2,colNum:F4} {result.SelectionScore,colNum:F2} {result.SelectionScore - minScore,colNum:F2}";
                if (hasHoldout) line += $" {(result.HoldoutIncrementErrorPercent.HasValue ? $"{result.HoldoutIncrementErrorPercent:+0.0;-0.0}" : "-"),colNum}";
                line += $" {result.EstimatedTotalBugs,colNum:F1}";
                if (verbose) line += $" {result.OptimizationTimeMs,colNum}";
                Console.WriteLine(line);
                
                Console.ResetColor();
            }
            Console.WriteLine();
        }
        
        // 推奨対象外のモデルと変化点の尤度比検定
        var excluded = results.Where(r => ModelComparisonGroup.IsComparable(r) && r.SelectionExclusionReason != null).ToList();
        if (excluded.Count > 0)
        {
            Console.WriteLine("† = 推奨対象外:");
            foreach (var r in excluded)
                Console.WriteLine($"    {r.ModelName}: {r.SelectionExclusionReason}");
        }
        foreach (var r in results.Where(r => r.ChangePointTest?.Success == true))
        {
            Console.WriteLine($"変化点の尤度比検定（{r.ModelName}）: {r.ChangePointTest!.Interpretation}");
        }
        if (bestResult.SelectionExclusionReason != null)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"警告: 推奨条件を満たすモデルがないため、選択基準値が最小のモデルを表示しています。このモデルも「{bestResult.SelectionExclusionReason}」ため、推定総バグ数・収束予測は参考値です。");
            Console.ResetColor();
        }
        
        // 使用された評価基準を表示
        var criterion = bestResult.ModelSelectionCriterion;
        Console.WriteLine($"* = 推奨モデル（比較グループ「{bestResult.ComparisonGroup}」内で{criterion}最小、損失関数: {bestResult.LossFunctionUsed}）");
        if (criterion == "AICc")
        {
            Console.WriteLine($"  （小標本補正: n={testData.DayCount} に対し n/k < 40 のモデルがあるため、グループ内は AICc で統一）");
        }
        
        // ホールドアウト検証サマリー
        if (hasHoldout)
        {
            Console.WriteLine("\n=== ホールドアウト検証結果 ===\n");
            Console.WriteLine("  HO誤差 = 末尾期間の発見数について（訓練区間のみで推定したモデルの予測 - 実測）/ 実測。正は過大予測");
            Console.WriteLine("  ※ 表の他の列（AIC・潜在バグ等）は全データで推定した最終結果です");
            
            var bestHoldout = results.Where(r => r.Success && r.HoldoutAbsIncrementErrorPercent.HasValue)
                                     .OrderBy(r => r.HoldoutAbsIncrementErrorPercent!.Value)
                                     .FirstOrDefault();
            if (bestHoldout != null)
            {
                var h = bestHoldout.Holdout!;
                Console.WriteLine($"\n予測精度最良モデル（HO誤差の絶対値最小）: {bestHoldout.ModelName} " +
                    $"(予測 {h.PredictedIncrement:F1} 件 / 実測 {h.ActualIncrement:F0} 件, 誤差 {h.IncrementErrorPercent:+0.0;-0.0}%)");
            }
            if (bestResult.Holdout != null)
            {
                var h = bestResult.Holdout;
                Console.WriteLine($"推奨モデル {bestResult.ModelName}: 予測 {h.PredictedIncrement:F1} 件 / 実測 {h.ActualIncrement:F0} 件" +
                    (double.IsFinite(h.IncrementErrorPercent) ? $", 誤差 {h.IncrementErrorPercent:+0.0;-0.0}%" : "") +
                    $", 日次MAE {h.DailyMae:F2} 件/日");
            }
            
            // 警告の表示
            var modelsWithHighError = results.Where(r => r.Success && r.HoldoutAbsIncrementErrorPercent > WarningService.Thresholds.HighHoldoutError).ToList();
            if (modelsWithHighError.Any())
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"注意: {modelsWithHighError.Count}個のモデルでHO誤差の絶対値 > {WarningService.Thresholds.HighHoldoutError:F0}%（予測精度が低い可能性）");
                Console.ResetColor();
            }
        }
        
        // 収束予測
        Console.WriteLine($"\n=== 収束予測（{bestResult.ModelName}）===\n");
        
        Console.WriteLine($"推定潜在バグ総数: {bestResult.EstimatedTotalBugs:F1} 件");
        Console.WriteLine($"残り推定バグ数: {bestResult.EstimatedTotalBugs - testData.CurrentCumulativeBugs:F1} 件");
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
        
        // 収束判断の目安を表示
        PrintConvergenceAssessment(testData, bestResult);
        
        // verboseモード時にパラメータ詳細を表示
        if (verbose)
        {
            PrintParameterDetails(bestResult);
        }
    }
    
    /// <summary>
    /// 現在の状況を表示
    /// </summary>
    static void PrintCurrentStatus(TestData testData)
    {
        Console.WriteLine("\n=== 現在の状況 ===\n");
        
        var cumulativePlanned = testData.GetCumulativePlanned();
        var cumulativeActual = testData.GetCumulativeActual();
        var cumulativeFound = testData.GetCumulativeBugsFound();
        var cumulativeFixed = testData.GetCumulativeBugsFixed();
        var remaining = testData.GetRemainingBugs();
        
        Console.WriteLine($"テスト消化（予定）: {cumulativePlanned.Last():F0} / {testData.TotalTestCases} ({cumulativePlanned.Last() / testData.TotalTestCases * 100:F1}%)");
        Console.WriteLine($"テスト消化（実績）: {cumulativeActual.Last():F0} / {testData.TotalTestCases} ({cumulativeActual.Last() / testData.TotalTestCases * 100:F1}%)");
        Console.WriteLine($"累積バグ発生数:     {cumulativeFound.Last():F0} 件");
        Console.WriteLine($"累積バグ修正数:     {cumulativeFixed.Last():F0} 件");
        Console.WriteLine($"残存バグ数:         {remaining.Last():F0} 件");
    }
    
    /// <summary>
    /// 収束判断の目安を表示
    /// </summary>
    static void PrintConvergenceAssessment(TestData testData, FittingResult bestResult)
    {
        double currentFound = testData.CurrentCumulativeBugs;
        var assessment = ConvergenceAssessment.Evaluate(currentFound, bestResult.EstimatedTotalBugs);

        Console.WriteLine("\n=== 収束判断の目安 ===\n");

        Console.ForegroundColor = assessment.Level switch
        {
            ConvergenceLevel.Converged => ConsoleColor.Green,
            ConvergenceLevel.NearlyConverged => ConsoleColor.Cyan,
            ConvergenceLevel.Converging => ConsoleColor.Yellow,
            _ => ConsoleColor.Red
        };
        Console.WriteLine($"  {assessment.Stars} {assessment.Message}");
        Console.ResetColor();

        if (assessment.Note != null)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"  注意: {assessment.Note}");
            Console.ResetColor();
        }

        Console.WriteLine($"\n  現在の発見率: {ConvergenceAssessment.FormatRatio(assessment.Ratio)} ({currentFound:F0} / {bestResult.EstimatedTotalBugs:F1})");
    }
    
    /// <summary>
    /// Fisher 情報行列による漸近信頼区間を計算（パラメータと推定潜在バグ総数）
    /// </summary>
    static void CalculateFisherIntervals(FittingResult bestResult, double[] tData, double[] yData, double confidenceLevel)
    {
        var model = bestResult.Model!;
        if (model.ParameterNames.Any(n => n.StartsWith("τ")))
        {
            Console.WriteLine("  Fisher情報行列: 変化点 τ は尤度が τ について微分できないため、変化点モデルでは計算しません。");
            return;
        }
        
        var service = new FisherInformationService(confidenceLevel);
        var fisher = service.CalculateNHPPStandardErrors(model, tData, yData, bestResult.ParameterVector);
        bestResult.FisherInformation = fisher;
        if (fisher.Success && fisher.CovarianceMatrix != null)
        {
            bestResult.TotalBugsFisherInterval = service.CalculateDerivedInterval(
                model.GetAsymptoticTotalBugs, bestResult.ParameterVector, fisher.CovarianceMatrix, logScale: true);
        }
        else
        {
            Console.WriteLine($"  Fisher情報行列の計算に失敗: {fisher.ErrorMessage}");
        }
    }
    
    /// <summary>
    /// 信頼区間（--ci）と予測区間（--pi）を計算する。パラメトリック・ブートストラップは1回だけ実行して共有する
    /// </summary>
    static void CalculateIntervals(
        CommandOptions options, ModelFitter fitter, FittingResult bestResult, double[] tData, double[] yData, int dayCount)
    {
        var bootstrapSettings = ConfigurationService.Current.Bootstrap;
        int iterations = options.BootstrapIterations > 0 ? options.BootstrapIterations : bootstrapSettings.Iterations;
        double level = bootstrapSettings.ConfidenceLevel;
        var model = bestResult.Model!;
        
        // 工数データ（TEF）は外生の説明変数なので固定したまま発見数だけを再生成できるが、
        // 修正数（FRE）は発見数と同時に推定するため、発見数だけの再生成では整合しない
        if (bestResult.ComparisonGroup != ModelComparisonGroup.DetectionAndCorrection)
        {
            Console.WriteLine($"パラメトリック・ブートストラップで{level * 100:F0}%区間を計算中（{iterations}回）...");
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            var bootstrap = ParametricBootstrap.Run(
                model, tData, bestResult.ParameterVector, fitter.CreateRefitFunction(model), iterations, bootstrapSettings.RandomSeed);
            Console.WriteLine($"  再推定の成功 {bootstrap.Succeeded}/{bootstrap.Requested}（{stopwatch.Elapsed.TotalSeconds:F1}秒）");
            
            // 予測期間: 観測期間と同じ長さ（14〜180日）
            int horizon = Math.Clamp(dayCount, 14, 180);
            
            if (options.CalculateConfidenceInterval)
            {
                var times = Enumerable.Range(1, dayCount + horizon).Select(d => (double)d).ToArray();
                bestResult.ConfidenceBand = new ConfidenceIntervalService().Calculate(
                    model, bestResult.ParameterVector, bootstrap, times, level);
            }
            if (options.CalculatePredictionInterval)
            {
                bestResult.PredictionInterval = new PredictionIntervalService().Calculate(
                    model, tData, yData, bestResult.ParameterVector, bootstrap, horizon, level, bootstrapSettings.RandomSeed);
            }
        }
        else
        {
            Console.WriteLine("  ブートストラップ区間: 修正数データを同時に推定する FRE モデルには対応していないため省略します。");
        }
        
        // Fisher 情報行列による漸近信頼区間（Poisson-NHPP の最尤推定値でのみ有効）
        if (options.CalculateConfidenceInterval && bestResult.LossFunctionUsed == "MLE")
        {
            CalculateFisherIntervals(bestResult, tData, yData, level);
        }
    }
    
    /// <summary>
    /// ブートストラップによる信頼区間を表示
    /// </summary>
    static void PrintConfidenceBand(FittingResult bestResult)
    {
        var band = bestResult.ConfidenceBand;
        if (band == null) return;
        
        Console.WriteLine($"\n=== 信頼区間（パラメトリック・ブートストラップ、{band.ConfidenceLevel:P0}、再推定の成功 {band.Succeeded}/{band.Requested}）===\n");
        foreach (var warning in band.Warnings)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"  注意: {warning}");
            Console.ResetColor();
        }
        if (band.Succeeded == 0) return;
        
        if (band.TotalBugs != null)
            Console.WriteLine($"  推定潜在バグ総数: {band.TotalBugs.Estimate:F1} 件  [{band.TotalBugs.Lower:F1}, {band.TotalBugs.Upper:F1}]");
        PrintMilestones(band.Milestones);
        Console.WriteLine("  ※ パラメータ推定の不確実性のみ。将来の観測値のばらつきを含む区間は --pi で計算します。");
    }
    
    /// <summary>
    /// 収束マイルストーン到達日の区間を表示
    /// </summary>
    static void PrintMilestones(IEnumerable<MilestoneInterval> milestones)
    {
        Console.WriteLine("\n  収束予測日の区間:");
        foreach (var m in milestones)
        {
            string FormatDay(double d) => double.IsPositiveInfinity(d) ? "到達せず" : $"{d:F1}日目";
            string note = m.UnreachableFraction > 0 ? $"（{m.UnreachableFraction:P0} の反復で到達せず）" : "";
            Console.WriteLine($"    {m.Ratio * 100:F0}%発見: {FormatDay(m.EstimateDay)}  [{FormatDay(m.LowerDay)}, {FormatDay(m.UpperDay)}]{note}");
        }
    }
    
    /// <summary>
    /// Fisher 情報行列による信頼区間を表示
    /// </summary>
    static void PrintFisherIntervals(FittingResult bestResult)
    {
        var fisher = bestResult.FisherInformation;
        if (fisher == null || !fisher.Success) return;
        
        Console.WriteLine($"\n=== パラメータの信頼区間（Fisher情報行列、{bestResult.TotalBugsFisherInterval?.ConfidenceLevel ?? 0.95:P0}）===\n");
        for (int i = 0; i < fisher.ParameterNames.Length; i++)
        {
            Console.WriteLine($"  {fisher.ParameterNames[i],-4} = {fisher.Parameters[i],10:G5}  SE={fisher.StandardErrors[i],10:G4}  [{fisher.LowerBounds[i]:G5}, {fisher.UpperBounds[i]:G5}]");
        }
        var total = bestResult.TotalBugsFisherInterval;
        if (total != null && total.IsValid)
        {
            Console.WriteLine($"\n  推定潜在バグ総数: {total.Estimate:F1} 件  [{total.Lower:F1}, {total.Upper:F1}]（デルタ法・対数スケール）");
        }
        Console.WriteLine("  ※ 漸近近似。パラメータが探索範囲の境界にある場合やデータが少ない場合は不正確です。");
    }
    
    /// <summary>
    /// 予測区間を表示
    /// </summary>
    static void PrintPredictionIntervals(FittingResult bestResult, TestData testData)
    {
        var pi = bestResult.PredictionInterval;
        if (pi == null) return;
        
        Console.WriteLine($"\n=== 予測区間（パラメトリック・ブートストラップ、{pi.ConfidenceLevel:P0}、再推定の成功 {pi.Succeeded}/{pi.Requested}）===\n");
        foreach (var warning in pi.Warnings)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"  注意: {warning}");
            Console.ResetColor();
        }
        if (pi.Succeeded == 0) return;
        
        // 総数・収束日の区間は --ci と同じ値になるため、--ci の場合はそちらに表示する
        bool shownInConfidenceBand = bestResult.ConfidenceBand?.Succeeded > 0;
        if (pi.TotalBugs != null && !shownInConfidenceBand)
            Console.WriteLine($"  推定潜在バグ総数: {pi.TotalBugs.Estimate:F1} 件  [{pi.TotalBugs.Lower:F1}, {pi.TotalBugs.Upper:F1}]（信頼区間）");
        if (pi.RemainingBugs != null)
            Console.WriteLine($"  今後発見される件数: {pi.RemainingBugs.Estimate:F1} 件  [{pi.RemainingBugs.Lower:F0}, {pi.RemainingBugs.Upper:F0}]（予測区間）");
        if (!shownInConfidenceBand)
            PrintMilestones(pi.Milestones);
        
        Console.WriteLine("\n  将来の累積発見数（予測区間）:");
        Console.WriteLine($"    {"日",6} {"日付",12} {"予測",8} {"下限",8} {"上限",8}");
        int step = Math.Max(1, pi.FutureTimes.Length / 8);
        for (int d = step - 1; d < pi.FutureTimes.Length; d += step)
        {
            string date = testData.StartDate.HasValue ? testData.StartDate.Value.AddDays(pi.FutureTimes[d] - 1).ToString("yyyy/MM/dd") : "-";
            Console.WriteLine($"    {pi.FutureTimes[d],6:F0} {date,12} {pi.PointForecast[d],8:F1} {pi.Lower[d],8:F0} {pi.Upper[d],8:F0}");
        }
    }
    
    /// <summary>
    /// 推奨モデルのパラメータ詳細を表示（verboseモード用）
    /// </summary>
    static void PrintParameterDetails(FittingResult bestResult)
    {
        Console.WriteLine($"\n=== 推奨モデル詳細（{bestResult.ModelName}）===\n");
        
        Console.WriteLine("パラメータ推定結果:");
        foreach (var (name, value) in bestResult.Parameters)
        {
            Console.WriteLine($"  {ParameterDescriptions.FormatLine(name, value)}");
        }
        
        Console.WriteLine("\n適合度指標:");
        Console.WriteLine($"  決定係数 (R²):       {bestResult.R2:F4}");
        Console.WriteLine($"  平均二乗誤差 (MSE):  {bestResult.MSE:F2}");
        Console.WriteLine($"  AIC:                 {bestResult.AIC:F2}");
        Console.WriteLine($"  AICc:                {bestResult.AICc:F2}");
        Console.WriteLine($"  選択基準:            {bestResult.ModelSelectionCriterion} = {bestResult.SelectionScore:F2}");
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
                    // 不完全デバッグモデルを削除したため、拡張オプションなしでは常に基本モデルのみを使用する
                    options.Notices.Add("--basic-only は不要になりました（拡張オプションを指定しなければ基本モデルのみを使用します）。このオプションは無視されます。");
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
                    // 擬似Coverageモデルは基本モデル（Goel一般化・ロジスティック・ゴンペルツ）の再パラメータ化で同一のため廃止
                    options.Notices.Add("--coverage は廃止しました（擬似Coverageモデルは基本モデルの再パラメータ化で同一のモデルのため）。このオプションは無視されます。");
                    break;

                case "--all-extended":
                    options.AllExtended = true;
                    options.IncludeChangePoint = true;
                    options.IncludeTEF = true;
                    options.IncludeFRE = true;
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
                            _ => LossType.Mle
                        };
                        if (loss != "mle" && loss != "sse")
                            options.Notices.Add($"--loss の値 '{loss}' は不明なため、MLE を使用します。");
                    }
                    break;
                
                // ホールドアウト検証オプション
                case "--holdout-days":
                case "--holdout":
                    if (i + 1 < args.Length && int.TryParse(args[++i], out int holdout))
                        options.HoldoutDays = Math.Max(0, holdout);
                    break;
                
                // 統計診断オプション（Phase 1-2）
                case "--diagnostics":
                case "-d":
                    options.RunDiagnostics = true;
                    break;
                
                // 予測区間オプション（Phase 3）
                case "--prediction-interval":
                case "--pi":
                    options.CalculatePredictionInterval = true;
                    break;
                
                // モデル平均化オプション（Phase 4）
                case "--model-averaging":
                case "--ma":
                    options.UseModelAveraging = true;
                    break;
                
                case "--multi-start":
                    if (i + 1 < args.Length && int.TryParse(args[++i], out int starts))
                        options.MultiStarts = Math.Max(1, starts);
                    break;
                
                case "--lrt-iterations":
                    if (i + 1 < args.Length && int.TryParse(args[++i], out int lrt))
                        options.LrtIterations = Math.Max(0, lrt);
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
        Console.WriteLine("  --multi-start N       N 個の開始点（Latin Hypercube Sampling）から並列に最適化（デフォルト: 1）");
        Console.WriteLine();
        Console.WriteLine("拡張モデルオプション:");
        Console.WriteLine("  --change-point        変化点モデルを含める");
        Console.WriteLine("  --tef                 テスト工数関数モデルを含める");
        Console.WriteLine("  --fre                 欠陥除去効率モデルを含める");
        Console.WriteLine("  --all-extended        全拡張モデルを含める");
        Console.WriteLine("  --lrt-iterations N    変化点の尤度比検定のシミュレーション回数（デフォルト: 99、0 で省略）");
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
        Console.WriteLine("                          mle - 最尤推定（Poisson-NHPP、デフォルト）");
        Console.WriteLine("                          sse - 残差二乗和（従来方式。AIC によるモデル選択は不正確）");
        Console.WriteLine("  --holdout-days N      末尾N日をホールドアウト検証に使用");
        Console.WriteLine("                        （末尾を除いた訓練区間で別途推定し、末尾期間の発見数の予測誤差を評価。");
        Console.WriteLine("                          最終結果は全データで推定）");
        Console.WriteLine();
        Console.WriteLine("統計診断オプション:");
        Console.WriteLine("  -d, --diagnostics     残差診断・適合度検定を実行");
        Console.WriteLine("                        （残差分析、自己相関検定、正規性検定、χ²検定、KS検定）");
        Console.WriteLine("  --pi, --prediction-interval");
        Console.WriteLine("                        将来の累積発見数・残りバグ数・収束日の予測区間を計算");
        Console.WriteLine("                        （パラメトリック・ブートストラップ。反復回数は --bootstrap）");
        Console.WriteLine("  --ma, --model-averaging");
        Console.WriteLine("                        AIC重みによるモデル平均化を実行");
        Console.WriteLine();
        Console.WriteLine("使用例:");
        Console.WriteLine("  BugConvergenceTool TestData.xlsx");
        Console.WriteLine("  BugConvergenceTool TestData.xlsx -o ./output");
        Console.WriteLine("  BugConvergenceTool TestData.xlsx --optimizer pso");
        Console.WriteLine("  BugConvergenceTool TestData.xlsx --change-point --fre");
        Console.WriteLine("  BugConvergenceTool TestData.xlsx --all-extended -v");
        Console.WriteLine("  BugConvergenceTool TestData.xlsx --loss sse --holdout-days 5");
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
    public bool Verbose { get; set; }
    public OptimizerType Optimizer { get; set; } = OptimizerType.DifferentialEvolution;
    
    // 拡張モデルオプション
    public bool IncludeChangePoint { get; set; } = false;
    public bool IncludeTEF { get; set; } = false;
    public bool IncludeFRE { get; set; } = false;
    public bool AllExtended { get; set; } = false;
    
    // 信頼区間オプション
    public bool CalculateConfidenceInterval { get; set; } = false;
    public int BootstrapIterations { get; set; } = 200;
    
    // 損失関数オプション
    public LossType LossFunction { get; set; } = LossType.Mle;
    
    // 実行時に表示する注意（廃止オプション・不正な値など）
    public List<string> Notices { get; } = new();
    
    // ホールドアウト検証オプション
    public int HoldoutDays { get; set; } = 0;
    
    // 診断オプション（Phase 1-2）
    public bool RunDiagnostics { get; set; } = false;
    
    // 予測区間オプション（Phase 3）
    public bool CalculatePredictionInterval { get; set; } = false;
    
    // モデル平均化オプション（Phase 4）
    public bool UseModelAveraging { get; set; } = false;
    
    // マルチスタート最適化の開始点数（1 ならマルチスタートなし）
    public int MultiStarts { get; set; } = 1;
    
    // 変化点の尤度比検定のシミュレーション回数（0 なら検定しない）
    public int LrtIterations { get; set; } = 99;
}
