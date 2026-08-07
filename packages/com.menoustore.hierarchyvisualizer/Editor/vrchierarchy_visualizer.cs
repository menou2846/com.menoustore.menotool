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

    private static bool ConstraintHasEmptySource(Component c, out int emptyIndex)
    {
        emptyIndex = -1;
        if (c == null || !IsVRCConstraint(c)) return false;
        try
        {
            var so = new SerializedObject(c);
            so.UpdateIfRequiredOrScript();
            // VRC系/Unity系の両方に対応（"sources" or "m_Sources"）
            var sources = so.FindProperty("sources") ?? so.FindProperty("m_Sources");
            if (sources == null || !sources.isArray) return false;

            if (sources.arraySize == 0)
            {
                emptyIndex = -2; // 完全に空
                return true;
            }

            for (int i = 0; i < sources.arraySize; i++)
            {
                var element = sources.GetArrayElementAtIndex(i);
                // フィールド名の差異にできるだけ対応
                var sourceTransform = element.FindPropertyRelative("sourceTransform")
                                      ?? element.FindPropertyRelative("m_SourceTransform");
                var weight = element.FindPropertyRelative("weight")
                             ?? element.FindPropertyRelative("m_Weight");

                bool noTransform = (sourceTransform == null) || (sourceTransform.objectReferenceValue == null);
                bool zeroWeight = (weight != null && Mathf.Approximately(weight.floatValue, 0f));
                // 「空」の定義: Transform が None もしくは Weight==0
                if (noTransform || zeroWeight)
                {
                    emptyIndex = i;
                    return true;
                }
            }
        }
        catch (System.Exception ex)
        {
            if (EnableVerboseLogging)
                Debug.LogError($"[VRC Constraint Check] Exception on {c?.GetType().Name}: {ex.Message}");
        }
        return false;
    }

    // 複数空ソースの件数カウント版
    private static int CountEmptyConstraintSources(Component c, out int total)
    {
        total = 0;
        int empties = 0;
        if (c == null || !IsVRCConstraint(c)) return 0;
        try
        {
            var so = new SerializedObject(c);
            so.UpdateIfRequiredOrScript();
            var sources = so.FindProperty("sources") ?? so.FindProperty("m_Sources");
            if (sources == null || !sources.isArray) return 0;
            total = sources.arraySize;
            for (int i = 0; i < sources.arraySize; i++)
            {
                var element = sources.GetArrayElementAtIndex(i);
                var sourceTransform = element.FindPropertyRelative("sourceTransform")
                                      ?? element.FindPropertyRelative("m_SourceTransform");
                var weight = element.FindPropertyRelative("weight")
                             ?? element.FindPropertyRelative("m_Weight");
                bool noTransform = (sourceTransform == null) || (sourceTransform.objectReferenceValue == null);
                bool zeroWeight = (weight != null && Mathf.Approximately(weight.floatValue, 0f));
                if (noTransform || zeroWeight) empties++;
            }
        }
        catch (System.Exception ex)
        {
            if (EnableVerboseLogging)
                Debug.LogError($"[VRC Constraint Count] Exception on {c?.GetType().Name}: {ex.Message}");
        }
        return empties;
    }

    private static void DrawWarningIcon(Rect selectionRect, string objectName, int index)
    {
        EditorGUI.DrawRect(selectionRect, new Color(1f, 0.3f, 0.3f, 0.6f));
        var warningRect = new Rect(selectionRect.x + 4, selectionRect.y, 16, ICON_SIZE);
        EditorGUI.LabelField(warningRect, "❗");

        Debug.LogWarning($"[VRC Constraint Check] {objectName} has empty source at index {index}.");
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
        
        // まず VRC Constraint の空ソース件数を集計（複数対応）
        int totalEmptySources = 0;
        int totalSources = 0;
        foreach (var c in components)
        {
            if (!IsVRCConstraint(c)) continue;
            totalEmptySources += CountEmptyConstraintSources(c, out int t);
            totalSources += t;
        }

        // VRC Constraint の空ソース検出をより堅牢に（最初の空のインデックス検出）
        bool hasEmptyVRCConstraint = false;
        foreach (var c in components)
        {
            if (!IsVRCConstraint(c)) continue;
            if (ConstraintHasEmptySource(c, out int idx))
            {
                DrawWarningIcon(selectionRect, c.gameObject.name, idx);
                hasEmptyVRCConstraint = true;
            }
        }

        bool hasAnyVRCConstraint = components.Any(IsVRCConstraint);

        Color? bgColor = GetBackgroundColor(gameObject, components, hasEmptyVRCConstraint, hasAnyVRCConstraint);

        if (bgColor.HasValue)
        {
            EditorGUI.DrawRect(selectionRect, bgColor.Value);
        }

        // 空ソースの件数バッジ（複数空を視覚化）
        if (totalEmptySources > 0)
        {
            var label = totalSources > 0 ? $"{totalEmptySources}/{totalSources}" : totalEmptySources.ToString();
            var badgeRect = new Rect(selectionRect.x + 20, selectionRect.y, 40, ICON_SIZE);
            var style = new GUIStyle(EditorStyles.miniBoldLabel) { alignment = TextAnchor.MiddleLeft };
            EditorGUI.LabelField(badgeRect, label, style); // 例: "3/16"
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
