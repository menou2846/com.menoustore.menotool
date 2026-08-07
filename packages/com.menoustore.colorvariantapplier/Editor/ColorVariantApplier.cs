// Meno Color Variant Applier - Resizable Log, Robust Material Copy, Rename+Material
// Drop this file under any "Editor" folder.
using UnityEngine;
using UnityEditor;
using UnityEditorInternal;
using System.IO;
using System.Linq;
using System.Collections.Generic;

public class ColorVariantApplier : EditorWindow
{
    // UI State
    private Vector2 _scroll;
    private Vector2 _logScroll;
    private List<string> _logs = new List<string>();
    private float _logHeight = 160f;
    private bool _draggingLogSplitter = false;

    // Working lists (shown in UI)
    private List<GameObject> _sources = new List<GameObject>();
    private List<GameObject> _targets = new List<GameObject>();

    // Reorderable lists
    private ReorderableList _srcList;
    private ReorderableList _tgtList;

    // Preset management
    private const string PresetFolder = "Assets/MenoTools/Presets";
    private string[] _presetPaths = new string[0];
    private string[] _presetNames = new string[0];
    private int _selPreset = 0;
    private PrefabReplacerPreset _preset;

    [MenuItem("Meno Tools/Color Variant Applier")]
    public static void Open()
    {
        GetWindow<ColorVariantApplier>("Color Variant Applier");
    }

    private void OnEnable()
    {
        Directory.CreateDirectory(PresetFolder);
        RefreshPresetList();
        LoadPresetByIndex(_selPreset);
        SetupLists();
    }

    private void SetupLists()
    {
        _srcList = new ReorderableList(_sources, typeof(GameObject), true, true, true, true);
        _srcList.drawHeaderCallback = (rect) => EditorGUI.LabelField(rect, "Source Prefabs");
        _srcList.onAddCallback = (list) => _sources.Add(null);
        _srcList.onRemoveCallback = (list) => { if (list.index >= 0 && list.index < _sources.Count) _sources.RemoveAt(list.index); };
        _srcList.drawElementCallback = (rect, index, active, focused) =>
        {
            rect.height = EditorGUIUtility.singleLineHeight;
            _sources[index] = (GameObject)EditorGUI.ObjectField(rect, _sources[index], typeof(GameObject), false);
        };

        _tgtList = new ReorderableList(_targets, typeof(GameObject), true, true, true, true);
        _tgtList.drawHeaderCallback = (rect) => EditorGUI.LabelField(rect, "Target Prefabs");
        _tgtList.onAddCallback = (list) => _targets.Add(null);
        _tgtList.onRemoveCallback = (list) => { if (list.index >= 0 && list.index < _targets.Count) _targets.RemoveAt(list.index); };
        _tgtList.drawElementCallback = (rect, index, active, focused) =>
        {
            rect.height = EditorGUIUtility.singleLineHeight;
            _targets[index] = (GameObject)EditorGUI.ObjectField(rect, _targets[index], typeof(GameObject), false);
        };
    }

    private void RefreshPresetList()
    {
        _presetPaths = AssetDatabase.FindAssets("t:PrefabReplacerPreset", new[] { PresetFolder })
            .Select(AssetDatabase.GUIDToAssetPath).ToArray();
        _presetNames = _presetPaths.Select(p => Path.GetFileNameWithoutExtension(p)).ToArray();

        if (_presetPaths.Length == 0)
        {
            CreateNewPreset("DefaultPreset");
            _presetPaths = AssetDatabase.FindAssets("t:PrefabReplacerPreset", new[] { PresetFolder })
                .Select(AssetDatabase.GUIDToAssetPath).ToArray();
            _presetNames = _presetPaths.Select(p => Path.GetFileNameWithoutExtension(p)).ToArray();
        }
        _selPreset = Mathf.Clamp(_selPreset, 0, Mathf.Max(0, _presetPaths.Length - 1));
    }

    private void CreateNewPreset(string name)
    {
        Directory.CreateDirectory(PresetFolder);
        var path = Path.Combine(PresetFolder, name + ".asset").Replace('\\', '/');
        var so = ScriptableObject.CreateInstance<PrefabReplacerPreset>();
        so.copyMaterials = true;
        so.copyParticleMaterials = true;
        so.copyNames = false;
        AssetDatabase.CreateAsset(so, path);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
    }

