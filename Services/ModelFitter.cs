using BugConvergenceTool.Models;
using BugConvergenceTool.Optimizers;

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
    private readonly int _multiStarts;

    // ホールドアウト用のデータ分割結果
    private TimeSeriesSplitResult? _splitResult;

    /// <summary>
    /// 変化点モデル（τ を1つ持つもの）をプロファイル尤度法で推定するか
    /// </summary>
    /// <remarks>
    /// 尤度は τ について階段状・多峰になりやすいため、τ を格子上で固定して他のパラメータを推定し、
    /// 最良の τ を選ぶ（<see cref="RobustChangePointDetector"/>）。false なら τ も連続パラメータとして同時に推定する。
    /// </remarks>
    public bool UseProfileLikelihoodForChangePoints { get; set; } = true;

    /// <param name="testData">テストデータ</param>
    /// <param name="optimizerType">最適化アルゴリズム</param>
    /// <param name="verbose">詳細出力</param>
    /// <param name="lossType">損失関数</param>
    /// <param name="holdoutDays">ホールドアウト検証に使う末尾の日数（0 なら検証しない）</param>
    /// <param name="multiStarts">マルチスタート最適化の開始点数（1 ならマルチスタートしない）</param>
    public ModelFitter(
        TestData testData,
        OptimizerType optimizerType = OptimizerType.DifferentialEvolution,
        bool verbose = false,
        LossType lossType = LossType.Mle,
        int holdoutDays = 0,
        int multiStarts = 1)
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
        _multiStarts = Math.Max(1, multiStarts);

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
    /// <remarks>
    /// τ を1つ持つ変化点モデルは、<see cref="UseProfileLikelihoodForChangePoints"/> が true ならプロファイル尤度法で推定する。
    /// </remarks>
    public FittingResult FitModel(ReliabilityGrowthModelBase model)
    {
        return FitModel(model, UseProfileLikelihoodForChangePoints);
    }
    
    /// <summary>
    /// 指定モデルでフィッティングを実行（変化点モデルにプロファイル尤度法を使うかを指定）
    /// </summary>
    private FittingResult FitModel(ReliabilityGrowthModelBase model, bool useProfileLikelihood)
    {
        var result = new FittingResult
        {
            ModelName = model.Name,
            Category = model.Category,
            Model = model
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

            // AIC を比較できるモデルの組（尤度に含まれるデータで決まる）
            result.ComparisonGroup = ModelComparisonGroup.Determine(model, hasCorrectionData: _yFixedData != null);

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

            // パラメータ推定（最終結果は常に全データで推定する。
            // ホールドアウト検証用の推定は PerformHoldoutValidation で訓練区間のみを使って別に行う）
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            var estimation = Estimate(model, _tData, _yData, lossFunction, allowParallel: true, useProfileLikelihood);
            stopwatch.Stop();

            if (estimation == null)
            {
                result.Success = false;
                result.ErrorMessage = "パラメータ推定に失敗しました";
                return result;
            }

            result.OptimizationTimeMs = stopwatch.ElapsedMilliseconds;
            result.OptimizerUsed = estimation.Optimization?.AlgorithmName ?? "";
            result.FunctionEvaluations = estimation.Optimization?.FunctionEvaluations ?? 0;
            result.OptimizationStarts = estimation.Optimization?.StartsSucceeded ?? 1;
            result.StartsConvergedToBest = estimation.Optimization?.StartsConvergedToBest ?? 1;
            if (result.OptimizationStarts > 1 && result.StartsConvergedToBest * 2 < result.OptimizationStarts)
            {
                result.Warnings.Add($"マルチスタート最適化で最良解に収束した開始点が {result.StartsConvergedToBest}/{result.OptimizationStarts} と少なく、解が初期点に依存している可能性があります。");
            }

            if (estimation.Optimization is { Converged: false } optimization)
            {
                result.Warnings.Add($"最適化（{optimization.AlgorithmName}）が収束判定を満たす前に最大反復回数に達しました。推定値が最適でない可能性があります。");
            }
            
            result.ChangePointSearchResult = estimation.ChangePointSearch;
            if (estimation.ChangePointSearch != null)
            {
                var reliability = estimation.ChangePointSearch.ChangePointReliability;
                if (reliability.Contains("非常に低") || reliability.Contains("低"))
                {
                    result.Warnings.Add($"変化点の信頼性が{reliability}です。変化点なしのモデルも検討してください。");
                }
                if (_verbose)
                {
                    Console.WriteLine($"  [{model.Name}] 最適変化点: τ={estimation.ChangePointSearch.BestTau}, 信頼性: {reliability}");
                }
            }

            CompleteResult(model, estimation.Parameters, lossFunction, result, useProfileLikelihood);
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
    /// 変化点モデルをフィッティング（プロファイル尤度法を使うかを指定できる）
    /// </summary>
    public FittingResult FitChangePointModel(ChangePointModelBase changePointModel, bool useRobustDetection = true)
    {
        // 共有の設定（UseProfileLikelihoodForChangePoints）を書き換えず、この推定だけに指定する
        return FitModel(changePointModel, useRobustDetection);
    }

    /// <summary>
    /// 推定したパラメータから適合度・AIC・ホールドアウト・収束予測などを計算して結果に格納する
    /// </summary>
    private void CompleteResult(
        ReliabilityGrowthModelBase model, double[] parameters, ILossFunction lossFunction, FittingResult result, bool useProfileLikelihood)
    {
        // パラメータを結果に格納
        for (int i = 0; i < model.ParameterNames.Length && i < parameters.Length; i++)
        {
            result.Parameters[model.ParameterNames[i]] = parameters[i];
        }

        // 信頼区間計算用：パラメータベクトル（順序を保持）
        result.ParameterVector = (double[])parameters.Clone();

        // 推定潜在バグ総数はパラメータ a ではなく漸近値 m(∞) を使う
        result.EstimatedTotalBugs = model.GetAsymptoticTotalBugs(parameters);

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
            // 十分なサンプルサイズがある場合（比較グループ内では後で AIC/AICc を揃える）
            result.ModelSelectionCriterion = "AIC";
        }

        // 探索範囲の境界への張り付き
        CheckParameterBounds(model, parameters, result);

        // ホールドアウト検証（訓練区間のみで別途推定）
        if (_splitResult != null && _splitResult.IsValid)
        {
            PerformHoldoutValidation(model, lossFunction, result, useProfileLikelihood);
        }

        // 収束予測を計算
        CalculateConvergencePredictions(model, parameters, result);
    }
    
    /// <summary>
    /// 推定の安定性を分析する（末尾の 1〜maxRemovedDays 日を除いて推定し直し、推定潜在バグ総数の変化を見る）
    /// </summary>
    /// <param name="result">対象の推定結果（通常は推奨モデル）</param>
    /// <param name="maxRemovedDays">除く末尾の日数の最大（推定に最低 10 日を残す）</param>
    public StabilityAnalysisResult? AnalyzeStability(FittingResult result, int maxRemovedDays = 5)
    {
        if (!result.Success || result.Model == null) return null;
        int n = _tData.Length;
        int k = Math.Min(maxRemovedDays, n - 10);
        if (k < 1) return null;
        
        var model = result.Model;
        var loss = LossFunctionFactory.GetForModel(_lossType, model, out _, out _);
        // 元の推定と同じ手順で推定し直す（プロファイル尤度法を使ったかは変化点探索結果の有無でわかる）
        bool useProfileLikelihood = result.ChangePointSearchResult != null;
        var points = new StabilityPoint[k];
        Parallel.For(1, k + 1, removed =>
        {
            var t = _tData[..(n - removed)];
            var y = _yData[..(n - removed)];
            double? total = null;
            bool atUpper = false;
            try
            {
                var p = Estimate(model, t, y, loss, allowParallel: false, useProfileLikelihood)?.Parameters;
                if (p != null)
                {
                    total = model.GetAsymptoticTotalBugs(p);
                    var (lower, upper) = model.GetBounds(t, y);
                    atUpper = p[0] >= upper[0] - 1e-3 * (upper[0] - lower[0]);
                }
            }
            catch
            {
                // 推定に失敗した打ち切りは null のまま
            }
            points[removed - 1] = new StabilityPoint(removed, n - removed, total, atUpper);
        });
        
        var warnings = new List<string>();
        var valid = points.Where(p => p.TotalBugs.HasValue && double.IsFinite(p.TotalBugs.Value) && !p.AtUpperBound)
            .Select(p => p.TotalBugs!.Value)
            .Append(result.EstimatedTotalBugs)
            .ToList();
        double? relativeRange = valid.Count >= 2 && result.EstimatedTotalBugs > 0
            ? (valid.Max() - valid.Min()) / result.EstimatedTotalBugs
            : null;
        
        string assessment;
        int boundCount = points.Count(p => p.AtUpperBound);
        if (boundCount > 0)
        {
            assessment = "不安定";
            warnings.Add($"末尾を除いて推定し直すと、{boundCount}/{k} 回で潜在バグ総数を推定できませんでした（a が上限に張り付き）。収束の兆候は直近の数日のデータだけに依存しています。");
        }
        else if (relativeRange == null)
        {
            assessment = "判定不能";
        }
        else if (relativeRange < StabilityAnalysisResult.StableThreshold)
        {
            assessment = "安定";
        }
        else if (relativeRange < StabilityAnalysisResult.UnstableThreshold)
        {
            assessment = "やや不安定";
        }
        else
        {
            assessment = "不安定";
            warnings.Add($"末尾の 1〜{k} 日を除いて推定し直すと、推定潜在バグ総数が {valid.Min():F0}〜{valid.Max():F0} 件（全データでの値の {relativeRange:P0}）変動します。推定は直近のデータに大きく依存しており、収束予測の信頼性は低いと考えられます。");
        }
        
        return new StabilityAnalysisResult
        {
            BaseTotalBugs = result.EstimatedTotalBugs,
            Points = points.ToList(),
            RelativeRange = relativeRange,
            Assessment = assessment,
            Warnings = warnings
        };
    }

    /// <summary>
    /// 推定値が探索範囲の境界に張り付いていないかを確認する
    /// </summary>
    /// <remarks>
    /// 規模パラメータ a（全モデルで先頭）が上限に張り付いている場合、尤度は a をさらに大きくすると改善する状態で、
    /// 潜在バグ総数はデータから推定できていない（上限の値がそのまま出ているだけ）。
    /// このモデルは推奨の対象から外す。
    /// その他のパラメータの張り付きは注意として警告する（ψ=0 や η=1 のような自然な境界は除く）。
    /// </remarks>
    private void CheckParameterBounds(ReliabilityGrowthModelBase model, double[] parameters, FittingResult result)
    {
        var (lower, upper) = model.GetBounds(_tData, _yData);
        var names = model.ParameterNames;

        for (int i = 0; i < parameters.Length && i < lower.Length; i++)
        {
            double tolerance = Math.Max(1e-12, 1e-3 * (upper[i] - lower[i]));
            bool atUpper = parameters[i] >= upper[i] - tolerance;
            bool atLower = parameters[i] <= lower[i] + tolerance;
            if (!atUpper && !atLower) continue;

            string name = i < names.Length ? names[i] : $"#{i}";
            double bound = atUpper ? upper[i] : lower[i];

            if (i == 0 && atUpper)
            {
                result.SelectionExclusionReason ??=
                    $"潜在バグ総数の規模 a が探索範囲の上限（{bound:F1}）に張り付いており、総数を推定できていません";
                result.Warnings.Add(
                    $"パラメータ a が探索範囲の上限（{bound:F1} = 観測最大値の{bound / _yData.Max():F0}倍）に張り付いています。" +
                    "累積曲線に収束の兆候がなく潜在バグ総数を推定できていないため、推定総バグ数・収束予測は信頼できません。");
                continue;
            }

            if (i == 0 && atLower)
            {
                result.Warnings.Add(
                    $"パラメータ a が探索範囲の下限（観測最大値 {bound:F1}）に張り付いています。モデル上はすべてのバグが発見済みとなっていますが、当てはまりを確認してください。");
                continue;
            }

            // 自然な境界（ψ = 0 で指数型、η = 1 で完全除去）は注意しない
            if (bound == 0 || (name.StartsWith("η") && bound == 1.0)) continue;

            result.Warnings.Add(
                $"パラメータ {name} が探索範囲の{(atUpper ? "上限" : "下限")}（{bound:G4}）に張り付いています。推定値の解釈に注意してください。");
        }
    }

    /// <summary>
    /// ホールドアウト検証を実行
    /// </summary>
    /// <remarks>
    /// 訓練区間のみでパラメータを推定し直し、ホールドアウト期間の発見数（増分）を予測して評価する。
    /// ここで得たパラメータは検証専用で、最終結果（全データで推定）には使わない。
    /// </remarks>
    private void PerformHoldoutValidation(ReliabilityGrowthModelBase model, ILossFunction lossFunction, FittingResult result, bool useProfileLikelihood)
    {
        if (_splitResult == null || !_splitResult.IsValid) return;

        var trainParameters = Estimate(model, _splitResult.TrainTimes, _splitResult.TrainValues, lossFunction, allowParallel: true, useProfileLikelihood)?.Parameters;
        if (trainParameters == null)
        {
            result.Warnings.Add("ホールドアウト検証: 訓練区間でのパラメータ推定に失敗したため、検証できませんでした。");
            return;
        }

        var validation = ValidationUtility.CalculateIncrementMetrics(
            _splitResult.TestTimes.Select(t => model.Calculate(t, trainParameters)).ToArray(),
            model.Calculate(_splitResult.TrainTimes[^1], trainParameters),
            _splitResult.TestValues,
            _splitResult.TrainValues[^1]);

        result.Holdout = validation;
        result.HoldoutTrainParameters = trainParameters;
        result.Warnings.AddRange(validation.Warnings);

        if (_verbose)
        {
            Console.WriteLine($"    -> ホールドアウト検証: 期間発見数 予測={validation.PredictedIncrement:F1} 実測={validation.ActualIncrement:F0} " +
                $"(誤差 {validation.IncrementErrorPercent:+0.0;-0.0}%), 日次MAE={validation.DailyMae:F2}");
        }
    }

    /// <summary>
    /// 全モデルでフィッティングを実行
    /// </summary>
    public List<FittingResult> FitAllModels()
    {
        return FitModels(ModelFactory.GetAllModels());
    }

    /// <summary>
    /// 指定モデル群でフィッティングを実行し、比較グループ内の選択基準（AIC/AICc）を揃える
    /// </summary>
    public List<FittingResult> FitModels(IEnumerable<ReliabilityGrowthModelBase> models)
    {
        var results = models.Select(FitModel).ToList();
        ModelComparisonGroup.HarmonizeCriterion(results);
        return results;
    }

    /// <summary>
    /// 推奨モデル（比較グループ内で SelectionScore 最小）を取得
    /// </summary>
    /// <remarks>
    /// AIC は同じデータ・同じ尤度のモデル同士でしか比較できないため、
    /// 発見数のみの尤度のグループ（存在しなければ優先順で次のグループ）から選ぶ。
    /// 推奨対象外（<see cref="FittingResult.SelectionExclusionReason"/>）のモデルは除くが、
    /// グループ内のすべてが対象外の場合は対象外のモデルから選ぶ（呼び出し側で理由を警告すること）。
    /// </remarks>
    public FittingResult? GetBestModel(List<FittingResult> results, string? category = null)
    {
        var filtered = results.Where(ModelComparisonGroup.IsComparable);

        if (category != null)
            filtered = filtered.Where(r => r.Category == category);

        var primaryGroup = ModelComparisonGroup.SelectPrimaryGroup(filtered);

        var ranked = filtered
            .Where(r => r.ComparisonGroup == primaryGroup)
            .OrderBy(r => r.SelectionScore)
            .ToList();

        return ranked.FirstOrDefault(r => r.SelectionExclusionReason == null) ?? ranked.FirstOrDefault();
    }

    /// <summary>
    /// 変化点モデルについて、変化点なしの帰無モデルとの尤度比検定を行う
    /// </summary>
    /// <remarks>
    /// 変化点 τ は AIC の正則条件を満たさないため、AIC/AICc だけで選ぶと変化点モデルが過大に選ばれる。
    /// 検定で有意でない変化点モデルは推奨の対象から外す。
    /// 観測データとシミュレーションで同じ推定手順（プロファイル尤度法など）を使う。
    /// </remarks>
    /// <param name="results">フィッティング結果（変化点なしの帰無モデルの結果が含まれていれば再利用する）</param>
    /// <param name="simulations">p 値のシミュレーション回数</param>
    /// <param name="seed">乱数シード</param>
    /// <returns>検定した変化点モデルの数</returns>
    /// <remarks>
    /// 検定は計算量が大きいため、推奨されうる変化点モデル
    /// （選択基準値が同じ比較グループの変化点なしモデルの最良値より小さいもの）だけを検定する。
    /// それ以外の変化点モデルは、検定の有無にかかわらず推奨されない。
    /// </remarks>
    public int TestChangePoints(List<FittingResult> results, int simulations, int? seed = null)
    {
        int tested = 0;
        foreach (var result in results.Where(r => ModelComparisonGroup.IsComparable(r) && r.Model is ChangePointModelBase).ToList())
        {
            double bestWithoutChangePoint = results
                .Where(r => ModelComparisonGroup.IsComparable(r) && r.ComparisonGroup == result.ComparisonGroup
                            && r.Model is not ChangePointModelBase && r.SelectionExclusionReason == null)
                .Select(r => r.SelectionScore)
                .DefaultIfEmpty(double.PositiveInfinity)
                .Min();
            if (result.SelectionScore >= bestWithoutChangePoint || result.SelectionExclusionReason != null)
            {
                continue;
            }
            
            tested++;
            var changePointModel = (ChangePointModelBase)result.Model!;
            var nullModel = changePointModel.CreateNullModel();

            var nullResult = results.FirstOrDefault(r => r.Success && r.ModelName == nullModel.Name && r.Model != null)
                ?? FitModel(nullModel);
            if (!nullResult.Success)
            {
                result.Warnings.Add($"変化点の尤度比検定: 帰無モデル {nullModel.Name} の推定に失敗したため検定できませんでした。");
                continue;
            }
            nullModel = nullResult.Model!;

            var loss = LossFunctionFactory.Create(_lossType);
            // 観測データと同じ推定手順で推定し直す（プロファイル尤度法を使ったかは変化点探索結果の有無でわかる）
            bool useProfileLikelihood = result.ChangePointSearchResult != null;
            var service = new ChangePointLRTService(simulations, seed, _verbose);
            var test = service.Test(
                _tData, _yData, nullModel, changePointModel,
                nullResult.ParameterVector, result.ParameterVector,
                y => Estimate(nullModel, _tData, y, loss, allowParallel: false, useProfileLikelihood)?.Parameters,
                y => Estimate(changePointModel, _tData, y, loss, allowParallel: false, useProfileLikelihood)?.Parameters);

            result.ChangePointTest = test;
            if (!test.Success)
            {
                result.Warnings.Add($"変化点の尤度比検定に失敗しました: {test.ErrorMessage}");
                continue;
            }
            if (test.Warning != null)
            {
                result.Warnings.Add(test.Warning);
            }
            if (!test.IsChangePointSignificant)
            {
                result.SelectionExclusionReason ??=
                    $"変化点の尤度比検定で有意でない（p={test.SimulatedPValue:F3}）。変化点なしの {test.NullModelName} で十分";
            }
        }
        return tested;
    }

    /// <summary>
    /// 累積データを受け取り、本推定と同じ手順（損失関数・最適化手法・変化点の推定方法）で
    /// パラメータを推定し直す関数を返す（ブートストラップ用。失敗時は null を返す）
    /// </summary>
    public Func<double[], double[]?> CreateRefitFunction(ReliabilityGrowthModelBase model)
    {
        var loss = LossFunctionFactory.GetForModel(_lossType, model, out _, out _);
        bool useProfileLikelihood = UseProfileLikelihoodForChangePoints;
        return y => Estimate(model, _tData, y, loss, allowParallel: false, useProfileLikelihood)?.Parameters;
    }

    /// <summary>
    /// 推定結果
    /// </summary>
    private sealed record EstimationOutcome(
        double[] Parameters, OptimizationResult? Optimization, ChangePointSearchResult? ChangePointSearch);

    /// <summary>
    /// パラメータ推定（変化点モデルはプロファイル尤度法、それ以外は選択された最適化手法）
    /// </summary>
    /// <param name="allowParallel">内部で並列化するか（外側で並列実行している場合は false）</param>
    /// <param name="useProfileLikelihood">τ を1つ持つ変化点モデルをプロファイル尤度法で推定するか</param>
    private EstimationOutcome? Estimate(
        ReliabilityGrowthModelBase model,
        double[] tData,
        double[] yData,
        ILossFunction lossFunction,
        bool allowParallel,
        bool useProfileLikelihood)
    {
        if (useProfileLikelihood
            && model is ChangePointModelBase changePointModel
            && FixedTauChangePointModel.Supports(changePointModel))
        {
            // τ を固定した部分問題は滑らかな低次元問題なので、局所最適化の Nelder-Mead で十分に解ける
            // （GO・変化点の合成データ 48 件で DE と同じ最良値に到達し、約 7 倍速い）。
            // ブートストラップや尤度比検定では部分問題を数千回解くため、ここは指定された最適化手法によらず固定する
            var detector = new RobustChangePointDetector(
                optimizerType: OptimizerType.NelderMead,
                lossType: _lossType,
                verbose: _verbose && allowParallel)
            {
                UseParallel = allowParallel
            };

            var search = detector.FindOptimalChangePoint(changePointModel, tData, yData, _yFixedData);
            if (search.Success && search.BestFittingResult != null)
            {
                // 固定τモデルのパラメータ（τ を除く）に τ を付け加えて元のモデルのパラメータにする
                double[] full = [.. search.BestFittingResult.ParameterVector, search.BestTau];
                return new EstimationOutcome(full, null, search);
            }

            if (_verbose && allowParallel)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"  [{model.Name}] 変化点探索に失敗したため τ も同時に推定します: {search.ErrorMessage}");
                Console.ResetColor();
            }
        }

        var optimization = Optimize(model, tData, yData, lossFunction, allowParallel);
        return optimization?.Success == true ? new EstimationOutcome(optimization.Parameters, optimization, null) : null;
    }

    /// <summary>
    /// 選択されたオプティマイザ（マルチスタート指定時はマルチスタート）で最適化
    /// </summary>
    private OptimizationResult? Optimize(
        ReliabilityGrowthModelBase model,
        double[] tData,
        double[] yData,
        ILossFunction lossFunction,
        bool allowParallel)
    {
        var (lower, upper) = model.GetBounds(tData, yData);
        var initial = model.GetInitialParameters(tData, yData);

        // 目的関数の構築
        // 全モデルで損失関数を使用（FREモデルの場合は発見+修正の同時推定）
        Func<double[], double> objective = p => lossFunction.Evaluate(tData, yData, model, p, _yFixedData);
        bool log = _verbose && allowParallel;

        OptimizationResult result;

        if (_optimizerType == OptimizerType.AutoSelect)
        {
            if (log)
                Console.WriteLine($"  [{model.Name}] 全アルゴリズムで最適化中...");

            result = OptimizerFactory.AutoOptimize(objective, lower, upper, initial, log);
        }
        else if (_multiStarts > 1)
        {
            result = OptimizerFactory.MultiStartOptimize(
                objective, lower, upper, initial,
                optimizerFactory: () => OptimizerFactory.Create(_optimizerType),
                numStarts: _multiStarts,
                verbose: log);
        }
        else
        {
            var optimizer = OptimizerFactory.Create(_optimizerType);

            if (log)
                Console.WriteLine($"  [{model.Name}] {optimizer.Name}で最適化中...");

            result = optimizer.Optimize(objective, lower, upper, initial);
        }

        if (log && result.Success)
        {
            string lossName = _lossType == LossType.Mle ? "NLL" : "SSE";
            Console.WriteLine($"    -> {lossName}={result.ObjectiveValue:F4}, " +
                $"評価回数={result.FunctionEvaluations}, 時間={result.ElapsedMilliseconds}ms");
        }

        return result;
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
                // 観測値は未到達でも、モデル上はすでに到達している（到達日 ≤ 現在日）ことがある。
                // その場合も到達日を示し、残り日数は 0 とする
                double predictedDay = model.DayForRatio(ratio, parameters);

                if (double.IsFinite(predictedDay))
                {
                    prediction.PredictedDay = predictedDay;
                    prediction.RemainingDays = Math.Max(0, predictedDay - currentDay);
                    prediction.PredictedDate = _testData.DateForDay(predictedDay);
                }
            }

            result.ConvergencePredictions[name] = prediction;
        }
    }
}
