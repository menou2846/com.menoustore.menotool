# Menou Tool

menou-store が公開しているVRChatアバター制作用のEditor拡張ツール集です。今後Public化するツールもここに追加していきます。

## 含まれるもの

- `vrchierarchy_visualizer` - VRChatアバターのヒエラルキー構造を可視化するEditor拡張
- `PhysBoneAutoBinder` - 衣装のPhysBoneを名前でアバター本体のボーンに自動マッチングしてrootTransformを一括設定するEditor拡張

## VCC(VRChat Creator Companion)での導入方法

GitHubアカウント不要で追加できます。

1. VCCを開く → `Settings` → `Packages` タブ → `Add Repository`
2. 以下のURLを入力して追加:

```
https://raw.githubusercontent.com/menou2846/com.menoustore.menotool/master/index.json
```

3. 対象プロジェクトの `Manage Project` 画面に `Menou Tool` が表示されるのでInstall

## 開発者向け: 新バージョンの出し方

1. `package.json` の `version` を上げてcommit・push
2. バージョンタグを作成してpush

```bash
git tag v1.0.1
git push origin v1.0.1
```

3. GitHub Actions(`.github/workflows/publish.yml`)が自動的にzipを作成し、`index.json` を更新してくれます
4. `Actions` タブでワークフローが成功していることを確認してください

## 依存関係

- Unity 2022.3以降
- VRChat SDK - Avatars (`com.vrchat.avatars`) 3.7.0以降
