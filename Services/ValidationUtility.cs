namespace BugConvergenceTool.Services;

/// <summary>
/// 時系列データ分割結果
/// </summary>
public sealed class TimeSeriesSplitResult
{
    /// <summary>
    /// 訓練用データの時間配列
    /// </summary>
    public double[] TrainTimes { get; init; } = Array.Empty<double>();
    
    /// <summary>
    /// 訓練用データの累積バグ数配列
    /// </summary>
    public double[] TrainValues { get; init; } = Array.Empty<double>();
    
    /// <summary>
    /// テスト用データの時間配列
    /// </summary>
    public double[] TestTimes { get; init; } = Array.Empty<double>();
    
    /// <summary>
    /// テスト用データの累積バグ数配列
    /// </summary>
    public double[] TestValues { get; init; } = Array.Empty<double>();
    
    /// <summary>
    /// 訓練データ点数
    /// </summary>
    public int TrainCount => TrainTimes.Length;
    
    /// <summary>
    /// テストデータ点数
    /// </summary>
    public int TestCount => TestTimes.Length;
    
    /// <summary>
    /// 分割が有効かどうか
    /// </summary>
    public bool IsValid => TrainCount >= 3 && TestCount >= 1;
    
    /// <summary>
    /// 分割に関する警告メッセージ
    /// </summary>
    public string? Warning { get; init; }
}

/// <summary>
/// ホールドアウト検証の結果
/// </summary>
/// <remarks>
/// 累積値そのものの誤差は、累積値が大きいため相対誤差が常に小さく出て予測性能を表さない。
/// そのため、ホールドアウト期間中の「増分」（新たに発見されるバグ数）で評価する。
/// </remarks>
public sealed class HoldoutValidationResult
{
    /// <summary>
    /// ホールドアウト期間中の予測発見数（m(T_end) - m(T_train)）
    /// </summary>
    public double PredictedIncrement { get; init; }
    
    /// <summary>
    /// ホールドアウト期間中の実測発見数
    /// </summary>
    public double ActualIncrement { get; init; }
    
    /// <summary>
    /// 期間増分の相対誤差（%、符号付き）= (予測 - 実測) / 実測 × 100
    /// 正なら過大予測、負なら過小予測。実測が 0 の場合は NaN
    /// </summary>
    public double IncrementErrorPercent { get; init; }
    
    /// <summary>
    /// 日次増分の平均絶対誤差（件/日）
    /// </summary>
    public double DailyMae { get; init; }
    
    /// <summary>
    /// 日次増分の二乗平均平方根誤差（件/日）
    /// </summary>
    public double DailyRmse { get; init; }
    
    /// <summary>
    /// テストデータ点数
    /// </summary>
    public int TestCount { get; init; }
    
    /// <summary>
    /// 検証に関する警告メッセージ
    /// </summary>
    public List<string> Warnings { get; init; } = new();
    
    /// <summary>
    /// 日次増分の予測値（テスト期間）
    /// </summary>
    public double[] PredictedDaily { get; init; } = Array.Empty<double>();
    
    /// <summary>
    /// 日次増分の実測値（テスト期間）
    /// </summary>
    public double[] ActualDaily { get; init; } = Array.Empty<double>();
}

/// <summary>
/// 時系列データ分割・検証ユーティリティ
/// </summary>
public static class ValidationUtility
{
    /// <summary>
    /// 末尾のN日をテスト用に分割
    /// </summary>
    /// <param name="tData">全時間データ</param>
    /// <param name="yData">全累積バグ数データ</param>
    /// <param name="holdoutDays">ホールドアウトする日数</param>
    /// <returns>分割結果</returns>
    public static TimeSeriesSplitResult SplitLastNDays(double[] tData, double[] yData, int holdoutDays)
    {
        if (tData.Length != yData.Length)
            throw new ArgumentException("時間データと値データの長さが一致しません");
        
        int totalCount = tData.Length;
        string? warning = null;
        
        // ガードチェック
        if (holdoutDays <= 0)
        {
            return new TimeSeriesSplitResult
            {
                TrainTimes = tData,
                TrainValues = yData,
                TestTimes = Array.Empty<double>(),
                TestValues = Array.Empty<double>(),
                Warning = "ホールドアウト日数が0以下のため、分割は行われません"
            };
        }
        
        if (holdoutDays >= totalCount)
        {
            return new TimeSeriesSplitResult
            {
                TrainTimes = Array.Empty<double>(),
                TrainValues = Array.Empty<double>(),
                TestTimes = tData,
                TestValues = yData,
                Warning = "ホールドアウト日数がデータ点数以上のため、訓練データがありません"
            };
        }
        
        int trainCount = totalCount - holdoutDays;
        
        // 訓練データが少なすぎる場合の警告
        if (trainCount < 5)
        {
            warning = $"訓練データ点数が少ないです（{trainCount}点）。推定結果の信頼性が低下する可能性があります。";
        }
        else if (trainCount < 10)
        {
            warning = $"訓練データ点数がやや少ないです（{trainCount}点）。";
        }
        
        return new TimeSeriesSplitResult
        {
            TrainTimes = tData[..trainCount],
            TrainValues = yData[..trainCount],
            TestTimes = tData[trainCount..],
            TestValues = yData[trainCount..],
            Warning = warning
        };
    }
    
