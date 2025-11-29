using System.Diagnostics;
using System.Security.Cryptography;
using MathNet.Numerics.LinearAlgebra;

namespace BugConvergenceTool.Optimizers;

/// <summary>
/// CMA-ES（Covariance Matrix Adaptation Evolution Strategy）
/// 共分散行列適応進化戦略 - 非線形・マルチモーダル問題に強いグローバル最適化
/// ロジスティック変換により無拘束空間で最適化を実行
/// </summary>
public class CMAESOptimizer : IOptimizer
{
    public string Name => "CMA-ES";
    public string Description => "共分散行列適応進化戦略 - 地形の難しい問題に強いグローバル最適化";
    
    private readonly int _maxIterations;
    private readonly double _tolerance;
    private readonly double _initialSigmaU;  // u空間での初期σ
    private readonly Random _random;
    
    /// <summary>
    /// コンストラクタ
    /// </summary>
    /// <param name="maxIterations">最大反復回数（デフォルト: 500）</param>
    /// <param name="tolerance">収束判定閾値（デフォルト: 1e-10）</param>
    /// <param name="initialSigmaU">u空間での初期ステップサイズ（デフォルト: 0.5）</param>
    /// <param name="seed">乱数シード</param>
    public CMAESOptimizer(
        int maxIterations = 500,
        double tolerance = 1e-10,
        double initialSigmaU = 0.5,
        int? seed = null)
    {
        _maxIterations = maxIterations;
        _tolerance = tolerance;
        _initialSigmaU = initialSigmaU;
        _random = seed.HasValue 
            ? new Random(seed.Value) 
            : new Random(RandomNumberGenerator.GetInt32(int.MaxValue));
    }
    
