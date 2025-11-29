using BugConvergenceTool.Models;
using BugConvergenceTool.Optimizers;
using MathNet.Numerics;
using MathNet.Numerics.Optimization;

namespace BugConvergenceTool.Services;

/// <summary>
/// 信頼度成長モデルのフィッティングを行うサービス
/// </summary>
public class ModelFitter
{
    private readonly TestData _testData;
    private readonly double[] _tData;
    private readonly double[] _yData;
    private readonly double[] _yFixedData;  // 累積修正数データ（FREモデル用）
    private readonly double[]? _effortData;  // 累積工数データ（TEFモデル用）
    private readonly OptimizerType _optimizerType;
    private readonly bool _verbose;
    private readonly LossType _lossType;
    private readonly int _holdoutDays;
    
    // ホールドアウト用のデータ分割結果
    private TimeSeriesSplitResult? _splitResult;
    
    public ModelFitter(
        TestData testData, 
        OptimizerType optimizerType = OptimizerType.DifferentialEvolution, 
        bool verbose = false,
        LossType lossType = LossType.Sse,
        int holdoutDays = 0)
    {
        _testData = testData;
        _tData = testData.GetTimeData();
        _yData = testData.GetCumulativeBugsFound();
        _yFixedData = testData.GetCumulativeBugsFixed();
        
        // 工数データが存在する場合のみ設定（TEFモデル用）
        var actualEffort = testData.GetCumulativeActual();
        _effortData = actualEffort.Any(e => e > 0) ? actualEffort : null;
        
        _optimizerType = optimizerType;
        _verbose = verbose;
        _lossType = lossType;
        _holdoutDays = holdoutDays;
        
        // ホールドアウト検証用のデータ分割
        if (holdoutDays > 0)
        {
            _splitResult = ValidationUtility.SplitLastNDays(_tData, _yData, holdoutDays);
            if (_verbose && _splitResult.Warning != null)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"  警告: {_splitResult.Warning}");
                Console.ResetColor();
            }
        }
    }
    
    /// <summary>
    /// 指定モデルでフィッティングを実行
    /// </summary>
    public FittingResult FitModel(ReliabilityGrowthModelBase model)
    {
        var result = new FittingResult
        {
            ModelName = model.Name,
            Category = model.Category
        };
        
        try
        {
            // TEFモデルの場合、工数データを設定
            if (model is TEFBasedModelBase tefModel)
            {
                if (_effortData != null)
                {
                    tefModel.ObservedEffortData = _effortData;
                }
                else
                {
                    result.Warnings.Add("警告: TEFモデルが選択されましたが、工数データ（予定/実績）がありません。パラメータ推定の精度が低下する可能性があります。");
                }
            }

            // FREモデルの場合、修正データを確認
            if (model is FaultRemovalEfficiencyModelBase && (_yFixedData == null || _yFixedData.All(v => v == 0)))
            {
                result.Warnings.Add("警告: FREモデルが選択されましたが、修正データ（BugsFixed）がありません。修正効率パラメータが正しく推定されません。");
            }
            
            // 損失関数の取得（MLEサポートチェック付き）
            var lossFunction = LossFunctionFactory.GetForModel(
                _lossType, model, out var actualLossType, out var fallbackWarning);
            
            result.LossFunctionUsed = actualLossType == LossType.Mle ? "MLE" : "SSE";
            
            if (fallbackWarning != null)
            {
                result.Warnings.Add(fallbackWarning);
                if (_verbose)
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine($"  [{model.Name}] {fallbackWarning}");
                    Console.ResetColor();
                }
            }
            
            // 訓練データの決定（ホールドアウトの有無で切り替え）
            double[] trainT = _splitResult?.TrainTimes ?? _tData;
            double[] trainY = _splitResult?.TrainValues ?? _yData;
            
            // パラメータ推定
            var parameters = EstimateParameters(model, trainT, trainY, lossFunction);
            
            if (parameters == null)
            {
                result.Success = false;
                result.ErrorMessage = "パラメータ推定に失敗しました";
                return result;
            }
            
            // パラメータを結果に格納
            for (int i = 0; i < model.ParameterNames.Length && i < parameters.Length; i++)
            {
                result.Parameters[model.ParameterNames[i]] = parameters[i];
            }
            
            // 信頼区間計算用：パラメータベクトル（順序を保持）
            result.ParameterVector = (double[])parameters.Clone();
            
            // 予測時刻と予測値を計算（全データに対して）
            result.PredictionTimes = (double[])_tData.Clone();
            result.PredictedValues = _tData.Select(t => model.Calculate(t, parameters)).ToArray();
            
            // 適合度指標を計算（全データに対して）
            result.R2 = model.CalculateR2(_tData, _yData, parameters);
            result.MSE = model.CalculateSSE(_tData, _yData, parameters) / _tData.Length;
            
            // AICは損失関数のタイプに応じて適切な方法で計算
            // SSE: 正規分布仮定のAIC近似式 n*ln(SSE/n) + 2k
            // MLE: Poisson-NHPPの対数尤度ベース 2k - 2ln(L)
            // FREモデルの場合は発見+修正の結合尤度を使用
            result.AIC = lossFunction.CalculateAIC(_tData, _yData, model, parameters, _yFixedData);
            
            // AICc（小標本補正AIC）を計算
            result.AICc = lossFunction.CalculateAICc(_tData, _yData, model, parameters, _yFixedData);
            
            // 【自動判定ロジック】Burnham & Anderson (2002) の基準
            int n = _tData.Length;
            int k = parameters.Length;
            
            if (n <= k + 1)
            {
                // サンプルサイズ不足で AICc 計算不能 → モデルとして評価不適
                result.ModelSelectionCriterion = "Invalid (n <= k+1)";
                result.Warnings.Add($"警告: サンプルサイズ不足 (n={n}, k={k})。このモデルは評価に適していません。");
            }
            else if ((double)n / k < 40.0)
            {
                // n/k < 40 の場合、小標本補正が必要 → AICc を採用
                result.ModelSelectionCriterion = "AICc";
            }
            else
            {
                // 十分なサンプルサイズがある場合
                // 注: Burnham & Anderson は「常に AICc を使用しても良い」としているが、
                // ここでは切り替えロジックを明示するため AIC を選択
                result.ModelSelectionCriterion = "AIC";
            }
            
            // ホールドアウト検証
            if (_splitResult != null && _splitResult.IsValid)
            {
                PerformHoldoutValidation(model, parameters, result);
            }
            
            // 収束予測を計算
            CalculateConvergencePredictions(model, parameters, result);
            
            // 感度分析を実行（推定総バグ数に対する感度）
            try
            {
                var sensitivityService = new SensitivityAnalysisService();
                result.SensitivityAnalysis = sensitivityService.AnalyzeTotalBugsSensitivity(model, parameters);
                
                // 感度分析の警告を結果に追加
                if (result.SensitivityAnalysis.Warnings.Count > 0)
                {
                    result.Warnings.AddRange(result.SensitivityAnalysis.Warnings);
                }
            }
            catch (Exception ex)
            {
                if (_verbose)
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine($"  [{model.Name}] 感度分析に失敗: {ex.Message}");
                    Console.ResetColor();
                }
            }
            
            result.Success = true;
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.ErrorMessage = ex.Message;
        }
        
        return result;
    }
    
    /// <summary>
    /// ホールドアウト検証を実行
    /// </summary>
    private void PerformHoldoutValidation(ReliabilityGrowthModelBase model, double[] parameters, FittingResult result)
    {
        if (_splitResult == null || !_splitResult.IsValid) return;
        
        // テストデータに対する予測
        var predictions = _splitResult.TestTimes.Select(t => model.Calculate(t, parameters)).ToArray();
        
        // 評価指標の計算
        var validation = ValidationUtility.CalculateMetrics(predictions, _splitResult.TestValues);
        
        result.HoldoutMse = validation.Mse;
        result.HoldoutMape = validation.Mape;
        result.HoldoutMae = validation.Mae;
        
        // 警告を追加
        result.Warnings.AddRange(validation.Warnings);
        
        if (_verbose)
        {
            Console.WriteLine($"    -> ホールドアウト検証: MSE={validation.Mse:F4}, MAPE={validation.Mape:F2}%");
        }
    }
    
    /// <summary>
    /// 全モデルでフィッティングを実行
    /// </summary>
    public List<FittingResult> FitAllModels(bool includeImperfectDebug = true)
    {
        var models = includeImperfectDebug 
            ? ModelFactory.GetAllModels() 
            : ModelFactory.GetBasicModels();
        
        return models.Select(FitModel).ToList();
    }
    
    /// <summary>
    /// 最適モデル（SelectionScore最小）を取得
    /// SelectionScore は n/k < 40 の場合 AICc、それ以外は AIC
    /// </summary>
    public FittingResult? GetBestModel(List<FittingResult> results, string? category = null)
    {
        var filtered = results.Where(r => r.Success);
        
        if (category != null)
            filtered = filtered.Where(r => r.Category == category);
        
        // Invalid なモデルを除外し、SelectionScore でソート
        return filtered
            .Where(r => !r.ModelSelectionCriterion.StartsWith("Invalid"))
            .OrderBy(r => r.SelectionScore)
            .FirstOrDefault();
    }
    
    /// <summary>
    /// パラメータ推定（選択されたオプティマイザを使用）
    /// </summary>
    private double[]? EstimateParameters(
        ReliabilityGrowthModelBase model,
        double[] tData,
        double[] yData,
        ILossFunction lossFunction)
    {
        var (lower, upper) = model.GetBounds(tData, yData);
        var initial = model.GetInitialParameters(tData, yData);
        
        // 目的関数の構築
        // 全モデルで損失関数を使用（FREモデルの場合は発見+修正の同時推定）
        Func<double[], double> objective = p => lossFunction.Evaluate(tData, yData, model, p, _yFixedData);
        
        OptimizationResult result;
        
        if (_optimizerType == OptimizerType.AutoSelect)
        {
            if (_verbose)
                Console.WriteLine($"  [{model.Name}] 全アルゴリズムで最適化中...");
            
            result = OptimizerFactory.AutoOptimize(objective, lower, upper, initial, _verbose);
        }
        else
        {
            var optimizer = OptimizerFactory.Create(_optimizerType);
            
            if (_verbose)
                Console.WriteLine($"  [{model.Name}] {optimizer.Name}で最適化中...");
            
            result = optimizer.Optimize(objective, lower, upper, initial);
        }
        
        if (_verbose && result.Success)
        {
            string lossName = _lossType == LossType.Mle ? "NLL" : "SSE";
            Console.WriteLine($"    -> {lossName}={result.ObjectiveValue:F4}, " +
                $"評価回数={result.FunctionEvaluations}, 時間={result.ElapsedMilliseconds}ms");
        }
        
        return result.Success ? result.Parameters : null;
    }
    
    /// <summary>
    /// <summary>
    /// 収束予測を計算
    /// </summary>
    private void CalculateConvergencePredictions(
        ReliabilityGrowthModelBase model, 
        double[] parameters, 
        FittingResult result)
    {
        double totalBugs = model.GetAsymptoticTotalBugs(parameters);
        int currentDay = _testData.DayCount;
        double currentBugs = _yData.Last();
        
        var ratios = new[] { (0.90, "90%発見"), (0.95, "95%発見"), (0.99, "99%発見"), (0.999, "99.9%発見") };
        
        foreach (var (ratio, name) in ratios)
        {
            var prediction = new ConvergencePrediction
            {
                Milestone = name,
                Ratio = ratio,
                BugsAtPoint = totalBugs * ratio
            };
            
            double target = totalBugs * ratio;
            
            if (currentBugs >= target)
            {
                prediction.AlreadyReached = true;
                prediction.PredictedDay = null;
                prediction.RemainingDays = 0;
            }
            else
            {
                var predictedDay = model.PredictDayForRatio(ratio, parameters, currentDay);
                
                if (predictedDay.HasValue && !double.IsInfinity(predictedDay.Value))
                {
                    prediction.PredictedDay = predictedDay.Value;
                    prediction.RemainingDays = predictedDay.Value - currentDay;
                    
                    if (_testData.StartDate.HasValue)
                    {
                        prediction.PredictedDate = _testData.StartDate.Value.AddDays(predictedDay.Value - 1);
                    }
                }
            }
            
            result.ConvergencePredictions[name] = prediction;
        }
    }
    
    /// <summary>
    /// 変化点モデルに対してプロファイル尤度法による堅牢な変化点探索を実行
    /// </summary>
    /// <param name="changePointModel">変化点モデル</param>
    /// <param name="useRobustDetection">堅牢な変化点検出を使用するか</param>
    /// <returns>フィッティング結果（変化点探索結果を含む）</returns>
    public FittingResult FitChangePointModel(ChangePointModelBase changePointModel, bool useRobustDetection = true)
    {
        if (!useRobustDetection)
        {
            // 従来の方法でフィッティング
            return FitModel(changePointModel);
        }
        
        if (_verbose)
        {
            Console.WriteLine($"  [{changePointModel.Name}] プロファイル尤度法による変化点探索を開始...");
        }
        
        // プロファイル尤度法による変化点探索
        var detector = new RobustChangePointDetector(
            optimizerType: _optimizerType == OptimizerType.AutoSelect ? OptimizerType.NelderMead : _optimizerType,
            lossType: _lossType,
            verbose: _verbose);
        
        var searchResult = detector.FindOptimalChangePoint(
            changePointModel,
            _tData,
            _yData,
            _yFixedData);
        
        if (!searchResult.Success || searchResult.BestFittingResult == null)
        {
            if (_verbose)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"  [{changePointModel.Name}] 変化点探索に失敗: {searchResult.ErrorMessage}");
                Console.ResetColor();
            }
            
            // フォールバック: 従来の方法でフィッティング
            var fallbackResult = FitModel(changePointModel);
            fallbackResult.Warnings.Add("変化点探索に失敗したため、従来の方法でフィッティングしました。");
            return fallbackResult;
        }
        
        // 最適な変化点でのフィッティング結果を取得
        var result = searchResult.BestFittingResult;
        result.ChangePointSearchResult = searchResult;
        
        // 変化点の信頼性に関する警告を追加
        if (searchResult.ChangePointReliability.Contains("非常に低") || searchResult.ChangePointReliability.Contains("低"))
        {
            result.Warnings.Add($"変化点の信頼性が{searchResult.ChangePointReliability}です。変化点なしのモデルも検討してください。");
        }
        
        // ホールドアウト検証
        if (_splitResult != null && _splitResult.IsValid)
        {
            PerformHoldoutValidationForChangePoint(changePointModel, result);
        }
        
        // 収束予測を計算
        CalculateConvergencePredictions(changePointModel, result.ParameterVector, result);
        
        // 感度分析を実行
        try
        {
            var sensitivityService = new SensitivityAnalysisService();
            result.SensitivityAnalysis = sensitivityService.AnalyzeTotalBugsSensitivity(
                changePointModel, result.ParameterVector);
            
            if (result.SensitivityAnalysis.Warnings.Count > 0)
            {
                result.Warnings.AddRange(result.SensitivityAnalysis.Warnings);
            }
        }
        catch (Exception ex)
        {
            if (_verbose)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"  [{changePointModel.Name}] 感度分析に失敗: {ex.Message}");
                Console.ResetColor();
            }
        }
        
        if (_verbose)
        {
            Console.WriteLine($"  [{changePointModel.Name}] 最適変化点: τ={searchResult.BestTau}, " +
                $"信頼性: {searchResult.ChangePointReliability}");
        }
        
        return result;
    }
    
    /// <summary>
    /// 変化点モデル用のホールドアウト検証
    /// </summary>
    private void PerformHoldoutValidationForChangePoint(ChangePointModelBase model, FittingResult result)
    {
        if (_splitResult == null || !_splitResult.IsValid) return;
        
        // テストデータに対する予測（固定τモデルの予測値を使用）
        var predictions = new double[_splitResult.TestTimes.Length];
        for (int i = 0; i < _splitResult.TestTimes.Length; i++)
        {
            double t = _splitResult.TestTimes[i];
            predictions[i] = model.Calculate(t, result.ParameterVector);
        }
        
        // 評価指標の計算
        var validation = ValidationUtility.CalculateMetrics(predictions, _splitResult.TestValues);
        
        result.HoldoutMse = validation.Mse;
        result.HoldoutMape = validation.Mape;
        result.HoldoutMae = validation.Mae;
        
        result.Warnings.AddRange(validation.Warnings);
        
        if (_verbose)
        {
            Console.WriteLine($"    -> ホールドアウト検証: MSE={validation.Mse:F4}, MAPE={validation.Mape:F2}%");
        }
    }
}
