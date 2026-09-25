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
    /// 期間発見数の予測区間の下限（件、<see cref="PredictionLevel"/>）
    /// </summary>
    public double PredictionLower { get; set; } = double.NaN;
    
    /// <summary>
    /// 期間発見数の予測区間の上限（件）
    /// </summary>
    public double PredictionUpper { get; set; } = double.NaN;
    
    /// <summary>
    /// 予測区間の水準
    /// </summary>
    public double PredictionLevel { get; set; } = 0.95;
    
    /// <summary>
    /// 実測の期間発見数の両側の裾確率（小さいほど予測から外れている）
    /// </summary>
    public double TailProbability { get; set; } = double.NaN;
    
    /// <summary>
    /// 予測区間にパラメータ推定の不確実性（Fisher 情報行列）を含めたか（false なら Poisson 変動のみ）
    /// </summary>
    public bool IncludesParameterUncertainty { get; set; }
    
    /// <summary>
    /// 実測の期間発見数が予測区間の外にあるか
    /// </summary>
    public bool IsOutsidePredictionInterval =>
        double.IsFinite(PredictionLower) && (ActualIncrement < PredictionLower || ActualIncrement > PredictionUpper);
    
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
    /// ホールドアウト期間の発見数の予測分布（Poisson と、パラメータの不確実性の対数正規分布の混合）の区間と裾確率
    /// </summary>
    /// <param name="predictedIncrement">予測発見数 Δ̂ = m(T_end) - m(T_train)</param>
    /// <param name="logStandardError">ln Δ̂ の標準誤差（パラメータの不確実性。0 なら Poisson のみ）</param>
    /// <param name="actualIncrement">実測発見数</param>
    /// <param name="level">予測区間の水準</param>
    /// <remarks>
    /// X ~ Poisson(Λ)、ln Λ ~ N(ln Δ̂, s²) とし、Λ の分布を正規分位点の格子（200 点）で平均する（決定的）。
    /// 以前は相対誤差 30%・60% の固定しきい値で警告しており、Poisson 変動だけで正しいモデルでも
    /// 末尾 5 日で約半数に警告が出ていた。
    /// </remarks>
    public static (double Lower, double Upper, double TailProbability) PredictiveInterval(
        double predictedIncrement, double logStandardError, double actualIncrement, double level = 0.95)
    {
        // 予測発見数が求まらない（負・非有限）場合は判定しない（区間外とも区間内ともしない）
        if (!(predictedIncrement >= 0) || !double.IsFinite(predictedIncrement))
            return (double.NaN, double.NaN, double.NaN);
        
        const int gridSize = 200;
        double s = double.IsFinite(logStandardError) && logStandardError > 0 ? logStandardError : 0;
        var lambdas = new double[s > 0 ? gridSize : 1];
        if (s > 0)
        {
            for (int j = 0; j < gridSize; j++)
                lambdas[j] = predictedIncrement * Math.Exp(s * MathNet.Numerics.Distributions.Normal.InvCDF(0, 1, (j + 0.5) / gridSize));
        }
        else
        {
            lambdas[0] = predictedIncrement;
        }
        
        // 混合分布の累積分布関数 F(x) = P(X ≤ x)
        double Cdf(double x)
        {
            if (x < 0) return 0;
            double sum = 0;
            foreach (double lambda in lambdas)
                sum += lambda > 1e-12 ? MathNet.Numerics.Distributions.Poisson.CDF(lambda, Math.Floor(x)) : 1.0;
            return sum / lambdas.Length;
        }
        
        // F(x) ≥ p となる最小の整数 x（倍々で上端を見つけてから二分探索する。
        // 以前は x = 0, 1, 2, … と線形に走査しており、s や予測件数が大きいと数十億回の評価になった）
        double Quantile(double p)
        {
            if (Cdf(0) >= p) return 0;
            double lo = 0, hi = 1;
            while (Cdf(hi) < p)
            {
                lo = hi;
                hi *= 2;
                if (hi > 1e15) return double.PositiveInfinity;
            }
            // 不変条件: F(lo) < p ≤ F(hi)
            while (hi - lo > 1)
            {
                double mid = Math.Floor((lo + hi) / 2);
                if (Cdf(mid) >= p) hi = mid; else lo = mid;
            }
            return hi;
        }
        
        double alpha = 1 - level;
        double lower = Quantile(alpha / 2);
        double upper = Quantile(1 - alpha / 2);
        
        double actual = Math.Round(actualIncrement);
        double tail = Math.Min(1.0, 2 * Math.Min(Cdf(actual), 1 - Cdf(actual - 1)));
        return (lower, upper, tail);
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
