// Hierarchy をまとめて折りたたむ/展開する機能。
// Unity Editor内部のSceneHierarchyWindow.SetExpandedRecursiveをリフレクションで呼び出している。
// 内部APIのため、Unityのバージョンによっては動作しない可能性がある(その場合はConsoleに警告を出すだけで例外は投げない)。
using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class MenoHierarchyProjectCollapser
{
    private const string PrefPrefix = "MenouStore.HierarchyVisualizer.Collapse.";
    private const string PrefKeepSceneHeader = PrefPrefix + "KeepSceneHeaderExpanded";
    private const string PrefConfirmBeforeRun = PrefPrefix + "ConfirmBeforeRun";

    internal static bool KeepSceneHeaderExpanded
    {
        get => EditorPrefs.GetBool(PrefKeepSceneHeader, true);
        set => EditorPrefs.SetBool(PrefKeepSceneHeader, value);
    }

    internal static bool ConfirmBeforeRun
    {
        get => EditorPrefs.GetBool(PrefConfirmBeforeRun, false);
        set => EditorPrefs.SetBool(PrefConfirmBeforeRun, value);
    }

    // ---------------- メニュー ----------------

    [MenuItem("GameObject/menou-store/Hierarchyを折りたたむ", false, 0)]
    private static void ContextMenuCollapseHierarchy() => RunCollapseHierarchy();

    [MenuItem("GameObject/menou-store/選択中のみ折りたたむ", false, 1)]
    private static void ContextMenuCollapseSelectionOnly() => CollapseSelectionOnly();

    [MenuItem("Meno Tools/Hierarchy Visualizer/Hierarchyを折りたたむ")]
    private static void MenuCollapseHierarchy() => RunCollapseHierarchy();

    [MenuItem("Meno Tools/Hierarchy Visualizer/Hierarchyを展開する")]
    private static void MenuExpandHierarchy() => ExpandAllHierarchy();

    [MenuItem("Meno Tools/Hierarchy Visualizer/折りたたみ設定...")]
    private static void OpenSettings() => HierarchyCollapseSettingsWindow.Open();

    private static void RunCollapseHierarchy()
    {
        if (ConfirmBeforeRun && !EditorUtility.DisplayDialog("Hierarchy Visualizer", "Hierarchyの表示をすべて折りたたみます。よろしいですか？", "実行", "キャンセル"))
            return;
        CollapseAllHierarchy(KeepSceneHeaderExpanded);
    }

    // ---------------- Hierarchy ----------------

    internal static void CollapseAllHierarchy(bool keepSceneHeaderExpanded)
    {
        var windowType = typeof(EditorWindow).Assembly.GetType("UnityEditor.SceneHierarchyWindow");
        if (windowType == null)
        {
            Debug.LogWarning("[Hierarchy Visualizer] SceneHierarchyWindowが見つかりませんでした。対応していないUnityバージョンの可能性があります。");
            return;
        }

        var window = GetOrOpenWindow(windowType, "Window/General/Hierarchy");
        var setExpandedRecursive = windowType.GetMethod("SetExpandedRecursive", new[] { typeof(int), typeof(bool) });
        if (window == null || setExpandedRecursive == null)
        {
            Debug.LogWarning("[Hierarchy Visualizer] Hierarchyウィンドウの折りたたみ処理を呼び出せませんでした。対応していないUnityバージョンの可能性があります。");
            return;
        }

        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            var scene = SceneManager.GetSceneAt(i);
            if (!scene.isLoaded) continue;

            if (!keepSceneHeaderExpanded)
                InvokeSafely(setExpandedRecursive, window, scene.handle, false, "Scene見出し");

            foreach (var root in scene.GetRootGameObjects())
                InvokeSafely(setExpandedRecursive, window, root.GetInstanceID(), false, root.name);
        }

        window.Repaint();
    }

    internal static void ExpandAllHierarchy()
    {
        var windowType = typeof(EditorWindow).Assembly.GetType("UnityEditor.SceneHierarchyWindow");
        if (windowType == null) return;

        var window = GetOrOpenWindow(windowType, "Window/General/Hierarchy");
        var setExpandedRecursive = windowType.GetMethod("SetExpandedRecursive", new[] { typeof(int), typeof(bool) });
        if (window == null || setExpandedRecursive == null)
        {
            Debug.LogWarning("[Hierarchy Visualizer] Hierarchyウィンドウの展開処理を呼び出せませんでした。対応していないUnityバージョンの可能性があります。");
            return;
        }

        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            var scene = SceneManager.GetSceneAt(i);
            if (!scene.isLoaded) continue;

            InvokeSafely(setExpandedRecursive, window, scene.handle, true, "Scene見出し");
            foreach (var root in scene.GetRootGameObjects())
                InvokeSafely(setExpandedRecursive, window, root.GetInstanceID(), true, root.name);
        }

        window.Repaint();
    }

    // 右クリックした(選択中の)オブジェクトだけを折りたたむ。全体には影響しない。
    internal static void CollapseSelectionOnly()
    {
        var selected = Selection.gameObjects;
        if (selected == null || selected.Length == 0) return;

        var windowType = typeof(EditorWindow).Assembly.GetType("UnityEditor.SceneHierarchyWindow");
        var window = windowType != null ? GetOrOpenWindow(windowType, "Window/General/Hierarchy") : null;
        var setExpandedRecursive = windowType?.GetMethod("SetExpandedRecursive", new[] { typeof(int), typeof(bool) });
        if (window == null || setExpandedRecursive == null)
        {
            Debug.LogWarning("[Hierarchy Visualizer] Hierarchyウィンドウの折りたたみ処理を呼び出せませんでした。対応していないUnityバージョンの可能性があります。");
            return;
        }

        foreach (var go in selected)
            InvokeSafely(setExpandedRecursive, window, go.GetInstanceID(), false, go.name);

        window.Repaint();
    }

    // ---------------- 共通ヘルパー ----------------

    private static EditorWindow GetOrOpenWindow(Type windowType, string openMenuPath)
    {
        var existing = Resources.FindObjectsOfTypeAll(windowType) as EditorWindow[];
        if (existing != null && existing.Length > 0) return existing[0];

        EditorApplication.ExecuteMenuItem(openMenuPath);
        existing = Resources.FindObjectsOfTypeAll(windowType) as EditorWindow[];
        return existing != null && existing.Length > 0 ? existing[0] : null;
    }

    private static void InvokeSafely(MethodInfo method, object target, int id, bool expand, string label)
    {
        try
        {
            method.Invoke(target, new object[] { id, expand });
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[Hierarchy Visualizer] {label} の折りたたみに失敗: {e.Message}");
        }
    }
}

internal sealed class HierarchyCollapseSettingsWindow : EditorWindow
{
    public static void Open()
    {
        var window = GetWindow<HierarchyCollapseSettingsWindow>(true, "Hierarchy Visualizer 折りたたみ設定");
        window.minSize = new Vector2(420, 160);
    }

    private void OnGUI()
    {
        EditorGUILayout.Space(6);
        EditorGUILayout.LabelField("表示を残す項目", EditorStyles.boldLabel);
        MenoHierarchyProjectCollapser.KeepSceneHeaderExpanded = EditorGUILayout.ToggleLeft(
            "Hierarchy の Scene 見出しは開いたまま残す",
            MenoHierarchyProjectCollapser.KeepSceneHeaderExpanded);

        EditorGUILayout.Space(10);
        EditorGUILayout.LabelField("実行時の確認", EditorStyles.boldLabel);
        MenoHierarchyProjectCollapser.ConfirmBeforeRun = EditorGUILayout.ToggleLeft(
            "実行前に確認ダイアログを表示する",
            MenoHierarchyProjectCollapser.ConfirmBeforeRun);
    }
}
