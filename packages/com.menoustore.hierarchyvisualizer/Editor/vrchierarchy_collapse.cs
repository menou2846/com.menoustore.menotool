// Hierarchy / Project をまとめて折りたたむ機能。
// Unity Editor内部のTreeView実装(SceneHierarchyWindow / ProjectBrowser)をリフレクションで呼び出している。
// 内部APIのため、Unityのバージョンによっては動作しない可能性がある(その場合はConsoleに警告を出すだけで例外は投げない)。
using System;
using System.Reflection;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class MenoHierarchyProjectCollapser
{
    private const string PrefPrefix = "MenouStore.HierarchyVisualizer.Collapse.";
    private const string PrefKeepSceneHeader = PrefPrefix + "KeepSceneHeaderExpanded";
    private const string PrefKeepAssetsPackages = PrefPrefix + "KeepAssetsPackagesExpanded";
    private const string PrefConfirmBeforeRun = PrefPrefix + "ConfirmBeforeRun";
    private const string PrefToolsMenuTarget = PrefPrefix + "ToolsMenuTarget";

    internal enum ToolsMenuTarget { Hierarchy = 0, Project = 1, Both = 2 }

    internal static bool KeepSceneHeaderExpanded
    {
        get => EditorPrefs.GetBool(PrefKeepSceneHeader, true);
        set => EditorPrefs.SetBool(PrefKeepSceneHeader, value);
    }

    internal static bool KeepAssetsPackagesExpanded
    {
        get => EditorPrefs.GetBool(PrefKeepAssetsPackages, true);
        set => EditorPrefs.SetBool(PrefKeepAssetsPackages, value);
    }

    internal static bool ConfirmBeforeRun
    {
        get => EditorPrefs.GetBool(PrefConfirmBeforeRun, false);
        set => EditorPrefs.SetBool(PrefConfirmBeforeRun, value);
    }

    internal static ToolsMenuTarget MenuTarget
    {
        get => (ToolsMenuTarget)EditorPrefs.GetInt(PrefToolsMenuTarget, (int)ToolsMenuTarget.Both);
        set => EditorPrefs.SetInt(PrefToolsMenuTarget, (int)value);
    }

    // ---------------- メニュー ----------------

    [MenuItem("GameObject/menou-store/Hierarchyを折りたたむ", false, 0)]
    private static void ContextMenuCollapseHierarchy() => RunCollapseHierarchy();

    [MenuItem("Assets/menou-store/Projectフォルダを折りたたむ", false, 0)]
    private static void ContextMenuCollapseProject() => RunCollapseProject();

    [MenuItem("GameObject/menou-store/選択中のみ折りたたむ", false, 1)]
    private static void ContextMenuCollapseSelectionOnly() => CollapseSelectionOnly();

    [MenuItem("Meno Tools/Hierarchy Visualizer/Hierarchyを折りたたむ")]
    private static void MenuCollapseHierarchy() => RunCollapseHierarchy();

    [MenuItem("Meno Tools/Hierarchy Visualizer/Projectフォルダを折りたたむ")]
    private static void MenuCollapseProject() => RunCollapseProject();

    [MenuItem("Meno Tools/Hierarchy Visualizer/Hierarchyを展開する")]
    private static void MenuExpandHierarchy() => ExpandAllHierarchy();

    [MenuItem("Meno Tools/Hierarchy Visualizer/Projectフォルダを展開する")]
    private static void MenuExpandProject() => ExpandAllProjectFolders();

    [MenuItem("Meno Tools/Hierarchy Visualizer/設定に従ってまとめて折りたたむ")]
    private static void MenuCollapseByTarget()
    {
        var target = MenuTarget;
        string message = target switch
        {
            ToolsMenuTarget.Hierarchy => "Hierarchyの表示をすべて折りたたみます。よろしいですか？",
            ToolsMenuTarget.Project => "Projectのフォルダをすべて折りたたみます。よろしいですか？",
            _ => "HierarchyとProjectの表示をすべて折りたたみます。よろしいですか？",
        };
        if (ConfirmBeforeRun && !EditorUtility.DisplayDialog("Hierarchy Visualizer", message, "実行", "キャンセル"))
            return;

        if (target == ToolsMenuTarget.Hierarchy || target == ToolsMenuTarget.Both)
            CollapseAllHierarchy(KeepSceneHeaderExpanded);
        if (target == ToolsMenuTarget.Project || target == ToolsMenuTarget.Both)
            CollapseAllProjectFolders(KeepAssetsPackagesExpanded);
    }

    [MenuItem("Meno Tools/Hierarchy Visualizer/折りたたみ設定...")]
    private static void OpenSettings() => HierarchyCollapseSettingsWindow.Open();

    private static void RunCollapseHierarchy()
    {
        if (ConfirmBeforeRun && !EditorUtility.DisplayDialog("Hierarchy Visualizer", "Hierarchyの表示をすべて折りたたみます。よろしいですか？", "実行", "キャンセル"))
            return;
        CollapseAllHierarchy(KeepSceneHeaderExpanded);
    }

    private static void RunCollapseProject()
    {
        if (ConfirmBeforeRun && !EditorUtility.DisplayDialog("Hierarchy Visualizer", "Projectのフォルダをすべて折りたたみます。よろしいですか？", "実行", "キャンセル"))
            return;
        CollapseAllProjectFolders(KeepAssetsPackagesExpanded);
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

    // ---------------- Project ----------------

    internal static void CollapseAllProjectFolders(bool keepAssetsAndPackagesExpanded)
    {
        var browserType = typeof(Editor).Assembly.GetType("UnityEditor.ProjectBrowser");
        if (browserType == null)
        {
            Debug.LogWarning("[Hierarchy Visualizer] ProjectBrowserが見つかりませんでした。対応していないUnityバージョンの可能性があります。");
            return;
        }

        var browser = GetOrOpenWindow(browserType, "Window/General/Project");
        if (browser == null)
        {
            Debug.LogWarning("[Hierarchy Visualizer] Projectウィンドウを取得できませんでした。");
            return;
        }

        // Two Column Layout(既定)は m_FolderTree、One Column Layoutは m_AssetTree を使う。
        object treeView = GetInstanceField(browser, browserType, "m_FolderTree")
                        ?? GetInstanceField(browser, browserType, "m_AssetTree");
        if (treeView == null)
        {
            Debug.LogWarning("[Hierarchy Visualizer] Projectウィンドウのフォルダツリーが取得できませんでした。対応していないUnityバージョンの可能性があります。");
            return;
        }

        if (!TryCollapseAll(treeView))
        {
            Debug.LogWarning("[Hierarchy Visualizer] Projectフォルダの折りたたみに失敗しました。対応していないUnityバージョンの可能性があります。");
            return;
        }

        if (keepAssetsAndPackagesExpanded)
        {
            if (!TryExpandTopLevel(treeView))
                Debug.LogWarning("[Hierarchy Visualizer] 「Assets / Packagesを開いたまま残す」設定はこのUnityバージョンでは反映できませんでした(フォルダの折りたたみ自体は実行済みです)。");
        }

        browser.Repaint();
    }

    internal static void ExpandAllProjectFolders()
    {
        var browserType = typeof(Editor).Assembly.GetType("UnityEditor.ProjectBrowser");
        var browser = browserType != null ? GetOrOpenWindow(browserType, "Window/General/Project") : null;
        object treeView = browser != null
            ? GetInstanceField(browser, browserType, "m_FolderTree") ?? GetInstanceField(browser, browserType, "m_AssetTree")
            : null;
        if (treeView == null)
        {
            Debug.LogWarning("[Hierarchy Visualizer] Projectウィンドウのフォルダツリーが取得できませんでした。対応していないUnityバージョンの可能性があります。");
            return;
        }

        if (!TryInvokeParameterless(treeView, "ExpandAll"))
            Debug.LogWarning("[Hierarchy Visualizer] Projectフォルダの展開に失敗しました。対応していないUnityバージョンの可能性があります。");

        browser.Repaint();
    }

    private static bool TryCollapseAll(object treeView) => TryInvokeParameterless(treeView, "CollapseAll");

    private static bool TryInvokeParameterless(object treeView, string methodName)
    {
        try
        {
            var method = treeView.GetType().GetMethod(methodName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, null, Type.EmptyTypes, null);
            if (method == null) return false;
            method.Invoke(treeView, null);
            return true;
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[Hierarchy Visualizer] {methodName}呼び出しで例外: {e.Message}");
            return false;
        }
    }

    // Assets / Packages(ルート直下の項目)だけ展開し直す。内部TreeView構造への依存度が高いためbest-effort。
    private static bool TryExpandTopLevel(object treeView)
    {
        try
        {
            var treeViewType = treeView.GetType();
            var dataProperty = treeViewType.GetProperty("data", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            var data = dataProperty?.GetValue(treeView);
            if (data == null) return false;

            var rootProperty = data.GetType().GetProperty("root", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            var root = rootProperty?.GetValue(data) as TreeViewItem;
            if (root == null || !root.hasChildren) return false;

            var setExpanded = data.GetType().GetMethod("SetExpanded", new[] { typeof(TreeViewItem), typeof(bool) })
                            ?? data.GetType().GetMethod("SetExpanded", new[] { typeof(int), typeof(bool) });
            if (setExpanded == null) return false;

            bool anyExpanded = false;
            foreach (TreeViewItem child in root.children)
            {
                if (child == null) continue;
                object[] args = setExpanded.GetParameters()[0].ParameterType == typeof(int)
                    ? new object[] { child.id, true }
                    : new object[] { child, true };
                setExpanded.Invoke(data, args);
                anyExpanded = true;
            }
            return anyExpanded;
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[Hierarchy Visualizer] Assets/Packagesの再展開で例外: {e.Message}");
            return false;
        }
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

    private static object GetInstanceField(object instance, Type type, string fieldName)
    {
        var field = type.GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance);
        return field?.GetValue(instance);
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
        window.minSize = new Vector2(420, 220);
    }

    private void OnGUI()
    {
        EditorGUILayout.Space(6);
        EditorGUILayout.LabelField("Toolsメニュー「設定に従ってまとめて折りたたむ」の対象", EditorStyles.boldLabel);
        MenoHierarchyProjectCollapser.MenuTarget =
            (MenoHierarchyProjectCollapser.ToolsMenuTarget)EditorGUILayout.EnumPopup(
                MenoHierarchyProjectCollapser.MenuTarget);

        EditorGUILayout.Space(10);
        EditorGUILayout.LabelField("表示を残す項目", EditorStyles.boldLabel);
        MenoHierarchyProjectCollapser.KeepSceneHeaderExpanded = EditorGUILayout.ToggleLeft(
            "Hierarchy の Scene 見出しは開いたまま残す",
            MenoHierarchyProjectCollapser.KeepSceneHeaderExpanded);
        MenoHierarchyProjectCollapser.KeepAssetsPackagesExpanded = EditorGUILayout.ToggleLeft(
            "Project の Assets / Packages は開いたまま残す",
            MenoHierarchyProjectCollapser.KeepAssetsPackagesExpanded);

        EditorGUILayout.Space(10);
        EditorGUILayout.LabelField("実行時の確認", EditorStyles.boldLabel);
        MenoHierarchyProjectCollapser.ConfirmBeforeRun = EditorGUILayout.ToggleLeft(
            "実行前に確認ダイアログを表示する",
            MenoHierarchyProjectCollapser.ConfirmBeforeRun);

        EditorGUILayout.Space(12);
        EditorGUILayout.HelpBox(
            "Projectフォルダの折りたたみはUnity Editor内部の非公開APIを利用しています。Unityのバージョンによっては" +
            "「Assets / Packagesを開いたまま残す」設定が効かない場合がありますが、その場合もConsoleに警告が出るだけで、" +
            "折りたたみ自体は実行されます。",
            MessageType.Info);
    }
}
