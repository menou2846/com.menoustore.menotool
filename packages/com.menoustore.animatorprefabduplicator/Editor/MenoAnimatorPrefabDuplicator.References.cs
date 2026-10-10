using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

// 参照チェック・修正タブ
public sealed partial class MenoAnimatorPrefabDuplicator
{
    private DefaultAsset refFolder;
    private MenoReferenceScanner.ScanResult refScan;
    private string refError;
    private bool refShowShared;
    private Vector2 refScroll;
    private readonly Dictionary<string, MenoReferenceScanner.FixAction> refActions = new Dictionary<string, MenoReferenceScanner.FixAction>();
    private readonly HashSet<string> refExpanded = new HashSet<string>();
    private readonly HashSet<string> refTrash = new HashSet<string>();

    private static readonly string[] ActionsOther = { "何もしない", "参照を外す(None)", "フォルダに取り込む" };
    private static readonly string[] ActionsNoImport = { "何もしない", "参照を外す(None)" };

    private void DrawRefMode()
    {
        EditorGUILayout.LabelField("参照チェック・修正", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "フォルダ内のアセットが、フォルダの外のアセットを参照していないかを調べます。「どのファイルの・どの階層/コンポーネント/プロパティが参照しているか」を表示し、" +
            "参照を外す(None)か、フォルダに取り込む(新GUIDでコピーして付け替え)かを選べます。\n" +
            "変更前のファイルは Library/MenoRefFixBackup/ にバックアップします。スクリプト・シェーダー・Packagesへの参照は共有として扱います。",
            MessageType.Info);

        EditorGUI.BeginChangeCheck();
        refFolder = (DefaultAsset)EditorGUILayout.ObjectField("調べるフォルダ", refFolder, typeof(DefaultAsset), false);
        if (EditorGUI.EndChangeCheck())
        {
            refScan = null;
            refError = null;
        }
        using (new EditorGUI.DisabledScope(refFolder == null))
        {
            if (GUILayout.Button("参照を調べる", GUILayout.Height(30)))
                RunRefScan();
        }

        if (!string.IsNullOrEmpty(refError))
            EditorGUILayout.HelpBox(refError, MessageType.Error);
        if (refScan == null) return;

        var others = refScan.Externals.Where(e => !e.IsShared).ToList();
        var shared = refScan.Externals.Where(e => e.IsShared).ToList();
        EditorGUILayout.LabelField("スキャン " + refScan.FilesScanned + " ファイル / フォルダ外への参照 " + others.Count +
                                   " 件 / 共有(スクリプト等) " + shared.Count + " 件 / 未使用の疑い " + refScan.Unreferenced.Count + " 件");
        refShowShared = EditorGUILayout.ToggleLeft("共有(スクリプト・シェーダー・Packages・内蔵)も表示する", refShowShared);

        var deadOnly = others.Where(x => x.AllDead).ToList();
        if (deadOnly.Count > 0)
        {
            EditorGUILayout.HelpBox(
                "「効いていない上書き」だけが参照している外部アセットが " + deadOnly.Count + " 件あります。FBXを作り直したときに残った、" +
                "対象が存在しない上書き(PrefabInstance)で、見た目には影響しません。「参照を外す」を選ぶと、その上書きエントリごと削除します。",
                MessageType.Warning);
            if (GUILayout.Button("効いていない上書きだけの参照を、まとめて「参照を外す」に設定"))
                foreach (var x in deadOnly) refActions[x.Guid] = MenoReferenceScanner.FixAction.SetNull;
        }

        refScroll = EditorGUILayout.BeginScrollView(refScroll);

        EditorGUILayout.LabelField("フォルダ外のアセットへの参照", EditorStyles.boldLabel);
        if (others.Count == 0)
            EditorGUILayout.LabelField("   なし", EditorStyles.miniLabel);
        foreach (var e in others) DrawExternal(e);

        if (refShowShared)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("共有(そのまま参照する想定)", EditorStyles.boldLabel);
            foreach (var e in shared) DrawExternal(e);
        }

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("このフォルダ内から参照されていないアセット(未使用の疑い)", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "フォルダ内のどのアセットからも参照されていません。フォルダ外からの参照は確認していないので、削除前に確認してください。" +
            "チェックしたものはゴミ箱へ送ります(復元できます)。",
            MessageType.None);
        if (refScan.Unreferenced.Count == 0)
            EditorGUILayout.LabelField("   なし", EditorStyles.miniLabel);
        foreach (string path in refScan.Unreferenced)
        {
            EditorGUILayout.BeginHorizontal();
            bool on = refTrash.Contains(path);
            bool next = EditorGUILayout.ToggleLeft(path, on);
            if (next != on) { if (next) refTrash.Add(path); else refTrash.Remove(path); }
            var asset = AssetDatabase.LoadMainAssetAtPath(path);
            if (asset != null && GUILayout.Button("Ping", GUILayout.Width(44)))
                EditorGUIUtility.PingObject(asset);
            EditorGUILayout.EndHorizontal();
        }

        EditorGUILayout.EndScrollView();

