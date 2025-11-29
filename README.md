# バグ収束推定ツール (Bug Convergence Tool)

バイブコーディングで開発したツール。
.NET 8で実装されたソフトウェアテスト品質分析のためのコマンドラインツールです。  
信頼度成長モデル（SRGM）を用いて、バグの収束状況を分析し、リリース判断に役立つ情報を提供します。

## 特徴

### 基本機能

- **8種類の基本信頼度成長モデル**（指数型、遅延S字型、ゴンペルツ等）
- **不完全デバッグモデル**（バグ修正時の新規バグ混入を考慮）
- **収束予測日の計算**（90%/95%/99%発見予測）
- **グラフ画像出力**（PNG形式）
- **Excel形式での結果出力**
- **詳細なテキストレポート**出力

## 必要環境

- .NET 8.0 SDK / Runtime

## インストール

```bash
# ビルド
dotnet build -c Release

# 発行（単一実行ファイル）
dotnet publish -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -p:PublishReadyToRun=true
```

## 使用方法

```bash
BugConvergenceTool <入力Excel> [オプション]
```

### オプション一覧

| オプション | 説明 |
|-----------|------|
| `-h`, `--help` | ヘルプを表示 |
| `-o`, `--output DIR` | 出力ディレクトリを指定 |
| `-v`, `--verbose` | 詳細出力 |
| `--basic-only` | 基本モデルのみ使用 |
| `--optimizer TYPE` | 最適化アルゴリズム（de/pso/gwo/cmaes/nm/grid/auto） |
| `--change-point` | 変化点モデルを含める |
| `--tef` | テスト工数関数モデルを含める |
| `--fre` | 欠陥除去効率モデルを含める |
| `--all-extended` | 全拡張モデルを含める |

### 使用例

```bash
# 基本的な使用法
BugConvergenceTool TestData.xlsx

# 出力先を指定
BugConvergenceTool TestData.xlsx -o ./output

# オプティマイザ指定
BugConvergenceTool TestData.xlsx --optimizer pso
BugConvergenceTool TestData.xlsx --optimizer auto -v

# 拡張モデル使用
BugConvergenceTool TestData.xlsx --change-point      # 変化点モデル
BugConvergenceTool TestData.xlsx --tef               # TEFモデル
BugConvergenceTool TestData.xlsx --fre               # FREモデル
BugConvergenceTool TestData.xlsx --all-extended      # 全拡張モデル
BugConvergenceTool TestData.xlsx --all-extended -v   # 詳細出力付き

# 組み合わせ
BugConvergenceTool TestData.xlsx --optimizer auto --all-extended -o ./results -v
```

## 入力Excelの形式

「データ入力」シートに以下の形式でデータを配置してください：

```
        |  B   |  C   |  D   |  E   | ...
--------|------|------|------|------|----
行2     | プロジェクト名         |
行3     | 総テストケース数       |
行4     | テスト開始日           |
        |      |      |      |      |
行6     | 1/6  | 1/7  | 1/8  | 1/9  | ...  ← 日付
行7     |  20  |  25  |  25  |  30  | ...  ← 予定消化（日次）
行8     |  15  |  22  |  28  |  25  | ...  ← 実績消化（日次）
行9     |   8  |  12  |  15  |  10  | ...  ← バグ発生（日次）
行10    |   2  |   5  |   8  |  10  | ...  ← バグ修正（日次）
```

## 出力ファイル

```
output/
├── Result_YYYYMMDD_HHmmss.xlsx    # 計算結果Excel
├── Result_YYYYMMDD_HHmmss.txt     # テキストレポート
└── Charts_YYYYMMDD_HHmmss/
    ├── test_progress.png          # テスト消化曲線
    ├── bug_cumulative.png         # バグ累積曲線
    ├── remaining_bugs.png         # 残存バグ推移
    ├── bug_convergence.png        # バグ収束確認グラフ
    └── reliability_growth.png     # 信頼度成長曲線
```

## 利用可能なモデル

### 基本モデル（5種類）

