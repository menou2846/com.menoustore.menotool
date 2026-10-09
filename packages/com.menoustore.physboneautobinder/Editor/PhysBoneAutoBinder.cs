// Assets/Editor/PhysBoneAutoBinder.cs
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using MenouStore.License;

#if VRC_SDK_VRCSDK3
using VRC.SDK3.Dynamics.PhysBone.Components;
#endif

public class PhysBoneAutoBinder : EditorWindow
{
    private const string ProductId = "default";

    [Header("検索対象")]
    public Transform armatureRoot;
    public Transform pbRootParent;
    public bool includeInactive = true;

#if VRC_SDK_VRCSDK3
    private readonly List<VRCPhysBone> _foundPBs = new List<VRCPhysBone>();
    private readonly Dictionary<VRCPhysBone, Transform> _matchMap = new Dictionary<VRCPhysBone, Transform>();

    private static readonly HashSet<string> ConstraintTypeNames = new HashSet<string>
    {
        "VRCAimConstraint", "VRCLookAtConstraint", "VRCParentConstraint",
        "VRCPositionConstraint", "VRCRotationConstraint", "VRCScaleConstraint",
    };

    // VRC Constraint 1件分。ソースは1個前提(衣装対応用)。1個以外は error として扱い、Apply をブロックする。
    private class ConstraintEntry
    {
        public Component constraint;
        public int sourceCount;
        public Transform current;   // 現在のソース(sourceCount==1 のとき)
        public Transform bone;      // 名前から見つけたボーン
        public bool IsError => sourceCount != 1;
        public bool NeedsChange => !IsError && bone != null && bone != current;
    }
    private readonly List<ConstraintEntry> _constraints = new List<ConstraintEntry>();
#endif

    private Vector2 _scrollPos;
    private bool _showOnlyUnmatched = false;

    // ★ メニューを Meno Tools に統合
    [MenuItem("Meno Tools/PhysBone・Constraint自動設定")]
    public static void ShowWindow()
    {
        if (!LicenseAuth.IsAuthenticated(ProductId))
        {
            LicenseAuth.OpenAuthWindow(ProductId);
            return;
        }

        var window = GetWindow<PhysBoneAutoBinder>();
        window.titleContent = new GUIContent("PhysBone・Constraint 自動設定");
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

#if !VRC_SDK_VRCSDK3
        EditorGUILayout.HelpBox("VRC SDK3 がプロジェクトに入っていません。", MessageType.Error);
        return;
#else
        EditorGUILayout.LabelField("PhysBone・VRC Constraint 自動設定", EditorStyles.boldLabel);
        EditorGUILayout.LabelField("衣装の PhysBone(rootTransform) と VRC Constraint(ソース) を、オブジェクト名と同名のボーンで一括設定します。", EditorStyles.miniLabel);
        EditorGUILayout.Space();

        float oldLabelWidth = EditorGUIUtility.labelWidth;
        EditorGUIUtility.labelWidth = 280f;
        armatureRoot = (Transform)EditorGUILayout.ObjectField(
            "Armature Root（アバター側のボーン検索元）", armatureRoot, typeof(Transform), true);
        pbRootParent = (Transform)EditorGUILayout.ObjectField(
            "衣装 Root（PhysBone / Constraint の検索範囲）", pbRootParent, typeof(Transform), true);
        EditorGUIUtility.labelWidth = oldLabelWidth;

        includeInactive = EditorGUILayout.Toggle("非アクティブも含める", includeInactive);

        EditorGUILayout.Space();

        using (new EditorGUI.DisabledScope(armatureRoot == null || pbRootParent == null))
        {
            if (GUILayout.Button("Scan（名前でボーンを検索）"))
            {
                Scan();
            }
        }

        EditorGUILayout.Space();

#if VRC_SDK_VRCSDK3
        if (_foundPBs.Count > 0 || _constraints.Count > 0)
        {
            EditorGUILayout.BeginHorizontal();
            _showOnlyUnmatched = EditorGUILayout.ToggleLeft("未マッチのみ表示", _showOnlyUnmatched, GUILayout.Width(130));
            EditorGUILayout.LabelField(
                $"Scan 結果: PhysBone {_foundPBs.Count} 件 / Constraint {_constraints.Count} 件（Apply で一括設定）",
                EditorStyles.miniLabel);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(4);

            DrawResultTable();
        }
#endif
#endif
    }

#if VRC_SDK_VRCSDK3
    private void DrawResultTable()
    {
        // テーブルヘッダ
        float totalWidth = position.width - 32f; // スクロールバーなどの分少し引く
        if (totalWidth < 200f) totalWidth = 200f;

        float colPB = totalWidth * 0.35f;
        float colCurrentRoot = totalWidth * 0.3f;
        float colMatched = totalWidth * 0.35f;

        EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
        GUILayout.Label("対象 (PhysBone / Constraint)", EditorStyles.boldLabel, GUILayout.Width(colPB));
        GUILayout.Label("現在 (rootTransform / ソース)", EditorStyles.boldLabel, GUILayout.Width(colCurrentRoot));
        GUILayout.Label("名前マッチしたボーン", EditorStyles.boldLabel, GUILayout.Width(colMatched));
        EditorGUILayout.EndHorizontal();

        _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos);