    public OptimizationResult Optimize(
        Func<double[], double> objectiveFunction,
        double[] lowerBounds,
        double[] upperBounds,
        double[]? initialGuess = null)
    {
        var stopwatch = Stopwatch.StartNew();
        var result = new OptimizationResult { AlgorithmName = Name };
        
        try
        {
            int n = lowerBounds.Length;  // 次元数
            int evaluations = 0;
            
            // 境界を保持
            var lower = (double[])lowerBounds.Clone();
            var upper = (double[])upperBounds.Clone();
            
            // ===== Step 2: 初期化（u空間） =====
            
            // (1) 初期 x を決定し、u空間に変換
            double[] xInit = new double[n];
            if (initialGuess != null && initialGuess.Length == n)
            {
                for (int i = 0; i < n; i++)
                    xInit[i] = Math.Min(upper[i], Math.Max(lower[i], initialGuess[i]));
            }
            else
            {
                for (int i = 0; i < n; i++)
                    xInit[i] = (lower[i] + upper[i]) / 2.0;
            }
            
            // u空間の平均ベクトル m_u
            double[] m_u_array = TransformXToU(xInit, lower, upper);
            var m_u = Vector<double>.Build.DenseOfArray(m_u_array);
            
            // (2) 初期ステップサイズ σ_u（u空間）
            double sigma_u = _initialSigmaU;
            
            // (3) 共分散行列 C = I（u空間）
            var C = Matrix<double>.Build.DenseIdentity(n);
            
            // (4) 進化経路（u空間）
            var p_c = Vector<double>.Build.Dense(n, 0.0);
            var p_sigma = Vector<double>.Build.Dense(n, 0.0);
            
            // (5) 行列分解 B, D（初期は単位行列）
            var B = Matrix<double>.Build.DenseIdentity(n);
            var D = Vector<double>.Build.Dense(n, 1.0);
            
            // (6) λ, μ, 重み
            int lambda = 4 + (int)Math.Floor(3 * Math.Log(n));
            lambda = Math.Max(lambda, 6);  // 最低6個体
            int mu = lambda / 2;
            
            // 重み w_i（対数スケーリング）
            double[] weights = new double[mu];
            double sumW = 0;
            for (int i = 0; i < mu; i++)
            {
                weights[i] = Math.Log(mu + 0.5) - Math.Log(i + 1);
                sumW += weights[i];
            }
            // 正規化
            for (int i = 0; i < mu; i++)
                weights[i] /= sumW;
            
            // μ_eff
            double sumW2 = 0;
            for (int i = 0; i < mu; i++)
                sumW2 += weights[i] * weights[i];
            double mu_eff = 1.0 / sumW2;
            
            // (7) アルゴリズムパラメータ（標準値）
            double c_sigma = (mu_eff + 2.0) / (n + mu_eff + 5.0);
            double d_sigma = 1.0 + 2.0 * Math.Max(0, Math.Sqrt((mu_eff - 1.0) / (n + 1.0)) - 1.0) + c_sigma;
            double c_c = (4.0 + mu_eff / n) / (n + 4.0 + 2.0 * mu_eff / n);
            double c1 = 2.0 / ((n + 1.3) * (n + 1.3) + mu_eff);
            double c_mu = Math.Min(1.0 - c1, 2.0 * (mu_eff - 2.0 + 1.0 / mu_eff) / ((n + 2.0) * (n + 2.0) + mu_eff));
            
            // χ_n（n次元標準正規分布のノルムの期待値）
            double chi_n = Math.Sqrt(n) * (1.0 - 1.0 / (4.0 * n) + 1.0 / (21.0 * n * n));
            
            // ベスト解の追跡（x空間で保持）
            double[] bestX = TransformUToX(m_u.ToArray(), lower, upper);
            double bestFitness = SafeEvaluate(objectiveFunction, bestX);
            evaluations++;
            
            result.ConvergenceHistory.Add(bestFitness);
            
            // 停滞カウント
            int stagnationCount = 0;
            double previousBest = bestFitness;
            
            // ===== メインループ =====
            for (int iter = 0; iter < _maxIterations; iter++)
            {
                // Step 3.1: サンプリング λ 個体（u空間）
                var population = new List<(Vector<double> u, Vector<double> y_u, Vector<double> z, double[] x, double f)>(lambda);
                
                for (int k = 0; k < lambda; k++)
                {
                    // 1) z ~ N(0, I)
                    var z = SampleStandardNormalVector(n);
                    
                    // 2) y_u = B * (D .* z)
                    var Dz = D.PointwiseMultiply(z);
                    var y_u = B * Dz;
                    
                    // 3) u_k = m_u + sigma_u * y_u
                    var u_k = m_u + sigma_u * y_u;
                    
                    // 4) x_k = TransformUToX(u_k)（境界処理不要）
                    double[] x_k = TransformUToX(u_k.ToArray(), lower, upper);
                    
                    // 5) objective 評価は x_k で
                    double f_k = SafeEvaluate(objectiveFunction, x_k);
                    evaluations++;
                    
                    // 6) 個体情報を保存
                    population.Add((u_k, y_u, z, x_k, f_k));
                    
                    // ベスト更新
                    if (f_k < bestFitness)
                    {
                        bestFitness = f_k;
                        bestX = (double[])x_k.Clone();
                    }
                }
                
                // Step 3.2: ソート（適合度昇順）
                population.Sort((a, b) => a.f.CompareTo(b.f));
                
                // Step 3.3: m_u の更新
                // y_w = Σ w_i * y_u_i
                var y_w = Vector<double>.Build.Dense(n, 0.0);
                for (int i = 0; i < mu; i++)
                {
                    y_w += weights[i] * population[i].y_u;
                }
                
                // m_u_new = m_u + sigma_u * y_w
                var m_u_new = m_u + sigma_u * y_w;
                
                // Step 3.4: 進化経路と共分散の更新（すべてu空間）
                
                // z_w: 上位μ個の z の重み付き平均
                var z_w = Vector<double>.Build.Dense(n, 0.0);
                for (int i = 0; i < mu; i++)
                {
                    z_w += weights[i] * population[i].z;
                }
                
                // p_sigma 更新（白色化空間で管理：B を掛けない）
                double sqrt_c_sigma = Math.Sqrt(c_sigma * (2.0 - c_sigma) * mu_eff);
                p_sigma = (1.0 - c_sigma) * p_sigma + sqrt_c_sigma * z_w;
                
                // sigma_u 更新
                double p_sigma_norm = p_sigma.L2Norm();
                sigma_u = sigma_u * Math.Exp((c_sigma / d_sigma) * (p_sigma_norm / chi_n - 1.0));
                
                // σ_u のクリッピング（発散防止）
                const double maxSigmaU = 10.0;
                const double minSigmaU = 1e-10;
                sigma_u = Math.Max(minSigmaU, Math.Min(maxSigmaU, sigma_u));
                
                // h_sigma 判定（進化経路の正規性チェック）
                double expectedNorm = Math.Sqrt(1.0 - Math.Pow(1.0 - c_sigma, 2.0 * (iter + 1)));
                if (expectedNorm < 1e-10) expectedNorm = 1e-10;
                bool h_sigma = (p_sigma_norm / expectedNorm) < (1.4 + 2.0 / (n + 1.0)) * chi_n;
                
                // p_c 更新
                double sqrt_c_c = Math.Sqrt(c_c * (2.0 - c_c) * mu_eff);
                if (h_sigma)
                {
                    p_c = (1.0 - c_c) * p_c + sqrt_c_c * y_w;
                }
                else
                {
                    p_c = (1.0 - c_c) * p_c;
                }
                
                // C 更新（rank-1 + rank-μ）
                double delta_h = h_sigma ? 0.0 : c_c * (2.0 - c_c);
                
                // rank-1 更新: c1 * p_c * p_c^T
                var rank1 = p_c.OuterProduct(p_c);
                
                // rank-μ 更新: c_mu * Σ w_i * y_u_i * y_u_i^T
                var rankMu = Matrix<double>.Build.Dense(n, n, 0.0);
                for (int i = 0; i < mu; i++)
                {
                    rankMu += weights[i] * population[i].y_u.OuterProduct(population[i].y_u);
                }
                
                // C = (1 - c1 - c_mu + delta_h * c1) * C + c1 * rank1 + c_mu * rankMu
                C = (1.0 - c1 - c_mu + delta_h * c1) * C + c1 * rank1 + c_mu * rankMu;
                
                // Step 3.5: C の固有値分解（周期的に実行）
                if ((iter + 1) % Math.Max(1, n / 5) == 0 || iter == 0)
                {
                    // 対称化（数値誤差対策）
                    C = (C + C.Transpose()) / 2.0;
                    
                    try
                    {
                        var evd = C.Evd();
                        var eigenValues = evd.EigenValues.Real();
                        var eigenVectors = evd.EigenVectors;
                        
                        // 固有値が正であることを確認
                        bool validEigenvalues = true;
                        for (int i = 0; i < n; i++)
                        {
                            if (eigenValues[i] <= 0)
                            {
                                validEigenvalues = false;
                                break;
                            }
                        }
                        
                        if (validEigenvalues)
                        {
                            B = eigenVectors;
                            for (int i = 0; i < n; i++)
                                D[i] = Math.Sqrt(eigenValues[i]);
                        }
                        else
                        {
                            // 固有値が非正の場合、Cをリセット
                            C = Matrix<double>.Build.DenseIdentity(n);
                            B = Matrix<double>.Build.DenseIdentity(n);
                            D = Vector<double>.Build.Dense(n, 1.0);
                        }
                    }
                    catch
                    {
                        // 固有値分解に失敗した場合、Cをリセット
                        C = Matrix<double>.Build.DenseIdentity(n);
                        B = Matrix<double>.Build.DenseIdentity(n);
                        D = Vector<double>.Build.Dense(n, 1.0);
                    }
                }
                
                // m_u を更新
                m_u = m_u_new;
                
                // 収束履歴に追加
                result.ConvergenceHistory.Add(bestFitness);
                
                // 終了条件
                // 停滞判定
                if (Math.Abs(previousBest - bestFitness) < _tolerance)
                {
                    stagnationCount++;
                    if (stagnationCount > 50)
                        break;
                }
                else
                {
                    stagnationCount = 0;
                }
                previousBest = bestFitness;
                
                // σ_u が極小になったら終了
                if (sigma_u < 1e-8)
                    break;
                
                result.Iterations = iter + 1;
            }
            
            result.Parameters = bestX;
            result.ObjectiveValue = bestFitness;
            result.FunctionEvaluations = evaluations;
            result.Success = !double.IsNaN(bestFitness) && !double.IsInfinity(bestFitness);
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.ErrorMessage = ex.Message;
        }
        
        stopwatch.Stop();
        result.ElapsedMilliseconds = stopwatch.ElapsedMilliseconds;
        
        return result;
    }
    
