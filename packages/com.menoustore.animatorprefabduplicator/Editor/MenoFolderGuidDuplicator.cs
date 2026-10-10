using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

/// <summary>
/// フォルダを丸ごと複製し、全アセットに新しいGUIDを振って、複製フォルダ内同士の参照を複製側へ付け替える。
/// ファイル単位でコピーして .meta のGUIDと参照(YAML内のGUID)を書き換え、最後にRefreshする方式。
/// 元のフォルダは読み取りのみ。失敗時は今回作ったフォルダだけを削除する。
/// スクリプト・シェーダー等(CodeExt)は複製すると型名/シェーダー名が重複するため複製せず、旧アセットを共有参照する。
/// </summary>
internal static class MenoFolderGuidDuplicator
{
    internal sealed class Plan
    {
        public string Source;   // Assets/...
        public string Dest;     // Assets/... (未作成)
        public readonly List<string> Files = new List<string>();        // 複製するファイル(Source相対, '/'区切り, .metaを除く)
        public readonly List<string> SkippedCode = new List<string>();  // 複製しない(スクリプト/シェーダー等)
        public readonly List<string> NoMeta = new List<string>();       // .metaが無く複製できないファイル
        public long TotalBytes;
    }

    internal sealed class Result
    {
        public string DestFolder;
        public string ReportPath;
        public int CopiedFiles;
        public int ReplacedReferences;
        public int LeftoverOldGuids;   // 複製側に残った旧GUID(0なら成功)
        public int SharedExternal;     // 複製せず共有参照しているアセット数(スクリプト/Packages/フォルダ外)
    }

