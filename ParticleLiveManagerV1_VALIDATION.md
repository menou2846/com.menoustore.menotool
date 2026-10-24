# Particle Live Manager V1 - VCC準備の検証

2026-10-24 / com.menoustore.particlelivemanager 0.1.0

- 既存めのうツール集に独立パッケージとして追加。既存の登録パッケージ・バージョン情報は保持。
- ZIPはpackage.jsonがルートにあり、同梱内容とpackages配下のファイルが一致。
- zipSHA256: `5224b53bf79018aaa2f26c457aa30fa184e512b2b4fe10ad53cd9d84ba882490`
- パッケージ版EditorコードをUnity 2022.3.22f1 / Timeline 1.7.6 / SDK 3.10.5 / MA 1.18.7 / NDMF 1.14.8の実体を参照してコンパイル成功。
- Runtimeの11アセットをUnityで一時インポートしPrefabの読み込み成功。Missing Script 0、プロジェクト固有アセットへの依存0。
- 共通Controllerの6つのAnimationClip参照とパラメータを確認。既存MA設定・ライブON/OFF・ワールド固定を維持。
- 元PrefabとMaterialは変更せず、パッケージ用コピーに新しいGUIDを付与。EditorスクリプトGUIDは既存制作設定との互換性のため維持。
- 同梱MaterialではParticles/Standard Unlitが使用しない_NormalMap参照のみ解除。Mochieテクスチャは同梱しない。
- セルフチェックのメニュー登録なし。通常のManagerメニューは保持。
- 検証用一時アセットは削除。制作中Sceneの保存・Play切替・再起動は実施していない。

## 未実施

VCC/ALCOMからの実インストール、クリーンプロジェクトでの実行、SDKビルド全工程、VRChat実機は未実施。
現在の制作プロジェクトはAssets版のまま。重複クラスとGUID衝突を避けるため、旧Editorスクリプトを残したままパッケージを導入しない。
legacyFiles/legacyFoldersによる自動削除は設定していない。既存のScene・Generatedデータは削除しない。
