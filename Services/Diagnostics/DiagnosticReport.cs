using BugConvergenceTool.Models;

namespace BugConvergenceTool.Services.Diagnostics;

/// <summary>
/// 診断レポートの総合結果
/// </summary>
public class DiagnosticReport
{
    /// <summary>モデル名</summary>
    public string ModelName { get; init; } = "";
    
    /// <summary>残差分析結果</summary>
    public ResidualAnalysisResult? ResidualAnalysis { get; init; }
    
    /// <summary>自己相関検定結果</summary>
    public AutocorrelationTestResult? AutocorrelationTest { get; init; }
    
    /// <summary>正規性検定結果</summary>
    public NormalityTestResult? NormalityTest { get; init; }
    
    /// <summary>診断の総合評価スコア（0-100）</summary>
    public int OverallScore { get; init; }
    
    /// <summary>診断の総合評価</summary>
    public DiagnosticGrade OverallGrade { get; init; }
    
    /// <summary>総合評価の説明</summary>
    public string OverallAssessment { get; init; } = "";
    
    /// <summary>推奨事項のリスト</summary>
    public List<string> Recommendations { get; init; } = new();
    
    /// <summary>警告のリスト</summary>
    public List<string> Warnings { get; init; } = new();
    
    /// <summary>診断実行日時</summary>
    public DateTime DiagnosticTime { get; init; } = DateTime.Now;
}

/// <summary>
/// 診断の総合評価グレード
/// </summary>
public enum DiagnosticGrade
{
    /// <summary>優良：すべての診断をパス</summary>
    Excellent,
    
    /// <summary>良好：軽微な問題のみ</summary>
    Good,
    
    /// <summary>許容：いくつかの問題あり</summary>
    Acceptable,
    
    /// <summary>要注意：重大な問題あり</summary>
    Caution,
    
    /// <summary>不適合：モデルの再検討が必要</summary>
    Poor
}

/// <summary>
/// 診断レポート生成サービス
/// </summary>
public class DiagnosticReportGenerator
{
    private readonly ResidualAnalyzer _residualAnalyzer;
    private readonly AutocorrelationTest _autocorrelationTest;
    private readonly NormalityTest _normalityTest;

    public DiagnosticReportGenerator()
    {
        _residualAnalyzer = new ResidualAnalyzer();
        _autocorrelationTest = new AutocorrelationTest();
        _normalityTest = new NormalityTest();
    }

    /// <summary>
    /// 診断レポートを生成
    /// </summary>
    /// <param name="model">信頼度成長モデル</param>
    /// <param name="tData">時刻データ</param>
    /// <param name="yData">累積バグ発見数データ</param>
    /// <param name="parameters">最適化されたパラメータ</param>
    /// <param name="residualType">残差の種類</param>
    /// <returns>診断レポート</returns>
    public DiagnosticReport Generate(
        ReliabilityGrowthModelBase model,
        double[] tData,
        double[] yData,
        double[] parameters,
        ResidualType residualType = ResidualType.Pearson)
    {
        var warnings = new List<string>();
        var recommendations = new List<string>();
        
        // 1. 残差分析
        var residualResult = _residualAnalyzer.Analyze(model, tData, yData, parameters, residualType);
        
        if (!string.IsNullOrEmpty(residualResult.SmallSampleWarning))
            warnings.Add(residualResult.SmallSampleWarning);
        
        // 2. 自己相関検定
        var autocorrelationResult = _autocorrelationTest.Test(residualResult.Residuals);
        
        if (!string.IsNullOrEmpty(autocorrelationResult.SmallSampleWarning))
            warnings.Add(autocorrelationResult.SmallSampleWarning);
        
        // 3. 正規性検定
        var normalityResult = _normalityTest.Test(residualResult.Residuals);
        
        if (!string.IsNullOrEmpty(normalityResult.SmallSampleWarning))
            warnings.Add(normalityResult.SmallSampleWarning);
        
        // 4. 総合評価を計算
        var (score, grade) = CalculateOverallScore(
            residualResult, autocorrelationResult, normalityResult);
        
        // 5. 推奨事項を生成
        recommendations.AddRange(GenerateRecommendations(
            residualResult, autocorrelationResult, normalityResult, grade));
        
        // 6. 総合評価文を生成
        string assessment = GenerateAssessment(grade, score, model.Name);
        
        return new DiagnosticReport
        {
            ModelName = model.Name,
            ResidualAnalysis = residualResult,
            AutocorrelationTest = autocorrelationResult,
            NormalityTest = normalityResult,
            OverallScore = score,
            OverallGrade = grade,
            OverallAssessment = assessment,
            Recommendations = recommendations,
            Warnings = warnings,
            DiagnosticTime = DateTime.Now
        };
    }

