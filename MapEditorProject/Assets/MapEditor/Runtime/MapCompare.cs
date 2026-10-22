using System.Collections.Generic;
using System.Text;
using UnityEngine;
using Metadream.Shoot.Content;
using Metadream.Shoot.Maps;

namespace Metadream.MapEditor
{
    /// <summary>Field-by-field comparison of two loaded maps (what the game actually builds from the JSON).</summary>
    public static class MapCompare
    {
        public static string Difference(MapDefinition a, MapDefinition b)
        {
            if (a == null || b == null) return a == b ? null : "one map failed to load";
            if (a.Id != b.Id) return "id " + a.Id + " vs " + b.Id;
            if (a.Name != b.Name) return "name";
            if (a.TutorialOnly != b.TutorialOnly) return "tutorialOnly";
            string d;
            if ((d = Blocks("solids", a.Solids, b.Solids)) != null) return d;
            if ((d = Blocks("boxes", a.Boxes, b.Boxes)) != null) return d;
            if (a.Spawns.Length != b.Spawns.Length) return "spawn count";
            for (int i = 0; i < a.Spawns.Length; i++) if (a.Spawns[i] != b.Spawns[i]) return "spawns[" + i + "]";
            if (a.Movers.Length != b.Movers.Length) return "mover count";
            for (int i = 0; i < a.Movers.Length; i++)
            {
                var x = a.Movers[i]; var y = b.Movers[i];
                if (!Same(x.Block, y.Block)) return "movers[" + i + "] block";
                if ((x.Name ?? "") != (y.Name ?? "")) return "movers[" + i + "] name";
                if (x.Speed != y.Speed || x.Wait != y.Wait) return "movers[" + i + "] speed/wait";
                if (x.Path.Length != y.Path.Length) return "movers[" + i + "] path count";
                for (int p = 0; p < x.Path.Length; p++) if (x.Path[p] != y.Path[p]) return "movers[" + i + "] path[" + p + "]";
            }
            if (a.Zones.Length != b.Zones.Length) return "zone count";
            for (int i = 0; i < a.Zones.Length; i++)
            {
                var x = a.Zones[i]; var y = b.Zones[i];
                if (x.Kind != y.Kind || !Same(x.Block, y.Block) || x.Power != y.Power || x.Dps != y.Dps || x.Force != y.Force || (x.Name ?? "") != (y.Name ?? "")) return "zones[" + i + "] " + x.Kind;
            }
            if (a.Areas.Length != b.Areas.Length) return "area count";
            for (int i = 0; i < a.Areas.Length; i++)
            {
                var x = a.Areas[i]; var y = b.Areas[i];
                if (x.Name != y.Name || !Same(x.Block, y.Block) || x.Show != y.Show || x.Color != y.Color) return "areas[" + i + "]";
            }
            if (string.Join("|", a.SolidNames) != string.Join("|", b.SolidNames)) return "solid names";
            if (string.Join("|", a.BoxNames) != string.Join("|", b.BoxNames)) return "box names";
            if (string.Join("|", a.ObjectNames) != string.Join("|", b.ObjectNames)) return "object names";
            return null;
        }

        /// <summary>Difference including what only the game loader keeps (script source and C# behaviors).</summary>
        public static string FullDifference(MapDefinition a, MapDefinition b)
        {
            string d = Difference(a, b);
            if (d != null || a == null || b == null) return d;
            if (string.Join("\n", a.ScriptSource ?? new string[0]) != string.Join("\n", b.ScriptSource ?? new string[0])) return "script";
            if ((a.Script == null) != (b.Script == null)) return "script parse";
            if (a.Script != null && (a.Script.Handlers.Count != b.Script.Handlers.Count || string.Join(",", a.Script.VarNames) != string.Join(",", b.Script.VarNames))) return "script handlers";
            if (a.Behaviors.Length != b.Behaviors.Length) return "behavior count";
            for (int i = 0; i < a.Behaviors.Length; i++)
                if (JsonUtility.ToJson(a.Behaviors[i]) != JsonUtility.ToJson(b.Behaviors[i])) return "behaviors[" + i + "]";
            return null;
        }

        private static string Blocks(string name, MapBlock[] a, MapBlock[] b)
        {
            if (a.Length != b.Length) return name + " count " + a.Length + " vs " + b.Length;
            for (int i = 0; i < a.Length; i++) if (!Same(a[i], b[i])) return name + "[" + i + "]";
            return null;
        }

        private static bool Same(MapBlock a, MapBlock b) => a.Center == b.Center && a.Size == b.Size && a.Rotation == b.Rotation;

        /// <summary>
        /// Every built-in map: game JSON → editor document → editor JSON → game loader, compared with the game
        /// loader's result for the original file. Returns failures (empty = lossless).
        /// </summary>
        public static List<string> RoundTripBuiltins(out int checkedCount)
        {
            var failures = new List<string>();
            checkedCount = 0;
            foreach (var t in Resources.LoadAll<TextAsset>(ContentDatabase.ResourceRoot + "/Maps"))
            {
                checkedCount++;
                var original = ContentDatabase.ParseMap(t.text, t.name, "builtin", new List<ContentIssue>());
                MapDefinition again;
                try
                {
                    var doc = MapDocument.FromJson(t.text, t.name);
                    again = ContentDatabase.ParseMap(doc.ToJson(true), t.name, "editor", new List<ContentIssue>());
                }
                catch (System.Exception e) { failures.Add(t.name + ": " + e.Message); continue; }
                string diff = FullDifference(original, again);
                if (diff != null) failures.Add(t.name + ": " + diff);
            }
            return failures;
        }
    }

    /// <summary>Which game build the runtime copy came from (written by the editor-side GameSync).</summary>
    [System.Serializable]
    public sealed class GameRuntimeInfo
    {
        public string sourceProject = "";
        public string sourceCommit = "";
        public string syncedAt = "";
        public string unityVersion = "";
        public int fileCount;
        public string runtimeSha256 = "";
        public string settingsSha256 = "";

        private static GameRuntimeInfo _current;

        public static GameRuntimeInfo Current
        {
            get
            {
                if (_current != null) return _current;
                var t = Resources.Load<TextAsset>("MapEditor/game-runtime");
                _current = t != null ? JsonUtility.FromJson<GameRuntimeInfo>(t.text) : new GameRuntimeInfo();
                return _current ?? (_current = new GameRuntimeInfo());
            }
        }

        public string Short
        {
            get
            {
                if (string.IsNullOrEmpty(runtimeSha256)) return "게임 런타임 정보 없음";
                var sb = new StringBuilder("게임 런타임 ");
                sb.Append(string.IsNullOrEmpty(sourceCommit) ? "?" : sourceCommit.Substring(0, Mathf.Min(7, sourceCommit.Length)));
                sb.Append(" · 파일 ").Append(fileCount).Append(" · ").Append(runtimeSha256.Substring(0, Mathf.Min(12, runtimeSha256.Length)));
                return sb.ToString();
            }
        }
    }
}
