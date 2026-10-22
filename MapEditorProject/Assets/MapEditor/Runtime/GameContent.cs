using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using UnityEngine;
using Metadream.Shoot.Content;
using Metadream.Shoot.Maps;

namespace Metadream.MapEditor
{
    /// <summary>
    /// Everything that turns an editor document into game content. Validation and loading always use the
    /// game's own ContentDatabase code, so "loads in the editor" means "loads in the game".
    /// </summary>
    public static class GameContent
    {
        public const string MapsFolder = "Maps";
        public const string CardsFolder = "Cards";
        private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false);

        /// <summary>The editor's own content folder used for in-editor play tests (never a game folder).</summary>
        public static string PlaytestContentDir => Path.Combine(Application.persistentDataPath, "PlaytestContent");
        /// <summary>Content folder passed to the real game with -content when testing a map exclusively.</summary>
        public static string GameTestContentDir => Path.Combine(Application.persistentDataPath, "GameTestContent");

        /// <summary>Lowercase snake_case id (the game requires file name = id).</summary>
        public static string SanitizeId(string s)
        {
            var sb = new StringBuilder();
            foreach (char c0 in (s ?? "").Trim().ToLowerInvariant())
            {
                char c = c0;
                if ((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '_') sb.Append(c);
                else if (c == ' ' || c == '-' || c == '.') sb.Append('_');
            }
            string id = sb.ToString().Trim('_');
            return id.Length == 0 ? "my_map" : id;
        }

        public static bool IsValidId(string id) => !string.IsNullOrEmpty(id) && SanitizeId(id) == id;

        /// <summary>Runs the game's ParseMap on exactly the JSON that would be written. def is null when the game would reject it.</summary>
        public static List<ContentIssue> Validate(MapDocument doc, bool includeEditorData, out MapDefinition def)
        {
            var issues = new List<ContentIssue>();
            string id = SanitizeId(doc.Data.id);
            def = ContentDatabase.ParseMap(doc.ToJson(includeEditorData), id, id + ".json", issues);
            if (doc.Data.id != id)
                issues.Insert(0, new ContentIssue(ContentSeverity.Error, id + ".json", "id '" + doc.Data.id + "'는 소문자 영문·숫자·밑줄만 쓸 수 있습니다 (예: '" + id + "')"));
            if (doc.Data.disabled) issues.Add(new ContentIssue(ContentSeverity.Warning, id + ".json", "disabled = true: 게임이 이 맵을 불러오지 않습니다"));
            return issues;
        }

        public static int ErrorCount(List<ContentIssue> issues)
        {
            int n = 0;
            foreach (var i in issues) if (i.Severity == ContentSeverity.Error) n++;
            return n;
        }

        // ------------------------------------------------------------------ builtin content (synced game copy)

        private static Dictionary<string, MapData> _builtin;

        /// <summary>Built-in maps of the synced game runtime (Resources/Content/Maps), id → data.</summary>
        public static IReadOnlyDictionary<string, MapData> BuiltinMaps
        {
            get
            {
                if (_builtin != null) return _builtin;
                _builtin = new Dictionary<string, MapData>();
                foreach (var t in Resources.LoadAll<TextAsset>(ContentDatabase.ResourceRoot + "/Maps"))
                {
                    try { _builtin[t.name] = JsonUtility.FromJson<MapData>(t.text); }
                    catch (Exception) { /* the game reports broken built-ins itself */ }
                }
                return _builtin;
            }
        }

        public static string BuiltinJson(string id)
        {
            var t = Resources.Load<TextAsset>(ContentDatabase.ResourceRoot + "/Maps/" + id);
            return t != null ? t.text : null;
        }

        public static bool IsBuiltinId(string id) => BuiltinMaps.ContainsKey(id ?? "");

        // ------------------------------------------------------------------ files

        public static void WriteMapFile(MapDocument doc, string path, bool includeEditorData)
        {
            string dir = Path.GetDirectoryName(Path.GetFullPath(path));
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(path, doc.ToJson(includeEditorData), Utf8);
        }

        /// <summary>
        /// Game folder (or a game executable / .app / server executable) → its "Content" folder, the place
        /// ContentDatabase reads next to the executable (next to the .app on macOS).
        /// </summary>
        public static string ContentDirForGame(string gamePath)
        {
            string root = GameRoot(gamePath);
            return root == null ? null : Path.Combine(root, "Content");
        }

        public static string GameRoot(string gamePath)
        {
            if (string.IsNullOrWhiteSpace(gamePath)) return null;
            string p = gamePath.Trim().TrimEnd('/', '\\');
            if (p.EndsWith(".app", StringComparison.OrdinalIgnoreCase)) return Path.GetDirectoryName(p);
            if (string.Equals(Path.GetFileName(p), "Content", StringComparison.OrdinalIgnoreCase)) return Path.GetDirectoryName(p);
            if (File.Exists(p)) return Path.GetDirectoryName(Path.GetFullPath(p));
            if (Directory.Exists(p)) return p;
            if (p.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || p.EndsWith(".x86_64", StringComparison.OrdinalIgnoreCase)) return Path.GetDirectoryName(p);
            return null;
        }

        /// <summary>Writes Content/Maps/&lt;id&gt;.json; returns the written path.</summary>
        public static string InstallToContentDir(MapDocument doc, string contentDir, bool includeEditorData, out bool replacedFile)
        {
            if (string.IsNullOrWhiteSpace(contentDir)) throw new ArgumentException("Content 폴더가 지정되지 않았습니다");
            string id = SanitizeId(doc.Data.id);
            string path = Path.Combine(contentDir, MapsFolder, id + ".json");
            replacedFile = File.Exists(path);
            if (replacedFile)
            {
                // Recoverable overwrite: the game only reads *.json, so the backup is ignored by it.
                if (File.ReadAllText(path) == doc.ToJson(includeEditorData)) replacedFile = false;
                else File.Copy(path, path + ".bak", true);
            }
            WriteMapFile(doc, path, includeEditorData);
            Debug.Log("[MapEditor] installed " + path + (replacedFile ? " (replaced)" : ""));
            return path;
        }

        /// <summary>
        /// Distribution zip for players and servers: Content/Maps/&lt;id&gt;.json plus install notes. Unzip next to the
        /// game executable (next to the .app on macOS) and next to the dedicated server executable.
        /// </summary>
        public static string BuildPackage(MapDocument doc, string zipPath, bool includeEditorData)
        {
            string id = SanitizeId(doc.Data.id);
            string dir = Path.GetDirectoryName(Path.GetFullPath(zipPath));
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            if (File.Exists(zipPath)) File.Delete(zipPath);
            using (var fs = new FileStream(zipPath, FileMode.CreateNew))
            using (var zip = new ZipArchive(fs, ZipArchiveMode.Create))
            {
                Entry(zip, "Content/Maps/" + id + ".json", doc.ToJson(includeEditorData));
                Entry(zip, "README_맵_설치방법.txt", InstallNotes(doc));
            }
            Debug.Log("[MapEditor] package " + zipPath);
            return zipPath;
        }

        private static void Entry(ZipArchive zip, string name, string text)
        {
            var e = zip.CreateEntry(name, System.IO.Compression.CompressionLevel.Optimal);
            using (var w = new StreamWriter(e.Open(), Utf8)) w.Write(text);
        }

        public static string InstallNotes(MapDocument doc)
        {
            string id = SanitizeId(doc.Data.id);
            var sb = new StringBuilder();
            sb.Append("Metadream Shoot 맵 '").Append(doc.Data.name).Append("' (").Append(id).Append(")\r\n\r\n");
            sb.Append("설치\r\n");
            sb.Append("1. 이 압축 파일의 Content 폴더를 게임 실행 파일 옆에 풉니다.\r\n");
            sb.Append("   - Windows: MetadreamShoot.exe 가 있는 폴더\r\n");
            sb.Append("   - macOS: MetadreamShoot.app 이 있는 폴더 (.app 안이 아니라 옆)\r\n");
            sb.Append("   - 전용 서버: 서버 실행 파일 옆 (또는 server.json 의 contentDir)\r\n");
            sb.Append("2. 게임을 다시 시작합니다 (디버그 모드 타이틀에서는 F5 로 다시 불러오기).\r\n");
            sb.Append("3. 온라인 게임은 방장/서버와 모든 참가자가 같은 파일을 가져야 접속됩니다.\r\n");
            if (IsBuiltinId(id)) sb.Append("\r\n주의: 같은 이름의 내장 맵 '").Append(id).Append("'을 덮어씁니다.\r\n");
            sb.Append("\r\n이 맵은 Metadream Shoot Map Maker 로 만들었습니다.\r\n");
            return sb.ToString();
        }

        // ------------------------------------------------------------------ play-test content

        /// <summary>
        /// Editor play tests load content exactly like the game: built-ins (synced copy) + an external folder.
        /// The folder holds the cards/maps of the configured game's own Content folder plus this map.
        /// </summary>
        public static string PreparePlaytestContent(MapDocument doc, string gameContentDir)
        {
            string dir = PlaytestContentDir;
            ResetContentDir(dir);
            CopyGameContent(gameContentDir, dir, false);
            InstallToContentDir(doc, dir, true, out _);
            return dir;
        }

        /// <summary>
        /// Content folder for launching the real game on this map only: the game's own external content, then
        /// "disabled" overrides for every other map (the documented way to switch a built-in map off), then the map.
        /// Tutorial-only maps stay so the tutorial keeps working.
        /// </summary>
        public static string PrepareExclusiveGameContent(MapDocument doc, string gameContentDir)
        {
            string dir = GameTestContentDir;
            ResetContentDir(dir);
            CopyGameContent(gameContentDir, dir, true);
            string id = SanitizeId(doc.Data.id);
            var others = new SortedSet<string>(StringComparer.Ordinal);
            foreach (var kv in BuiltinMaps) if (kv.Key != id && (kv.Value == null || !kv.Value.tutorialOnly)) others.Add(kv.Key);
            string maps = Path.Combine(dir, MapsFolder);
            foreach (var file in Directory.GetFiles(maps, "*.json"))
            {
                string key = Path.GetFileNameWithoutExtension(file);
                if (key == id) continue;
                MapData d = null;
                try { d = JsonUtility.FromJson<MapData>(File.ReadAllText(file)); } catch (Exception) { }
                if (d == null || !d.tutorialOnly) others.Add(key);
            }
            foreach (var other in others)
                File.WriteAllText(Path.Combine(maps, other + ".json"), "{ \"id\": " + MapDocument.Str(other) + ", \"disabled\": true }\n", Utf8);
            InstallToContentDir(doc, dir, true, out _);
            return dir;
        }

        /// <summary>Deletes only the *.json files this editor writes into its own content folder.</summary>
        public static void ClearPlaytestContent() => ResetContentDir(PlaytestContentDir);

        private static void ResetContentDir(string dir)
        {
            foreach (var sub in new[] { MapsFolder, CardsFolder })
            {
                string d = Path.Combine(dir, sub);
                Directory.CreateDirectory(d);
                foreach (var f in Directory.GetFiles(d, "*.json")) File.Delete(f);
            }
        }

        private static void CopyGameContent(string gameContentDir, string dir, bool includeMaps)
        {
            if (string.IsNullOrEmpty(gameContentDir) || !Directory.Exists(gameContentDir)) return;
            foreach (var sub in new[] { CardsFolder, MapsFolder })
            {
                string from = Path.Combine(gameContentDir, sub);
                if (!Directory.Exists(from)) continue;
                foreach (var f in Directory.GetFiles(from, "*.json"))
                    File.Copy(f, Path.Combine(dir, sub, Path.GetFileName(f)), true);
            }
        }

        /// <summary>
        /// Loads the game's ContentDatabase with an external folder and returns (hash, playable map count, issues).
        /// Restores the previous external folder afterwards.
        /// </summary>
        public static string ContentHashWith(string contentDir, out int playableMaps, out List<ContentIssue> issues)
        {
            string previous = ContentDatabase.ExternalDirectory;
            try
            {
                ContentDatabase.ExternalDirectory = contentDir;
                ContentDatabase.Reload();
                playableMaps = 0;
                foreach (var m in ContentDatabase.Maps) if (!m.TutorialOnly) playableMaps++;
                issues = new List<ContentIssue>(ContentDatabase.Issues);
                return ContentDatabase.Hash;
            }
            finally
            {
                ContentDatabase.ExternalDirectory = previous;
                ContentDatabase.Reload();
            }
        }
    }
}