    static readonly HashSet<string> CodeExt = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".cs", ".dll", ".asmdef", ".asmref", ".shader", ".cginc", ".hlsl", ".compute",
        ".shadergraph", ".shadersubgraph", ".rsp", ".so", ".dylib", ".a", ".bundle",
    };

    // GUID参照を持ちうるアセット。YAML(テキスト)でないと付け替えできないので、バイナリなら中止する。
    static readonly HashSet<string> RefExt = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".prefab", ".asset", ".controller", ".overrideController", ".anim", ".mask", ".mat", ".unity",
        ".playable", ".mixer", ".physicMaterial", ".physicsMaterial2D", ".flare", ".guiskin",
        ".renderTexture", ".cubemap", ".lighting", ".signal", ".spriteatlas", ".terrainlayer",
    };

    static readonly Regex GuidToken = new Regex("(?<![0-9A-Fa-f])[0-9a-f]{32}(?![0-9A-Fa-f])", RegexOptions.Compiled);
    static readonly Regex MetaGuid = new Regex("(?m)^guid: ([0-9a-f]{32})", RegexOptions.Compiled);
    // バイトを1:1で文字に対応させる(UTF-8/BOM/改行を含め、GUID以外のバイトを一切変えないため)
    static readonly Encoding Latin1 = Encoding.GetEncoding("ISO-8859-1");

    static string ProjectRoot() => Directory.GetParent(Application.dataPath).FullName;

    static bool IsHidden(string rel)
    {
        foreach (var seg in rel.Split('/'))
            if (seg.StartsWith(".", StringComparison.Ordinal) || seg.EndsWith("~", StringComparison.Ordinal)) return true;
        return false;
    }

    static bool IsYaml(byte[] data) =>
        data.Length >= 5 && data[0] == '%' && data[1] == 'Y' && data[2] == 'A' && data[3] == 'M' && data[4] == 'L';

    static string ReadGuid(string metaPath)
    {
        if (!File.Exists(metaPath)) return null;
        var m = MetaGuid.Match(File.ReadAllText(metaPath));
        return m.Success ? m.Groups[1].Value : null;
    }

    /// <summary>複製計画を作る(ファイルは作らない)。条件を満たさなければ例外。</summary>
    internal static Plan BuildPlan(string source, string parent, string folderName)
    {
        if (string.IsNullOrEmpty(source) || source == "Assets" || !source.StartsWith("Assets/", StringComparison.Ordinal) ||
            !AssetDatabase.IsValidFolder(source))
            throw new InvalidOperationException("複製元は Assets 内のフォルダ(Assets そのものは不可)を指定してください。");
        if (string.IsNullOrEmpty(parent) || !AssetDatabase.IsValidFolder(parent) ||
            (parent != "Assets" && !parent.StartsWith("Assets/", StringComparison.Ordinal)))
            throw new InvalidOperationException("保存先はAssets内のフォルダを指定してください。");
        if (EditorSettings.serializationMode != SerializationMode.ForceText)
            throw new InvalidOperationException(
                "参照の付け替えにテキストシリアライズが必要です。Edit > Project Settings > Editor > Asset Serialization を「Force Text」にしてください。");

        string baseName = string.IsNullOrWhiteSpace(folderName)
            ? MenoAnimatorPrefabDuplicator.CleanName(Path.GetFileName(source)) + "_Independent"
            : MenoAnimatorPrefabDuplicator.CleanName(folderName);
        string dest = AssetDatabase.GenerateUniqueAssetPath(parent + "/" + baseName);
        if ((dest + "/").StartsWith(source + "/", StringComparison.Ordinal))
            throw new InvalidOperationException("保存先が複製元フォルダの中になっています。別の場所を指定してください。");

        var plan = new Plan { Source = source, Dest = dest };
        string absSource = Path.Combine(ProjectRoot(), source);
        foreach (string abs in Directory.EnumerateFiles(absSource, "*", SearchOption.AllDirectories))
        {
            string rel = abs.Substring(absSource.Length).TrimStart('\\', '/').Replace('\\', '/');
            if (IsHidden(rel) || rel.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)) continue;
            if (CodeExt.Contains(Path.GetExtension(rel))) { plan.SkippedCode.Add(rel); continue; }
            if (!File.Exists(abs + ".meta")) { plan.NoMeta.Add(rel); continue; }
            plan.Files.Add(rel);
            plan.TotalBytes += new FileInfo(abs).Length;
        }
        if (plan.Files.Count == 0)
            throw new InvalidOperationException("複製できるファイルがありません: " + source);
        return plan;
    }

    // GUID文字列を置換する。置換したものは count に、置換対象外のGUID形式の値は external に集める。
    static byte[] Remap(byte[] data, Dictionary<string, string> map, HashSet<string> external, out int count)
    {
        int n = 0;
        string text = Latin1.GetString(data);
        string result = GuidToken.Replace(text, m =>
        {
            if (map.TryGetValue(m.Value, out string newGuid)) { n++; return newGuid; }
            external.Add(m.Value);
            return m.Value;
        });
        count = n;
        return n == 0 ? data : Latin1.GetBytes(result);
    }

    /// <summary>複製を実行する。失敗時は今回作成したフォルダだけを削除して例外を投げる。</summary>
    internal static Result Duplicate(Plan plan, Action<string, float> progress = null)
    {
        string root = ProjectRoot();
        string absSource = Path.Combine(root, plan.Source);
        string absDest = Path.Combine(root, plan.Dest);
        if (Directory.Exists(absDest) || File.Exists(absDest + ".meta"))
            throw new InvalidOperationException("保存先が既に存在します: " + plan.Dest);

        bool created = false;
        try
        {
            // 1) 旧GUID -> 新GUID
            var map = new Dictionary<string, string>(StringComparer.Ordinal);
            var oldPath = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (string rel in plan.Files)
            {
                string guid = ReadGuid(Path.Combine(absSource, rel) + ".meta");
                if (guid == null) throw new InvalidOperationException(".metaにGUIDが見つかりません: " + rel);
                if (map.ContainsKey(guid)) throw new InvalidOperationException("複製元内でGUIDが重複しています: " + rel + " / " + oldPath[guid]);
                map[guid] = Guid.NewGuid().ToString("N");
                oldPath[guid] = rel;
            }
            var skippedCodeGuids = new HashSet<string>(StringComparer.Ordinal);
            foreach (string rel in plan.SkippedCode)
            {
                string g = ReadGuid(Path.Combine(absSource, rel) + ".meta");
                if (g != null) skippedCodeGuids.Add(g);
            }

            // 2) コピー + 参照の付け替え(.meta自身のGUIDも同じ置換で新GUIDになる)
            Directory.CreateDirectory(absDest);
            created = true;
            var external = new HashSet<string>(StringComparer.Ordinal);
            int replaced = 0, i = 0;
            var writtenText = new List<string>();
            foreach (string rel in plan.Files)
            {
                progress?.Invoke("コピー中 " + (++i) + "/" + plan.Files.Count + "  " + rel, (float)i / plan.Files.Count);
                string src = Path.Combine(absSource, rel);
                string dst = Path.Combine(absDest, rel);
                Directory.CreateDirectory(Path.GetDirectoryName(dst));

                byte[] data = File.ReadAllBytes(src);
                if (IsYaml(data))
                {
                    data = Remap(data, map, external, out int c);
                    replaced += c;
                    writtenText.Add(dst);
                }
                else if (RefExt.Contains(Path.GetExtension(rel)))
                {
                    throw new InvalidOperationException(
                        "バイナリ形式のため参照を付け替えできません: " + plan.Source + "/" + rel + "(Force Textで再保存してください)");
                }
                File.WriteAllBytes(dst, data);

                byte[] meta = Remap(File.ReadAllBytes(src + ".meta"), map, external, out int mc);
                replaced += mc;
                File.WriteAllBytes(dst + ".meta", meta);
                writtenText.Add(dst + ".meta");
            }

            progress?.Invoke("Unityに取り込み中...", 1f);
            AssetDatabase.Refresh();

            // 3) 検証: 複製側に旧GUIDが残っていないこと
            int leftover = 0;
            foreach (string f in writtenText)
                foreach (Match m in GuidToken.Matches(Latin1.GetString(File.ReadAllBytes(f))))
                    if (map.ContainsKey(m.Value)) leftover++;
            if (leftover > 0)
                throw new InvalidOperationException("旧GUIDへの参照が " + leftover + " 件残っています。複製を中止しました。");

            // 4) 複製せず共有している参照を分類
            var sharedCode = new List<string>();
            var packages = new List<string>();
            var outside = new List<string>();
            var oldFolderLeft = new List<string>();
            foreach (string tok in external.OrderBy(x => x, StringComparer.Ordinal))
            {
                string p = AssetDatabase.GUIDToAssetPath(tok);
                if (string.IsNullOrEmpty(p) || p.StartsWith(plan.Dest + "/", StringComparison.Ordinal)) continue;
                if (p.StartsWith("Packages/", StringComparison.Ordinal)) packages.Add(p);
                else if (p.StartsWith(plan.Source + "/", StringComparison.Ordinal))
                    (skippedCodeGuids.Contains(tok) ? sharedCode : oldFolderLeft).Add(p);
                else outside.Add(p);
            }

            // 5) レポート
            var rep = new StringBuilder();
            rep.AppendLine("Meno Folder GUID Duplicator");
            rep.AppendLine("Source: " + plan.Source);
            rep.AppendLine("Clone: " + plan.Dest);
            rep.AppendLine("Files copied: " + plan.Files.Count + " (" + (plan.TotalBytes / 1024) + " KB)");
            rep.AppendLine("References rewritten: " + replaced);
            rep.AppendLine("RESULT: PASS (no references to original GUIDs remain in the clone)");
            rep.AppendLine();
            AppendList(rep, "SHARED: scripts/shaders not cloned, still referencing the original", sharedCode);
            AppendList(rep, "SHARED: Packages", packages);
            AppendList(rep, "SHARED: assets outside the source folder", outside);
            AppendList(rep, "WARNING: assets in the source folder that were NOT cloned and are still referenced", oldFolderLeft);
            AppendList(rep, "NOT cloned: scripts/shaders in the source folder", plan.SkippedCode);
            AppendList(rep, "NOT cloned: files without .meta", plan.NoMeta);
            rep.AppendLine("GUID MAP (old path -> new GUID)");
            foreach (var kv in map.OrderBy(k => oldPath[k.Key], StringComparer.Ordinal))
                rep.AppendLine(oldPath[kv.Key] + "  " + kv.Key + " -> " + kv.Value);

            string reportPath = plan.Dest + "/GUID_Duplication_Report.txt";
            File.WriteAllText(Path.Combine(root, reportPath), rep.ToString(), new UTF8Encoding(false));
            AssetDatabase.ImportAsset(reportPath);
            AssetDatabase.Refresh();

            created = false; // ここから先は成功扱い。ロールバックしない
            Debug.Log("[Folder GUID Duplicator] 複製に成功しました: " + plan.Source + " -> " + plan.Dest +
                      "\nファイル " + plan.Files.Count + " 件 / 参照の付け替え " + replaced + " 件");
            return new Result
            {
                DestFolder = plan.Dest,
                ReportPath = reportPath,
                CopiedFiles = plan.Files.Count,
                ReplacedReferences = replaced,
                LeftoverOldGuids = 0,
                SharedExternal = sharedCode.Count + packages.Count + outside.Count,
            };
        }
        catch
        {
            if (created)
            {
                // 今回自分で作ったフォルダだけを消す(元のフォルダには触れない)
                try { Directory.Delete(absDest, true); } catch (Exception) { /* 次のRefreshで整合する */ }
                if (File.Exists(absDest + ".meta")) File.Delete(absDest + ".meta");
                AssetDatabase.Refresh();
            }
            throw;
        }
    }

    static void AppendList(StringBuilder sb, string title, List<string> items)
    {
        sb.AppendLine(title + ": " + items.Count);
        foreach (string s in items.Distinct().OrderBy(x => x, StringComparer.Ordinal)) sb.AppendLine("  " + s);
        sb.AppendLine();
    }
}
