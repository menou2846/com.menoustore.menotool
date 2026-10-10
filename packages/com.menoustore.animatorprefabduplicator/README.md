# Folder GUID Duplicator

フォルダを丸ごと複製し、**全アセットに新しいGUIDを振って、複製フォルダ内どうしの参照を複製側へ付け替える**Unity Editor拡張です。過去に販売した「Animator付きPrefab」のフォルダを、旧商品とGUIDが衝突しない形で新しい衣装に取り込む用途を想定しています。

**Fanbox限定ツールです。** 初回利用時にパスワード認証が必要です。パスワードは[Fanbox](https://kannazukimenou.fanbox.cc/)の支援者限定記事で配布しています(VCCに `com.menoustore.license` リポジトリも追加してください)。

## なぜ必要か

- Explorerなどで普通にコピーすると、`.meta` ごと同じGUIDになり、旧商品と新商品の両方を入れたお客さんの環境でGUIDが衝突します。
- Unity上でコピーすると新しいGUIDにはなりますが、複製したPrefabが**旧フォルダのController / Clip / Materialを参照したまま**です。

このツールは、フォルダ内の全ファイルを新GUIDでコピーし、フォルダ内の参照を複製側へ付け替えます。

## 使い方

1. VCCの `menotool` から `[menotool] Folder GUID Duplicator` をインストールします。
2. 旧商品のフォルダを、このプロジェクトの `Assets` 内に置きます。
3. Unityメニューの `Meno Tools > フォルダを新GUIDで複製` を開きます(Projectでフォルダを選んでから開くと、複製元に入ります)。
4. `複製元フォルダ`・`保存先の親フォルダ`・`新しいフォルダ名(省略可)` を指定し、`複製対象を調べる` で内容を確認してから `新しいGUIDで複製` を押します。
5. 複製先の `GUID_Duplication_Report.txt` で、複製したファイル・共有している外部アセット・旧GUID→新GUIDの対応を確認します。
6. **新しい衣装に使うのは、必ず複製先のフォルダのものです。**

## 複製するもの / しないもの

| 種類 | 扱い |
|---|---|
| Prefab / Animator Controller / Override Controller / Animation Clip / Avatar Mask / Material / Texture / FBX / 音源 / ScriptableObject など | 新GUIDで複製し、フォルダ内の参照を付け替え |
| スクリプト・シェーダー(`.cs` `.dll` `.asmdef` `.shader` `.cginc` `.hlsl` `.compute` など) | **複製しない**(複製すると型名・シェーダー名が重複して壊れるため)。元のものを共有して参照 |
| フォルダ外のアセット / Packages配下(VRChat SDKなど) | 複製しない。元のものを共有して参照(レポートに一覧) |

元のフォルダは**読み取りのみ**で、変更しません。処理に失敗した場合は、今回作成したフォルダだけを削除して中止します。旧GUIDへの参照が複製側に1件でも残る場合も、成功扱いにせず中止します。

## 制限・注意

- **Force Text が必要です。** `Edit > Project Settings > Editor > Asset Serialization` を `Force Text` にしてください。参照を持つアセット(Prefab / Controller / Clip / Material など)がバイナリ形式だった場合は中止します。
- フォルダ外のアセットを参照している箇所(別のPrefabを元にしたVariantなど)は、付け替えできません。レポートの `SHARED: assets outside the source folder` を確認し、必要ならそれらも同じフォルダ内に入れてから複製してください。
- Animation Clip内の対象Objectのパス(例: `Body/Outfit`)は書き換えません。新衣装で階層名が変わる場合は別途調整してください。
- テクスチャなどを含めて全部コピーするため、フォルダが大きいと時間と容量がかかります(確認ダイアログにサイズを表示します)。

## Prefab単体モード(従来機能)

ウィンドウ上部のタブで `Prefab単体(従来)` に切り替えると、Animator付きのPrefab1つについて、Controller / Override Controller / Clip / Avatar Maskだけを新GUIDで複製して参照を付け替えます(Materialなどは共有のまま)。FBX(Model Prefab)由来のVariant / Nested Prefabにも対応し、元が `.prefab` のVariant / Nestedは停止します。

## 動作確認の状況

Unity 2022.3.22 の実プロジェクトで、Prefab・Controller・Clip・Material・Texture・FBXを含むフォルダ(54ファイル)を複製し、次を確認しています。

- 元フォルダの全ファイルと `.meta` がバイト単位で変更されていないこと
- 新旧のGUIDが1件も重複しないこと
- 複製後のPrefab・Controller・Materialが、旧フォルダのアセットを一切参照していないこと
- 取り込み時にConsoleエラーが出ないこと
- 保存先が複製元の中にある場合の拒否、バイナリ形式のアセットが混ざった場合の中止とロールバック

アニメーションの実再生は未確認です。販売物に組み込む前に、複製結果をテストしてください。
