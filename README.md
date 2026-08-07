# menotool (Public)

menou-store が公開しているVRChatアバター制作用のEditor拡張ツール集です。今後Public化するツールもここに追加していきます。Privateとは異なり、GitHubアカウント不要で誰でもインストールできます。

## 含まれるパッケージ

| パッケージID | 内容 |
|---|---|
| `com.menoustore.hierarchyvisualizer` | VRChatアバターのヒエラルキー構造を可視化するEditor拡張 |
| `com.menoustore.physboneautobinder` | 衣装のPhysBoneを名前でアバター本体のボーンに自動マッチングしてrootTransformを一括設定するEditor拡張 |
| `com.menoustore.objectnamecleanup` | オブジェクト名の一括クリーンアップ |
| `com.menoustore.colorvariantapplier` | ソースプレハブのマテリアルをターゲットプレハブへパスマッチングで一括コピーする、色違い(カラバリ)量産向けEditor拡張 |
| `com.menoustore.armaturepathchecker` | 2つのプレハブのアーマチュア構造を比較し、指定したボーン名のパス一致をチェックするEditor拡張 |

必要なものだけ個別にインストールできます。

## VCC(VRChat Creator Companion)での導入方法

GitHubアカウント不要で追加できます。

1. VCCを開く → `Settings` → `Packages` タブ → `Add Repository`
2. 以下のURLを入力して追加:

```
https://raw.githubusercontent.com/menou2846/com.menoustore.menotool/master/index.json
```

3. 対象プロジェクトの `Manage Project` 画面に上記パッケージが個別に表示されるので、必要なものだけInstall

## 開発者向け: 新バージョンの出し方

1. 変更したいパッケージの `packages/<name>/package.json` の `version` を上げてcommit・push
2. バージョンタグを作成してpush(全パッケージまとめてリリースされます)

```bash
git tag v1.2.0
git push origin v1.2.0
```

3. GitHub Actions(`.github/workflows/publish.yml`)が全パッケージをzip化し、`index.json` を更新します
4. `Actions` タブでワークフローが成功していることを確認してください

## 依存関係

- Unity 2022.3以降
- VRChat SDK - Avatars (`com.vrchat.avatars`) 3.7.0以降
