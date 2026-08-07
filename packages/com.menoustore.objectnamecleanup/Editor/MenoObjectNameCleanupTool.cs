using UnityEngine;
using UnityEditor;
using System.Text.RegularExpressions;
using System.Collections.Generic;

public class MenoObjectNameCleanupTool : EditorWindow
{
    private Vector2 scrollPos;
    private List<string> changeLogs = new List<string>();
    private int processedCount = 0;

    private bool doRename = true;
    private bool doBounds = true;

    [MenuItem("Meno Tools/Object Name Cleanup")]
    public static void ShowWindow()
    {
        GetWindow<MenoObjectNameCleanupTool>("meno Tools - Object Name Cleanup");
    }

    private void OnGUI()
    {
        GUILayout.Label("meno Tools - Object Name Cleanup", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "・GameObject名から末尾の .数字 を削除します\n" +
            "・SkinnedMeshRendererのlocalBoundsを (0,0,0) / (2,2,2) に設定します\n" +
            "・SkinnedMeshRenderer を持っていないオブジェクトは完全に無視されます",
            MessageType.Info
        );

        GUILayout.Space(10);

        doRename = EditorGUILayout.Toggle("✓ .数字 の削除を実行", doRename);
        doBounds = EditorGUILayout.Toggle("✓ Bounds 設定を実行", doBounds);

        GUILayout.Space(5);
        int potentialCount = GetPotentialRenameCount();
        GUILayout.Label($"対象オブジェクト数（SkinnedMeshRenderer あり）: {potentialCount} 件");

        GUILayout.Space(5);
        GUI.backgroundColor = new Color(1.0f, 0.5f, 0.5f);
        if (GUILayout.Button("⚠ 選択中のオブジェクトを処理", GUILayout.Height(40)))
        {
            RunCleanup();
        }
        GUI.backgroundColor = Color.white;

        GUILayout.Space(10);
        GUILayout.Label("Change Log", EditorStyles.boldLabel);

        scrollPos = GUILayout.BeginScrollView(scrollPos, GUILayout.ExpandHeight(true));
        foreach (var log in changeLogs)
        {
            GUILayout.Label(log);
        }
        GUILayout.EndScrollView();

        GUILayout.Space(5);
        GUILayout.Label($"Total objects processed: {processedCount}");
    }

    private void RunCleanup()
    {
        changeLogs.Clear();
        processedCount = 0;

        var selectedObjects = Selection.gameObjects;
        int validObjectCount = 0;

        foreach (var root in selectedObjects)
        {
            CleanObjectNamesRecursive(root, ref validObjectCount);
        }

        if (validObjectCount == 0)
        {
            changeLogs.Add("⚠ SkinnedMeshRenderer を持つオブジェクトが選択されていません。");
        }
        else if (processedCount == 0)
        {
            changeLogs.Add("処理対象のオブジェクトが見つかりませんでした。");
        }
    }

    private void CleanObjectNamesRecursive(GameObject obj, ref int validObjectCount)
    {
        var smr = obj.GetComponent<SkinnedMeshRenderer>();
        if (smr == null)
        {
            foreach (Transform child in obj.transform)
            {
                CleanObjectNamesRecursive(child.gameObject, ref validObjectCount);
            }
            return;
        }

        validObjectCount++;
        bool changed = false;

        if (doRename)
        {
            string originalName = obj.name;
            string cleanedName = Regex.Replace(originalName, @"\.\d+$", "");

            if (originalName != cleanedName)
            {
                Undo.RecordObject(obj, "Rename GameObject");
                obj.name = cleanedName;
                changeLogs.Add($"✔ Renamed: \"{originalName}\" → \"{cleanedName}\"");
                changed = true;
            }
        }

        if (doBounds)
        {
            Undo.RecordObject(smr, "Set Fixed Bounds");
            smr.localBounds = new Bounds(Vector3.zero, Vector3.one * 2f);
            changeLogs.Add($"   ↳ Bounds set for: {obj.name}");
            changed = true;
        }

        if (changed) processedCount++;

        foreach (Transform child in obj.transform)
        {
            CleanObjectNamesRecursive(child.gameObject, ref validObjectCount);
        }
    }

    private int GetPotentialRenameCount()
    {
        if (!doRename) return 0;

        int count = 0;
        var selectedObjects = Selection.gameObjects;

        foreach (var root in selectedObjects)
        {
            count += CountTargetsRecursive(root);
        }

        return count;
    }

    private int CountTargetsRecursive(GameObject obj)
    {
        int count = 0;

        if (obj.GetComponent<SkinnedMeshRenderer>() != null &&
            Regex.IsMatch(obj.name, @"\.\d+$"))
        {
            count++;
        }

        foreach (Transform child in obj.transform)
        {
            count += CountTargetsRecursive(child.gameObject);
        }

        return count;
    }
}