    private void LoadPresetByIndex(int idx)
    {
        if (idx < 0 || idx >= _presetPaths.Length) return;
        _preset = AssetDatabase.LoadAssetAtPath<PrefabReplacerPreset>(_presetPaths[idx]);
        _selPreset = idx;
        if (_preset != null)
        {
            // Load lists from preset
            _sources = _preset.sourcePaths?.Select(p => AssetDatabase.LoadAssetAtPath<GameObject>(p)).Where(o => o != null).ToList()
                        ?? new List<GameObject>();
            _targets = _preset.targetPaths?.Select(p => AssetDatabase.LoadAssetAtPath<GameObject>(p)).Where(o => o != null).ToList()
                        ?? new List<GameObject>();
            SetupLists();
        }
    }

    private void SaveListsToPreset()
    {
        if (_preset == null) return;
        _preset.sourcePaths = _sources.Select(AssetDatabase.GetAssetPath).Where(p => !string.IsNullOrEmpty(p)).ToArray();
        _preset.targetPaths = _targets.Select(AssetDatabase.GetAssetPath).Where(p => !string.IsNullOrEmpty(p)).ToArray();
        EditorUtility.SetDirty(_preset);
        AssetDatabase.SaveAssets();
    }

    private void OnGUI()
    {
        if (_presetNames == null) RefreshPresetList();
        _scroll = EditorGUILayout.BeginScrollView(_scroll);

        // Preset Bar
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        EditorGUILayout.LabelField("プリセット", EditorStyles.boldLabel);
        EditorGUILayout.BeginHorizontal();
        int newIdx = EditorGUILayout.Popup("使用するプリセット", _selPreset, _presetNames);
        if (newIdx != _selPreset)
        {
            LoadPresetByIndex(newIdx);
        }
        if (GUILayout.Button("新規"))
        {
            string name = EditorUtility.SaveFilePanelInProject("新しいプリセット", "NewPreset", "asset", "保存先を選択", PresetFolder);
            if (!string.IsNullOrEmpty(name))
            {
                var so = ScriptableObject.CreateInstance<PrefabReplacerPreset>();
                AssetDatabase.CreateAsset(so, name);
                AssetDatabase.SaveAssets();
                RefreshPresetList();
                _selPreset = System.Array.IndexOf(_presetPaths, name);
                LoadPresetByIndex(_selPreset);
            }
        }
        if (GUILayout.Button("削除") && _presetPaths.Length > 0)
        {
            if (EditorUtility.DisplayDialog("削除確認", "このプリセットを削除しますか？", "はい", "いいえ"))
            {
                AssetDatabase.DeleteAsset(_presetPaths[_selPreset]);
                RefreshPresetList();
                LoadPresetByIndex(_selPreset);
            }
        }
        if (GUILayout.Button("保存"))
        {
            SaveListsToPreset();
            EditorUtility.SetDirty(_preset);
            AssetDatabase.SaveAssets();
            Log("プリセットを保存しました。");
        }
        EditorGUILayout.EndHorizontal();

        if (_preset != null)
        {
            _preset.copyMaterials = EditorGUILayout.ToggleLeft("マテリアルをコピー", _preset.copyMaterials);
            EditorGUI.indentLevel++;
            _preset.copyParticleMaterials = EditorGUILayout.ToggleLeft("パーティクルのマテリアルもコピー", _preset.copyParticleMaterials);
            _preset.copyNames = EditorGUILayout.ToggleLeft("プレハブ名を変更する（Source名に合わせる）", _preset.copyNames);
            EditorGUI.indentLevel--;
        }
        EditorGUILayout.EndVertical();

        GUILayout.Space(8);

        // Source / Target Columns
        EditorGUILayout.BeginHorizontal();

        // SOURCE COLUMN
        EditorGUILayout.BeginVertical(GUILayout.Width(position.width * 0.5f - 8));
        _srcList.DoLayoutList();

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("選択中をソースに追加"))
        {
            var sel = Selection.gameObjects.Where(go => go != null).ToArray();
            foreach (var go in sel) if (!_sources.Contains(go)) _sources.Add(go);
            SaveListsToPreset();
        }
        if (GUILayout.Button("選択削除", GUILayout.Width(90)))
        {
            if (_srcList.index >= 0 && _srcList.index < _sources.Count) _sources.RemoveAt(_srcList.index);
            _srcList.list = _sources;
            SaveListsToPreset();
        }
        if (GUILayout.Button("空行/Null クリア", GUILayout.Width(120)))
        {
            _sources = _sources.Where(x => x != null).ToList();
            _srcList.list = _sources;
            SaveListsToPreset();
        }
        if (GUILayout.Button("全部クリア", GUILayout.Width(100)))
        {
            if (EditorUtility.DisplayDialog("確認", "Sourceリストを全消去しますか？", "はい", "いいえ"))
            {
                _sources.Clear();
                _srcList.list = _sources;
                SaveListsToPreset();
            }
        }
        EditorGUILayout.EndHorizontal();
        EditorGUILayout.EndVertical();

