using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;
using Metadream.Shoot.Abilities;
using Metadream.Shoot.Cards;
using Metadream.Shoot.Maps;

namespace Metadream.Shoot.Content
{
    // ---------------------------------------------------------------- JSON schema (see Docs/EXTENDING.md)

    [Serializable]
    public sealed class CardLineData
    {
        public string label = "";
        public string amount = "";
        public bool positive = true;
    }

    [Serializable]
    public sealed class CardData
    {
        public string id = "";
        public string title = "";
        public string subtitle = "";
        public string description = "";
        public string rarity = "Common";
        public bool allowMultiple = true;
        public bool disabled;
        public string[] categories = new string[0];
        public string[] blacklistedCategories = new string[0];
        public CardModifiers modifiers = new CardModifiers();
        public EffectSpec[] effects = new EffectSpec[0];
        public CardLineData[] lines = new CardLineData[0];
    }

    [Serializable]
    public sealed class RectData
    {
        public float x, y, w, h;
    }

    [Serializable]
    public sealed class PointData
    {
        public float x, y;
    }

    [Serializable]
    public sealed class MapData
    {
        public string id = "";
        public string name = "";
        public bool tutorialOnly;
        public bool disabled;
        public RectData[] solids = new RectData[0];
        public RectData[] boxes = new RectData[0];
        public PointData[] spawns = new PointData[0];
    }

    public enum ContentSeverity
    {
        Info,
        Warning,
        Error
    }

    public readonly struct ContentIssue
    {
        public readonly ContentSeverity Severity;
        public readonly string Source;
        public readonly string Message;

        public ContentIssue(ContentSeverity severity, string source, string message)
        {
            Severity = severity;
            Source = source;
            Message = message;
        }

        public override string ToString() => Severity + " " + Source + ": " + Message;
    }

    /// <summary>
    /// Loads cards and maps from JSON (built-in Resources/Content plus an optional external "Content" folder),
    /// validates them and computes a content hash shared by clients and servers.
    /// Network ids are positions in the id-sorted lists, so both sides must load identical content.
    /// </summary>
    public static class ContentDatabase
    {
        public const string ResourceRoot = "Content";
        public const float ArenaHalfWidth = 35.56f;  // OutOfBoundsHandler bounds
        public const float ArenaHalfHeight = 20f;

        private static List<CardDefinition> _cards;
        private static List<MapDefinition> _maps;
        private static readonly List<ContentIssue> _issues = new List<ContentIssue>();
        private static string _hash = "";

        /// <summary>Extra content folder (set before first use, e.g. from -content or server.json).</summary>
        public static string ExternalDirectory { get; set; }
        public static string LoadedExternalDirectory { get; private set; }

        public static IReadOnlyList<CardDefinition> Cards { get { EnsureLoaded(); return _cards; } }
        public static IReadOnlyList<MapDefinition> Maps { get { EnsureLoaded(); return _maps; } }
        public static IReadOnlyList<ContentIssue> Issues { get { EnsureLoaded(); return _issues; } }
        public static string Hash { get { EnsureLoaded(); return _hash; } }

        public static int ErrorCount
        {
            get
            {
                int n = 0;
                foreach (var i in Issues) if (i.Severity == ContentSeverity.Error) n++;
                return n;
            }
        }

        public static int WarningCount
        {
            get
            {
                int n = 0;
                foreach (var i in Issues) if (i.Severity == ContentSeverity.Warning) n++;
                return n;
            }
        }

        public static event Action Reloaded;

        public static CardDefinition FindCard(string id)
        {
            foreach (var c in Cards) if (c.Id == id) return c;
            return null;
        }

        public static MapDefinition FindMap(string id)
        {
            foreach (var m in Maps) if (m.Id == id) return m;
            return null;
        }

        /// <summary>First playable (non-tutorial) map, used as the title backdrop.</summary>
        public static MapDefinition DefaultMap
        {
            get
            {
                foreach (var m in Maps) if (!m.TutorialOnly) return m;
                return Maps.Count > 0 ? Maps[0] : null;
            }
        }

        public static void Reload()
        {
            _cards = null;
            _maps = null;
            EnsureLoaded();
            Reloaded?.Invoke();
        }

