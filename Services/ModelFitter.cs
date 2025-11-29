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
    
    public ModelFitter(TestData testData, OptimizerType optimizerType = OptimizerType.DifferentialEvolution, bool verbose = false)
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
            
            // パラメータ推定（グリッドサーチ + 勾配降下法）
            var parameters = EstimateParameters(model);
            
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
            
            // 予測時刻と予測値を計算
            result.PredictionTimes = (double[])_tData.Clone();
            result.PredictedValues = _tData.Select(t => model.Calculate(t, parameters)).ToArray();
            
            // 適合度指標を計算
            result.R2 = model.CalculateR2(_tData, _yData, parameters);
            result.MSE = model.CalculateSSE(_tData, _yData, parameters) / _tData.Length;
            result.AIC = model.CalculateAIC(_tData, _yData, parameters);
            
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
    private double[]? EstimateParameters(ReliabilityGrowthModelBase model)
    {
        var (lower, upper) = model.GetBounds(_tData, _yData);
        var initial = model.GetInitialParameters(_tData, _yData);
        
        // 目的関数（SSE最小化）
        // FREモデルの場合は発見数SSE + 修正数SSEの合計を最小化
        Func<double[], double> objective;
        
        if (model is FaultRemovalEfficiencyModelBase freModel)
        {
            objective = p => CalculateFREObjective(freModel, p);
        }
        else
        {
            objective = p => model.CalculateSSE(_tData, _yData, p);
        }
        
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
            Console.WriteLine($"    -> SSE={result.ObjectiveValue:F4}, " +
                $"評価回数={result.FunctionEvaluations}, 時間={result.ElapsedMilliseconds}ms");
        }
        
        return result.Success ? result.Parameters : null;
    }
    
    /// <summary>
    /// FREモデル用の目的関数（発見数SSE + 修正数SSE）
    /// </summary>
    private double CalculateFREObjective(FaultRemovalEfficiencyModelBase model, double[] parameters)
    {
        double sseDetected = 0;
        double sseCorrected = 0;
        
        for (int i = 0; i < _tData.Length; i++)
        {
            // 発見数の残差
            double predictedDetected = model.CalculateDetected(_tData[i], parameters);
            double residualDetected = _yData[i] - predictedDetected;
            sseDetected += residualDetected * residualDetected;
            
            // 修正数の残差
            double predictedCorrected = model.CalculateCorrected(_tData[i], parameters);
            double residualCorrected = _yFixedData[i] - predictedCorrected;
            sseCorrected += residualCorrected * residualCorrected;
        }
        
        // 両方の誤差を合計（重み付けは1:1）
        return sseDetected + sseCorrected;
    }
    
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