    #region 変換ヘルパー（u ↔ x）
    
    /// <summary>
    /// シグモイド関数（オーバーフロー対策込み）
    /// </summary>
    private static double Sigmoid(double u)
    {
        if (u >= 0)
        {
            double e = Math.Exp(-u);
            return 1.0 / (1.0 + e);
        }
        else
        {
            double e = Math.Exp(u);
            return e / (1.0 + e);
        }
    }
    
    /// <summary>
    /// ロジット関数（逆シグモイド）
    /// </summary>
    private static double Logit(double p)
    {
        const double eps = 1e-12;
        double pp = Math.Min(1.0 - eps, Math.Max(eps, p));
        return Math.Log(pp / (1.0 - pp));
    }
    
    /// <summary>
    /// u空間からx空間への変換
    /// x_i = lower_i + (upper_i - lower_i) * sigmoid(u_i)
    /// </summary>
    private static double[] TransformUToX(double[] u, double[] lower, double[] upper)
    {
        var x = new double[u.Length];
        for (int i = 0; i < u.Length; i++)
        {
            double range = upper[i] - lower[i];
            if (range <= 0)
            {
                x[i] = lower[i];  // 固定パラメータ
                continue;
            }
            double s = Sigmoid(u[i]);  // 0〜1
            x[i] = lower[i] + range * s;
        }
        return x;
    }
    
