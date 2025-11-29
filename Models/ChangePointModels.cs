namespace BugConvergenceTool.Models;

/// <summary>
/// 変化点（Change Point）モデルの基底クラス
/// テスト環境変化により欠陥検出率が変化点τで不連続に変化
/// </summary>
public abstract class ChangePointModelBase : ReliabilityGrowthModelBase
{
    public override string Category => "変化点";
    
    /// <summary>
    /// 変化点τ
    /// </summary>
    public double ChangePoint { get; protected set; }
}

/// <summary>
/// 指数型 + 変化点モデル
/// t ≤ τ: m₁(t) = a₁(1 - e^(-b₁t))
/// t > τ: m₂(t) = m₁(τ) + a₂(1 - e^(-b₂(t-τ)))
/// </summary>
public class ExponentialChangePointModel : ChangePointModelBase
{
    public override string Name => "指数型+変化点";
    public override string Formula => "m(t) = a₁(1-e^(-b₁t)) [t≤τ], m₁(τ)+a₂(1-e^(-b₂(t-τ))) [t>τ]";
    public override string Description => "指数型モデルに変化点を導入";
    public override string[] ParameterNames => new[] { "a₁", "b₁", "a₂", "b₂", "τ" };
    
    /// <summary>
    /// 漸近的総欠陥数: a₁ + a₂
    /// </summary>
    public override double GetAsymptoticTotalBugs(double[] parameters)
    {
        double a1 = parameters[0];
        double a2 = parameters[2];
        return a1 + a2;
    }
    
    public override double Calculate(double t, double[] p)
    {
        double a1 = p[0], b1 = p[1], a2 = p[2], b2 = p[3], tau = p[4];
        
        if (t <= tau)
        {
            // 変化点前
            return a1 * (1 - Math.Exp(-b1 * t));
        }
        else
        {
            // 変化点後
            double m_tau = a1 * (1 - Math.Exp(-b1 * tau));
            return m_tau + a2 * (1 - Math.Exp(-b2 * (t - tau)));
        }
    }
    
    public override double[] GetInitialParameters(double[] tData, double[] yData)
    {
        double maxY = yData.Max();
        int n = tData.Length;
        double midT = n / 2.0;
        
        return new[] { maxY * 0.6, 0.15, maxY * 0.6, 0.15, midT };
    }
    
    public override (double[] lower, double[] upper) GetBounds(double[] tData, double[] yData)
    {
        double maxY = yData.Max();
        int n = tData.Length;
        
        return (
            new[] { 1.0, 0.001, 1.0, 0.001, 2.0 },
            new[] { maxY * 3, 1.0, maxY * 3, 1.0, n - 2.0 }
        );
    }
}

/// <summary>
/// 遅延S字型 + 変化点モデル
/// t ≤ τ: m₁(t) = a₁(1 - (1+b₁t)e^(-b₁t))
/// t > τ: m₂(t) = m₁(τ) + a₂(1 - (1+b₂(t-τ))e^(-b₂(t-τ)))
/// </summary>
public class DelayedSChangePointModel : ChangePointModelBase
{
    public override string Name => "遅延S字型+変化点";
    public override string Formula => "m(t) = a₁(1-(1+b₁t)e^(-b₁t)) [t≤τ], m₁(τ)+a₂(...) [t>τ]";
    public override string Description => "遅延S字型に変化点を導入";
    public override string[] ParameterNames => new[] { "a₁", "b₁", "a₂", "b₂", "τ" };
    
    /// <summary>
    /// 漸近的総欠陥数: a₁ + a₂
    /// </summary>
    public override double GetAsymptoticTotalBugs(double[] parameters)
    {
        double a1 = parameters[0];
        double a2 = parameters[2];
        return a1 + a2;
    }
    
    public override double Calculate(double t, double[] p)
    {
        double a1 = p[0], b1 = p[1], a2 = p[2], b2 = p[3], tau = p[4];
        
        if (t <= tau)
        {
            return a1 * (1 - (1 + b1 * t) * Math.Exp(-b1 * t));
        }
        else
        {
            double m_tau = a1 * (1 - (1 + b1 * tau) * Math.Exp(-b1 * tau));
            double dt = t - tau;
            return m_tau + a2 * (1 - (1 + b2 * dt) * Math.Exp(-b2 * dt));
        }
    }
    
    public override double[] GetInitialParameters(double[] tData, double[] yData)
    {
        double maxY = yData.Max();
        int n = tData.Length;
        double midT = n / 2.0;
        
        return new[] { maxY * 0.6, 0.2, maxY * 0.6, 0.2, midT };
    }
    