| モデル名 | 数式 | 特徴 |
|---------|------|------|
| 指数型（Goel-Okumoto） | m(t) = a(1 - e^(-bt)) | 最もシンプル、バグ発見率一定 |
| 遅延S字型 | m(t) = a(1 - (1+bt)e^(-bt)) | テスト初期の習熟を考慮 |
| ゴンペルツ | m(t) = a·e^(-b·e^(-ct)) | 終盤の収束が急 |
| 修正ゴンペルツ | m(t) = a(c - e^(-bt)) | S字カーブの柔軟性向上 |
| ロジスティック | m(t) = a / (1 + e^(-b(t-c))) | 対称S字カーブ |

#### 基本モデルのパラメータ

| モデル | パラメータ | 探索範囲 | 意味 |
|--------|-----------|---------|------|
| **指数型** | a | [maxY, maxY×5] | 総バグ数 |
| | b | [0.001, 1.0] | 発見率 |
| **遅延S字型** | a | [maxY, maxY×5] | 総バグ数 |
| | b | [0.001, 1.0] | 発見率 |
| **ゴンペルツ** | a | [maxY, maxY×5] | 総バグ数 |
| | b | [0.1, 10.0] | 初期遅延係数 |
| | c | [0.001, 1.0] | 成長率 |
| **修正ゴンペルツ** | a | [1.0, maxY×5] | スケール係数 |
| | b | [0.001, 1.0] | 発見率 |
| | c | [1.01, 2.0] | 漸近補正係数 |
| **ロジスティック** | a | [maxY, maxY×5] | 総バグ数 |
| | b | [0.01, 2.0] | 成長率 |
| | c | [1.0, n×2] | 変曲点（日） |

※ maxY: 観測データの最大累積バグ数、n: データ点数

### 不完全デバッグ考慮モデル（3種類）

| モデル名 | 数式 | 特徴 |
|---------|------|------|
| 不完全デバッグ指数型 | m(t) = a(1-e^(-bt)) / (1+p·e^(-bt)) | Pham-Nordmann-Zhang型。修正時の新バグ発生を考慮 |
| 修正S字型不完全デバッグ | 遅延S字 × 補正項 | S字型基盤の不完全デバッグ統合モデル |
| 一般化不完全デバッグ | m(t) = a(1-e^(-bt^c)) / (1+p(1-e^(-bt^c))) | 発見率変化と不完全デバッグを統合した汎用モデル |

#### 不完全デバッグモデルのパラメータ

| モデル | パラメータ | 探索範囲 | 意味 |
|--------|-----------|---------|------|
| **不完全デバッグ指数型** | a | [maxY, maxY×5] | 総バグ数 |
| | b | [0.001, 1.0] | 発見率 |
| | p | [-0.5, 0.99] | 不完全デバッグ率（バグ混入率） |
| **修正S字型不完全デバッグ** | a | [maxY, maxY×5] | 総バグ数 |
| | b | [0.001, 1.0] | 発見率 |
| | p | [-0.5, 0.99] | 不完全デバッグ率 |
| **一般化不完全デバッグ** | a | [maxY, maxY×5] | 総バグ数 |
| | b | [0.001, 1.0] | 発見率 |
| | c | [0.5, 2.0] | 形状パラメータ |
| | p | [-0.5, 0.99] | 不完全デバッグ率 |

#### パラメータの解釈（不完全デバッグ系モデル共通）

- **a（総バグ数スケール）**  
  潜在バグ総数の規模を表すスケールパラメータ。  
  完全デバッグ（p = 0）の場合や PNZ 型では、時間が十分経過したときの累積検出バグ数の上限に対応する。  
  不完全デバッグ（p ≠ 0）の一部モデルでは、実効的な上限は a/(1+p) となる。

- **b（発見率）**  
  バグが検出される速さ（発見率）を表すパラメータ。  
  他の条件が同じなら、b が大きいほど曲線の立ち上がりが急になり、早期に収束しやすい。

- **p（不完全デバッグ率）**  
  デバッグ時に新たなバグがどの程度混入／除去されるかを表す。
  - p > 0: 不完全デバッグ。修正時に新バグが混入し、信頼性成長が遅くなる。
  - p = 0: 完全デバッグ。修正による新バグ混入はない（従来モデルと同等）。
  - p < 0: 改善的デバッグ。修正に伴い関連バグも同時に除去され、見かけ上「期待以上に」信頼性が向上する。

## 最適化アルゴリズム

パラメータ推定に使用する最適化アルゴリズムの詳細です。

### アルゴリズム一覧

