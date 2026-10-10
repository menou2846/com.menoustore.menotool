using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using MenouStore.License;
using Object = UnityEngine.Object;

/// <summary>
/// Duplicates a regular prefab and its animation-related dependencies with new GUIDs.
/// Never modifies the input prefab or source animation assets.
/// Unity 2022.3 Editor only.
/// </summary>
public sealed class MenoAnimatorPrefabDuplicator : EditorWindow
{
    private const string ProductId = "default";

    private GameObject sourcePrefab;
    private DefaultAsset outputParent;
    private string folderName = "";
    private Vector2 scroll;
    private List<Object> previewObjects = new List<Object>();
    private List<string> previewWarnings = new List<string>();
    private string previewError;

    [MenuItem("Meno Tools/Animatorプレハブを新GUIDで複製")]
    private static void Open()
    {
        if (!LicenseAuth.IsAuthenticated(ProductId))
        {
            LicenseAuth.OpenAuthWindow(ProductId);
            return;
        }

        var window = GetWindow<MenoAnimatorPrefabDuplicator>("Animatorプレハブ複製");
        var selected = Selection.activeObject as GameObject;
        if (selected != null && PrefabUtility.IsPartOfPrefabAsset(selected))
            window.sourcePrefab = selected;
        if (window.outputParent == null)
            window.outputParent = AssetDatabase.LoadAssetAtPath<DefaultAsset>("Assets"); // 開き直しで保存先指定を消さない
        window.minSize = new Vector2(520, 400);
        window.Show();
    }

    private void OnGUI()
    {
        if (!LicenseAuth.IsAuthenticated(ProductId))
        {
            EditorGUILayout.HelpBox("認証が必要です。一度ウィンドウを閉じてメニューから開き直してください。", MessageType.Warning);
            if (GUILayout.Button("認証する"))
            {
                LicenseAuth.OpenAuthWindow(ProductId);
                Close();
            }
            return;
        }

        EditorGUILayout.LabelField("Animator付きPrefabを独立GUIDで複製", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "Prefab / Animator Controller / Override Controller / Animation Clip / Avatar Maskを新規作成し、" +
            "複製先の参照を自動的に付け替えます。元データは変更しません。", MessageType.Info);

        EditorGUI.BeginChangeCheck();
        sourcePrefab = (GameObject)EditorGUILayout.ObjectField("元Prefab", sourcePrefab, typeof(GameObject), false);
        outputParent = (DefaultAsset)EditorGUILayout.ObjectField("保存先の親フォルダ", outputParent, typeof(DefaultAsset), false);
        folderName = EditorGUILayout.TextField("新しいフォルダ名（省略可）", folderName);
        if (EditorGUI.EndChangeCheck())
        {
            previewObjects.Clear();
            previewWarnings.Clear();
            previewError = null;
        }

        EditorGUILayout.Space();
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("複製対象を調べる", GUILayout.Height(32)))
            RefreshPreview();
        using (new EditorGUI.DisabledScope(sourcePrefab == null))
        {
            if (GUILayout.Button("新しいGUIDで複製", GUILayout.Height(32)))
                CreateCopy();
        }
        EditorGUILayout.EndHorizontal();

        if (!string.IsNullOrEmpty(previewError))
            EditorGUILayout.HelpBox(previewError, MessageType.Error);
        if (previewObjects.Count > 0)
            EditorGUILayout.LabelField("複製候補: " + previewObjects.Count + " アニメーションアセット + Prefab", EditorStyles.boldLabel);
        if (previewWarnings.Count > 0)
            EditorGUILayout.HelpBox(string.Join("\n", previewWarnings.ToArray()), MessageType.Warning);

        scroll = EditorGUILayout.BeginScrollView(scroll);
        foreach (var obj in previewObjects)
        {
            if (obj == null) continue;
            EditorGUILayout.LabelField("• " + obj.name + "  [" + obj.GetType().Name + "]");
            EditorGUILayout.LabelField("   " + AssetDatabase.GetAssetPath(obj), EditorStyles.miniLabel);
        }
        EditorGUILayout.EndScrollView();