        int pending = refActions.Count(kv => kv.Value != MenoReferenceScanner.FixAction.None) + refTrash.Count;
        using (new EditorGUI.DisabledScope(pending == 0))
        {
            if (GUILayout.Button("選択した操作を実行 (" + pending + " 件)", GUILayout.Height(30)))
                RunRefFix();
        }
    }

    private void DrawExternal(MenoReferenceScanner.ExternalRef e)
    {
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        EditorGUILayout.BeginHorizontal();

        bool open = refExpanded.Contains(e.Guid);
        string title = (string.IsNullOrEmpty(e.Path) ? "(内蔵/不明) " + e.Guid : e.Path) + "   [" + e.Owners.Count + " か所]";
        bool nextOpen = EditorGUILayout.Foldout(open, title, true);
        if (nextOpen != open) { if (nextOpen) refExpanded.Add(e.Guid); else refExpanded.Remove(e.Guid); }

        var asset = string.IsNullOrEmpty(e.Path) ? null : AssetDatabase.LoadMainAssetAtPath(e.Path);
        if (asset != null && GUILayout.Button("Ping", GUILayout.Width(44)))
            EditorGUIUtility.PingObject(asset);

        if (e.Category == MenoReferenceScanner.CatOther || e.Category == MenoReferenceScanner.CatBuiltin)
        {
            refActions.TryGetValue(e.Guid, out MenoReferenceScanner.FixAction action);
            var labels = e.Category == MenoReferenceScanner.CatOther ? ActionsOther : ActionsNoImport;
            int picked = EditorGUILayout.Popup((int)action, labels, GUILayout.Width(140));
            if ((MenoReferenceScanner.FixAction)picked != action)
                refActions[e.Guid] = (MenoReferenceScanner.FixAction)picked;
        }
        else
        {
            EditorGUILayout.LabelField(e.Category + "(そのまま)", EditorStyles.miniLabel, GUILayout.Width(140));
        }
        EditorGUILayout.EndHorizontal();

        if (nextOpen)
        {
            foreach (var o in e.Owners)
            {
                EditorGUILayout.BeginHorizontal();
                GUILayout.Space(18);
                if (GUILayout.Button(Path.GetFileName(o.File), EditorStyles.miniButton, GUILayout.Width(220)))
                {
                    var owner = AssetDatabase.LoadMainAssetAtPath(o.File);
                    if (owner != null) { Selection.activeObject = owner; EditorGUIUtility.PingObject(owner); }
                }
                EditorGUILayout.LabelField(o.Where + (o.Blocked ? "   [構造に関わるため外せません]" : "") +
                                           (o.Dead ? "   [効いていない上書き: 対象がFBX内に存在しません]" : ""), EditorStyles.miniLabel);
                EditorGUILayout.EndHorizontal();
            }
        }
        EditorGUILayout.EndVertical();
    }

    private void RunRefScan()
    {
        refScan = null;
        refError = null;
        try
        {
            EditorUtility.DisplayProgressBar("参照を調べています", "フォルダ内のアセットを読み込み中...", 0.5f);
            refScan = MenoReferenceScanner.Scan(AssetDatabase.GetAssetPath(refFolder));
            refActions.Clear();
            refTrash.Clear();
        }
        catch (Exception ex)
        {
            refError = ex.Message;
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }
    }

    private void RunRefFix()
    {
        try
        {
            var actions = new Dictionary<MenoReferenceScanner.ExternalRef, MenoReferenceScanner.FixAction>();
            foreach (var e in refScan.Externals)
                if (refActions.TryGetValue(e.Guid, out MenoReferenceScanner.FixAction a) && a != MenoReferenceScanner.FixAction.None)
                    actions[e] = a;
            int nulls = actions.Count(kv => kv.Value == MenoReferenceScanner.FixAction.SetNull);
            int imports = actions.Count(kv => kv.Value == MenoReferenceScanner.FixAction.Import);
            var trash = refTrash.ToList();

            string msg = "参照を外す: " + nulls + " 件\nフォルダに取り込む: " + imports + " 件\nゴミ箱へ送る: " + trash.Count + " 件\n\n" +
                         "変更するファイルは Library/MenoRefFixBackup/ にバックアップします。実行しますか？";
            if (!EditorUtility.DisplayDialog("参照の修正", msg, "実行する", "キャンセル")) return;

            var summary = MenoReferenceScanner.Apply(AssetDatabase.GetAssetPath(refFolder), actions, trash);
            EditorUtility.DisplayDialog("修正しました",
                "参照を外した箇所: " + summary.Nulled + "(うち効いていない上書きを削除: " + summary.RemovedOverrides + ")\n取り込んだアセット: " + summary.Imported + "\n" +
                "変更したファイル: " + summary.FilesModified + "\nゴミ箱へ送った: " + summary.Trashed + "\n" +
                (summary.Blocked > 0 ? "構造に関わるため外さなかった箇所: " + summary.Blocked + "\n" : "") +
                "\nバックアップ: " + summary.BackupPath, "OK");
            RunRefScan();
        }
        catch (Exception ex)
        {
            Debug.LogError("[Reference Checker] 修正失敗: " + ex);
            EditorUtility.DisplayDialog("修正を中止しました", ex.Message, "OK");
        }
    }
}