    /// <summary>
    /// 総合評価スコアとグレードを計算
    /// </summary>
    private static (int score, DiagnosticGrade grade) CalculateOverallScore(
        ResidualAnalysisResult residual,
        AutocorrelationTestResult autocorrelation,
        NormalityTestResult normality)
    {
        int score = 100;
        
        // 残差の系統的パターン（最も重要）
        if (residual.HasSystematicPattern)
        {
            score -= 30;
        }
        
        // 外れ値の数に応じて減点
        int outlierCount = residual.OutlierIndices.Length;
        int n = residual.Residuals.Length;
        double outlierRatio = n > 0 ? (double)outlierCount / n : 0;
        
        if (outlierRatio > 0.1)
            score -= 15;
        else if (outlierRatio > 0.05)
            score -= 8;
        
        // 歪度と尖度
        if (Math.Abs(residual.Skewness) > 1.0)
            score -= 10;
        else if (Math.Abs(residual.Skewness) > 0.5)
            score -= 5;
        
        if (Math.Abs(residual.Kurtosis) > 3.0)
            score -= 10;
        else if (Math.Abs(residual.Kurtosis) > 1.0)
            score -= 5;
        
        // 自己相関
        if (autocorrelation.HasSignificantAutocorrelation)
        {
            // Durbin-Watson の程度による
            double dw = autocorrelation.DurbinWatsonStatistic;
            if (dw < 1.0 || dw > 3.0)
                score -= 25;
            else if (dw < 1.5 || dw > 2.5)
                score -= 15;
            
            // Ljung-Box のp値による
            if (autocorrelation.LjungBoxPValue < 0.01)
                score -= 10;
            else if (autocorrelation.LjungBoxPValue < 0.05)
                score -= 5;
        }
        
        // 正規性（NHPPではある程度の逸脱は許容）
        if (normality.IsNormalityRejected)
        {
            // 軽度の非正規性は減点を抑える
            if (normality.JarqueBeraPValue < 0.01 && normality.AndersonDarlingPValue < 0.01)
                score -= 10;
            else
                score -= 5;
        }
        
        // スコアを0-100に制限
        score = Math.Max(0, Math.Min(100, score));
        
        // グレード判定
        DiagnosticGrade grade = score switch
        {
            >= 85 => DiagnosticGrade.Excellent,
            >= 70 => DiagnosticGrade.Good,
            >= 55 => DiagnosticGrade.Acceptable,
            >= 40 => DiagnosticGrade.Caution,
            _ => DiagnosticGrade.Poor
        };
        
        return (score, grade);
    }

