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
| `-c`, `--config FILE` | 設定ファイルを指定 |
| `-v`, `--verbose` | 詳細出力 |
| `--basic-only` | 基本モデルのみ使用 |
| `--optimizer TYPE` | 最適化アルゴリズム（de/pso/gwo/cmaes/nm/grid/auto） |
| `--change-point` | 変化点モデルを含める |
| `--tef` | テスト工数関数モデルを含める |
| `--fre` | 欠陥除去効率モデルを含める |
| `--coverage` | Coverageモデルを含める |
| `--all-extended` | 全拡張モデルを含める |
| `--ci`, `--confidence-interval` | 95%信頼区間を計算（ブートストラップ法） |
| `--bootstrap N` | ブートストラップ反復回数（デフォルト: 200） |

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
BugConvergenceTool TestData.xlsx --coverage          # Coverageモデル
BugConvergenceTool TestData.xlsx --all-extended      # 全拡張モデル
BugConvergenceTool TestData.xlsx --all-extended -v   # 詳細出力付き

# 信頼区間付き分析
BugConvergenceTool TestData.xlsx --ci                # 95%信頼区間を計算
BugConvergenceTool TestData.xlsx --ci --bootstrap 500  # 反復回数を増やして精度向上

# 組み合わせ
BugConvergenceTool TestData.xlsx --optimizer auto --all-extended --ci -o ./results -v
```

## 入力Excelの形式

「データ入力」シートに以下の形式でデータを配置してください：

```text
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