    public override (double[] lower, double[] upper) GetBounds(double[] tData, double[] yData)
    {
        double maxY = yData.Max();
        int n = tData.Length;
        
        return (
            new[] { 1.0, 0.001, 1.0, 0.001, 2.0 },
            new[] { maxY * 3, 1.0, maxY * 3, 1.0, n - 2.0 }
        );
    }
}

/// <summary>
/// 不完全デバッグ + 変化点統合モデル
/// dm(t)/dt = b(t)·[a(t) - m(t)]
/// b(t) = b₁ (t≤τ), b₂ (t>τ)
/// a(t) = a + α·m(t)
/// </summary>
public class ImperfectDebugChangePointModel : ChangePointModelBase
{
    public override string Name => "不完全デバッグ+変化点";
    public override string Category => "変化点+不完全";
    public override string Formula => "dm/dt = b(t)(a+αm-m), b変化点で切替";
    public override string Description => "不完全デバッグと変化点を統合";
    public override string[] ParameterNames => new[] { "a", "b₁", "b₂", "α", "τ" };
    
    /// <summary>
    /// 漸近的総欠陥数: α < 1 の場合 a / (1 - α)
    /// </summary>
    public override double GetAsymptoticTotalBugs(double[] parameters)
    {
        double a = parameters[0];
        double alpha = parameters[3];
        
        if (alpha >= 1.0)
            alpha = 0.99;
        
        return a / (1 - alpha);
    }
    
    public override double Calculate(double t, double[] p)
    {
        double a = p[0], b1 = p[1], b2 = p[2], alpha = p[3], tau = p[4];
        
        if (alpha >= 1.0)
            alpha = 0.99;
        
        if (t <= tau)
        {
            // 変化点前
            double factor = 1 - alpha;
            double expTerm = Math.Exp(-b1 * factor * t);
            double num = a * (1 - expTerm);
            double denom = 1 - alpha * (1 - expTerm);
            return denom > 0 ? num / denom : a;
        }
        else
        {
            // 変化点τでの値
            double factor1 = 1 - alpha;
            double expTerm1 = Math.Exp(-b1 * factor1 * tau);
            double num1 = a * (1 - expTerm1);
            double denom1 = 1 - alpha * (1 - expTerm1);
            double m_tau = denom1 > 0 ? num1 / denom1 : a;
            
            // 変化点後（τからの相対時間で再計算）
            // 残存バグ数を考慮
            double remainingA = a + alpha * m_tau - m_tau;
            if (remainingA <= 0)
                return m_tau;
            
            double dt = t - tau;
            double factor2 = 1 - alpha;
            double expTerm2 = Math.Exp(-b2 * factor2 * dt);
            double increment = remainingA * (1 - expTerm2) / (1 - alpha * (1 - expTerm2));
            
            return m_tau + Math.Max(0, increment);
        }
    }
    
    public override double[] GetInitialParameters(double[] tData, double[] yData)
    {
        double maxY = yData.Max();
        int n = tData.Length;
        double midT = n / 2.0;
        
        return new[] { maxY * 1.2, 0.1, 0.15, 0.1, midT };
    }
    
    public override (double[] lower, double[] upper) GetBounds(double[] tData, double[] yData)
    {
        double maxY = yData.Max();
        int n = tData.Length;
        
        return (
            new[] { maxY, 0.001, 0.001, 0.0, 2.0 },
            new[] { maxY * 5, 1.0, 1.0, 0.5, n - 2.0 }
        );
    }
}

/// <summary>
/// 複数変化点モデル
/// 最大3つの変化点をサポート
/// </summary>
public class MultipleChangePointModel : ChangePointModelBase
{
    private readonly int _numChangePoints;
    
    public MultipleChangePointModel(int numChangePoints = 2)
    {
        _numChangePoints = Math.Min(3, Math.Max(1, numChangePoints));
    }
    
    public override string Name => $"複数変化点({_numChangePoints}点)";
    public override string Formula => $"指数型モデルに{_numChangePoints}個の変化点";
    public override string Description => $"{_numChangePoints}個の変化点で欠陥検出率が変化";
    
    public override string[] ParameterNames
    {
        get
        {
            var names = new List<string>();
            for (int i = 0; i <= _numChangePoints; i++)
            {
                names.Add($"a{i + 1}");
                names.Add($"b{i + 1}");
            }
            for (int i = 1; i <= _numChangePoints; i++)
            {
                names.Add($"τ{i}");
            }
            return names.ToArray();
        }
    }
    
    /// <summary>
    /// 漸近的総欠陥数: 全セグメントのaの合計
    /// </summary>
    public override double GetAsymptoticTotalBugs(double[] parameters)
    {
        int numSegments = _numChangePoints + 1;
        double total = 0;
        for (int i = 0; i < numSegments; i++)
        {
            total += parameters[i * 2]; // a_i
        }
        return total;
    }
    