        GUILayout.Space(8);

        // TARGET COLUMN
        EditorGUILayout.BeginVertical(GUILayout.Width(position.width * 0.5f - 8));
        _tgtList.DoLayoutList();

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("選択中をターゲットに追加"))
        {
            var sel = Selection.gameObjects.Where(go => go != null).ToArray();
            foreach (var go in sel) if (!_targets.Contains(go)) _targets.Add(go);
            SaveListsToPreset();
        }
        if (GUILayout.Button("選択削除", GUILayout.Width(90)))
        {
            if (_tgtList.index >= 0 && _tgtList.index < _targets.Count) _targets.RemoveAt(_tgtList.index);
            _tgtList.list = _targets;
            SaveListsToPreset();
        }
        if (GUILayout.Button("空行/Null クリア", GUILayout.Width(120)))
        {
            _targets = _targets.Where(x => x != null).ToList();
            _tgtList.list = _targets;
            SaveListsToPreset();
        }
        if (GUILayout.Button("全部クリア", GUILayout.Width(100)))
        {
            if (EditorUtility.DisplayDialog("確認", "Targetリストを全消去しますか？", "はい", "いいえ"))
            {
                _targets.Clear();
                _tgtList.list = _targets;
                SaveListsToPreset();
            }
        }
        EditorGUILayout.EndHorizontal();
        EditorGUILayout.EndVertical();

        EditorGUILayout.EndHorizontal();

        GUILayout.Space(10);

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("実行：ペア順にマテリアルを反映", GUILayout.Height(28)))
        {
            ApplyAllPairs();
        }
        if (GUILayout.Button("ログをクリア", GUILayout.Width(120)))
        {
            _logs.Clear();
        }
        EditorGUILayout.EndHorizontal();

        // --- Resizable, scrollable log area ---
        GUILayout.Space(6);
        EditorGUILayout.LabelField("ログ", EditorStyles.boldLabel);

        // Scroll view with persistent scroll state
        _logScroll = EditorGUILayout.BeginScrollView(_logScroll, GUILayout.Height(_logHeight));
        foreach (var l in _logs) EditorGUILayout.LabelField(l, EditorStyles.wordWrappedLabel);
        EditorGUILayout.EndScrollView();

        // Resize handle
        Rect r = GUILayoutUtility.GetRect(0, 6, GUILayout.ExpandWidth(true));
        EditorGUI.DrawRect(r, new Color(0,0,0,0.1f));
        EditorGUIUtility.AddCursorRect(r, MouseCursor.ResizeVertical);
        if (Event.current.type == EventType.MouseDown && r.Contains(Event.current.mousePosition))
        {
            _draggingLogSplitter = true;
        }
        if (_draggingLogSplitter && Event.current.type == EventType.MouseDrag)
        {
            _logHeight = Mathf.Max(80f, _logHeight + Event.current.delta.y);
            Repaint();
        }
        if (Event.current.type == EventType.MouseUp) _draggingLogSplitter = false;

        EditorGUILayout.EndScrollView();
    }

    private void ApplyAllPairs()
    {
        if (_preset == null || !_preset.copyMaterials)
        {
            Log("マテリアルコピーがOFFのためスキップ。");
            return;
        }

        int n = Mathf.Min(_sources.Count, _targets.Count);
        for (int i = 0; i < n; i++)
        {
            var src = _sources[i];
            var dst = _targets[i];
            if (src == null || dst == null) continue;
            try
            {
                ApplyOnePair(src, dst);
            }
            catch (System.Exception e)
            {
                Debug.LogException(e);
                Log($"[ERROR] {src?.name} → {dst?.name}: {e.Message}");
            }
        }
        Log("完了。");
    }

    private void ApplyOnePair(GameObject sourcePrefab, GameObject targetPrefab)
    {
        var srcRoot = PrefabUtility.InstantiatePrefab(sourcePrefab) as GameObject;
        var dstRoot = PrefabUtility.InstantiatePrefab(targetPrefab) as GameObject;
        try
        {
            if (srcRoot == null || dstRoot == null)
            {
                Log($"[WARN] インスタンス化に失敗: {sourcePrefab?.name} / {targetPrefab?.name}");
                return;
            }
            Undo.RegisterCreatedObjectUndo(srcRoot, "Src");
            Undo.RegisterCreatedObjectUndo(dstRoot, "Dst");

            // Apply materials first
            CopyMaterialsRobust(srcRoot, dstRoot, _preset.copyParticleMaterials);

            // Set root name to source BEFORE save, so prefab internal root name matches
            if (_preset.copyNames)
            {
                dstRoot.name = srcRoot.name;
            }

            // Save back to target prefab (always save first to avoid rename/save collisions)
            var dstPath = AssetDatabase.GetAssetPath(targetPrefab);
            if (!string.IsNullOrEmpty(dstPath))
            {
                PrefabUtility.SaveAsPrefabAsset(dstRoot, dstPath);
                Log($"保存: {dstPath}");
            }

            // Then try to rename asset file (safer after save)
            if (_preset.copyNames && !string.IsNullOrEmpty(dstPath))
            {
                string newName = srcRoot.name; // keep extension
                string err = AssetDatabase.RenameAsset(dstPath, newName);
                if (string.IsNullOrEmpty(err))
                {
                    Log($"アセット名を {Path.GetFileName(dstPath)} → {newName} に変更しました。");
                }
                else
                {
                    Log($"[WARN] アセット名のリネームに失敗: {err}");
                }
            }
        }
        finally
        {
            if (srcRoot != null) GameObject.DestroyImmediate(srcRoot);
            if (dstRoot != null) GameObject.DestroyImmediate(dstRoot);
        }
    }

    // --- Robust material copy: relative-path first, then index/type fallback ---
    private void CopyMaterialsRobust(GameObject source, GameObject target, bool includeParticles)
    {
        var srcAll = source.GetComponentsInChildren<Renderer>(true).ToList();
        var dstAll = target.GetComponentsInChildren<Renderer>(true).ToList();

        // 1) Map by relative path
        var srcMap = new Dictionary<string, Renderer>();
        foreach (var r in srcAll)
        {
            var path = GetRelativePath(source.transform, r.transform);
            if (!srcMap.ContainsKey(path)) srcMap.Add(path, r);
        }

        int applied = 0;
        var unmatchedDst = new List<Renderer>();

        foreach (var t in dstAll)
        {
            if (!includeParticles && t is ParticleSystemRenderer) continue;
            var path = GetRelativePath(target.transform, t.transform);
            if (srcMap.TryGetValue(path, out var s))
            {
                if (ApplyMaterials(s, t)) applied++;
            }
            else
            {
                unmatchedDst.Add(t);
            }
        }

        // 2) Fallback: match by renderer type and order (best-effort)
        if (unmatchedDst.Count > 0)
        {
            // group by type name to align counts
            var srcByType = srcAll.GroupBy(r => r.GetType().Name)
                                  .ToDictionary(g => g.Key, g => g.ToList());
            var dstByType = unmatchedDst.GroupBy(r => r.GetType().Name)
                                        .ToDictionary(g => g.Key, g => g.ToList());

            foreach (var kv in dstByType)
            {
                var type = kv.Key;
                var dstList = kv.Value;
                if (!srcByType.TryGetValue(type, out var srcList)) continue;

                int m = Mathf.Min(srcList.Count, dstList.Count);
                for (int i = 0; i < m; i++)
                {
                    if (!includeParticles && dstList[i] is ParticleSystemRenderer) continue;
                    if (ApplyMaterials(srcList[i], dstList[i])) applied++;
                }
            }
        }

        Log($"Renderer適用数: {applied}");
    }

    private bool ApplyMaterials(Renderer src, Renderer dst)
    {
        var srcMats = src.sharedMaterials;
        if (srcMats == null || srcMats.Length == 0) return false;
        Undo.RecordObject(dst, "Copy Materials");
        dst.sharedMaterials = srcMats;
        Log($"[{RendererLabel(src)} → {RendererLabel(dst)}] {dst.transform.GetHierarchyPath()}");
        return true;
    }

    private string GetRelativePath(Transform root, Transform target)
    {
        var stack = new Stack<string>();
        var t = target;
        while (t != null && t != root)
        {
            stack.Push(t.name);
            t = t.parent;
        }
        return string.Join("/", stack);
    }

    private string RendererLabel(Renderer r)
    {
        if (r is ParticleSystemRenderer) return "ParticleSystemRenderer";
        if (r is SkinnedMeshRenderer)   return "SkinnedMeshRenderer";
        if (r is MeshRenderer)          return "MeshRenderer";
        if (r is TrailRenderer)         return "TrailRenderer";
        if (r is LineRenderer)          return "LineRenderer";
        return r != null ? r.GetType().Name : "Renderer";
    }

    private void Log(string msg)
    {
        _logs.Add($"[{System.DateTime.Now:HH:mm:ss}] {msg}");
        Repaint();
    }
}

public static class TransformPathUtil
{
    public static string GetHierarchyPath(this Transform t)
    {
        if (t == null) return "(null)";
        var stack = new Stack<string>();
        var cur = t;
        while (cur != null)
        {
            stack.Push(cur.name);
            cur = cur.parent;
        }
        return string.Join("/", stack);
    }
}