| コマンド | アルゴリズム名 | 種別 | 特徴 |
|---------|--------------|------|------|
| `de` | 差分進化（DE） | メタヒューリスティック | デフォルト・推奨。非微分可能・マルチモーダル関数に強い |
| `pso` | 粒子群最適化（PSO） | メタヒューリスティック | 群知能に基づく大域的最適化 |
| `gwo` | Grey Wolf Optimizer | メタヒューリスティック | オオカミの群れ行動に基づく最適化 |
| `cmaes` | CMA-ES | 進化戦略 | 共分散行列適応。地形の難しい問題に強い |
| `nm` | Nelder-Mead法 | 直接探索 | 勾配不要・低次元に強い局所最適化 |
| `grid` | グリッドサーチ+勾配降下法 | 従来手法 | グリッドサーチ後に勾配降下で精緻化 |
| `auto` | 自動選択 | - | 全アルゴリズムで比較し最良を選択 |

### パラメータ設定

#### DE（差分進化）

| パラメータ | デフォルト値 | 説明 |
|-----------|-------------|------|
| populationSize | 50 | 個体数 |
| maxIterations | 500 | 最大反復回数 |
| F | 0.8 | スケーリング係数 |
| CR | 0.9 | 交叉率 |
| tolerance | 1e-10 | 収束判定閾値 |

#### PSO（粒子群最適化）

| パラメータ | デフォルト値 | 説明 |
|-----------|-------------|------|
| swarmSize | 30 | 粒子数 |
| maxIterations | 500 | 最大反復回数 |
| w | 0.729 | 慣性重み |
| c1 | 1.49445 | 認知係数（個人最良への引力） |
| c2 | 1.49445 | 社会係数（全体最良への引力） |
| tolerance | 1e-10 | 収束判定閾値 |

#### GWO（Grey Wolf Optimizer）

| パラメータ | デフォルト値 | 説明 |
|-----------|-------------|------|
| packSize | 30 | 群れサイズ |
| maxIterations | 500 | 最大反復回数 |
| tolerance | 1e-10 | 収束判定閾値 |

#### CMA-ES（共分散行列適応進化戦略）

| パラメータ | デフォルト値 | 説明 |
|-----------|-------------|------|
| maxIterations | 500 | 最大反復回数 |
| initialSigmaU | 0.5 | u空間での初期ステップサイズ |
| tolerance | 1e-10 | 収束判定閾値 |
| λ | 4 + floor(3·ln(n)) | 個体数（自動計算） |
| μ | λ / 2 | 選択個体数（自動計算） |

※ CMA-ESはロジスティック変換により無拘束空間で最適化を実行

#### Nelder-Mead法（単体法）

| パラメータ | デフォルト値 | 説明 |
|-----------|-------------|------|
| maxIterations | 1000 | 最大反復回数 |
| tolerance | 1e-10 | 収束判定閾値 |
| α (alpha) | 1.0 | 反射係数 |
| γ (gamma) | 2.0 | 拡大係数 |
| ρ (rho) | 0.5 | 収縮係数 |
| σ (sigma) | 0.5 | 縮小係数 |

#### GridSearch+GD（グリッドサーチ+勾配降下法）

| パラメータ | デフォルト値 | 説明 |
|-----------|-------------|------|
| gridSize | 自動 | グリッドサイズ（次元数に応じて自動設定） |
| maxIterations | 2000 | 勾配降下法の最大反復回数 |
| learningRate | 0.00005 | 学習率 |
| delta | 0.0001 | 数値微分のデルタ |

## ライセンス

MIT License

### 使用ライブラリ

| ライブラリ | ライセンス | 用途 |
|-----------|-----------|------|
| [ClosedXML](https://github.com/ClosedXML/ClosedXML) | MIT | Excel読み書き |
| [MathNet.Numerics](https://numerics.mathdotnet.com/) | MIT | 数値計算 |
| [ScottPlot](https://scottplot.net/) | MIT | グラフ画像生成 |

## 参考文献

本ツールの拡張機能は以下の学術研究領域を参考に実装：

- Software Reliability Growth Models (SRGM)
- Change Point Models in Software Reliability
- Test Effort Functions in SRGM
- Fault Removal Efficiency Models
- Metaheuristic Optimization for Parameter Estimation