    /// <summary>
    /// x空間からu空間への変換
    /// u_i = logit((x_i - lower_i) / (upper_i - lower_i))
    /// </summary>
    private static double[] TransformXToU(double[] x, double[] lower, double[] upper)
    {
        var u = new double[x.Length];
        for (int i = 0; i < x.Length; i++)
        {
            double range = upper[i] - lower[i];
            if (range <= 0)
            {
                u[i] = 0.0;  // 固定パラメータ扱い
                continue;
            }
            double p = (x[i] - lower[i]) / range;  // 0〜1
            u[i] = Logit(p);
        }
        return u;
    }
    
    #endregion
    
    /// <summary>
    /// 標準正規分布からn次元ベクトルをサンプリング（Box-Muller法）
    /// </summary>
    private Vector<double> SampleStandardNormalVector(int n)
    {
        var z = new double[n];
        
        for (int i = 0; i < n; i += 2)
        {
            // Box-Muller 変換
            double u1 = _random.NextDouble();
            double u2 = _random.NextDouble();
            
            // u1 が 0 にならないようにする
            while (u1 < 1e-10)
                u1 = _random.NextDouble();
            
            double r = Math.Sqrt(-2.0 * Math.Log(u1));
            double theta = 2.0 * Math.PI * u2;
            
            z[i] = r * Math.Cos(theta);
            if (i + 1 < n)
                z[i + 1] = r * Math.Sin(theta);
        }
        
        return Vector<double>.Build.DenseOfArray(z);
    }
    
    private static double SafeEvaluate(Func<double[], double> f, double[] x)
    {
        try
        {
            double val = f(x);
            return double.IsNaN(val) || double.IsInfinity(val) ? double.MaxValue : val;
        }
        catch
        {
            return double.MaxValue;
        }
    }
}
