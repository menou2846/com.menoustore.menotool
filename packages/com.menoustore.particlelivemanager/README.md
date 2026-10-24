# Particle Live Manager

Unity 2022.3 / VRChat Avatars / Modular Avatar向けのパーティクルライブ制作ツール。

## 導入

VCCにModular Avatarの配布先 https://vpm.nadena.dev/vpm.json を追加してから、本パッケージをインストールする。
未公開版はVCCのUser Packagesへこのパッケージフォルダを追加して確認できる。

旧Assets版のEditorスクリプトとの同時導入はできない。既存プロジェクトへの移行前に旧Editorフォルダをプロジェクト外へ退避する。
Generatedフォルダ、制作設定、Scene、AnimationClip、Controllerは削除しない。スクリプトGUIDは旧版から維持している。
自動削除するlegacyFolders / legacyFilesは指定していない。

## 使用

1. Unityメニュー「Meno Tools → パーティクルライブ制作」を開く。
2. アバター・ライブ名・音源を指定し「制作を始める」。
3. particle / skybox / FXのRecordedを編集する。TimelineとAnimationウィンドウの時間は連動する。
4. アバターへ出力する演出は「アバター用データを作成・更新」で生成する。Sceneは手動保存。

音源はデフォルトONで、親のライブのON/OFFに従う。制作ControllerとDirectorは出力用コピーから除外する。
既存MA設定・ライブON/OFF・ワールド固定を維持する。自動生成Controllerだけを更新し、旧出力は保持する。
既存のControllerがある項目は自動上書きしない。完成した既存作品の一括取り込みは未対応。
Control TrackによるParticleSystemシミュレーションは手動プレビュー専用で、自動変換しない。
Transform、ブレンド、速度変更、ループなどV1で再現できない条件は書き出し前に拒否する。

## 同梱データ

雛形Prefab、共通ON/OFF・ワールド固定のAnimationとController、メニュー、標準Particle Materialを同梱。
元Prefab/Materialは変更していない。同梱MaterialからはShaderが使用していない旧_NormalMap参照だけを取り除いた。
MochieのテクスチャやSDK・MA・NDMF本体、音源・制作中作品は同梱しない。

## 検証範囲

初期開発版0.1.0。元のAssets版ではCompileと内部SelfCheckを実施済み。パッケージ版の確認結果はリポジトリの検証記録を参照。
VCCの実導入、クリーンプロジェクトでの動作、SDKビルド全工程、VRChat実機は未検証。
Timeline 1.7の内部Editor APIとの連携を使うため、Timeline更新時は再検証が必要。