    /// <summary>
    /// 推奨事項を生成
    /// </summary>
    private static List<string> GenerateRecommendations(
        ResidualAnalysisResult residual,
        AutocorrelationTestResult autocorrelation,
        NormalityTestResult normality,
        DiagnosticGrade grade)
    {
        var recommendations = new List<string>();
        
        if (grade == DiagnosticGrade.Excellent)
        {
            recommendations.Add("モデルは良好に適合しています。現在の予測結果を信頼できます。");
            return recommendations;
        }
        
        // 系統的パターンに対する推奨
        if (residual.HasSystematicPattern)
        {
            recommendations.Add("残差に系統的パターンが検出されました。" +
                "異なるモデル（例：S字型モデル、変化点モデル）の使用を検討してください。");
        }
        
        // 外れ値に対する推奨
        if (residual.OutlierIndices.Length > 0)
        {
            string indices = string.Join(", ", residual.OutlierIndices.Take(5).Select(i => i + 1));
            if (residual.OutlierIndices.Length > 5)
                indices += " ...";
            
            recommendations.Add($"外れ値が検出されました（日 {indices}）。" +
                "データの異常（例：大規模リリース、テスト中断）を確認してください。");
        }
        
        // 自己相関に対する推奨
        if (autocorrelation.HasSignificantAutocorrelation)
        {
            if (autocorrelation.DurbinWatsonStatistic < 1.5)
            {
                recommendations.Add("正の自己相関が検出されました。" +
                    "モデルが時間的なトレンドを十分に捉えていない可能性があります。" +
                    "より複雑なモデル（遅延S字型、変化点モデル等）を検討してください。");
            }
            else if (autocorrelation.DurbinWatsonStatistic > 2.5)
            {
                recommendations.Add("負の自己相関が検出されました。" +
                    "モデルが過剰適合している可能性があります。" +
                    "よりシンプルなモデルの使用を検討してください。");
            }
        }
        
        // 正規性の問題に対する推奨
        if (normality.IsNormalityRejected)
        {
            if (Math.Abs(normality.ExcessKurtosis) > 2.0)
            {
                recommendations.Add("残差の分布が裾の重い分布になっています。" +
                    "外れ値の影響、またはPoisson分布からの逸脱が考えられます。");
            }
            
            if (Math.Abs(normality.Skewness) > 1.0)
            {
                recommendations.Add("残差の分布に歪みがあります。" +
                    "モデルが一部の期間でバグ数を過大/過小評価している可能性があります。");
            }
        }
        
        // グレード別の一般的推奨
        if (grade == DiagnosticGrade.Poor)
        {
            recommendations.Add("総合評価が低いため、このモデルの予測結果は慎重に解釈してください。" +
                "複数のモデルの結果を比較し、モデル平均化の使用を検討してください。");
        }
        else if (grade == DiagnosticGrade.Caution)
        {
            recommendations.Add("いくつかの診断項目で問題が検出されました。" +
                "予測の不確実性（信頼区間）を必ず確認してください。");
        }
        
        return recommendations;
    }

    /// <summary>
    /// 総合評価文を生成
    /// </summary>
    private static string GenerateAssessment(DiagnosticGrade grade, int score, string modelName)
    {
        string gradeText = grade switch
        {
            DiagnosticGrade.Excellent => "優良",
            DiagnosticGrade.Good => "良好",
            DiagnosticGrade.Acceptable => "許容範囲",
            DiagnosticGrade.Caution => "要注意",
            DiagnosticGrade.Poor => "不適合",
            _ => "不明"
        };
        
        string confidence = grade switch
        {
            DiagnosticGrade.Excellent => "高い信頼性",
            DiagnosticGrade.Good => "十分な信頼性",
            DiagnosticGrade.Acceptable => "一定の信頼性",
            DiagnosticGrade.Caution => "限定的な信頼性",
            DiagnosticGrade.Poor => "低い信頼性",
            _ => "不明な信頼性"
        };
        
        return $"モデル「{modelName}」の診断結果: {gradeText}（スコア: {score}/100）。" +
               $"このモデルの予測は{confidence}を持ちます。";
    }

