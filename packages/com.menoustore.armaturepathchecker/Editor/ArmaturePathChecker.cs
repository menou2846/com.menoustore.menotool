using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.Linq;

public class ArmaturePathChecker : EditorWindow
{
    private GameObject prefabA;
    private GameObject prefabB;

    private Vector2 scroll;
private List<string> boneNameList = new();
private string currentPreset = "default";
private const string Bone_PREFS_KEY_PREFIX = "VRCComparer_BoneNames_";
private string newBoneName = "";

    private class CompareResult
    {
        public bool foldoutA = true;
        public bool foldoutB = true;
        public string name;
        public string status;
        public string detailA;
        public string detailB;
        public Color rowColor = Color.white;
    }

    private List<CompareResult> results = new();

    [MenuItem("Meno Tools/Armature Path Checker")]
    public static void ShowWindow()
    {
        GetWindow<ArmaturePathChecker>("Armature Path Checker");
    }

    void OnGUI()
    {
        GUILayout.Label("アーマチュア構造比較", EditorStyles.boldLabel);
        prefabA = (GameObject)EditorGUILayout.ObjectField("Prefab A", prefabA, typeof(GameObject), false);
        prefabB = (GameObject)EditorGUILayout.ObjectField("Prefab B", prefabB, typeof(GameObject), false);

        if (prefabA && prefabB && GUILayout.Button("比較実行"))
        {
            RunComparison();
        }

        GUILayout.Space(10);
        GUILayout.Space(10);

        GUILayout.Label("比較したいボーン名チェック", EditorStyles.boldLabel);
        var allPresets = EditorPrefs.GetString("VRCComparer_PresetList", "default").Split('|').Distinct().Where(p => !string.IsNullOrWhiteSpace(p)).ToList();
        if (allPresets.Count > 0)
        {
            int currentIndex = Mathf.Max(0, allPresets.IndexOf(currentPreset));
            int selected = EditorGUILayout.Popup("プリセット一覧", currentIndex, allPresets.ToArray());
            if (selected != currentIndex)
        {
            currentPreset = allPresets[selected];
            LoadBoneNames();
        }

        GUILayout.BeginHorizontal();
        if (GUILayout.Button("🗑️ 削除", GUILayout.Width(60)))
        {
            if (EditorUtility.DisplayDialog("プリセット削除", $"プリセット '{currentPreset}' を削除しますか？", "削除", "キャンセル"))
            {
                EditorPrefs.DeleteKey(Bone_PREFS_KEY_PREFIX + currentPreset);
                allPresets.Remove(currentPreset);
                EditorPrefs.SetString("VRCComparer_PresetList", string.Join("|", allPresets));
                currentPreset = allPresets.FirstOrDefault() ?? "default";
                LoadBoneNames();
            }
        }
        if (GUILayout.Button("📄 複製", GUILayout.Width(60)))
        {
            string copyName = currentPreset + "_copy";
            int counter = 1;
            while (EditorPrefs.HasKey(Bone_PREFS_KEY_PREFIX + copyName))
                copyName = currentPreset + "_copy" + counter++;
            EditorPrefs.SetString(Bone_PREFS_KEY_PREFIX + copyName, string.Join("|", boneNameList));
            allPresets.Add(copyName);
            EditorPrefs.SetString("VRCComparer_PresetList", string.Join("|", allPresets));
            currentPreset = copyName;
            LoadBoneNames();
        }
        GUILayout.EndHorizontal();
        }
        GUILayout.BeginHorizontal();
        currentPreset = EditorGUILayout.TextField("プリセット名", currentPreset);
        if (GUILayout.Button("📥 読込", GUILayout.Width(60))) LoadBoneNames();
        if (GUILayout.Button("💾 保存", GUILayout.Width(60))) SaveBoneNames();
        GUILayout.EndHorizontal();

        newBoneName = EditorGUILayout.TextField("追加ボーン名", newBoneName);
        if (GUILayout.Button("↑ リファレンスに追加") && !string.IsNullOrWhiteSpace(newBoneName))
        {
            boneNameList.Add(newBoneName.Trim());
            SaveBoneNames();
            newBoneName = "";
        }

        if (boneNameList.Count > 0)
        {
            GUILayout.Label("現在の比較対象ボーン名:");
            for (int i = 0; i < boneNameList.Count; i++)
            {
                EditorGUILayout.BeginHorizontal();
                GUILayout.Label("• " + boneNameList[i], EditorStyles.helpBox);
                if (GUILayout.Button("×", GUILayout.Width(20)))
                {
                    boneNameList.RemoveAt(i);
                    SaveBoneNames();
                    break;
                }
                EditorGUILayout.EndHorizontal();
            }
        }

        DrawResults();
    }

