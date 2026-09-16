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
#endif

    private Vector2 _scrollPos;
    private bool _showOnlyUnmatched = false;

    // ★ メニューを Meno Tools に統合
    [MenuItem("Meno Tools/PhysBone Auto Binder")]
    public static void ShowWindow()
    {
        if (!LicenseAuth.IsAuthenticated(ProductId))
        {
            LicenseAuth.OpenAuthWindow(ProductId);
            return;
        }

        var window = GetWindow<PhysBoneAutoBinder>();
        window.titleContent = new GUIContent("PhysBone Auto Binder");
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
        EditorGUILayout.LabelField("PhysBone Auto Binder", EditorStyles.boldLabel);
        EditorGUILayout.Space();

        armatureRoot = (Transform)EditorGUILayout.ObjectField(
            "Armature Root (ボーン検索元)", armatureRoot, typeof(Transform), true);
        pbRootParent = (Transform)EditorGUILayout.ObjectField(
            "PB Root Parent (PhysBone 付き空オブジェクトの親)", pbRootParent, typeof(Transform), true);

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
        if (_foundPBs.Count > 0)
        {
            EditorGUILayout.BeginHorizontal();
            _showOnlyUnmatched = EditorGUILayout.ToggleLeft("未マッチのみ表示", _showOnlyUnmatched, GUILayout.Width(130));
            EditorGUILayout.LabelField(
                $"Scan 結果: {_foundPBs.Count} 件（Apply で rootTransform を一括設定）",
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
        GUILayout.Label("PhysBone", EditorStyles.boldLabel, GUILayout.Width(colPB));
        GUILayout.Label("現在 rootTransform", EditorStyles.boldLabel, GUILayout.Width(colCurrentRoot));
        GUILayout.Label("名前マッチしたボーン", EditorStyles.boldLabel, GUILayout.Width(colMatched));
        EditorGUILayout.EndHorizontal();

        _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos);

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

        EditorGUILayout.EndScrollView();

        using (new EditorGUI.DisabledScope(_matchMap.Values.All(t => t == null)))
        {
            if (GUILayout.Button("Apply（マッチしているものだけ rootTransform を設定）"))
            {
                Apply();
            }
        }
    }

    private void Scan()
    {
        _foundPBs.Clear();
        _matchMap.Clear();

        if (armatureRoot == null || pbRootParent == null)
        {
            Debug.LogError("Armature Root と PB Root Parent を設定してください。");
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

        Debug.Log($"Scan 完了: {pbs.Length} 個の PhysBone を確認しました。");
    }

    private void Apply()
    {
        Undo.RecordObjects(_foundPBs.ToArray(), "Set PhysBone rootTransform From Name");

        int success = 0;
        foreach (var kvp in _matchMap)
        {
            var pb = kvp.Key;
            var bone = kvp.Value;
            if (pb == null || bone == null) continue;

            pb.rootTransform = bone;
            success++;
        }

        Debug.Log($"Apply 完了: {success} 個の PhysBone に rootTransform を設定しました。");
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
