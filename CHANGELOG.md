# Changelog

## [2.12.0] - 2026-10-10

- `com.menoustore.animatorprefabduplicator`(v1.2.0・要認証): **参照チェック・修正**タブを追加。フォルダ内のアセットがフォルダ外のアセットを参照している場所を、`ファイル ▸ 階層 ▸ コンポーネント ▸ プロパティ`まで特定して一覧し、参照ごとに「参照を外す(None)」「フォルダに取り込む(新GUIDでコピーして付け替え)」を選べる。FBX作り直し後に残る「対象が存在しないPrefabInstanceの上書き」(見た目に影響しないゴミの参照)を自動判定し、上書きエントリごと削除できる。構造に関わる参照(m_Script/Variantの親/Nestedの対象)は外せない。フォルダ内から参照されていないアセットの一覧とゴミ箱送りも可能。変更前のファイルは`Library/MenoRefFixBackup/`へバックアップ。メニューを`Meno Tools/フォルダ複製・参照チェック`に変更。実プロジェクトの複製フォルダで、18件の外部参照を特定し、コピーで修正(見た目不変・Consoleエラー0)を確認

## [2.11.0] - 2026-10-10

- `com.menoustore.animatorprefabduplicator`(v1.1.0・要認証): **フォルダごと複製**をメイン機能として追加。フォルダ内の全アセット(Prefab/Controller/Clip/Material/Texture/FBXなど)を新GUIDで複製し、フォルダ内どうしの参照を複製側へ付け替える。スクリプト/シェーダー(.cs/.dll/.shader等)は複製せず共有。旧GUIDが残れば中止してロールバック。従来のPrefab単体複製はウィンドウのタブで残した。メニューを`Meno Tools/フォルダを新GUIDで複製`に変更、displayNameを`Folder GUID Duplicator`に変更(パッケージIDは公開済みのため`animatorprefabduplicator`のまま)。実プロジェクト(54ファイル)で、元フォルダ不変・GUID重複0・旧フォルダ参照0・Consoleエラー0を確認

## [2.10.0] - 2026-10-10

- 新規: `com.menoustore.animatorprefabduplicator`(v1.0.0・Fanbox限定/要認証)。Animator付きPrefabをController / Override Controller / Animation Clip / Avatar Maskごと新しいGUIDで複製し、参照を複製先へ付け替える。元データは変更せず、旧参照が残れば自動でロールバック。通常Prefabに加え、FBX(Model Prefab)由来のVariant / Nested Prefabにも対応(元が`.prefab`のVariant / Nestedは安全のため停止)。ChatGPT製の原案を精査し、メニューを`Meno Tools/Animatorプレハブを新GUIDで複製`に統一。実際の衣装プレハブ6件でUnity 2022.3.22上の実機検証済み

## [2.9.0] - 2026-10-09

- `com.menoustore.physboneautobinder`(v1.2.0): VRC Constraint対応を追加。PhysBoneと同じ命名規則(`PB_胸`/`PB-[LeftArm]`/ボーン名そのまま)で、コンストレイントのソースにアバター本体のボーンを自動設定。ソースは1個前提で、既に入っていても名前で見つけたボーンに上書き(衣装対応での入れ間違い防止)。ソースが1個でないコンストレイントはエラー表示となり、解消するまでApplyできない。メニュー名を`Meno Tools/PhysBone・Constraint自動設定`に変更。ウィンドウ名・入力欄ラベル(`PB Root Parent`→`衣装 Root`)・列見出しをPhysBone/Constraint両対応の表記に変更。VRC SDKのソースは`Sources.source0〜15.SourceTransform`/`Sources.totalLength`の固定スロット構造(Unity 2022.3.22 / SDK 3.10.3で実機確認)

- `com.menoustore.hierarchyvisualizer`(v1.3.0): VRC Constraintのソース有無の色分けを修正。従来は`sources`(小文字)を配列として読んでいたため空ソース検出が一度も働いていなかった。実際は`Sources.source0〜15.SourceTransform`/`Sources.totalLength`の固定スロット構造(Unity 2022.3.22 / SDK 3.10.3で実機確認)。ソース0個、またはTransformがNoneのソースがあれば橙、全ソースが入っていれば紫(色分けのみ。警告アイコン/バッジは付けない)。描画のたびにConsoleへ警告を出していた処理も削除。行の背景色が濃くてヒエラルキーが読みにくい問題も、全色の濃さを約45%に下げて解消(`ColorAlphaScale`で調整可)

## [2.8.0] - 2026-10-07

- Unityメニューを日本語化(ルートは`Meno Tools`のまま)。例: `Meno Tools/ヒエラルキー/すべて折りたたむ`、`Meno Tools/サムネイル撮影`、右クリックは`GameObject/Meno Tools/…`に統一。対象: 全7パッケージ(シェーダー名・パッケージIDは変更なし)

## [2.7.0] - 2026-09-16

- 有料/無料の入れ替え: `com.menoustore.physboneautobinder`(v1.1.0)を要認証(Fanbox限定)に、`com.menoustore.prefabgridplacer`(v1.2.0)を無料に変更

## [2.6.0] - 2026-09-16

- `com.menoustore.hierarchyvisualizer`(v1.2.0): Projectフォルダの折りたたみ/展開機能を削除。実機(2022.3.22)で`CollapseAll`呼び出しが失敗する報告があり、内部API依存の切り分けが難航したため、安定して動くHierarchy側の折りたたみ/展開/選択中のみ折りたたむ機能に絞った
- README: 認証必須ツールの配布先表記をBOOTHからFanboxに変更

## [2.5.0] - 2026-09-16

- 全7パッケージの`displayName`から`(Public)`表記を廃止(ユーザーにとって意味がないため)。代わりに認証が必要なパッケージ(`com.menoustore.colorvariantapplier`, `com.menoustore.prefabgridplacer`)には`(要認証)`を明記
- READMEのパッケージ一覧に🔓無料/🔒要パスワード認証の列を追加し、`com.menoustore.license`リポジトリの追加が必要な理由とパスワードの入手先(BOOTH)を明記

## [2.4.1] - 2026-09-16

- `com.menoustore.hierarchyvisualizer`(v1.1.1): Projectフォルダの折りたたみで実機報告された失敗を修正。`Type.GetMethod`/`GetField`/`GetProperty`が非公開メンバーを基底クラスまで遡って探さないため、階層を自前で辿るヘルパーに置き換え。原因特定用の診断コマンド(`Meno Tools/Hierarchy Visualizer/デバッグ: Projectツリー情報を表示`)を追加

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