    public override double Calculate(double t, double[] p)
    {
        int numSegments = _numChangePoints + 1;
        
        // パラメータ抽出
        var a = new double[numSegments];
        var b = new double[numSegments];
        var tau = new double[_numChangePoints];
        
        for (int i = 0; i < numSegments; i++)
        {
            a[i] = p[i * 2];
            b[i] = p[i * 2 + 1];
        }
        for (int i = 0; i < _numChangePoints; i++)
        {
            tau[i] = p[numSegments * 2 + i];
        }
        
        // 変化点をソート
        Array.Sort(tau);
        
        // どのセグメントにいるか判定
        int segment = 0;
        for (int i = 0; i < _numChangePoints; i++)
        {
            if (t > tau[i])
                segment = i + 1;
        }
        
        // 累積値を計算
        double cumulative = 0;
        double prevT = 0;
        
        for (int i = 0; i <= segment; i++)
        {
            double segStart = (i == 0) ? 0 : tau[i - 1];
            double segEnd = (i < segment) ? tau[i] : t;
            double dt = segEnd - segStart;
            
            if (dt > 0)
            {
                cumulative += a[i] * (1 - Math.Exp(-b[i] * dt));
            }
        }
        
        return cumulative;
    }
    
    public override double[] GetInitialParameters(double[] tData, double[] yData)
    {
        double maxY = yData.Max();
        int n = tData.Length;
        int numSegments = _numChangePoints + 1;
        
        var initial = new List<double>();
        
        // 各セグメントのa, b
        double segmentBugs = maxY / numSegments;
        for (int i = 0; i < numSegments; i++)
        {
            initial.Add(segmentBugs);
            initial.Add(0.15);
        }
        
        // 変化点
        for (int i = 1; i <= _numChangePoints; i++)
        {
            initial.Add(n * i / (double)(numSegments));
        }
        
        return initial.ToArray();
    }
    
    public override (double[] lower, double[] upper) GetBounds(double[] tData, double[] yData)
    {
        double maxY = yData.Max();
        int n = tData.Length;
        int numSegments = _numChangePoints + 1;
        
        var lower = new List<double>();
        var upper = new List<double>();
        
        for (int i = 0; i < numSegments; i++)
        {
            lower.Add(1.0);
            lower.Add(0.001);
            upper.Add(maxY * 2);
            upper.Add(1.0);
        }
        
        for (int i = 1; i <= _numChangePoints; i++)
        {
            lower.Add(2.0);
            upper.Add(n - 2.0);
        }
        
        return (lower.ToArray(), upper.ToArray());
    }
}

/// <summary>
/// 変化点検出ユーティリティ
/// </summary>
public static class ChangePointDetector
{
    /// <summary>
    /// データから変化点候補を検出（傾き変化に基づく）
    /// </summary>
    public static List<int> DetectCandidates(double[] yData, int windowSize = 5)
    {
        var candidates = new List<int>();
        int n = yData.Length;
        
        if (n < windowSize * 2 + 1)
            return candidates;
        
        // 各点で前後の傾きを比較
        for (int i = windowSize; i < n - windowSize; i++)
        {
            // 前の傾き
            double slopeBefore = (yData[i] - yData[i - windowSize]) / windowSize;
            // 後の傾き
            double slopeAfter = (yData[i + windowSize] - yData[i]) / windowSize;
            
            // 傾きの変化率
            double change = Math.Abs(slopeAfter - slopeBefore) / Math.Max(0.1, Math.Abs(slopeBefore));
            
            // 変化が大きい点を候補とする
            if (change > 0.3)
            {
                candidates.Add(i);
            }
        }
        
        // 近接する候補を統合
        var filtered = new List<int>();
        foreach (var c in candidates)
        {
            if (filtered.Count == 0 || c - filtered.Last() > windowSize)
            {
                filtered.Add(c);
            }
        }
        
        return filtered;
    }
    
    /// <summary>
    /// グリッドサーチで最適変化点を探索
    /// </summary>
    public static double FindOptimalChangePoint(
        ReliabilityGrowthModelBase model,
        double[] tData,
        double[] yData,
        int tauIndex,
        double[] otherParams)
    {
        int n = tData.Length;
        double bestTau = n / 2.0;
        double bestSSE = double.MaxValue;
        
        for (int t = 3; t < n - 3; t++)
        {
            var testParams = (double[])otherParams.Clone();
            testParams[tauIndex] = t;
            
            try
            {
                double sse = model.CalculateSSE(tData, yData, testParams);
                if (sse < bestSSE)
                {
                    bestSSE = sse;
                    bestTau = t;
                }
            }
            catch { }
        }
        
        return bestTau;
    }
}

