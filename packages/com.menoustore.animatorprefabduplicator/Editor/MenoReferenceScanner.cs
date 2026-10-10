using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

/// <summary>
/// フォルダ内のアセット(YAML)が、フォルダの外のアセットを参照していないかを調べ、
/// 「どのファイルの・どの階層/コンポーネント/プロパティが参照しているか」を突き止める。
/// 参照を外す(None) / フォルダに取り込む、およびフォルダ内で使われていないアセットの検出もできる。
/// 変更する前に、変更するファイルを Library/MenoRefFixBackup/ にバックアップする。
/// </summary>
internal static class MenoReferenceScanner
{
    internal sealed class Owner
    {
        public string File;     // 参照しているアセット(Assets/...)
        public string Where;    // 階層 ▸ コンポーネント ▸ プロパティ
        public string Key;      // プロパティ名
        public bool Blocked;    // 構造に関わるキー(m_Script / Variantの親など)なので外せない
        public bool Dead;       // PrefabInstanceの上書きで、対象がFBX内に存在しない(=効いていないゴミの上書き)
    }

    internal sealed class ExternalRef
    {
        public string Guid;
        public string Path;       // 参照先(見つからなければ空)
        public string Category;   // 他のフォルダ / Packages / スクリプト・シェーダー / 内蔵・不明
        public readonly List<Owner> Owners = new List<Owner>();
        public bool IsShared { get { return Category != CatOther; } }
        public bool AllDead { get { return Owners.Count > 0 && Owners.All(o => o.Dead); } }
    }

    internal sealed class ScanResult
    {
        public string Folder;
        public int FilesScanned;
        public readonly List<ExternalRef> Externals = new List<ExternalRef>();
        public readonly List<string> Unreferenced = new List<string>();
    }

    internal enum FixAction { None, SetNull, Import }

    internal sealed class FixSummary
    {
        public int Nulled, Imported, Blocked, FilesModified, Trashed, RemovedOverrides;
        public string BackupPath;
    }

    internal const string CatOther = "他のフォルダ";
    internal const string CatPackages = "Packages";
    internal const string CatCode = "スクリプト・シェーダー";
    internal const string CatBuiltin = "内蔵・不明";

    // これらのキーの参照を消すとPrefabの構造やコンポーネント自体が壊れるため、外さない
    static readonly HashSet<string> BlockedKeys = new HashSet<string>(StringComparer.Ordinal)
    {
        "m_Script", "m_SourcePrefab", "m_CorrespondingSourceObject", "target", "m_PrefabAsset",
    };

