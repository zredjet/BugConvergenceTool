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
| `--optimizer TYPE` | 最適化アルゴリズム（de/pso/gwo/grid/auto） |
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

### 不完全デバッグ考慮モデル（3種類）

| モデル名 | 特徴 |
|---------|------|
| 不完全デバッグ指数型 | Pham-Nordmann-Zhang型。修正時の新バグ発生を考慮 |
| 不完全デバッグS字型 | Yamada型。習熟効果と不完全デバッグを統合 |
| 一般化不完全デバッグ | 発見率変化と不完全デバッグを統合した汎用モデル |

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
