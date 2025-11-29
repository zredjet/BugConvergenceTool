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
            if (model is TEFBasedModelBase tefModel && _effortData != null)
            {
                tefModel.ObservedEffortData = _effortData;
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
            
            // ホールドアウト検証
            if (_splitResult != null && _splitResult.IsValid)
            {
                PerformHoldoutValidation(model, parameters, result);
            }
            
            // 収束予測を計算
            CalculateConvergencePredictions(model, parameters, result);
            
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
    /// 最適モデル（AIC最小）を取得
    /// </summary>
    public FittingResult? GetBestModel(List<FittingResult> results, string? category = null)
    {
        var filtered = results.Where(r => r.Success);
        
        if (category != null)
            filtered = filtered.Where(r => r.Category == category);
        
        return filtered.OrderBy(r => r.AIC).FirstOrDefault();
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
}