    /// <summary>
    /// 比率でデータを分割（例: 0.8 = 80%を訓練用）
    /// </summary>
    public static TimeSeriesSplitResult SplitByRatio(double[] tData, double[] yData, double trainRatio)
    {
        if (trainRatio <= 0 || trainRatio >= 1)
            throw new ArgumentOutOfRangeException(nameof(trainRatio), "訓練比率は0より大きく1より小さい値である必要があります");
        
        int trainCount = (int)(tData.Length * trainRatio);
        int holdoutDays = tData.Length - trainCount;
        
        return SplitLastNDays(tData, yData, holdoutDays);
    }
    
    /// <summary>
    /// ホールドアウト期間の増分に基づく評価指標を計算
    /// </summary>
    /// <param name="predictedCumulative">テスト期間の各時点のモデル予測累積値 m(tᵢ)</param>
    /// <param name="predictedAtTrainEnd">訓練最終時点のモデル予測累積値 m(T_train)</param>
    /// <param name="actualCumulative">テスト期間の各時点の実測累積値</param>
    /// <param name="actualAtTrainEnd">訓練最終時点の実測累積値</param>
    /// <remarks>
    /// 予測増分はモデル自身の m(T_train) を起点とする（訓練最終時点での当てはめのずれを評価に混ぜない）。
    /// </remarks>
    public static HoldoutValidationResult CalculateIncrementMetrics(
        double[] predictedCumulative,
        double predictedAtTrainEnd,
        double[] actualCumulative,
        double actualAtTrainEnd)
    {
        if (predictedCumulative.Length != actualCumulative.Length)
            throw new ArgumentException("予測値と実測値の長さが一致しません");
        
        int n = predictedCumulative.Length;
        if (n == 0)
        {
            return new HoldoutValidationResult
            {
                IncrementErrorPercent = double.NaN,
                DailyMae = double.NaN,
                DailyRmse = double.NaN,
                TestCount = 0,
                Warnings = new List<string> { "テストデータがありません" }
            };
        }
        
        var predictedDaily = new double[n];
        var actualDaily = new double[n];
        double prevPredicted = predictedAtTrainEnd;
        double prevActual = actualAtTrainEnd;
        for (int i = 0; i < n; i++)
        {
            predictedDaily[i] = predictedCumulative[i] - prevPredicted;
            actualDaily[i] = actualCumulative[i] - prevActual;
            prevPredicted = predictedCumulative[i];
            prevActual = actualCumulative[i];
        }
        
        double predictedIncrement = predictedCumulative[^1] - predictedAtTrainEnd;
        double actualIncrement = actualCumulative[^1] - actualAtTrainEnd;
        
        var warnings = new List<string>();
        double incrementErrorPercent;
        if (actualIncrement > 0)
        {
            incrementErrorPercent = (predictedIncrement - actualIncrement) / actualIncrement * 100.0;
        }
        else
        {
            incrementErrorPercent = double.NaN;
            warnings.Add("ホールドアウト期間に新たなバグ発見がないため、期間増分の相対誤差を計算できません（日次誤差のみで評価）");
        }
        
        double dailyMae = 0, sumSq = 0;
        for (int i = 0; i < n; i++)
        {
            double e = predictedDaily[i] - actualDaily[i];
            dailyMae += Math.Abs(e);
            sumSq += e * e;
        }
        dailyMae /= n;
        
        return new HoldoutValidationResult
        {
            PredictedIncrement = predictedIncrement,
            ActualIncrement = actualIncrement,
            IncrementErrorPercent = incrementErrorPercent,
            DailyMae = dailyMae,
            DailyRmse = Math.Sqrt(sumSq / n),
            TestCount = n,
            PredictedDaily = predictedDaily,
            ActualDaily = actualDaily,
            Warnings = warnings
        };
    }
}