    void RunComparison()
    {
        results.Clear();

        var aTransforms = prefabA.GetComponentsInChildren<Transform>(true);
        var bTransforms = prefabB.GetComponentsInChildren<Transform>(true);

        var aMap = aTransforms.ToDictionary(t => GetPath(t, prefabA.transform));
        var bMap = bTransforms.ToDictionary(t => GetPath(t, prefabB.transform));

        var allPaths = new HashSet<string>(aMap.Keys.Concat(bMap.Keys));
        // すべてのパス比較は無視（処理なし）

        // ボーン名チェック
        foreach (var boneName in boneNameList)
        {
            var result = new CompareResult { name = boneName + "（ボーン名チェック）" };
            result.foldoutA = false;
            result.foldoutB = false;
            var ta = aTransforms.FirstOrDefault(t => t.name == boneName);
            var tb = bTransforms.FirstOrDefault(t => t.name == boneName);

            string aPath = ta != null ? GetPath(ta, prefabA.transform) : null;
            string bPath = tb != null ? GetPath(tb, prefabB.transform) : null;

            result.detailA = aPath ?? "-";
            result.detailB = bPath ?? "-";

            if (aPath != null && bPath != null)
            {
                result.status = aPath == bPath ? "◎ 一致" : "⚠ パス不一致";
                if (aPath == bPath)
                {
                    result.foldoutA = false;
                    result.foldoutB = false;
                }
                else
                {
                    result.foldoutA = true;
                    result.foldoutB = true;
                    result.rowColor = new Color(1f, 1f, 0.8f);
                }
                if (aPath != bPath)
                    result.rowColor = new Color(1f, 1f, 0.8f);
            }
            else if (aPath != null)
            {
                result.status = "❌ Bに欠落（PB）";
                result.rowColor = new Color(1f, 0.85f, 0.85f);
            }
            else if (bPath != null)
            {
                result.status = "❌ Aに欠落（PB）";
                result.rowColor = new Color(1f, 0.85f, 0.85f);
            }
            else
            {
                result.status = "❌ 両方に存在しない（PB）";
                result.rowColor = new Color(1f, 0.6f, 0.6f);
            }

            results.Add(result);
        }

        results = results.OrderBy(r => r.name).ToList();
    }