    /// <summary>
    /// 診断レポートをテキスト形式でフォーマット
    /// </summary>
    public static string FormatReport(DiagnosticReport report)
    {
        var sb = new System.Text.StringBuilder();
        
        sb.AppendLine("═══════════════════════════════════════════════════════════════");
        sb.AppendLine($"  残差診断レポート: {report.ModelName}");
        sb.AppendLine($"  診断日時: {report.DiagnosticTime:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine("═══════════════════════════════════════════════════════════════");
        sb.AppendLine();
        
        // 総合評価
        sb.AppendLine($"【総合評価】{report.OverallAssessment}");
        sb.AppendLine();
        
        // 残差分析
        if (report.ResidualAnalysis != null)
        {
            var r = report.ResidualAnalysis;
            sb.AppendLine("─────────────────────────────────────────────────────────────────");
            sb.AppendLine("  残差分析");
            sb.AppendLine("─────────────────────────────────────────────────────────────────");
            sb.AppendLine($"  残差タイプ: {r.Type}");
            sb.AppendLine($"  平均: {r.Mean:F4}");
            sb.AppendLine($"  標準偏差: {r.StandardDeviation:F4}");
            sb.AppendLine($"  歪度: {r.Skewness:F4}");
            sb.AppendLine($"  尖度: {r.Kurtosis:F4}");
            sb.AppendLine($"  外れ値: {r.OutlierIndices.Length}点");
            
            if (r.RunsTest != null)
            {
                sb.AppendLine($"  ラン検定: 観測={r.RunsTest.ObservedRuns}, 期待={r.RunsTest.ExpectedRuns:F1}, p={r.RunsTest.PValue:F4}");
            }
            
            if (r.HasSystematicPattern)
            {
                sb.AppendLine($"  ⚠ パターン検出: {r.PatternDescription}");
            }
            sb.AppendLine();
        }
        
        // 自己相関検定
        if (report.AutocorrelationTest != null)
        {
            var a = report.AutocorrelationTest;
            sb.AppendLine("─────────────────────────────────────────────────────────────────");
            sb.AppendLine("  自己相関検定");
            sb.AppendLine("─────────────────────────────────────────────────────────────────");
            sb.AppendLine($"  Durbin-Watson: {a.DurbinWatsonStatistic:F4}");
            sb.AppendLine($"    → {a.DurbinWatsonInterpretation}");
            sb.AppendLine($"  Ljung-Box Q: {a.LjungBoxQ:F4} (df={a.LjungBoxDegreesOfFreedom}, p={a.LjungBoxPValue:F4})");
            
            if (a.SignificantLags.Length > 0)
            {
                string lags = string.Join(", ", a.SignificantLags.Take(5));
                sb.AppendLine($"  有意なラグ: {lags}");
            }
            
            sb.AppendLine($"  判定: {(a.HasSignificantAutocorrelation ? "⚠ 自己相関あり" : "✓ 自己相関なし")}");
            sb.AppendLine();
        }
        
        // 正規性検定
        if (report.NormalityTest != null)
        {
            var n = report.NormalityTest;
            sb.AppendLine("─────────────────────────────────────────────────────────────────");
            sb.AppendLine("  正規性検定");
            sb.AppendLine("─────────────────────────────────────────────────────────────────");
            sb.AppendLine($"  Jarque-Bera: {n.JarqueBeraStatistic:F4} (p={n.JarqueBeraPValue:F4})");
            sb.AppendLine($"  Anderson-Darling: {n.AndersonDarlingStatistic:F4} (p={n.AndersonDarlingPValue:F4})");
            sb.AppendLine($"  歪度: {n.Skewness:F4}");
            sb.AppendLine($"  超過尖度: {n.ExcessKurtosis:F4}");
            sb.AppendLine($"  判定: {(n.IsNormalityRejected ? "⚠ 正規性棄却" : "✓ 正規性維持")}");
            sb.AppendLine();
        }
        
        // 警告
        if (report.Warnings.Count > 0)
        {
            sb.AppendLine("─────────────────────────────────────────────────────────────────");
            sb.AppendLine("  警告");
            sb.AppendLine("─────────────────────────────────────────────────────────────────");
            foreach (var warning in report.Warnings)
            {
                sb.AppendLine($"  • {warning}");
            }
            sb.AppendLine();
        }
        
        // 推奨事項
        if (report.Recommendations.Count > 0)
        {
            sb.AppendLine("─────────────────────────────────────────────────────────────────");
            sb.AppendLine("  推奨事項");
            sb.AppendLine("─────────────────────────────────────────────────────────────────");
            foreach (var rec in report.Recommendations)
            {
                sb.AppendLine($"  • {rec}");
            }
            sb.AppendLine();
        }
        
        sb.AppendLine("═══════════════════════════════════════════════════════════════");
        
        return sb.ToString();
    }
}
