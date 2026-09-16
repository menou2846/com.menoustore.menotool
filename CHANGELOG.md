# Changelog

## [2.4.0] - 2026-09-16

- `com.menoustore.hierarchyvisualizer`(v1.1.0): Hierarchy/Projectをまとめて折りたたむ機能を追加
  - `Meno Tools/Hierarchy Visualizer/`配下にHierarchy/Project個別の折りたたみ・展開、設定対象に従ったまとめ実行、設定ウィンドウ
  - `GameObject`/`Assets`右クリックメニューからも実行可能(選択中オブジェクトのみ折りたたむコマンドも追加)
  - 設定: Toolsメニューでの実行対象(Hierarchy/Project/両方)、Scene見出し・Assets/Packagesを開いたまま残すか、実行前確認ダイアログの有無
  - Projectフォルダの折りたたみはUnity Editor内部APIに依存するため、対応していないバージョンではConsole警告のうえ安全に処理をスキップ

## [2.2.1] - 2026-08-07

- Unityメニュー名を全ツールで `Meno Tools/<英語ツール名>` に統一(`Object Name Cleanup`は日本語表記から、`Thumbnail Capture`は`MenoTools`(スペースなし)表記から変更)

## [2.2.0] - 2026-08-07

- `com.menoustore.thumbnailcapture`(PPv2対応サムネイル撮影ツール)を新規追加

## [2.1.0] - 2026-08-07

- Private版から3パッケージを追加: `com.menoustore.objectnamecleanup` / `com.menoustore.colorvariantapplier`(旧 `prefabreplacer`) / `com.menoustore.armaturepathchecker`(旧 `structurecomparer`)

## [2.0.0] - 2026-08-07

- 単一パッケージ(`com.menoustore.menotool`)から `com.menoustore.hierarchyvisualizer` と `com.menoustore.physboneautobinder` の2つの個別パッケージへ分割(Private版と同じ「ツール1本ごと」方式に統一)
- 表示名はそれぞれ `[menotool] Hierarchy Visualizer (Public)` / `[menotool] PhysBone Auto Binder (Public)`

## [1.1.0] - 2026-08-07

- `PhysBoneAutoBinder`(旧 `PhysBoneRootFromNameTool`、Private版より移動)を追加
- パッケージ全体の表示名を `[menotool] Public Tools` に変更(複数ツール収録のため)

## [1.0.2] - 2026-08-07

- VCC上での表示名をPrivate版と揃えて `[menotool] Hierarchy Visualizer (Public)` に統一

## [1.0.1] - 2026-08-07

- VCC上での表示名を `Menou Tool` から `VRC Hierarchy Visualizer (menou Tool)` に変更(中身がわかるように)

## [1.0.0] - 2026-08-07

- 初回リリース。`vrchierarchy_visualizer` をPublicパッケージとして公開