        if (_foundPBs.Count > 0)
            EditorGUILayout.LabelField("PhysBone（rootTransform を名前のボーンに設定）", EditorStyles.boldLabel);

        foreach (var pb in _foundPBs)
        {
            if (pb == null) continue;

            _matchMap.TryGetValue(pb, out var targetBone);
            if (_showOnlyUnmatched && targetBone != null) continue;

            var currentRoot = pb.rootTransform;
            string goName = pb.gameObject.name;
            string boneName = ExtractBoneName(goName);

            // 行本体
            EditorGUILayout.BeginHorizontal();

            // 1列目：PB
            EditorGUILayout.BeginVertical(GUILayout.Width(colPB));
            EditorGUILayout.ObjectField(pb, typeof(VRCPhysBone), true);
            EditorGUILayout.LabelField($"GO名: {goName}", EditorStyles.miniLabel);
            EditorGUILayout.EndVertical();

            // 2列目：現在 root
            EditorGUILayout.BeginVertical(GUILayout.Width(colCurrentRoot));
            EditorGUILayout.ObjectField("現在", currentRoot, typeof(Transform), true);
            EditorGUILayout.EndVertical();

            // 3列目：マッチした Transform
            EditorGUILayout.BeginVertical(GUILayout.Width(colMatched));
            if (targetBone != null)
            {
                EditorGUILayout.ObjectField("候補", targetBone, typeof(Transform), true);
                EditorGUILayout.LabelField($"ボーン名: {boneName}", EditorStyles.miniLabel);
            }
            else
            {
                EditorGUILayout.LabelField("候補なし", EditorStyles.miniLabel);
                EditorGUILayout.LabelField($"期待ボーン名: {boneName}", EditorStyles.miniLabel);
            }
            EditorGUILayout.EndVertical();

            EditorGUILayout.EndHorizontal();
            EditorGUILayout.Space(2);
        }

        if (_constraints.Count > 0)
        {
            EditorGUILayout.LabelField("VRC Constraint（ソース1個前提・名前のボーンで上書き）", EditorStyles.boldLabel);
            foreach (var e in _constraints)
            {
                if (e.constraint == null) continue;
                // 「未マッチのみ表示」: エラーと候補なし、および変更が発生するものだけ残す
                if (_showOnlyUnmatched && !e.IsError && e.bone != null && !e.NeedsChange) continue;

                string goName = e.constraint.gameObject.name;
                string boneName = ExtractBoneName(goName);

                EditorGUILayout.BeginHorizontal();

                EditorGUILayout.BeginVertical(GUILayout.Width(colPB));
                EditorGUILayout.ObjectField(e.constraint, typeof(Component), true);
                EditorGUILayout.LabelField($"GO名: {goName}", EditorStyles.miniLabel);
                EditorGUILayout.EndVertical();

                if (e.IsError)
                {
                    EditorGUILayout.HelpBox($"エラー: ソースが {e.sourceCount} 個です(1個のみ対応)。手動で直してください。", MessageType.Error);
                }
                else
                {
                    EditorGUILayout.BeginVertical(GUILayout.Width(colCurrentRoot));
                    EditorGUILayout.ObjectField("現在", e.current, typeof(Transform), true);
                    EditorGUILayout.EndVertical();

                    EditorGUILayout.BeginVertical(GUILayout.Width(colMatched));
                    if (e.bone == null)
                    {
                        EditorGUILayout.LabelField("候補なし(変更しません)", EditorStyles.miniLabel);
                        EditorGUILayout.LabelField($"期待ボーン名: {boneName}", EditorStyles.miniLabel);
                    }
                    else
                    {
                        EditorGUILayout.ObjectField("候補", e.bone, typeof(Transform), true);
                        EditorGUILayout.LabelField(
                            e.NeedsChange ? $"ボーン名: {boneName} → 上書き" : $"ボーン名: {boneName} (一致済み)",
                            EditorStyles.miniLabel);
                    }
                    EditorGUILayout.EndVertical();
                }

                EditorGUILayout.EndHorizontal();
                EditorGUILayout.Space(2);
            }
        }

        EditorGUILayout.EndScrollView();

        // 入れ間違い防止: ソースが1個でないコンストレイントが1つでもあれば Apply させない
        bool hasError = _constraints.Any(e => e.IsError);
        if (hasError)
            EditorGUILayout.HelpBox("ソースが1個でない VRC Constraint があります。解消してから Scan し直してください。", MessageType.Error);

