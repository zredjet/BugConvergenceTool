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
        
        /// <summary>
        /// 高いMAPE（予測精度低下の警告）
        /// </summary>
        public const double HighMape = 50.0;
        
        /// <summary>
        /// 非常に高いMAPE（信頼性警告）
        /// </summary>
        public const double VeryHighMape = 100.0;
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
        
        if (!result.HoldoutMape.HasValue)
            return warnings;
        
        double mape = result.HoldoutMape.Value;
        
        // MAPEが高い
        if (mape > Thresholds.VeryHighMape)
        {
            warnings.Add($"ホールドアウト検証でMAPEが{mape:F1}%と非常に高く、このモデルの将来予測は信頼できない可能性があります。");
        }
        else if (mape > Thresholds.HighMape)
        {
            warnings.Add($"ホールドアウト検証でMAPEが{mape:F1}%と高く、将来予測の不確実性が高いと考えられます。");
        }
        
        // 他モデルと比較して明らかに悪い
        var successfulResults = allResults.Where(r => r.Success && r.HoldoutMape.HasValue).ToList();
        if (successfulResults.Count > 1)
        {
            double avgMape = successfulResults.Average(r => r.HoldoutMape!.Value);
            double minMape = successfulResults.Min(r => r.HoldoutMape!.Value);
            
            if (mape > avgMape * 1.5 && mape > minMape * 2)
            {
                warnings.Add($"本モデルのMAPE（{mape:F1}%）は他モデルの平均（{avgMape:F1}%）より明らかに高く、予測性能が低い可能性があります。");
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
        
        // AICベストだがホールドアウトでは最良でない場合
        if (selectedResult.HoldoutMape.HasValue)
        {
            var bestByMape = successfulResults
                .Where(r => r.HoldoutMape.HasValue)
                .OrderBy(r => r.HoldoutMape!.Value)
                .FirstOrDefault();
            
            if (bestByMape != null && bestByMape.ModelName != selectedResult.ModelName)
            {
                double selectedMape = selectedResult.HoldoutMape.Value;
                double bestMape = bestByMape.HoldoutMape!.Value;
                
                if (selectedMape > bestMape * 1.3)
                {
                    warnings.Add($"AIC最小で選択された '{selectedResult.ModelName}' のMAPE（{selectedMape:F1}%）は、" +
                                $"MAPE最小の '{bestByMape.ModelName}'（{bestMape:F1}%）より高いです。" +
                                $"予測精度を重視する場合は後者を検討してください。");
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