    // 複製・削除の入口になるアセット。他から参照されなくて当然なので「未使用」には含めない
    static readonly HashSet<string> EntryExt = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".prefab", ".unity", ".txt",
    };

    static readonly Regex DocHeader = new Regex(@"^--- !u!(\d+) &(-?\d+)", RegexOptions.Compiled);
    static readonly Regex FileIdRx = new Regex(@"fileID: (-?\d+)", RegexOptions.Compiled);
    static readonly Regex PropertyPathRx = new Regex(@"propertyPath: (.+)", RegexOptions.Compiled);

    sealed class Doc
    {
        public int ClassId;
        public long FileId;
        public string Name;
        public long GameObjectId, FatherId;
        public string ScriptGuid;
        public string SourceGuid;   // PrefabInstance の元(FBX/Prefab)
    }

    // ---------------------------------------------------------------- スキャン

    internal static ScanResult Scan(string folder)
    {
        if (string.IsNullOrEmpty(folder) || folder == "Assets" || !folder.StartsWith("Assets/", StringComparison.Ordinal) ||
            !AssetDatabase.IsValidFolder(folder))
            throw new InvalidOperationException("Assets 内のフォルダ(Assets そのものは不可)を指定してください。");

        string root = MenoFolderGuidDuplicator.ProjectRoot();
        string absFolder = Path.Combine(root, folder);
        var result = new ScanResult { Folder = folder };

        // フォルダ内のアセット(guid -> パス)
        var assetByGuid = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string metaAbs in Directory.EnumerateFiles(absFolder, "*.meta", SearchOption.AllDirectories))
        {
            string assetAbs = metaAbs.Substring(0, metaAbs.Length - 5);
            if (!File.Exists(assetAbs)) continue; // フォルダのmeta
            string rel = assetAbs.Substring(root.Length).TrimStart('\\', '/').Replace('\\', '/');
            if (MenoFolderGuidDuplicator.IsHidden(rel)) continue;
            string g = MenoFolderGuidDuplicator.ReadGuid(metaAbs);
            if (g != null) assetByGuid[g] = rel;
        }

        var referrers = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal); // 内部アセットを参照しているファイル
        var externals = new Dictionary<string, ExternalRef>(StringComparer.Ordinal);
        var idCache = new Dictionary<string, HashSet<long>>(StringComparer.Ordinal);

        foreach (var kv in assetByGuid)
        {
            string path = kv.Value;
            string abs = Path.Combine(root, path);

            // .meta(Importer設定がフォルダ内のMaterial等を参照することがある): 内部参照の記録のみ
            string meta = Encoding.UTF8.GetString(File.ReadAllBytes(abs + ".meta"));
            foreach (Match m in MenoFolderGuidDuplicator.GuidToken.Matches(meta))
                if (m.Value != kv.Key && assetByGuid.ContainsKey(m.Value)) AddRef(referrers, m.Value, path);

            if (MenoFolderGuidDuplicator.CodeExt.Contains(Path.GetExtension(path))) continue;
            byte[] bytes = File.ReadAllBytes(abs);
            if (!MenoFolderGuidDuplicator.IsYaml(bytes)) continue;
            result.FilesScanned++;

            string[] lines = Encoding.UTF8.GetString(bytes).Split('\n');
            int[] docOfLine;
            Dictionary<long, Doc> byId;
            Dictionary<long, Doc> transformByGo;
            List<Doc> docs = ParseDocs(lines, out docOfLine, out byId, out transformByGo);

            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];
                if (line.IndexOf("guid:", StringComparison.Ordinal) < 0) continue;
                foreach (Match m in MenoFolderGuidDuplicator.GuidToken.Matches(line))
                {
                    string g = m.Value;
                    if (g == kv.Key) continue;
                    if (assetByGuid.ContainsKey(g)) { AddRef(referrers, g, path); continue; }

                    string target = AssetDatabase.GUIDToAssetPath(g);
                    if (!externals.TryGetValue(g, out ExternalRef ext))
                    {
                        ext = new ExternalRef { Guid = g, Path = target ?? "", Category = Categorize(target) };
                        externals[g] = ext;
                    }
                    if (ext.Owners.Count >= 300) continue;
                    string key = KeyOf(lines, i);
                    ext.Owners.Add(new Owner
                    {
                        File = path,
                        Key = key,
                        Blocked = BlockedKeys.Contains(key),
                        Dead = TryDeadOverride(lines, i, idCache, out int deadStart),
                        Where = DescribeWhere(lines, i, key, docs, docOfLine, byId, transformByGo),
                    });
                }
            }
        }

        result.Externals.AddRange(externals.Values
            .OrderBy(e => e.IsShared ? 1 : 0).ThenBy(e => e.Category, StringComparer.Ordinal).ThenBy(e => e.Path, StringComparer.Ordinal));

        foreach (var kv in assetByGuid)
        {
            string ext = Path.GetExtension(kv.Value);
            if (MenoFolderGuidDuplicator.CodeExt.Contains(ext) || EntryExt.Contains(ext)) continue;
            if (referrers.ContainsKey(kv.Key)) continue;
            result.Unreferenced.Add(kv.Value);
        }
        result.Unreferenced.Sort(StringComparer.Ordinal);
        return result;
    }

    static void AddRef(Dictionary<string, HashSet<string>> map, string guid, string from)
    {
        if (!map.TryGetValue(guid, out HashSet<string> set)) map[guid] = set = new HashSet<string>(StringComparer.Ordinal);
        set.Add(from);
    }

    static string Categorize(string path)
    {
        if (string.IsNullOrEmpty(path)) return CatBuiltin;
        if (path.StartsWith("Packages/", StringComparison.Ordinal)) return CatPackages;
        if (MenoFolderGuidDuplicator.CodeExt.Contains(Path.GetExtension(path))) return CatCode;
        if (!path.StartsWith("Assets/", StringComparison.Ordinal)) return CatBuiltin; // Resources/unity_builtin_extra など
        return CatOther;
    }

    // ---------------------------------------------------------------- 効いていない上書きの検出

    static readonly Regex TargetRx = new Regex(@"fileID: (-?\d+), guid: ([0-9a-f]{32})", RegexOptions.Compiled);

    // objectReference の行から、同じ上書きエントリの "- target:" 行を探し、対象がFBX内に存在するか調べる。
    // FBX(モデル)が元のときだけ判定する(Prefab由来の入れ子はfileIDが合成されるため判定しない)。
    // 対象が存在しなければ true と、エントリの開始行(- target:)を返す。
    static bool TryDeadOverride(string[] lines, int objRefLine, Dictionary<string, HashSet<long>> idCache, out int blockStart)
    {
        blockStart = -1;
        if (!lines[objRefLine].TrimStart(' ', '-').StartsWith("objectReference:", StringComparison.Ordinal)) return false;
        for (int j = objRefLine - 1; j >= Math.Max(0, objRefLine - 8); j--)
        {
            string tj = lines[j].TrimStart(' ');
            if (!tj.StartsWith("- target:", StringComparison.Ordinal)) continue;
            string joined = lines[j] + " " + (j + 1 < lines.Length ? lines[j + 1] : "");
            var m = TargetRx.Match(joined);
            if (!m.Success) return false;
            long fileId = long.Parse(m.Groups[1].Value);
            string guid = m.Groups[2].Value;
            if (!idCache.TryGetValue(guid, out HashSet<long> ids))
            {
                ids = LocalIdsOfModel(guid);
                idCache[guid] = ids;
            }
            if (ids == null || ids.Contains(fileId)) return false;
            blockStart = j;
            return true;
        }
        return false;
    }

    static HashSet<long> LocalIdsOfModel(string guid)
    {
        string path = AssetDatabase.GUIDToAssetPath(guid);
        if (string.IsNullOrEmpty(path) || !(AssetImporter.GetAtPath(path) is ModelImporter)) return null;
        var ids = new HashSet<long>();
        foreach (UnityEngine.Object o in AssetDatabase.LoadAllAssetsAtPath(path))
            if (o != null && AssetDatabase.TryGetGUIDAndLocalFileIdentifier(o, out string g, out long id)) ids.Add(id);
        return ids;
    }

    // ---------------------------------------------------------------- YAMLの簡易解析(表示用)

    static List<Doc> ParseDocs(string[] lines, out int[] docOfLine, out Dictionary<long, Doc> byId, out Dictionary<long, Doc> transformByGo)
    {
        var docs = new List<Doc>();
        docOfLine = new int[lines.Length];
        byId = new Dictionary<long, Doc>();
        transformByGo = new Dictionary<long, Doc>();
        int cur = -1;
        for (int i = 0; i < lines.Length; i++)
        {
            string l = lines[i];
            if (l.StartsWith("--- !u!", StringComparison.Ordinal))
            {
                var d = new Doc();
                var m = DocHeader.Match(l);
                if (m.Success)
                {
                    d.ClassId = int.Parse(m.Groups[1].Value);
                    d.FileId = long.Parse(m.Groups[2].Value);
                    byId[d.FileId] = d;
                }
                docs.Add(d);
                cur = docs.Count - 1;
            }
            docOfLine[i] = cur;
            if (cur < 0) continue;
            if (l.Length > 2 && l[0] == ' ' && l[1] == ' ' && l[2] != ' ')
            {
                Doc doc = docs[cur];
                if (doc.Name == null && l.StartsWith("  m_Name:", StringComparison.Ordinal)) doc.Name = UnescapeName(l.Substring(9));
                else if (l.StartsWith("  m_GameObject:", StringComparison.Ordinal)) doc.GameObjectId = FileIdOf(l);
                else if (l.StartsWith("  m_Father:", StringComparison.Ordinal)) doc.FatherId = FileIdOf(l);
                else if (l.StartsWith("  m_SourcePrefab:", StringComparison.Ordinal))
                {
                    var sm = MenoFolderGuidDuplicator.GuidToken.Match(l);
                    if (sm.Success) doc.SourceGuid = sm.Value;
                }
                else if (l.StartsWith("  m_Script:", StringComparison.Ordinal))
                {
                    var gm = MenoFolderGuidDuplicator.GuidToken.Match(l);
                    if (gm.Success) doc.ScriptGuid = gm.Value;
                }
            }
        }
        foreach (Doc d in docs)
            if ((d.ClassId == 4 || d.ClassId == 224) && d.GameObjectId != 0) transformByGo[d.GameObjectId] = d;
        return docs;
    }

    // Unityは日本語名を "\u30E1..." のように書くことがあるので戻す
    static string UnescapeName(string raw)
    {
        string t = raw.Trim();
        if (t.Length >= 2 && t[0] == '"' && t[t.Length - 1] == '"')
        {
            try { return Regex.Unescape(t.Substring(1, t.Length - 2)); }
            catch (Exception) { return t.Trim('"'); }
        }
        return t;
    }

    static long FileIdOf(string line)
    {
        var m = FileIdRx.Match(line);
        return m.Success ? long.Parse(m.Groups[1].Value) : 0;
    }

    // その行のプロパティ名。リスト要素("- {fileID: ...}")は直前の親キー、PrefabInstanceのobjectReferenceはpropertyPathを使う。
    static string KeyOf(string[] lines, int i)
    {
        string t = lines[i].TrimStart(' ', '-');
        int indent = lines[i].Length - lines[i].TrimStart(' ').Length;
        int c = t.IndexOf(':');
        string key = c > 0 && !t.StartsWith("{", StringComparison.Ordinal) ? t.Substring(0, c) : "";

        if (key == "objectReference")
        {
            for (int j = i - 1; j >= Math.Max(0, i - 4); j--)
            {
                var pm = PropertyPathRx.Match(lines[j]);
                if (pm.Success) return pm.Groups[1].Value.Trim();
            }
        }
        if (key == "m_Texture")
        {
            // マテリアルのテクスチャスロット名(例: _MainTex)を付ける
            for (int j = i - 1; j >= Math.Max(0, i - 6); j--)
            {
                string lj = lines[j];
                string tj = lj.Trim();
                int ind = lj.Length - lj.TrimStart(' ').Length;
                if (ind < indent && tj.EndsWith(":", StringComparison.Ordinal))
                    return tj.TrimStart('-', ' ').TrimEnd(':') + "." + key;
            }
        }
        if (key != "") return key;

        for (int j = i - 1; j >= Math.Max(0, i - 60); j--)
        {
            string lj = lines[j];
            if (lj.StartsWith("--- ", StringComparison.Ordinal)) break;
            int ind = lj.Length - lj.TrimStart(' ').Length;
            string tj = lj.Trim();
            if (ind < indent && tj.EndsWith(":", StringComparison.Ordinal) && !tj.StartsWith("-", StringComparison.Ordinal))
                return tj.TrimEnd(':') + "[]";
        }
        return "(不明)";
    }

    static string ClassName(Doc d)
    {
        switch (d.ClassId)
        {
            case 1: return "GameObject";
            case 4: return "Transform";
            case 21: return "Material";
            case 23: return "MeshRenderer";
            case 33: return "MeshFilter";
            case 74: return "AnimationClip";
            case 91: return "AnimatorController";
            case 95: return "Animator";
            case 111: return "Animation";
            case 137: return "SkinnedMeshRenderer";
            case 198: return "ParticleSystem";
            case 199: return "ParticleSystemRenderer";
            case 224: return "RectTransform";
            case 206: return "BlendTree";
            case 1001:
                if (d.SourceGuid != null)
                {
                    string sp0 = AssetDatabase.GUIDToAssetPath(d.SourceGuid);
                    if (!string.IsNullOrEmpty(sp0)) return "PrefabInstance(" + Path.GetFileName(sp0) + ")";
                }
                return "PrefabInstance";
            case 1101: return "AnimatorStateTransition";
            case 1102: return "AnimatorState";
            case 1107: return "AnimatorStateMachine";
            case 1109: return "AnimatorTransition";
            case 114:
                if (d.ScriptGuid != null)
                {
                    string sp = AssetDatabase.GUIDToAssetPath(d.ScriptGuid);
                    if (!string.IsNullOrEmpty(sp)) return Path.GetFileNameWithoutExtension(sp);
                }
                return "MonoBehaviour";
            default: return "Class" + d.ClassId;
        }
    }

    static string HierarchyPath(Doc go, Dictionary<long, Doc> byId, Dictionary<long, Doc> transformByGo)
    {
        var names = new List<string>();
        Doc cur = go;
        for (int depth = 0; cur != null && depth < 40; depth++)
        {
            names.Add(cur.Name ?? "?");
            if (!transformByGo.TryGetValue(cur.FileId, out Doc tr) || tr.FatherId == 0) break;
            if (!byId.TryGetValue(tr.FatherId, out Doc father) || father.GameObjectId == 0) break;
            byId.TryGetValue(father.GameObjectId, out cur);
        }
        names.Reverse();
        return string.Join("/", names.ToArray());
    }

    static string DescribeWhere(string[] lines, int i, string key, List<Doc> docs, int[] docOfLine,
                                Dictionary<long, Doc> byId, Dictionary<long, Doc> transformByGo)
    {
        if (docOfLine[i] < 0) return key;
        Doc doc = docs[docOfLine[i]];
        string go = null;
        if (doc.ClassId == 1) go = HierarchyPath(doc, byId, transformByGo);
        else if (doc.GameObjectId != 0 && byId.TryGetValue(doc.GameObjectId, out Doc g)) go = HierarchyPath(g, byId, transformByGo);

        var sb = new StringBuilder();
        if (go != null) sb.Append(go).Append(" ▸ ");
        sb.Append(ClassName(doc));
        if (doc.ClassId != 1 && !string.IsNullOrEmpty(doc.Name)) sb.Append(" [").Append(doc.Name).Append("]");
        sb.Append(" ▸ ").Append(key);
        return sb.ToString();
    }

    // ---------------------------------------------------------------- 修正

    /// <summary>
    /// 参照を外す/取り込む、未使用アセットをゴミ箱へ。変更するファイルは先に Library/MenoRefFixBackup/ へバックアップする。
    /// </summary>
    internal static FixSummary Apply(string folder, Dictionary<ExternalRef, FixAction> actions, List<string> trash)
    {
        var summary = new FixSummary();
        string root = MenoFolderGuidDuplicator.ProjectRoot();
        string backupRoot = Path.Combine(root, "Library", "MenoRefFixBackup", DateTime.Now.ToString("yyyyMMdd-HHmmss"));
        summary.BackupPath = backupRoot;

        // 取り込み: 参照先のファイルを <folder>/Imported へ新GUIDでコピー
        var importMap = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var kv in actions.Where(a => a.Value == FixAction.Import))
        {
            ExternalRef r = kv.Key;
            if (r.Category != CatOther || string.IsNullOrEmpty(r.Path) || !r.Path.StartsWith("Assets/", StringComparison.Ordinal))
                throw new InvalidOperationException("取り込めない参照です: " + r.Path);
            string srcAbs = Path.Combine(root, r.Path);
            if (!File.Exists(srcAbs) || !File.Exists(srcAbs + ".meta"))
                throw new InvalidOperationException("参照先ファイルが見つかりません: " + r.Path);

            string destDir = Path.Combine(root, folder, "Imported");
            Directory.CreateDirectory(destDir);
            string name = Path.GetFileNameWithoutExtension(r.Path), ext = Path.GetExtension(r.Path);
            string destAbs = Path.Combine(destDir, name + ext);
            for (int n = 1; File.Exists(destAbs) || File.Exists(destAbs + ".meta"); n++)
                destAbs = Path.Combine(destDir, name + "_" + n + ext);

            string newGuid = Guid.NewGuid().ToString("N");
            File.Copy(srcAbs, destAbs);
            byte[] meta = File.ReadAllBytes(srcAbs + ".meta");
            string metaText = MenoFolderGuidDuplicator.Latin1.GetString(meta);
            metaText = metaText.Replace("guid: " + r.Guid, "guid: " + newGuid);
            File.WriteAllBytes(destAbs + ".meta", MenoFolderGuidDuplicator.Latin1.GetBytes(metaText));
            importMap[r.Guid] = newGuid;
            summary.Imported++;
        }

        // 参照元ファイルごとに書き換え
        var idCache = new Dictionary<string, HashSet<long>>(StringComparer.Ordinal);
        var nullGuids = new HashSet<string>(actions.Where(a => a.Value == FixAction.SetNull).Select(a => a.Key.Guid), StringComparer.Ordinal);
        var files = actions.Where(a => a.Value != FixAction.None).SelectMany(a => a.Key.Owners.Select(o => o.File)).Distinct().ToList();
        foreach (string file in files)
        {
            string abs = Path.Combine(root, file);
            byte[] bytes = File.ReadAllBytes(abs);
            string[] lines = MenoFolderGuidDuplicator.Latin1.GetString(bytes).Split('\n');
            bool changed = false;
            var remove = new bool[lines.Length];
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];
                if (line.IndexOf("guid:", StringComparison.Ordinal) < 0) continue;
                foreach (string g in nullGuids)
                {
                    if (line.IndexOf(g, StringComparison.Ordinal) < 0) continue;
                    if (BlockedKeys.Contains(KeyOf(lines, i))) { summary.Blocked++; continue; }
                    if (TryDeadOverride(lines, i, idCache, out int blockStart))
                    {
                        // 効いていない上書き: Noneにするのではなく、エントリ(target〜objectReference)ごと削除
                        for (int k = blockStart; k <= i; k++) remove[k] = true;
                        changed = true;
                        summary.Nulled++;
                        summary.RemovedOverrides++;
                        continue;
                    }
                    string replaced = Regex.Replace(line, @"\{fileID: -?\d+, guid: " + g + @", type: \d+\}", "{fileID: 0}");
                    if (replaced != line) { lines[i] = line = replaced; changed = true; summary.Nulled++; }
                }
                foreach (var kv in importMap)
                {
                    if (line.IndexOf(kv.Key, StringComparison.Ordinal) < 0) continue;
                    lines[i] = line = line.Replace(kv.Key, kv.Value);
                    changed = true;
                }
            }
            if (!changed) continue;

            string backup = Path.Combine(backupRoot, file.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(backup));
            File.WriteAllBytes(backup, bytes);
            var kept = new List<string>(lines.Length);
            for (int i = 0; i < lines.Length; i++) if (!remove[i]) kept.Add(lines[i]);
            File.WriteAllBytes(abs, MenoFolderGuidDuplicator.Latin1.GetBytes(string.Join("\n", kept.ToArray())));
            summary.FilesModified++;
        }

        AssetDatabase.Refresh();
        foreach (string p in trash)
            if (AssetDatabase.MoveAssetToTrash(p)) summary.Trashed++;
        AssetDatabase.Refresh();
        return summary;
    }
}
