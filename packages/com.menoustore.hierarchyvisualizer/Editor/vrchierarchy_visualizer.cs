using UnityEngine;
using System.Collections.Generic;
using UnityEditor;
using System.Linq;
using VRC.SDK3.Dynamics.PhysBone.Components; // VRCPhysBone
using UnityEngine.Animations; // Unity制約
using VRC.SDK3.Avatars.Components; // VRC Constraint群
using System.Text.RegularExpressions;

[InitializeOnLoad]
public static class VRCHierarchyVisualizer
{
    private const int ICON_SIZE = 16;
    // 行の背景色の濃さ。文字の上に重ねて描くので、濃いとヒエラルキーが読めなくなる。0〜1で調整。
    private const float ColorAlphaScale = 0.45f;
    private static readonly HashSet<GameObject> loggedObjects = new HashSet<GameObject>();
    private static readonly HashSet<string> VRCConstraintNames = new HashSet<string>
    {
        "VRCAimConstraint",
        "VRCLookAtConstraint",
        "VRCParentConstraint",
        "VRCPositionConstraint",
        "VRCRotationConstraint",
        "VRCScaleConstraint"
    };

    private static readonly bool EnableVerboseLogging = false;

    static VRCHierarchyVisualizer()
    {
        EditorApplication.hierarchyWindowItemOnGUI += OnGUI;
        EditorApplication.update += () => loggedObjects.Clear();
    }

    private static bool IsVRCConstraint(Component c) => c != null && VRCConstraintNames.Contains(c.GetType().Name);

    // VRC Constraint のソースは配列ではなく固定スロット: Sources.source0〜15.SourceTransform、有効数は Sources.totalLength。
    // (Unity 2022.3 / SDK 3.10.3 で実機確認。シリアライズ名は大文字始まり)
    // 読めなければ false。total=ソース数、empty=そのうちTransformがNoneの数。
    private static bool ReadConstraintSources(Component c, out int total, out int empty)
    {
        total = 0;
        empty = 0;
        if (c == null || !IsVRCConstraint(c)) return false;
        try
        {
            var so = new SerializedObject(c);
            var len = so.FindProperty("Sources.totalLength");
            if (len == null) return false;
            total = len.intValue;
            for (int i = 0; i < total && i < 16; i++)
            {
                var src = so.FindProperty($"Sources.source{i}.SourceTransform");
                if (src == null) return false;
                if (src.objectReferenceValue == null) empty++;
            }
            return true;
        }
        catch (System.Exception ex)
        {
            if (EnableVerboseLogging)
                Debug.LogError($"[VRC Constraint Check] Exception on {c.GetType().Name}: {ex.Message}");
            return false;
        }
    }

