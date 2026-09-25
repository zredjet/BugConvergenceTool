using BugConvergenceTool.Models;

namespace BugConvergenceTool.Services;

/// <summary>
/// データ品質・モデル選択に関する警告を生成するサービス
/// </summary>
public static class WarningService
{
    /// <summary>
    /// データ品質に関する警告しきい値
    /// </summary>
    public static class Thresholds
    {
        /// <summary>
        /// 非常に少ないデータ点数
        /// </summary>
        public const int VeryFewDataPoints = 7;
        
        /// <summary>
        /// パラメータ数に対する最小データ倍率
        /// </summary>
        public const int MinDataPerParameter = 3;
        
        /// <summary>
        /// 少ないバグ総数
        /// </summary>
        public const int FewTotalBugs = 20;
    }
    
    /// <summary>
    /// データ品質に関する警告を生成
    /// </summary>
    /// <param name="dataPointCount">データ点数</param>
    /// <param name="totalBugs">バグ総数</param>
    /// <param name="parameterCount">パラメータ数（モデルの）</param>
    /// <returns>警告メッセージのリスト</returns>
    public static List<string> GenerateDataQualityWarnings(int dataPointCount, double totalBugs, int parameterCount)
    {
        var warnings = new List<string>();
        
        // データ点数が非常に少ない
        if (dataPointCount < Thresholds.VeryFewDataPoints)
        {
            warnings.Add($"データ点数が少ないため（{dataPointCount}点）、パラメータ推定が不安定になる可能性があります。");
        }
        
        // パラメータ数に対してデータ点数が少ない
        int minRequired = parameterCount * Thresholds.MinDataPerParameter;
        if (dataPointCount < minRequired)
        {
            warnings.Add($"パラメータ数（{parameterCount}）に比べてデータ点数（{dataPointCount}点）が少ないため、過適合のリスクがあります。" +
                        $"最低{minRequired}点以上を推奨します。");
        }
        
        // バグ件数が少ない
        if (totalBugs < Thresholds.FewTotalBugs)
        {
            warnings.Add($"バグ件数が少ないため（{totalBugs:F0}件）、信頼度成長モデルによる評価の信頼性は限定的です。");
        }
        
        return warnings;
    }
    
    /// <summary>
    /// ホールドアウト検証結果に関する警告を生成
    /// </summary>
    /// <param name="result">フィッティング結果</param>
    /// <param name="allResults">全モデルの結果</param>
    /// <returns>警告メッセージのリスト</returns>
    public static List<string> GenerateHoldoutWarnings(FittingResult result, IEnumerable<FittingResult> allResults)
    {
        var warnings = new List<string>();
        
        // 実測の発見数が予測区間（Poisson 変動 + 推定の不確実性）の外にあるときだけ警告する。
        // 以前は相対誤差 30%・60% の固定しきい値で判定しており、正しいモデルでも Poisson 変動だけで
        // 末尾 5 日のホールドアウトの約半数に警告が出ていた
        var h = result.Holdout;
        if (h == null || !h.IsOutsidePredictionInterval)
            return warnings;
        
        string direction = h.PredictedIncrement > h.ActualIncrement ? "過大" : "過小";
        string uncertainty = h.IncludesParameterUncertainty ? "" : "（推定の不確実性を計算できなかったため Poisson 変動のみの区間）";
        warnings.Add($"ホールドアウト検証で、末尾 {h.TestCount} 日の発見数の実測 {h.ActualIncrement:F0} 件が " +
                     $"{h.PredictionLevel:P0}予測区間 [{h.PredictionLower:F0}, {h.PredictionUpper:F0}]（予測 {h.PredictedIncrement:F1} 件）の外にあり、" +
                     $"{direction}に予測しています{uncertainty}。このモデルの将来予測は信頼できない可能性があります。");
        
        // 他モデルと比較して明らかに悪い（自身が予測区間の外にある場合だけ比較する）
        var successfulResults = allResults.Where(r => r.Success && r.HoldoutAbsIncrementErrorPercent.HasValue).ToList();
        if (successfulResults.Count > 1 && result.HoldoutAbsIncrementErrorPercent is double absError)
        {
            double avgError = successfulResults.Average(r => r.HoldoutAbsIncrementErrorPercent!.Value);
            double minError = successfulResults.Min(r => r.HoldoutAbsIncrementErrorPercent!.Value);
            
            if (absError > avgError * 1.5 && absError > minError * 2)
            {
                warnings.Add($"本モデルのホールドアウト誤差（{absError:F1}%）は他モデルの平均（{avgError:F1}%）より明らかに大きく、予測性能が低い可能性があります。");
            }
        }
        
        return warnings;
    }
    