        private static void EnsureLoaded()
        {
            if (_cards != null) return;
            _issues.Clear();
            var cardSources = new SortedDictionary<string, KeyValuePair<string, string>>(StringComparer.Ordinal);
            var mapSources = new SortedDictionary<string, KeyValuePair<string, string>>(StringComparer.Ordinal);
            foreach (var t in Resources.LoadAll<TextAsset>(ResourceRoot + "/Cards"))
                cardSources[t.name] = new KeyValuePair<string, string>(t.text, "builtin Cards/" + t.name + ".json");
            foreach (var t in Resources.LoadAll<TextAsset>(ResourceRoot + "/Maps"))
                mapSources[t.name] = new KeyValuePair<string, string>(t.text, "builtin Maps/" + t.name + ".json");

            LoadedExternalDirectory = null;
            string external = ResolveExternalDirectory();
            if (external != null)
            {
                LoadedExternalDirectory = external;
                ReadFolder(Path.Combine(external, "Cards"), cardSources);
                ReadFolder(Path.Combine(external, "Maps"), mapSources);
            }

            var cards = new List<CardDefinition>();
            foreach (var kv in cardSources)
            {
                var card = ParseCard(kv.Value.Key, kv.Key, kv.Value.Value, _issues);
                if (card == null) continue;
                if (cards.Exists(c => c.Id == card.Id)) { _issues.Add(new ContentIssue(ContentSeverity.Error, card.Source, "duplicate card id '" + card.Id + "'")); continue; }
                cards.Add(card);
            }
            cards.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id));
            for (int i = 0; i < cards.Count; i++) cards[i].Index = i;