/// <summary>
/// PNZ型不完全デバッグ＋変化点モデル（実効時間 u(t) 方式）
/// m(t) = a * (1 - e^(-u(t))) / (1 + p * e^(-u(t)))
/// u(t) = b₁*t (t ≤ τ), b₁*τ + b₂*(t-τ) (t > τ)
/// </summary>
/// <remarks>
/// PNZ型をベースに、変化点τで検出率bが変化するモデル。
/// 「実効テスト時間」u(t)を導入することでm(t)がτ前後で自動的に連続となる。
/// - b₂ > b₁: テスト強化（変化点以降の収束が加速）
/// - b₂ &lt; b₁: テスト弱体化（変化点以降の収束が減速）
/// </remarks>
public class ImperfectDebugExponentialChangePointModel : ChangePointModelBase
{
    public override string Name => "PNZ型+変化点";
    public override string Category => "変化点+不完全";
    public override string Formula => "m(t) = a(1-e^(-u(t)))/(1+p·e^(-u(t))), u(t)=b₁t [t≤τ], b₁τ+b₂(t-τ) [t>τ]";
    public override string Description => "PNZ型不完全デバッグに検出率変化点を導入（実効時間方式）";
    public override string[] ParameterNames => new[] { "a", "b₁", "b₂", "p", "τ" };

    /// <summary>
    /// 漸近的総欠陥数
    /// t→∞ で u(t)→∞ より m(∞) = a / (1 + p)
    /// ただし p が負の場合は a に収束
    /// </summary>
    public override double GetAsymptoticTotalBugs(double[] parameters)
    {
        double a = parameters[0];
        double p = parameters[3];

        if (p >= 0)
            return a / (1 + p);
        else
            return a; // p < 0 の場合は a が上界
    }

    public override double Calculate(double t, double[] parameters)
    {
        double a = parameters[0];
        double b1 = parameters[1];
        double b2 = parameters[2];
        double p = parameters[3];
        double tau = parameters[4];

        // 実効テスト時間 u(t) を計算
        double u;
        if (t <= tau)
        {
            u = b1 * t;
        }
        else
        {
            u = b1 * tau + b2 * (t - tau);
        }

        // PNZ型の式: m(t) = a * (1 - e^(-u)) / (1 + p * e^(-u))
        double expU = Math.Exp(-u);
        double numerator = a * (1 - expU);
        double denominator = 1 + p * expU;

        // 分母が0に近い場合の保護
        if (Math.Abs(denominator) < 1e-10)
            return a;

        return numerator / denominator;
    }

    public override double[] GetInitialParameters(double[] tData, double[] yData)
    {
        double maxY = yData.Max();
        int n = tData.Length;

        // 中央時刻を変化点の初期値とする（既存変化点モデルと同じくインデックスベース）
        double midT = n / 2.0;

        // 初期値: 変化点なしPNZに近い状態からスタート
        return new[] {
            maxY * 1.5,  // a: 総欠陥数スケール
            0.1,         // b₁: 変化点前の検出率
            0.1,         // b₂: 変化点後の検出率（最初は同じ）
            0.1,         // p: 不完全デバッグ率
            midT         // τ: 変化点
        };
    }

    public override (double[] lower, double[] upper) GetBounds(double[] tData, double[] yData)
    {
        double maxY = yData.Max();
        int n = tData.Length;

        return (
            new[] {
                maxY,      // a: 下限は観測最大値
                1e-8,      // b₁: 正の小さな値
                1e-8,      // b₂: 正の小さな値
                -0.5,      // p: 既存実装に合わせる
                2.0        // τ: 最小インデックス+1
            },
            new[] {
                maxY * 100, // a: 上限は観測最大値の100倍
                10.0,       // b₁
                10.0,       // b₂
                5.0,        // p
                n - 2.0     // τ: 最大インデックス-2
            }
        );
    }
}

/// <summary>
/// 変化点モデルのファクトリ
/// </summary>
public static class ChangePointModelFactory
{
    public static IEnumerable<ReliabilityGrowthModelBase> GetAllChangePointModels()
    {
        yield return new ExponentialChangePointModel();
        yield return new DelayedSChangePointModel();
        yield return new ImperfectDebugChangePointModel();
        yield return new ImperfectDebugExponentialChangePointModel();
        yield return new MultipleChangePointModel(2);
    }

    public static IEnumerable<ReliabilityGrowthModelBase> GetBasicChangePointModels()
    {
        yield return new ExponentialChangePointModel();
        yield return new DelayedSChangePointModel();
    }

    /// <summary>
    /// 不完全デバッグ＋変化点モデルを取得
    /// </summary>
    public static IEnumerable<ReliabilityGrowthModelBase> GetImperfectDebugChangePointModels()
    {
        yield return new ImperfectDebugChangePointModel();
        yield return new ImperfectDebugExponentialChangePointModel();
    }
}
