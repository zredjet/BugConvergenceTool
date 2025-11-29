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
public sealed class HoldoutValidationResult
{
    /// <summary>
    /// 平均二乗誤差（Mean Squared Error）
    /// </summary>
    public double Mse { get; init; }
    
    /// <summary>
    /// 平均絶対パーセント誤差（Mean Absolute Percentage Error）
    /// </summary>
    public double Mape { get; init; }
    
    /// <summary>
    /// 平均絶対誤差（Mean Absolute Error）
    /// </summary>
    public double Mae { get; init; }
    
    /// <summary>
    /// テストデータ点数
    /// </summary>
    public int TestCount { get; init; }
    
    /// <summary>
    /// 検証に関する警告メッセージ
    /// </summary>
    public List<string> Warnings { get; init; } = new();
    
    /// <summary>
    /// 予測値の配列（テスト期間）
    /// </summary>
    public double[] Predictions { get; init; } = Array.Empty<double>();
    
    /// <summary>
    /// 実測値の配列（テスト期間）
    /// </summary>
    public double[] Actuals { get; init; } = Array.Empty<double>();
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
    /// ホールドアウト検証の評価指標を計算
    /// </summary>
    /// <param name="predictions">予測値</param>
    /// <param name="actuals">実測値</param>
    /// <returns>検証結果</returns>
    public static HoldoutValidationResult CalculateMetrics(double[] predictions, double[] actuals)
    {
        if (predictions.Length != actuals.Length)
            throw new ArgumentException("予測値と実測値の長さが一致しません");
        
        int n = predictions.Length;
        if (n == 0)
        {
            return new HoldoutValidationResult
            {
                Mse = double.NaN,
                Mape = double.NaN,
                Mae = double.NaN,
                TestCount = 0,
                Warnings = new List<string> { "テストデータがありません" }
            };
        }
        
        var warnings = new List<string>();
        
        // 残差の計算
        var residuals = new double[n];
        for (int i = 0; i < n; i++)
        {
            residuals[i] = actuals[i] - predictions[i];
        }
        
        // MSE: Mean Squared Error
        double mse = residuals.Select(e => e * e).Average();
        
        // MAE: Mean Absolute Error
        double mae = residuals.Select(e => Math.Abs(e)).Average();
        
        // MAPE: Mean Absolute Percentage Error（0除算ガード付き）
        const double epsilon = 1e-6;
        int zeroCount = 0;
        double mapeSum = 0;
        
        for (int i = 0; i < n; i++)
        {
            double actual = actuals[i];
            if (Math.Abs(actual) < epsilon)
            {
                zeroCount++;
                continue;
            }
            mapeSum += Math.Abs(residuals[i] / actual);
        }
        
        double mape;
        if (zeroCount == n)
        {
            mape = double.NaN;
            warnings.Add("全ての実測値がゼロに近いため、MAPEを計算できません");
        }
        else
        {
            mape = (mapeSum / (n - zeroCount)) * 100.0;
            if (zeroCount > 0)
            {
                warnings.Add($"{zeroCount}点の実測値がゼロに近いため、MAPEの計算から除外しました");
            }
        }
        
        // 警告の追加
        if (mape > 50 && !double.IsNaN(mape))
        {
            warnings.Add("ホールドアウト区間での予測誤差が大きく（MAPE > 50%）、将来予測の不確実性が高いと考えられます");
        }
        
        return new HoldoutValidationResult
        {
            Mse = mse,
            Mape = mape,
            Mae = mae,
            TestCount = n,
            Predictions = predictions,
            Actuals = actuals,
            Warnings = warnings
        };
    }
}