        bool anyMatch = _matchMap.Values.Any(t => t != null) || _constraints.Any(e => e.NeedsChange);
        using (new EditorGUI.DisabledScope(!anyMatch || hasError))
        {
            if (GUILayout.Button("Apply（マッチしているものだけ rootTransform / Constraint ソースを設定・上書き）"))
            {
                Apply();
            }
        }
    }

    private void Scan()
    {
        _foundPBs.Clear();
        _matchMap.Clear();
        _constraints.Clear();

        if (armatureRoot == null || pbRootParent == null)
        {
            Debug.LogError("Armature Root と 衣装 Root を設定してください。");
            return;
        }

        // アーマチュア内の全ボーンを取得
        var bones = armatureRoot.GetComponentsInChildren<Transform>(includeInactive);
        var boneDict = bones.GroupBy(b => b.name).ToDictionary(g => g.Key, g => g.First());

        // PB 探索
        var pbs = pbRootParent.GetComponentsInChildren<VRCPhysBone>(includeInactive);
        foreach (var pb in pbs)
        {
            if (pb == null) continue;

            _foundPBs.Add(pb);

            string goName = pb.gameObject.name;
            string boneName = ExtractBoneName(goName);

            if (boneDict.TryGetValue(boneName, out var bone))
            {
                _matchMap[pb] = bone;
            }
            else
            {
                _matchMap[pb] = null;
            }
        }

        // VRC Constraint 探索(ソースは1個前提。1個以外はエラー)
        int unreadable = 0;
        foreach (var c in pbRootParent.GetComponentsInChildren<Component>(includeInactive))
        {
            if (c == null || !ConstraintTypeNames.Contains(c.GetType().Name)) continue;

            if (!TryReadSource(c, out int count, out Transform current)) { unreadable++; continue; }

            var entry = new ConstraintEntry { constraint = c, sourceCount = count, current = current };
            if (!entry.IsError)
                boneDict.TryGetValue(ExtractBoneName(c.gameObject.name), out entry.bone);
            else
                Debug.LogError($"[PhysBone Auto Binder] {c.gameObject.name} の {c.GetType().Name} はソースが {count} 個です(1個のみ対応)。", c);
            _constraints.Add(entry);
        }

        if (unreadable > 0)
            Debug.LogWarning($"VRC Constraint {unreadable} 個はこのSDKバージョンのソース構造を読めず、対象外にしました。");

        Debug.Log($"Scan 完了: PhysBone {pbs.Length} 個 / VRC Constraint {_constraints.Count} 個を確認しました。");
    }

    // VRC Constraint のソースは配列ではなく固定スロット: Sources.source0〜15.SourceTransform、有効数は Sources.totalLength。(Unity 2022.3 / SDK 3.10.3 で実機確認済み。大文字小文字に注意)
    // 読めなければ false。読めれば有効数と(1個のとき)現在のソースを返す。
    private static bool TryReadSource(Component constraint, out int count, out Transform current)
    {
        count = 0;
        current = null;
        var so = new SerializedObject(constraint);
        var total = so.FindProperty("Sources.totalLength");
        var first = so.FindProperty("Sources.source0.SourceTransform");
        if (total == null || first == null) return false;

        count = total.intValue;
        if (count == 1) current = first.objectReferenceValue as Transform;
        return true;
    }

    private void Apply()
    {
        Undo.RecordObjects(_foundPBs.ToArray(), "Set PhysBone rootTransform From Name");

        // ソース1個前提。既に入っていても名前で見つけたボーンに上書きする(衣装対応での入れ間違い防止)。
        int constraintsSet = 0;
        foreach (var e in _constraints)
        {
            if (e.constraint == null || !e.NeedsChange) continue;

            var so = new SerializedObject(e.constraint);
            var src = so.FindProperty("Sources.source0.SourceTransform");
            if (src == null) continue;

            src.objectReferenceValue = e.bone;
            so.ApplyModifiedProperties(); // SerializedObject経由なのでUndo対応
            e.current = e.bone;
            constraintsSet++;
        }

        int success = 0;
        foreach (var kvp in _matchMap)
        {
            var pb = kvp.Key;
            var bone = kvp.Value;
            if (pb == null || bone == null) continue;

            pb.rootTransform = bone;
            success++;
        }

        Debug.Log($"Apply 完了: PhysBone {success} 個に rootTransform、VRC Constraint {constraintsSet} 個のソースを設定(上書き)しました。");
    }

    /// <summary>
    /// オブジェクト名からボーン名を抽出
    /// 例:
    ///   PB_胸       → 胸
    ///   PB-[LeftArm] → LeftArm
    ///   それ以外     → そのまま
    /// </summary>
    private static string ExtractBoneName(string goName)
    {
        if (string.IsNullOrEmpty(goName))
            return goName;

        // PB_XXXX → XXXX
        if (goName.StartsWith("PB_"))
            return goName.Substring("PB_".Length);

        // PB-[XXXX]
        int start = goName.IndexOf('[');
        int end = goName.IndexOf(']');
        if (start >= 0 && end > start)
            return goName.Substring(start + 1, end - start - 1);

        return goName;
    }
#endif
}