    void DrawResults()
    {
        if (results.Count == 0) return;

        scroll = EditorGUILayout.BeginScrollView(scroll);

        foreach (var r in results)
        {
            Color bg = r.rowColor;
            GUI.backgroundColor = bg;
            GUILayout.BeginVertical(GUI.skin.box);
            GUI.backgroundColor = Color.white;

            GUIStyle headerStyle = new GUIStyle(EditorStyles.boldLabel);
            if (r.status.Contains("差分あり") || r.status.Contains("パス不一致"))
                headerStyle.normal.textColor = new Color(0.9f, 0.5f, 0.1f);
            else if (r.status.Contains("Aに欠落") || r.status.Contains("Bに欠落") || r.status.Contains("存在しない"))
                headerStyle.normal.textColor = new Color(0.7f, 0.1f, 0.1f);

            GUILayout.BeginHorizontal();
            GUILayout.Label(r.name, headerStyle, GUILayout.Width(position.width * 0.4f));
            GUILayout.Label(r.status, headerStyle);
            GUILayout.EndHorizontal();

            GUILayout.Space(2);
            EditorGUILayout.BeginHorizontal();

            EditorGUILayout.BeginVertical("box", GUILayout.Width(position.width / 2 - 20));
            GUILayout.Label("A", EditorStyles.boldLabel);
            GUIStyle aStyle = new GUIStyle(EditorStyles.label);
            aStyle.richText = true;
            aStyle.normal.textColor = r.status.Contains("Aに欠落") ? Color.red : Color.black;
            r.foldoutA = EditorGUILayout.Foldout(r.foldoutA, "Aの構造", true);
            if (r.foldoutA)
                GUILayout.Label(GetIndentedHierarchy(r.detailA), aStyle);
            EditorGUILayout.EndVertical();

            EditorGUILayout.BeginVertical("box", GUILayout.Width(position.width / 2 - 20));
            GUILayout.Label("B", EditorStyles.boldLabel);
            GUIStyle bStyle = new GUIStyle(EditorStyles.label);
            bStyle.richText = true;
            bStyle.normal.textColor = r.status.Contains("Bに欠落") ? Color.red : Color.black;
            r.foldoutB = EditorGUILayout.Foldout(r.foldoutB, "Bの構造", true);
            if (r.foldoutB)
                GUILayout.Label(GetIndentedHierarchy(r.detailB), bStyle);
            EditorGUILayout.EndVertical();

            EditorGUILayout.EndHorizontal();
            GUILayout.EndVertical();
            GUILayout.Space(5);
        }

        EditorGUILayout.EndScrollView();
    }

    string GetPath(Transform t, Transform root)
    {
        if (t == root) return "";
        var path = t.name;
        while (t.parent != null && t.parent != root)
        {
            t = t.parent;
            path = t.name + "/" + path;
        }
        return path;
    }

    string GetIndentedName(string path)
    {
        string[] parts = path.Split('/');
        int depth = parts.Length - 1;

        if (depth == 0)
            return $"■ {parts[0]}";

        string indent = "";
        for (int i = 0; i < depth - 1; i++)
            indent += "│  ";
        indent += "└─ ";

        return indent + parts.Last();
    }

    string GetIndentedHierarchy(string path)
    {
        if (string.IsNullOrEmpty(path) || path == "-") return "-";
        string[] parts = path.Split('/');
        string output = "";

        // 対象の比較結果を取得
        var matched = results.FirstOrDefault(r => r.detailA == path || r.detailB == path);
        bool isMismatch = matched != null && matched.status.Contains("パス不一致");

        for (int i = 0; i < parts.Length; i++)
        {
            string indent = new string(' ', i * 3);
            bool isLast = i == parts.Length - 1;
            string prefix = isLast ? "└─ " : "├─ ";

            string part = parts[i];
            string colorStart = "";
            string colorEnd = "";
            if (isMismatch) {
                if (i == parts.Length - 1)
                {
                    colorStart = "<color=#ff6600>";
                    colorEnd = "</color>";
                }
                else if (i == parts.Length - 2)
                {
                    colorStart = "<color=#ffaa00>";
                    colorEnd = "</color>";
                }
            }

            output += indent + prefix + colorStart + part + colorEnd + "\n";
        }
        return output;
    }
    void SaveBoneNames()
    {
        EditorPrefs.SetString(Bone_PREFS_KEY_PREFIX + currentPreset, string.Join("|", boneNameList));
        var existing = EditorPrefs.GetString("VRCComparer_PresetList", "default").Split('|').ToList();
        if (!existing.Contains(currentPreset))
        {
            existing.Add(currentPreset);
            EditorPrefs.SetString("VRCComparer_PresetList", string.Join("|", existing));
        }
    }

    void LoadBoneNames()
    {
        var data = EditorPrefs.GetString(Bone_PREFS_KEY_PREFIX + currentPreset, "");
        if (!string.IsNullOrEmpty(data))
        {
            var names = data.Split('|');
            boneNameList = names.ToList();
        }
    }
    void OnEnable()
    {
        LoadBoneNames();
    }
}