    private static Color? GetBackgroundColor(GameObject gameObject, Component[] components, bool hasEmptyVRCConstraint, bool hasAnyVRCConstraint)
    {
        var physBones = components.OfType<VRCPhysBone>().ToArray();
        bool hasPhysBoneWithoutRoot = physBones.Any(pb => pb != null && pb.rootTransform == null);

        foreach (var pb in physBones)
        {
            if (pb.rootTransform == null && !loggedObjects.Contains(pb.gameObject))
            {
                Debug.LogWarning($"[PhysBone Check] {pb.gameObject.name} has a VRCPhysBone without rootTransform.");
                loggedObjects.Add(pb.gameObject);
            }
        }

        // SkinnedMeshRenderer の名前チェック & bounds チェック（名前優先）
        var smr = components.OfType<SkinnedMeshRenderer>().FirstOrDefault();
        if (smr != null)
        {
            // 1) 名前に .001, .002 などが含まれていたら紫で警告
            if (Regex.IsMatch(smr.name, @"\.+\d{3}$") || Regex.IsMatch(smr.name, @"\.\d{3}$"))
            {
                if (!loggedObjects.Contains(smr.gameObject))
                {
                    Debug.LogWarning($"[Name Check] SkinnedMeshRenderer {smr.name} has a duplicate-style name.");
                    loggedObjects.Add(smr.gameObject);
                }
                return new Color(0.6f, 0.4f, 1f, 0.4f); // 紫
            }

            // 2) bounds の妥当性（center==0, extents==(1,1,1)）
            var center = smr.localBounds.center;
            var extents = smr.localBounds.extents;
            bool isCenterCorrect = center == Vector3.zero;
            bool isExtentsCorrect = extents == new Vector3(1f, 1f, 1f);

            if (!isCenterCorrect || !isExtentsCorrect)
            {
                if (!loggedObjects.Contains(gameObject))
                {
                    Debug.LogWarning($"[Bounds Check] {gameObject.name} has non-default bounds. center: {center}, extents: {extents}");
                    loggedObjects.Add(gameObject);
                }
                return new Color(1f, 0.6f, 0.2f, 0.4f); // オレンジ
            }
        }

        if (!gameObject.activeInHierarchy)
            return new Color(0.5f, 0.5f, 0.5f, 0.3f);
        // PhysBoneCollider（黄色）
        if (components.Any(c => c is VRCPhysBoneCollider))
            return new Color(1f, 1f, 0.4f, 0.3f);
        // PhysBone root 未設定（赤ピンク）
        if (hasPhysBoneWithoutRoot)
            return new Color(1f, 0.5f, 0.5f, 0.4f);
        // ★ ここで VRC Constraint の空ソース（オレンジ）を PhysBone 緑より優先
        if (hasEmptyVRCConstraint)
            return new Color(1f, 0.7f, 0.2f, 0.4f);
        // 何かしら VRC Constraint がある（紫）
        if (hasAnyVRCConstraint)
            return new Color(0.8f, 0.4f, 1f, 0.3f);
        // PhysBone がある（緑）
        if (physBones.Length > 0)
            return new Color(0.3f, 1f, 0.3f, 0.3f);
        // Unity 制約（シアン）
        if (components.Any(c => c is ParentConstraint || c is PositionConstraint || c is RotationConstraint || c is ScaleConstraint))
            return new Color(0.4f, 1f, 1f, 0.3f);

        // 手動タグ色
        if (gameObject.name.StartsWith("[Red]"))
            return new Color(1f, 0.3f, 0.3f, 0.3f);
        if (gameObject.name.StartsWith("[Green]"))
            return new Color(0.3f, 1f, 0.3f, 0.3f);
        if (gameObject.name.StartsWith("[Blue]"))
            return new Color(0.3f, 0.3f, 1f, 0.3f);

        return null;
    }

    private static void OnGUI(int instanceID, Rect selectionRect)
    {
        var gameObject = EditorUtility.InstanceIDToObject(instanceID) as GameObject;
        if (gameObject == null) return;

        var components = gameObject.GetComponents<Component>();
        
        // VRC Constraint のソース有無(色分け用)。ソース0個、またはTransformがNoneのソースがあれば「ソースなし」扱い。
        bool hasAnyVRCConstraint = false;
        bool hasEmptyVRCConstraint = false;
        foreach (var c in components)
        {
            if (!IsVRCConstraint(c)) continue;
            hasAnyVRCConstraint = true;
            if (ReadConstraintSources(c, out int total, out int empty) && (total == 0 || empty > 0))
                hasEmptyVRCConstraint = true;
        }

        Color? bgColor = GetBackgroundColor(gameObject, components, hasEmptyVRCConstraint, hasAnyVRCConstraint);

        if (bgColor.HasValue)
        {
            var c = bgColor.Value;
            c.a *= ColorAlphaScale;
            EditorGUI.DrawRect(selectionRect, c);
        }

        // アイコン描画（Transform除外）
        var iconComponents = components.Where(c => !(c is Transform)).ToArray();
        if (iconComponents.Length > 0)
        {
            Rect iconRect = selectionRect;
            iconRect.x = selectionRect.xMax - ICON_SIZE * iconComponents.Length;
            iconRect.width = ICON_SIZE;

            foreach (var component in iconComponents)
            {
                if (component == null) continue;
                var icon = AssetPreview.GetMiniThumbnail(component);
                if (icon != null)
                {
                    GUI.DrawTexture(iconRect, icon);
                    iconRect.x += ICON_SIZE;
                }
            }
        }
    }
}