```text
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

### PNZ型＋変化点モデル（実効時間方式）

テスト方針や環境が途中で変わるケースに対応する拡張モデルです。

| モデル名 | 数式 | 特徴 |
|---------|------|------|
| PNZ型+変化点 | m(t) = a(1-e^(-u(t)))/(1+p·e^(-u(t))) | PNZ型に検出率変化点を導入 |

#### 実効時間 u(t) の定義

```text
u(t) = b₁·t           （t ≤ τ）
u(t) = b₁·τ + b₂·(t-τ) （t > τ）
```

- **τ（変化点）**: テスト方針・環境が変わる時刻
- **b₁**: 変化点前の検出率
- **b₂**: 変化点後の検出率

この方式により m(t) は変化点 τ の前後で自動的に連続となります。

#### パラメータ

| パラメータ | 探索範囲 | 意味 |
|-----------|---------|------|
| a | [maxY, maxY×100] | 総バグ数スケール |
| b₁ | [1e-8, 10.0] | 変化点前の検出率 |
| b₂ | [1e-8, 10.0] | 変化点後の検出率 |
| p | [-0.5, 5.0] | 不完全デバッグ率 |
| τ | [2, n-2] | 変化点時刻（n=データ点数） |

#### 解釈

- **b₂ > b₁**: テスト強化（変化点以降の収束が加速）
- **b₂ < b₁**: テスト弱体化（変化点以降の収束が減速）
- **b₂ ≈ b₁**: 変化点の影響が小さい（通常のPNZ型に近い）

### Coverage モデル（パラメトリック NHPP）

テストカバレッジを時間 t の関数として近似し、m(t) = a · C(t) として定義するモデル群です。
実測カバレッジデータがなくても、時間を説明変数としてカバレッジ的な振る舞いを表現できます。

| モデル名 | カバレッジ関数 C(t) | 特徴 |
|---------|-------------------|------|
| Weibull Coverage | 1 - e^(-(βt)^γ) | γ>1でS字型、γ=1で指数型、γ<1で凸型 |
| Logistic Coverage | 1 / (1 + e^(-β(t-τ))) | τでカバレッジ50%となる対称S字型 |
| Gompertz Coverage | exp(-e^(-β(t-τ))) | 非対称S字型、初期の遅い立ち上がり |

#### Weibull Coverage のパラメータ

| パラメータ | 探索範囲 | 意味 |
|-----------|---------|------|
| a | [maxY, maxY×100] | 総バグ数（m(∞) = a） |
| β | [1e-8, 10.0] | 時間スケール（大きいほど立ち上がりが早い） |
| γ | [0.1, 5.0] | 形状パラメータ |

#### 形状パラメータ γ の解釈

- **γ < 1**: 凸型（初期に急速、後半に減速）
- **γ = 1**: 指数型（通常の Goel-Okumoto と等価）
- **γ > 1**: S字型（初期に遅く、中盤に加速、後半に減速）

#### Logistic Coverage のパラメータ

| パラメータ | 探索範囲 | 意味 |
|-----------|---------|------|
| a | [maxY, maxY×100] | 総バグ数 |
| β | [0.01, 10.0] | 立ち上がりの鋭さ |
| τ | [1, n] | カバレッジ50%到達時刻（n=データ点数） |

#### Gompertz Coverage のパラメータ

| パラメータ | 探索範囲 | 意味 |
|-----------|---------|------|
| a | [maxY, maxY×100] | 総バグ数 |
| β | [0.001, 5.0] | 増加率 |
| τ | [1, n] | 変曲点時刻（n=データ点数） |

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

### パラメータヒューリスティックと個別最適化方針

#### ヒューリスティック定義箇所

- モデルパラメータの初期値と探索範囲は、`Models/*.cs` の `GetInitialParameters` および `GetBounds` で決定されます。初期値のしきい値や倍率は `config.json` で外部設定可能になっており、デフォルト値は `Models/ConfigurationModels.cs` に定義されています。
- オプティマイザのハイパーパラメータ（個体数・反復回数・係数など）も `config.json` でカスタマイズ可能です。デフォルト値は30〜120点程度の一般的なバグ推移データを想定した経験値です。
- 探索範囲の上限・下限（`GetBounds`）はモデルの安全性に直結するため、コード内に固定されています。

#### 初期値の妥当性と修正の指針

- 初期値は、「30〜100日程度である程度収束している典型的な累積欠陥データ」を想定した**経験値ベース**ですが、入力データに応じて自動的に調整されます。
- `a` は終盤の増分（直近2点の差）を見て収束度合いを判定し、設定ファイルの `ScaleFactorA` に基づいてスケール係数を決定します：
  - 収束時（増分 ≤ しきい値）: `1.1〜1.4 × maxY`（デフォルト）
  - 未収束時（増分 > しきい値）: `1.5〜1.9 × maxY`（デフォルト）
- `b` は期間全体の平均増分から、設定ファイルの `AverageSlopeThresholds` に基づいて初期値を決定します（指数型: 0.05〜0.3、S字型: 0.08〜0.35）。
- 変化点 `τ` は、設定ファイルの `ChangePoint.CumulativeRatio`（デフォルト: 0.5）に基づき、累積バグ数がその比率に達する日を初期値とします。
- 専門的な意味での「統計的に最適な初期値」を保証するものではなく、「大域的オプティマイザが迷いにくくなるよう、データのスケールと形状に合わせた無難なスタート地点」を与える設計です。

具体的な修正方針は次の通りです：

- `a`（総バグ数スケール）
  - `config.json` の `ModelInitialization.ScaleFactorA` でスケール係数を調整できます。
  - `IncrementThreshold.ConvergenceThreshold`（デフォルト: 1.0）で収束判定のしきい値を変更できます。
  - 必要に応じて `GetBounds` の上限（`maxY×5` など）をコードで調整することで過大・過小推定を防ぎます。
- `b`, `c`（成長率・形状）
  - `config.json` の `AverageSlopeThresholds` で傾き区分と対応する `b` 初期値を調整できます。
  - ロジスティックや S 字系の `c` は「変曲点の日」に相当するため、`ChangePoint.CumulativeRatio` で調整可能です。
- 変化点 `τ`（Change Point 系）
  - `config.json` の `ChangePoint.CumulativeRatio` で変化点の初期位置を調整できます（デフォルト: 0.5 = 累積50%到達日）。
  - 複数変化点モデルでは `MultipleChangePoints.TwoPointRatios` / `ThreePointRatios` で分割比を設定できます。

#### 個別最適化を行う際の流れ

1. まず `--optimizer de` もしくは `--optimizer auto` で全パラメータを一括推定し、出力されたテキストレポート／Excelで現在値と目的関数値（RMSEや残差）を確認します。
2. 改善したい場合は、`config.json` を編集してオプティマイザのハイパーパラメータ（反復回数、個体数など）やモデル初期値のしきい値を調整します。
3. 特定のパラメータを固定したい場合は、対象モデルの `GetBounds` をコードで編集し、そのパラメータの下限=上限に設定します。
4. 低次元の微調整には `--optimizer nm`（単体法）や `--optimizer grid` を使うのが効果的です。Nelder-Mead で連続的に追い込み、必要なら GridSearch+GD で1変数ずつの感度を確認してください。
5. 調整後は必ず再度 `--optimizer auto` など大域的手法で回し、他のモデル・パラメータとの整合と汎化性能（過剰適合していないか）を確認します。

この手順により、設定ファイルで調整可能なパラメータと、コード内に固定されている範囲設定を明示的にコントロールしつつ、個々のパラメータを安全にチューニングできます。

## 設定ファイル（config.json）

設定ファイルを使用することで、オプティマイザのハイパーパラメータやモデルの初期値推定に使われるしきい値・倍率をカスタマイズできます。

### 設定ファイルの配置場所

以下の順序で設定ファイルが検索されます：

1. `-c, --config` オプションで明示的に指定されたパス
2. カレントディレクトリの `config.json`
3. 実行ファイルと同じディレクトリの `config.json`
4. `Templates/config.json`
5. `Templates/default-config.json`

設定ファイルが見つからない場合は、デフォルト値が使用されます。

### 設定ファイルの構造

```json
{
  "Optimizers": {
    "DE": {
      "PopulationSize": 50,
      "MaxIterations": 500,
      "F": 0.8,
      "CR": 0.9,
      "Tolerance": 1e-10
    },
    "PSO": {
      "SwarmSize": 30,
      "MaxIterations": 500,
      "W": 0.729,
      "C1": 1.49445,
      "C2": 1.49445,
      "Tolerance": 1e-10
    },
    "CMAES": {
      "MaxIterations": 500,
      "InitialSigmaU": 0.5,
      "Tolerance": 1e-10
    },
    "GWO": {
      "PackSize": 30,
      "MaxIterations": 500,
      "Tolerance": 1e-10
    },
    "NelderMead": {
      "MaxIterations": 1000,
      "Tolerance": 1e-10,
      "Alpha": 1.0,
      "Gamma": 2.0,
      "Rho": 0.5,
      "Sigma": 0.5
    },
    "GridSearchGradient": {
      "GridSize": 0,
      "MaxIterations": 2000,
      "LearningRate": 0.00005,
      "Delta": 0.0001
    }
  },
  "ModelInitialization": {
    "ScaleFactorA": {
      "ConvergedMin": 1.1,
      "ConvergedMax": 1.4,
      "NotConvergedMin": 1.5,
      "NotConvergedMax": 1.9
    },
    "IncrementThreshold": {
      "ConvergenceThreshold": 1.0
    },
    "AverageSlopeThresholds": {
      "VeryLow": 0.1,
      "Low": 0.5,
      "Medium": 1.0,
      "BValuesExponential": {
        "VeryLow": 0.05,
        "Low": 0.1,
        "Medium": 0.2,
        "High": 0.3
      },
      "BValuesSCurve": {
        "VeryLow": 0.08,
        "Low": 0.15,
        "Medium": 0.25,
        "High": 0.35
      }
    }
  },
  "ChangePoint": {
    "CumulativeRatio": 0.5,
    "MultipleChangePoints": {
      "TwoPointRatios": [0.33, 0.67],
      "ThreePointRatios": [0.25, 0.5, 0.75]
    }
  },
  "ImperfectDebug": {
    "P0": 0.1,
    "Eta0": 0.8,
    "EtaInfinity": 0.95,
    "Alpha0": 0.1,
    "GompertzB0": 2.0
  }
}
```

### 設定項目の説明

#### オプティマイザ設定（`Optimizers`）

| セクション | パラメータ | 説明 |
|-----------|-----------|------|
| **DE** | `PopulationSize` | 差分進化の個体数（デフォルト: 50） |
| | `MaxIterations` | 最大反復回数（デフォルト: 500） |
| | `F` | スケーリング係数（デフォルト: 0.8） |
| | `CR` | 交叉率（デフォルト: 0.9） |
| **PSO** | `SwarmSize` | 粒子数（デフォルト: 30） |
| | `W` | 慣性重み（デフォルト: 0.729） |
| | `C1`, `C2` | 認知・社会係数（デフォルト: 1.49445） |
| **CMAES** | `InitialSigmaU` | u空間での初期ステップサイズ（デフォルト: 0.5） |
| **GWO** | `PackSize` | 群れサイズ（デフォルト: 30） |
| **NelderMead** | `Alpha`, `Gamma`, `Rho`, `Sigma` | 反射・拡大・収縮・縮小係数 |

#### モデル初期値推定設定（`ModelInitialization`）

| セクション | パラメータ | 説明 |
|-----------|-----------|------|
| **ScaleFactorA** | `ConvergedMin/Max` | 収束時のaスケール係数範囲 |
| | `NotConvergedMin/Max` | 未収束時のaスケール係数範囲 |
| **IncrementThreshold** | `ConvergenceThreshold` | 収束判定の増分しきい値（デフォルト: 1.0） |
| **AverageSlopeThresholds** | `VeryLow`, `Low`, `Medium` | 平均傾き区分のしきい値 |
| | `BValuesExponential` | 指数型モデルのb初期値 |
| | `BValuesSCurve` | S字型モデルのb初期値 |

#### 変化点設定（`ChangePoint`）

| パラメータ | 説明 |
|-----------|------|
| `CumulativeRatio` | 変化点τを決める累積比率（デフォルト: 0.5） |
| `TwoPointRatios` | 2変化点モデルの累積比率 |
| `ThreePointRatios` | 3変化点モデルの累積比率 |

#### 不完全デバッグ設定（`ImperfectDebug`）

| パラメータ | 説明 |
|-----------|------|
| `P0` | 不完全デバッグ係数pの初期値（デフォルト: 0.1） |
| `Eta0` | 初期欠陥除去効率η₀（デフォルト: 0.8） |
| `EtaInfinity` | 漸近欠陥除去効率η∞（デフォルト: 0.95） |
| `Alpha0` | バグ混入率αの初期値（デフォルト: 0.1） |
| `GompertzB0` | ゴンペルツモデルの初期遅延係数（デフォルト: 2.0） |

### カスタマイズ例

#### 計算を軽量化したい場合

```json
{
  "Optimizers": {
    "DE": {
      "PopulationSize": 20,
      "MaxIterations": 200
    },
    "PSO": {
      "SwarmSize": 15,
      "MaxIterations": 200
    }
  }
}
```

#### バグ報告のスケールが大きいプロジェクトの場合

```json
{
  "ModelInitialization": {
    "IncrementThreshold": {
      "ConvergenceThreshold": 5.0
    }
  }
}
```

#### 変化点を早めに設定したい場合

```json
{
  "ChangePoint": {
    "CumulativeRatio": 0.4
  }
}
```

## 信頼区間（ブートストラップ法）

`--ci` オプションを指定すると、最適モデルの予測曲線に対して95%信頼区間を計算します。

### アルゴリズム概要

1. **残差リサンプリング（パラメトリック・ブートストラップ）**
   - 元の最適解 θ\* で残差 e\_i = y\_i - m(t\_i; θ\*) を計算
   - 残差をランダムに再サンプリングして擬似データを生成
   - 累積バグ数は非負にクリップ

2. **ブートストラップ最適化**
   - 擬似データに対して軽量オプティマイザ（Nelder-Mead、MaxIterations=80）で再フィッティング
   - 初期値には元の最適解 θ\* を使用

3. **信頼区間の計算**
   - 指定回数（デフォルト: 200回）のブートストラップを並列実行
   - 各時刻 t について予測値のパーセンタイル（2.5%, 97.5%）を計算

### 信頼区間の使用例

```bash
# 基本的な信頼区間計算
BugConvergenceTool TestData.xlsx --ci

# 反復回数を増やして精度向上（時間がかかる）
BugConvergenceTool TestData.xlsx --ci --bootstrap 500 -v
```

### 出力

信頼区間を計算すると、信頼度成長曲線のグラフ（`reliability_growth.png`）に95%予測区間が半透明の帯として描画されます。

### 注意事項

- **計算時間**: ブートストラップは多数の最適化を実行するため、通常の分析より時間がかかります
- **スレッドセーフティ**: `ReliabilityGrowthModelBase.Calculate` は純粋関数（内部状態を持たない）である前提で並列実行されます
- **フォールバック**: ブートストラップ最適化が収束しない場合は元の最適解を使用します
- **軽量オプティマイザ**: ブートストラップでは Nelder-Mead（MaxIterations=80）を使用して計算コストを抑えています

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