            var maps = new List<MapDefinition>();
            foreach (var kv in mapSources)
            {
                var map = ParseMap(kv.Value.Key, kv.Key, kv.Value.Value, _issues);
                if (map == null) continue;
                if (maps.Exists(m => m.Id == map.Id)) { _issues.Add(new ContentIssue(ContentSeverity.Error, map.Source, "duplicate map id '" + map.Id + "'")); continue; }
                maps.Add(map);
            }
            maps.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id));
            for (int i = 0; i < maps.Count; i++) maps[i].Index = i;

            if (cards.Count == 0) _issues.Add(new ContentIssue(ContentSeverity.Error, "Content", "no cards loaded"));
            if (!maps.Exists(m => !m.TutorialOnly)) _issues.Add(new ContentIssue(ContentSeverity.Error, "Content", "no playable (non-tutorial) map loaded"));

            _cards = cards;
            _maps = maps;
            _hash = ComputeHash(cardSources, mapSources, cards, maps);

            Debug.Log("[Content] " + cards.Count + " cards, " + maps.Count + " maps, hash " + _hash
                + (external != null ? ", external " + external : "") + ", " + ErrorCount + " errors, " + WarningCount + " warnings");
            foreach (var issue in _issues)
            {
                if (issue.Severity == ContentSeverity.Error) Debug.LogError("[Content] " + issue);
                else if (issue.Severity == ContentSeverity.Warning) Debug.LogWarning("[Content] " + issue);
            }
        }

        private static string ResolveExternalDirectory()
        {
            if (!string.IsNullOrEmpty(ExternalDirectory)) return Directory.Exists(ExternalDirectory) ? Path.GetFullPath(ExternalDirectory) : null;
            if (Application.isEditor) return null;
            // Players: a "Content" folder next to the executable (next to the .app on macOS).
            string root = Path.GetDirectoryName(Application.dataPath);
            if (root != null && root.EndsWith(".app", StringComparison.OrdinalIgnoreCase)) root = Path.GetDirectoryName(root);
            if (root == null) return null;
            string dir = Path.Combine(root, "Content");
            return Directory.Exists(dir) ? dir : null;
        }

        private static void ReadFolder(string folder, SortedDictionary<string, KeyValuePair<string, string>> into)
        {
            if (!Directory.Exists(folder)) return;
            foreach (var file in Directory.GetFiles(folder, "*.json"))
            {
                try
                {
                    string key = Path.GetFileNameWithoutExtension(file);
                    bool overrides = into.ContainsKey(key);
                    into[key] = new KeyValuePair<string, string>(File.ReadAllText(file), file);
                    _issues.Add(new ContentIssue(ContentSeverity.Info, file, overrides ? "overrides built-in '" + key + "'" : "added"));
                }
                catch (Exception e)
                {
                    _issues.Add(new ContentIssue(ContentSeverity.Error, file, "cannot read: " + e.Message));
                }
            }
        }

        // ---------------------------------------------------------------- cards

        public static CardDefinition ParseCard(string json, string fileKey, string source, List<ContentIssue> issues)
        {
            CardData d;
            try
            {
                d = JsonUtility.FromJson<CardData>(json);
            }
            catch (Exception e)
            {
                issues.Add(new ContentIssue(ContentSeverity.Error, source, "invalid JSON: " + e.Message));
                return null;
            }
            if (d == null) { issues.Add(new ContentIssue(ContentSeverity.Error, source, "empty JSON")); return null; }
            if (d.disabled) { issues.Add(new ContentIssue(ContentSeverity.Info, source, "disabled")); return null; }
            if (string.IsNullOrWhiteSpace(d.id))
            {
                d.id = fileKey;
                issues.Add(new ContentIssue(ContentSeverity.Warning, source, "missing \"id\", using file name '" + fileKey + "'"));
            }
            else if (d.id != fileKey)
            {
                issues.Add(new ContentIssue(ContentSeverity.Warning, source, "id '" + d.id + "' differs from file name '" + fileKey + "'"));
            }

            bool ok = true;
            void Error(string m) { ok = false; issues.Add(new ContentIssue(ContentSeverity.Error, source, m)); }
            void Warn(string m) => issues.Add(new ContentIssue(ContentSeverity.Warning, source, m));

            if (string.IsNullOrWhiteSpace(d.title)) Error("\"title\" is empty");
            if (!Enum.TryParse(d.rarity, true, out CardRarity rarity)) Error("unknown rarity '" + d.rarity + "' (Common, Uncommon, Rare)");
            var m = d.modifiers ?? new CardModifiers();
            void Positive(string name, float v) { if (!(v > 0f)) Error("modifiers." + name + " must be > 0 (multiplier, 1 = unchanged), got " + v); }
            Positive("health", m.health);
            Positive("size", m.size);
            Positive("movementSpeed", m.movementSpeed);
            Positive("jump", m.jump);
            Positive("damage", m.damage);
            Positive("attackSpeed", m.attackSpeed);
            Positive("projectileSpeed", m.projectileSpeed);
            Positive("reloadTimeMultiplier", m.reloadTimeMultiplier);
            Positive("blockCdMultiplier", m.blockCdMultiplier);
            Positive("projectileSize", m.projectileSize);
            if (m.gravity < 0f) Error("modifiers.gravity must be >= 0");
            if (m.numberOfProjectiles < 0 || m.numberOfProjectiles > 20) Error("modifiers.numberOfProjectiles must be 0..20");
            if (m.bursts < 0 || m.bursts > 20) Error("modifiers.bursts must be 0..20");
            if (m.lifeSteal < 0f || m.lifeSteal > 5f) Warn("modifiers.lifeSteal " + m.lifeSteal + " looks unusual (0.5 = 50%)");

            var effects = d.effects ?? new EffectSpec[0];
            for (int i = 0; i < effects.Length; i++)
            {
                string problem = EffectRegistry.Validate(effects[i]);
                if (problem != null) Error("effects[" + i + "]: " + problem);
            }
            if (d.lines == null || d.lines.Length == 0) Warn("no \"lines\": the card face will show no numbers");
            if (!ok) return null;

            var lines = new List<CardStatLine>();
            foreach (var l in d.lines ?? new CardLineData[0]) lines.Add(new CardStatLine(l.label, l.amount, l.positive));
            return new CardDefinition
            {
                Id = d.id,
                Title = d.title,
                Subtitle = d.subtitle ?? "",
                Description = d.description ?? "",
                Rarity = rarity,
                AllowMultiple = d.allowMultiple,
                Categories = d.categories ?? new string[0],
                BlacklistedCategories = d.blacklistedCategories ?? new string[0],
                Modifiers = m,
                Effects = effects,
                Lines = lines.ToArray(),
                Source = source
            };
        }

        // ---------------------------------------------------------------- maps

        public static MapDefinition ParseMap(string json, string fileKey, string source, List<ContentIssue> issues)
        {
            MapData d;
            try
            {
                d = JsonUtility.FromJson<MapData>(json);
            }
            catch (Exception e)
            {
                issues.Add(new ContentIssue(ContentSeverity.Error, source, "invalid JSON: " + e.Message));
                return null;
            }
            if (d == null) { issues.Add(new ContentIssue(ContentSeverity.Error, source, "empty JSON")); return null; }
            if (d.disabled) { issues.Add(new ContentIssue(ContentSeverity.Info, source, "disabled")); return null; }
            if (string.IsNullOrWhiteSpace(d.id))
            {
                d.id = fileKey;
                issues.Add(new ContentIssue(ContentSeverity.Warning, source, "missing \"id\", using file name '" + fileKey + "'"));
            }
            else if (d.id != fileKey)
            {
                issues.Add(new ContentIssue(ContentSeverity.Warning, source, "id '" + d.id + "' differs from file name '" + fileKey + "'"));
            }

            bool ok = true;
            void Error(string m) { ok = false; issues.Add(new ContentIssue(ContentSeverity.Error, source, m)); }
            void Warn(string m) => issues.Add(new ContentIssue(ContentSeverity.Warning, source, m));

            var solids = d.solids ?? new RectData[0];
            var boxes = d.boxes ?? new RectData[0];
            var spawns = d.spawns ?? new PointData[0];
            if (string.IsNullOrWhiteSpace(d.name)) Warn("\"name\" is empty");
            if (solids.Length == 0) Error("needs at least one entry in \"solids\"");
            if (spawns.Length < 2) Error("needs at least 2 spawns (team 0 = spawns[0], team 1 = spawns[1])");

            for (int i = 0; i < solids.Length; i++) CheckRect("solids", i, solids[i]);
            for (int i = 0; i < boxes.Length; i++) CheckRect("boxes", i, boxes[i]);
            void CheckRect(string list, int i, RectData r)
            {
                if (r == null) { Error(list + "[" + i + "] is null"); return; }
                if (!(r.w > 0f) || !(r.h > 0f)) Error(list + "[" + i + "] needs w > 0 and h > 0");
                if (Mathf.Abs(r.x) - r.w / 2f > ArenaHalfWidth || Mathf.Abs(r.y) - r.h / 2f > ArenaHalfHeight)
                    Warn(list + "[" + i + "] is completely outside the arena (±" + ArenaHalfWidth + ", ±" + ArenaHalfHeight + ")");
            }

            for (int s = 0; s < spawns.Length; s++)
            {
                var p = spawns[s];
                if (p == null) { Error("spawns[" + s + "] is null"); continue; }
                if (Mathf.Abs(p.x) > ArenaHalfWidth - 1.5f || Mathf.Abs(p.y) > ArenaHalfHeight - 1.5f)
                    Error("spawns[" + s + "] (" + p.x + ", " + p.y + ") is outside or too close to the arena edge");
                bool ground = false;
                for (int i = 0; i < solids.Length; i++)
                {
                    var r = solids[i];
                    if (r == null) continue;
                    bool insideX = Mathf.Abs(p.x - r.x) < r.w / 2f + 0.62f;
                    bool insideY = Mathf.Abs(p.y - r.y) < r.h / 2f + 0.62f;
                    if (insideX && insideY) Error("spawns[" + s + "] overlaps solids[" + i + "] (a player is ~1.24 wide)");
                    if (Mathf.Abs(p.x - r.x) < r.w / 2f && r.y + r.h / 2f < p.y) ground = true;
                }
                if (!ground) Warn("spawns[" + s + "] has no solid below it: players will fall off the map");
            }
            if (!ok) return null;

            var def = new MapDefinition
            {
                Id = d.id,
                Name = string.IsNullOrWhiteSpace(d.name) ? d.id : d.name,
                TutorialOnly = d.tutorialOnly,
                Solids = Array.ConvertAll(solids, r => new MapBlock(r.x, r.y, r.w, r.h)),
                Boxes = Array.ConvertAll(boxes, r => new MapBlock(r.x, r.y, r.w, r.h)),
                Spawns = Array.ConvertAll(spawns, p => new Vector2(p.x, p.y)),
                Source = source
            };
            return def;
        }

        // ---------------------------------------------------------------- hash

        private static string ComputeHash(SortedDictionary<string, KeyValuePair<string, string>> cards, SortedDictionary<string, KeyValuePair<string, string>> maps,
            List<CardDefinition> loadedCards, List<MapDefinition> loadedMaps)
        {
            var sb = new StringBuilder();
            foreach (var c in loadedCards) sb.Append("card:").Append(c.Id).Append('\n').Append(Normalize(cards[KeyOf(cards, c.Source)].Key)).Append('\n');
            foreach (var m in loadedMaps) sb.Append("map:").Append(m.Id).Append('\n').Append(Normalize(maps[KeyOf(maps, m.Source)].Key)).Append('\n');
            using (var sha = SHA256.Create())
            {
                var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(sb.ToString()));
                var hex = new StringBuilder();
                for (int i = 0; i < 6; i++) hex.Append(bytes[i].ToString("x2"));
                return hex.ToString();
            }
        }

        private static string KeyOf(SortedDictionary<string, KeyValuePair<string, string>> dict, string source)
        {
            foreach (var kv in dict) if (kv.Value.Value == source) return kv.Key;
            return null;
        }

        /// <summary>Whitespace and line endings do not change the hash.</summary>
        private static string Normalize(string json)
        {
            var sb = new StringBuilder(json.Length);
            bool inString = false;
            for (int i = 0; i < json.Length; i++)
            {
                char c = json[i];
                if (c == '"' && (i == 0 || json[i - 1] != '\\')) inString = !inString;
                if (!inString && char.IsWhiteSpace(c)) continue;
                sb.Append(c);
            }
            return sb.ToString();
        }
    }
}

