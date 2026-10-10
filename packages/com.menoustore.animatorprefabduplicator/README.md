# Animator Prefab Duplicator

Animator付きのPrefabを、Animator Controller / Override Controller / Animation Clip / Avatar Maskごと**新しいGUIDで複製**し、参照を複製先へ付け替えるUnity Editor拡張です。旧衣装のPrefabを元に、旧データと完全に切り離した新衣装の土台を作る用途を想定しています。

**Fanbox限定ツールです。** 初回利用時にパスワード認証が必要です。パスワードは[Fanbox](https://kannazukimenou.fanbox.cc/)の支援者限定記事で配布しています(VCCに `com.menoustore.license` リポジトリも追加してください)。

## 使い方

1. VCCの `menotool` から `[menotool] Animator Prefab Duplicator` をインストールします。
2. Unityメニューの `Meno Tools > Animatorプレハブを新GUIDで複製` を開きます。
3. `元Prefab` に、複製したいPrefabアセット(Projectウィンドウ内)を指定します。
4. 必要に応じて保存先フォルダと新フォルダ名を指定します。
5. `複製対象を調べる` で候補と警告を確認し、`新しいGUIDで複製` を押します。
6. 出力フォルダ内の `GUID_Duplication_Report.txt` で、旧/新GUIDと検証結果を確認します。
7. **新しい衣装に使うのは、必ず生成されたPrefab・Controller・Clipです。** Animator や Modular Avatar Merge Animator などのController参照が複製先を指しているか、Inspectorでも確認してください。

## 複製対象

- `.prefab` 本体
- Animator Controller (`.controller`)・内部のState / Blend Tree など
- Animator Override Controller (`.overrideController`)
- Animation Clip (`.anim`)
- Avatar Mask (`.mask`)
- FBXなどに内蔵されたAnimation Clip(新しい `.anim` として抽出)
- Prefabに直列化されているAnimator参照・その他コンポーネントのアニメーション参照

このツールは**新規フォルダにのみ書き込みます**。元のPrefab・Controller・Clipとその `.meta` は変更しません。

## 対応するPrefab

| 種類 | 対応 |
|---|---|
| 通常Prefab | ✅ |
| FBX(Model Prefab)由来のVariant | ✅ |
| FBX(Model Prefab)をNestedで含むPrefab | ✅ |
| 元が `.prefab` のVariant / Nested Prefab | ❌ 停止(旧Prefabへのリンクが残るため) |
| Missing Scriptを含むPrefab | ❌ 停止(先に参照を修復してください) |

元のアニメーション関連アセットへの参照が複製後に見つかった場合は、成功扱いにせず、今回作成したフォルダを削除して中止します。

## 意図的に共有するもの

Material / Texture / Mesh / FBX本体 / Script、およびPackages配下のアセット(VRChat SDKの標準Avatar Maskなど)は複製しません。Animation Clipのカーブに保存された対象Objectのパス(例: `Body/Outfit`)も自動では書き換えません。新衣装側の階層名が変わる場合は、別途Clipのパスを調整してください。

## 確認推奨

1. 旧Prefabと新Prefabで、GUIDとPrefabの参照先が別であること。
2. 新ControllerのMotionが新Clipを指していること(Override Controllerの上書きClipも)。
3. Animator・MA Merge Animatorなどの参照が新Controllerになっていること。
4. 新Prefabのアニメーションが実際に再生され、パスが新衣装の階層と一致していること。
5. 元の衣装のController / Clipを変更しても、新衣装に影響しないこと。

## 動作確認の状況

Unity 2022.3.22 で、実際の衣装・ギミックPrefab 6件(通常Prefab、FBX由来のVariant、FBXをNestedで含むPrefab)を複製し、次を確認しています。

- 元のPrefab・Controller・Clip・`.meta` がバイト単位で変更されていないこと
- 複製したPrefabが旧アニメーションアセットを参照していないこと(0件)
- 複製後のClipがすべて複製先フォルダに入っていること

アニメーションの実再生は未確認です。販売物に組み込む前に、新規複製の出力をテストしてください。