        EditorGUILayout.HelpBox(
            "対応: 通常Prefab、およびFBX(Model Prefab)由来のVariant / Nested Prefab。\n" +
            "元が.prefabのVariant・Nested Prefabは、旧Prefabとの隠れた参照を防ぐため処理を停止します。\n" +
            "Material / Texture / Mesh / FBX本体 / SDKアセットは複製対象外です。" +
            "FBX内蔵AnimationClipは独立した.animに抽出します。", MessageType.None);
    }

    private void RefreshPreview()
    {
        previewObjects.Clear();
        previewWarnings.Clear();
        previewError = null;
        try
        {
            ValidateSource(sourcePrefab);
            previewObjects = FindAnimationDependencies(sourcePrefab, previewWarnings);
        }
        catch (Exception ex)
        {
            previewError = ex.Message;
        }
    }

    internal static void ValidateSource(GameObject sourcePrefab)
    {
        if (sourcePrefab == null)
            throw new InvalidOperationException("元Prefabを指定してください。");
        string path = AssetDatabase.GetAssetPath(sourcePrefab);
        if (string.IsNullOrEmpty(path) || !path.StartsWith("Assets/", StringComparison.Ordinal) ||
            !path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Assets配下の.prefabアセットを指定してください（Scene上のObjectは不可）。");
        var assetType = PrefabUtility.GetPrefabAssetType(sourcePrefab);
        if (assetType != PrefabAssetType.Regular && assetType != PrefabAssetType.Variant)
            throw new InvalidOperationException("通常PrefabまたはFBX由来のVariantのみ対応です（Model Prefabそのものは不可）。");

        GameObject root = null;
        try
        {
            root = PrefabUtility.LoadPrefabContents(path);
            if (assetType == PrefabAssetType.Variant)
                RequireModelSource(root, "このPrefab(Variant)の親");
            foreach (var transform in root.GetComponentsInChildren<Transform>(true))
            {
                if (transform == null) continue;
                if (transform != root.transform && PrefabUtility.IsAnyPrefabInstanceRoot(transform.gameObject))
                    RequireModelSource(transform.gameObject, "Nested Prefab「" + transform.name + "」の元");
                var components = transform.GetComponents<Component>();
                if (components.Any(c => c == null))
                    throw new InvalidOperationException(
                        "Missing ScriptのあるObjectが含まれています: " + transform.name +
                        "。先に参照を修復してください。");
            }
        }
        finally
        {
            if (root != null) PrefabUtility.UnloadPrefabContents(root);
        }
    }

    // Variantの親 / Nestedの元がFBX等のModel Prefabなら許可する(FBXは共有対象で旧Prefabへの隠れた参照を持たない)。
    // .prefab由来だと旧Prefabへのリンクが残るため停止する。複製後にもVerifyNoOldAnimationReferencesで再検査する。
    private static void RequireModelSource(GameObject instance, string what)
    {
        var source = PrefabUtility.GetCorrespondingObjectFromSource(instance);
        if (source != null && PrefabUtility.GetPrefabAssetType(source) == PrefabAssetType.Model) return;
        throw new InvalidOperationException(
            what + "がFBX(Model Prefab)ではありません: " + (source != null ? AssetDatabase.GetAssetPath(source) : "不明") +
            "。旧Prefabへのリンクを残さないため複製を停止しました。");
    }

    private static bool IsAnimationDependency(Object obj)
    {
        return obj is RuntimeAnimatorController || obj is AnimationClip || obj is AvatarMask;
    }

    private static bool CanDuplicate(Object obj)
    {
        var path = AssetDatabase.GetAssetPath(obj);
        if (obj is AnimationClip) return true; // imported FBX subasset -> extract .anim
        if (!AssetDatabase.IsMainAsset(obj)) return false;
        if (obj is UnityEditor.Animations.AnimatorController)
            return path.EndsWith(".controller", StringComparison.OrdinalIgnoreCase);
        if (obj is AnimatorOverrideController)
            return path.EndsWith(".overrideController", StringComparison.OrdinalIgnoreCase);
        if (obj is AvatarMask)
            return path.EndsWith(".mask", StringComparison.OrdinalIgnoreCase);
        return false;
    }

    internal static List<Object> FindAnimationDependencies(GameObject prefab, List<string> warnings)
    {
        var results = new HashSet<Object>();
        // CollectDependencies is recursive and also finds clips referenced by controller subassets.
        foreach (var obj in EditorUtility.CollectDependencies(new Object[] { prefab }))
        {
            if (obj == null || !IsAnimationDependency(obj)) continue;
            var path = AssetDatabase.GetAssetPath(obj);
            if (string.IsNullOrEmpty(path))
            {
                warnings.Add("保存アセットではないアニメーション参照を除外: " + obj.name);
                continue;
            }
            if (!path.StartsWith("Assets/", StringComparison.Ordinal))
            {
                warnings.Add("共有アセットのため複製しません: " + path + " (" + obj.name + ")");
                continue;
            }
            if (!CanDuplicate(obj))
                throw new InvalidOperationException("複製できない形式のアニメーションアセット: " + path + " (" + obj.name + ")");
            results.Add(obj);
        }
        return results.OrderBy(x => AssetDatabase.GetAssetPath(x), StringComparer.Ordinal)
                      .ThenBy(x => x.name, StringComparer.Ordinal)
                      .ThenBy(x => x.GetType().Name, StringComparer.Ordinal)
                      .ToList();
    }

    private static string CleanName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "Animation";
        var invalid = new HashSet<char>(Path.GetInvalidFileNameChars());
        foreach (char c in "\\/:*?\"<>|") invalid.Add(c);
        var sb = new StringBuilder();
        foreach (char c in name)
            sb.Append(invalid.Contains(c) || char.IsControl(c) ? '_' : c);
        var result = sb.ToString().Trim().Trim('.');
        return string.IsNullOrEmpty(result) ? "Animation" : result;
    }

    private static string ExtensionFor(Object obj)
    {
        if (obj is UnityEditor.Animations.AnimatorController) return ".controller";
        if (obj is AnimatorOverrideController) return ".overrideController";
        if (obj is AvatarMask) return ".mask";
        if (obj is AnimationClip) return ".anim";
        throw new InvalidOperationException("非対応のアセット型: " + obj.GetType().Name);
    }

    private static Object CopyOne(Object original, string animationsFolder, out string copyPath)
    {
        string srcPath = AssetDatabase.GetAssetPath(original);
        bool canCopyFile = AssetDatabase.IsMainAsset(original) &&
                           string.Equals(Path.GetExtension(srcPath), ExtensionFor(original),
                                         StringComparison.OrdinalIgnoreCase);
        string name = CleanName(original.name);
        if (!canCopyFile)
            name = CleanName(Path.GetFileNameWithoutExtension(srcPath)) + "__" + name;
        copyPath = AssetDatabase.GenerateUniqueAssetPath(animationsFolder + "/" + name + ExtensionFor(original));

        if (canCopyFile)
        {
            if (!AssetDatabase.CopyAsset(srcPath, copyPath))
                throw new InvalidOperationException("CopyAssetに失敗: " + srcPath);
            var result = AssetDatabase.LoadMainAssetAtPath(copyPath);
            if (result == null || result.GetType() != original.GetType())
                throw new InvalidOperationException("複製アセットの読み込みに失敗: " + copyPath);
            return result;
        }

        // AnimationClip is a subasset of an FBX or another imported asset.
        if (!(original is AnimationClip sourceClip))
            throw new InvalidOperationException("AnimationClip以外のサブアセットは抽出できません: " + srcPath);
        var newClip = Instantiate(sourceClip);
        newClip.name = original.name;
        newClip.hideFlags = HideFlags.None;
        AssetDatabase.CreateAsset(newClip, copyPath);
        return newClip;
    }

    private static int ReplaceSerializedReferences(Object target, Dictionary<Object, Object> replacements)
    {
        if (target == null) return 0;
        var serialized = new SerializedObject(target);
        var property = serialized.GetIterator();
        int count = 0;
        // Next(true) includes hidden serialized references (controller states / blend trees).
        while (property.Next(true))
        {
            if (property.propertyType != SerializedPropertyType.ObjectReference || property.propertyPath == "m_Script")
                continue;
            var current = property.objectReferenceValue;
            if (current != null && replacements.TryGetValue(current, out var replacement))
            {
                property.objectReferenceValue = replacement;
                count++;
            }
        }
        if (count > 0)
            serialized.ApplyModifiedPropertiesWithoutUndo();
        return count;
    }

    private static int ReplaceObjectReferenceCurves(AnimationClip clip, Dictionary<Object, Object> replacements)
    {
        int count = 0;
        foreach (var binding in AnimationUtility.GetObjectReferenceCurveBindings(clip))
        {
            var frames = AnimationUtility.GetObjectReferenceCurve(clip, binding);
            bool changed = false;
            for (int i = 0; i < frames.Length; i++)
            {
                if (frames[i].value != null && replacements.TryGetValue(frames[i].value, out var replacement))
                {
                    frames[i].value = replacement;
                    changed = true;
                    count++;
                }
            }
            if (changed) AnimationUtility.SetObjectReferenceCurve(clip, binding, frames);
        }
        return count;
    }

    private static int PatchAnimationAssets(IEnumerable<string> paths, Dictionary<Object, Object> replacements)
    {
        int count = 0;
        foreach (string path in paths)
        {
            foreach (var obj in AssetDatabase.LoadAllAssetsAtPath(path))
            {
                if (obj == null) continue;
                count += ReplaceSerializedReferences(obj, replacements);
                if (obj is AnimationClip clip)
                    count += ReplaceObjectReferenceCurves(clip, replacements);
            }
        }
        return count;
    }

    private static int PatchPrefab(string prefabPath, Dictionary<Object, Object> replacements)
    {
        GameObject root = null;
        try
        {
            root = PrefabUtility.LoadPrefabContents(prefabPath);
            int count = 0;
            foreach (var transform in root.GetComponentsInChildren<Transform>(true))
            {
                if (transform == null) continue;
                count += ReplaceSerializedReferences(transform.gameObject, replacements);
                foreach (var component in transform.GetComponents<Component>())
                    count += ReplaceSerializedReferences(component, replacements);
            }
            if (PrefabUtility.SaveAsPrefabAsset(root, prefabPath) == null)
                throw new InvalidOperationException("複製Prefabの保存に失敗しました。");
            return count;
        }
        finally
        {
            if (root != null) PrefabUtility.UnloadPrefabContents(root);
        }
    }

    private static void VerifyNoOldAnimationReferences(string copyPrefabPath, IEnumerable<Object> originals)
    {
        var originalSet = new HashSet<Object>(originals);
        var newPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(copyPrefabPath);
        if (newPrefab == null) throw new InvalidOperationException("複製Prefabを検証用に再読み込みできません。");

        var stale = EditorUtility.CollectDependencies(new Object[] { newPrefab })
            .Where(o => o != null && originalSet.Contains(o))
            .Select(o => AssetDatabase.GetAssetPath(o) + " :: " + o.name)
            .Distinct()
            .ToArray();
        if (stale.Length != 0)
            throw new InvalidOperationException("旧アニメーション参照が残っています:\n" + string.Join("\n", stale));
    }

    private void CreateCopy()
    {
        try
        {
            string parent = outputParent != null ? AssetDatabase.GetAssetPath(outputParent) : "Assets";
            string resultPath = Duplicate(sourcePrefab, parent, folderName, true);
            if (resultPath == null) return; // キャンセル

            var resultPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(resultPath);
            Selection.activeObject = resultPrefab;
            EditorGUIUtility.PingObject(resultPrefab);
            EditorUtility.DisplayDialog("複製完了", "新しいPrefabとアニメーションを作成しました。\n" +
                "旧アニメーション参照: 0件\n" +
                "詳細はGUID_Duplication_Report.txtを確認してください。", "OK");
            RefreshPreview();
        }
        catch (Exception ex)
        {
            Debug.LogError("[Animator Prefab Duplicator] 複製失敗: " + ex);
            EditorUtility.DisplayDialog("複製を中止しました", ex.Message +
                "\n\n作成途中のフォルダがあれば自動で削除しました。元データは変更していません。", "OK");
        }
    }

    /// <summary>
    /// 複製の本体。confirm=trueなら実行前に確認ダイアログを出す(キャンセルならnullを返す)。
    /// 失敗時は今回作成したフォルダだけを削除して例外を投げる。成功時は新Prefabのパスを返す。
    /// </summary>
    internal static string Duplicate(GameObject sourcePrefab, string parent, string folderName, bool confirm)
    {
        ValidateSource(sourcePrefab);
        var warnings = new List<string>();
        var originals = FindAnimationDependencies(sourcePrefab, warnings);
        if (!AssetDatabase.IsValidFolder(parent) ||
            (parent != "Assets" && !parent.StartsWith("Assets/", StringComparison.Ordinal)))
            throw new InvalidOperationException("保存先はAssets内のフォルダを指定してください。");

        string srcPath = AssetDatabase.GetAssetPath(sourcePrefab);
        string baseName = string.IsNullOrWhiteSpace(folderName)
            ? CleanName(sourcePrefab.name) + "_Independent"
            : CleanName(folderName);
        string uniquePath = AssetDatabase.GenerateUniqueAssetPath(parent + "/" + baseName);
        string proposed = uniquePath.Substring(uniquePath.LastIndexOf('/') + 1);
        if (confirm)
        {
            string msg = "複製先: " + uniquePath + "\n" +
                         "Prefab 1件 / アニメーションアセット " + originals.Count + "件\n" +
                         "元アセットは変更しません。処理しますか？";
            if (!EditorUtility.DisplayDialog("Animatorプレハブ複製", msg, "複製する", "キャンセル")) return null;
        }

        string createdFolder = null;
        try
        {
            string folderGuid = AssetDatabase.CreateFolder(parent, proposed);
            if (string.IsNullOrEmpty(folderGuid))
                throw new InvalidOperationException("複製フォルダを作成できませんでした。");
            createdFolder = AssetDatabase.GUIDToAssetPath(folderGuid);
            string animFolderGuid = AssetDatabase.CreateFolder(createdFolder, "Animations");
            if (string.IsNullOrEmpty(animFolderGuid))
                throw new InvalidOperationException("Animationsフォルダを作成できませんでした。");
            string animFolder = AssetDatabase.GUIDToAssetPath(animFolderGuid);
            string prefabDest = createdFolder + "/" + CleanName(sourcePrefab.name) + ".prefab";
            if (!AssetDatabase.CopyAsset(srcPath, prefabDest))
                throw new InvalidOperationException("Prefabの複製に失敗しました。");
            if (string.Equals(AssetDatabase.AssetPathToGUID(srcPath),
                              AssetDatabase.AssetPathToGUID(prefabDest), StringComparison.Ordinal))
                throw new InvalidOperationException("Prefabの新しいGUIDを確認できませんでした。");

            var replacements = new Dictionary<Object, Object>();
            var copyPaths = new List<string>();
            var idReport = new StringBuilder();
            idReport.AppendLine("Meno Animator Prefab Duplicator");
            idReport.AppendLine("Source: " + srcPath);
            idReport.AppendLine("Clone: " + prefabDest);
            idReport.AppendLine("Prefab GUID: " + AssetDatabase.AssetPathToGUID(srcPath) +
                                " -> " + AssetDatabase.AssetPathToGUID(prefabDest));
            idReport.AppendLine();
            idReport.AppendLine("ANIMATION ASSET GUID MAP");

            foreach (var original in originals)
            {
                string clonePath;
                var clone = CopyOne(original, animFolder, out clonePath);
                string oldGuid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(original));
                string newGuid = AssetDatabase.AssetPathToGUID(clonePath);
                if (string.IsNullOrEmpty(newGuid) || string.Equals(oldGuid, newGuid, StringComparison.Ordinal))
                    throw new InvalidOperationException("新しいGUIDの検証に失敗: " + clonePath);
                replacements.Add(original, clone);
                copyPaths.Add(clonePath);
                idReport.AppendLine(AssetDatabase.GetAssetPath(original) + " [" + original.name + "]");
                idReport.AppendLine(" -> " + clonePath);
                idReport.AppendLine(" GUID " + oldGuid + " -> " + newGuid);
            }

            int patched = PatchAnimationAssets(copyPaths, replacements);
            patched += PatchPrefab(prefabDest, replacements);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            VerifyNoOldAnimationReferences(prefabDest, originals);

            idReport.AppendLine();
            idReport.AppendLine("RESULT: PASS (no references to original animation assets detected)");
            idReport.AppendLine("Updated serialized/object-curve references: " + patched);
            idReport.AppendLine();
            idReport.AppendLine("SHARED/UNMODIFIED DEPENDENCIES");
            if (warnings.Count == 0) idReport.AppendLine("None reported");
            else foreach (string warning in warnings.Distinct()) idReport.AppendLine(warning);
            idReport.AppendLine("Materials, textures, meshes, models, scripts and packages are NOT cloned.");

            string reportPath = createdFolder + "/GUID_Duplication_Report.txt";
            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            File.WriteAllText(Path.Combine(projectRoot, reportPath), idReport.ToString(), Encoding.UTF8);
            AssetDatabase.ImportAsset(reportPath);
            AssetDatabase.Refresh();

            createdFolder = null; // ここから先は成功扱い。ロールバックしない
            Debug.Log("[Animator Prefab Duplicator] 複製に成功しました。\n" + idReport);
            return prefabDest;
        }
        catch
        {
            if (createdFolder != null)
            {
                // 今回自分で作ったフォルダだけを消す(元データには触れない)
                AssetDatabase.DeleteAsset(createdFolder);
                AssetDatabase.Refresh();
            }
            throw;
        }
    }
}