    /// <summary>
    /// モデル選択に関する警告を生成
    /// </summary>
    /// <param name="selectedResult">選択されたモデルの結果</param>
    /// <param name="allResults">全モデルの結果</param>
    /// <returns>警告メッセージのリスト</returns>
    public static List<string> GenerateModelSelectionWarnings(FittingResult selectedResult, IEnumerable<FittingResult> allResults)
    {
        var warnings = new List<string>();
        var successfulResults = allResults.Where(r => r.Success).ToList();
        
        if (successfulResults.Count <= 1)
            return warnings;
        
        // AICベストだがホールドアウトでは最良でない場合（選択したモデル自身が予測区間の外にある場合だけ。
        // 区間内なら誤差の差は Poisson 変動の範囲で、予測性能の差とはいえない）
        if (selectedResult.HoldoutAbsIncrementErrorPercent.HasValue && selectedResult.Holdout?.IsOutsidePredictionInterval == true)
        {
            var bestByHoldout = successfulResults
                .Where(r => r.HoldoutAbsIncrementErrorPercent.HasValue)
                .OrderBy(r => r.HoldoutAbsIncrementErrorPercent!.Value)
                .FirstOrDefault();
            
            if (bestByHoldout != null && bestByHoldout.ModelName != selectedResult.ModelName)
            {
                double selectedError = selectedResult.HoldoutAbsIncrementErrorPercent.Value;
                double bestError = bestByHoldout.HoldoutAbsIncrementErrorPercent!.Value;
                
                // 相対比較だけだと誤差が数%同士でも警告が出るため、差が5ポイント以上ある場合に限る
                if (selectedError > bestError * 1.3 && selectedError - bestError >= 5.0)
                {
                    warnings.Add($"AIC最小で選択された '{selectedResult.ModelName}' のホールドアウト誤差（{selectedError:F1}%）は、" +
                                $"誤差最小の '{bestByHoldout.ModelName}'（{bestError:F1}%）より大きいです。" +
                                $"予測精度を重視する場合は後者も検討してください。");
                }
            }
        }
        
        // R²が低い場合
        if (selectedResult.R2 < 0.9)
        {
            warnings.Add($"選択されたモデルの決定係数（R²={selectedResult.R2:F4}）が0.9未満です。" +
                        $"データへの当てはまりが不十分な可能性があります。");
        }
        
        return warnings;
    }
    
    /// <summary>
    /// 全ての警告をまとめて生成
    /// </summary>
    public static List<string> GenerateAllWarnings(
        FittingResult selectedResult,
        IEnumerable<FittingResult> allResults,
        int dataPointCount,
        double totalBugs)
    {
        var warnings = new List<string>();
        
        // データ品質の警告
        warnings.AddRange(GenerateDataQualityWarnings(
            dataPointCount, 
            totalBugs, 
            selectedResult.Parameters.Count));
        
        // ホールドアウト検証の警告
        warnings.AddRange(GenerateHoldoutWarnings(selectedResult, allResults));
        
        // モデル選択の警告
        warnings.AddRange(GenerateModelSelectionWarnings(selectedResult, allResults));
        
        // 結果に含まれる警告を追加
        warnings.AddRange(selectedResult.Warnings);
        
        return warnings.Distinct().ToList();
    }
    
    /// <summary>
    /// 警告をコンソールに出力
    /// </summary>
    public static void PrintWarnings(IEnumerable<string> warnings)
    {
        var warningList = warnings.ToList();
        if (!warningList.Any()) return;
        
        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine("=== 警告・注意事項 ===");
        Console.WriteLine();
        
        for (int i = 0; i < warningList.Count; i++)
        {
            Console.WriteLine($"  {i + 1}. {warningList[i]}");
        }
        
        Console.ResetColor();
    }
}
