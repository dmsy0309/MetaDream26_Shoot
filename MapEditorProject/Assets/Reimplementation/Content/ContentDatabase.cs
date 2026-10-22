using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;
using Metadream.Shoot.Abilities;
using Metadream.Shoot.Cards;
using Metadream.Shoot.Maps;
using Metadream.Shoot.Maps.Scripting;

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
        /// <summary>Optional image name in Content/CardArt (default: the card id). Docs/CARD_ART.md.</summary>
        public string image = "";
        /// <summary>"fill" (art rectangle, default) or "icon" (inside the emblem circle).</summary>
        public string imageMode = "";
    }

    [Serializable]
    public sealed class RectData
    {
        public float x, y, w, h;
        /// <summary>Rotation in degrees (counter-clockwise), optional.</summary>
        public float r;
        /// <summary>Optional object name for map scripts (Docs/MAP_SCRIPTING.md).</summary>
        public string name = "";
    }

    [Serializable]
    public sealed class PointData
    {
        public float x, y;
    }

    [Serializable]
    public sealed class MoverData
    {
        public float x, y, w, h, r;
        /// <summary>Waypoints as offsets from (x, y); the platform loops 0 → 1 → … → 0.</summary>
        public PointData[] path = new PointData[0];
        public float speed = 6f;
        public float wait = 1f;
        public string name = "";
    }

    [Serializable]
    public sealed class ZoneData
    {
        public float x, y, w, h, r;
        /// <summary>Jump pad: launch speed along the pad's up direction.</summary>
        public float power = 32f;
        /// <summary>Hazard: damage per second.</summary>
        public float dps = 40f;
        /// <summary>Wind: acceleration (units/s²).</summary>
        public float fx, fy;
        public string name = "";
    }

    /// <summary>Named trigger rectangle for map scripts. Invisible unless show = true.</summary>
    [Serializable]
    public sealed class AreaData
    {
        public float x, y, w, h, r;
        public string name = "";
        public bool show;
        /// <summary>Fill colour when shown ("#rrggbbaa" or a colour name).</summary>
        public string color = "#ffffff26";
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
        public MoverData[] movers = new MoverData[0];
        public ZoneData[] pads = new ZoneData[0];
        public ZoneData[] hazards = new ZoneData[0];
        public ZoneData[] winds = new ZoneData[0];
        /// <summary>Named trigger rectangles for map scripts.</summary>
        public AreaData[] areas = new AreaData[0];
        /// <summary>Map script, one line per string (Docs/MAP_SCRIPTING.md).</summary>
        public string[] script = new string[0];
        /// <summary>C# map behaviors ([MapBehavior] classes) with their parameters.</summary>
        public MapBehaviorSpec[] behaviors = new MapBehaviorSpec[0];
        /// <summary>Free text, e.g. "ROUNDS level12".</summary>
        public string origin = "";
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
                if (card.Image.Length > 0 && !Presentation.CardArt.Exists(card.Image, external))
                    _issues.Add(new ContentIssue(ContentSeverity.Warning, card.Source, "image '" + card.Image + "' not found (Content/CardArt/" + card.Image + ".png or Resources/Content/CardArt): the card shows its letter emblem"));
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
            string imageMode = string.IsNullOrWhiteSpace(d.imageMode) ? Presentation.CardArt.ModeFill : d.imageMode.Trim().ToLowerInvariant();
            if (imageMode != Presentation.CardArt.ModeFill && imageMode != Presentation.CardArt.ModeIcon) Error("imageMode must be \"fill\" or \"icon\", got '" + d.imageMode + "'");
            string image = (d.image ?? "").Trim();
            if (image.Length > 0 && !Presentation.CardArt.IsSafeName(image))
                Error("image '" + image + "' must be a file name without extension or folders (letters, digits, _ and -), e.g. \"ice_rounds\"");
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
                Image = image,
                ImageMode = imageMode,
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
            var movers = d.movers ?? new MoverData[0];
            var pads = d.pads ?? new ZoneData[0];
            var hazards = d.hazards ?? new ZoneData[0];
            var winds = d.winds ?? new ZoneData[0];
            var areas = d.areas ?? new AreaData[0];
            if (string.IsNullOrWhiteSpace(d.name)) Warn("\"name\" is empty");
            if (solids.Length == 0 && movers.Length == 0) Error("needs at least one entry in \"solids\" (or \"movers\")");
            if (spawns.Length < 2) Error("needs at least 2 spawns (team 0 = spawns[0], team 1 = spawns[1])");
            if (spawns.Length > 4) Warn("spawns[4] and later are unused (2 teams × 2 players)");

            for (int i = 0; i < solids.Length; i++) CheckRect("solids", i, solids[i]);
            for (int i = 0; i < boxes.Length; i++) CheckRect("boxes", i, boxes[i]);
            if (boxes.Length + movers.Length > 120) Error("boxes + movers must be at most 120 (network snapshot size), got " + (boxes.Length + movers.Length));
            void CheckRect(string list, int i, RectData r)
            {
                if (r == null) { Error(list + "[" + i + "] is null"); return; }
                CheckArea(list, i, r.x, r.y, r.w, r.h);
            }
            void CheckArea(string list, int i, float x, float y, float w, float h)
            {
                if (!(w > 0f) || !(h > 0f)) Error(list + "[" + i + "] needs w > 0 and h > 0");
                if (Mathf.Abs(x) - Mathf.Max(w, h) / 2f > ArenaHalfWidth || Mathf.Abs(y) - Mathf.Max(w, h) / 2f > ArenaHalfHeight)
                    Warn(list + "[" + i + "] is completely outside the arena (±" + ArenaHalfWidth + ", ±" + ArenaHalfHeight + ")");
            }
            for (int i = 0; i < movers.Length; i++)
            {
                var m = movers[i];
                if (m == null) { Error("movers[" + i + "] is null"); continue; }
                CheckArea("movers", i, m.x, m.y, m.w, m.h);
                if (m.path == null || m.path.Length < 2) Error("movers[" + i + "] needs at least 2 path points (offsets, usually starting with {\"x\":0,\"y\":0})");
                if (!(m.speed > 0f)) Error("movers[" + i + "] needs speed > 0");
                if (m.wait < 0f) Error("movers[" + i + "] needs wait >= 0");
            }
            for (int i = 0; i < pads.Length; i++)
            {
                if (pads[i] == null) { Error("pads[" + i + "] is null"); continue; }
                CheckArea("pads", i, pads[i].x, pads[i].y, pads[i].w, pads[i].h);
                if (!(pads[i].power > 0f)) Error("pads[" + i + "] needs power > 0");
            }
            for (int i = 0; i < hazards.Length; i++)
            {
                if (hazards[i] == null) { Error("hazards[" + i + "] is null"); continue; }
                CheckArea("hazards", i, hazards[i].x, hazards[i].y, hazards[i].w, hazards[i].h);
                if (!(hazards[i].dps > 0f)) Error("hazards[" + i + "] needs dps > 0");
            }
            for (int i = 0; i < winds.Length; i++)
            {
                if (winds[i] == null) { Error("winds[" + i + "] is null"); continue; }
                CheckArea("winds", i, winds[i].x, winds[i].y, winds[i].w, winds[i].h);
                if (winds[i].fx == 0f && winds[i].fy == 0f) Warn("winds[" + i + "] has no force (fx, fy)");
            }
            for (int i = 0; i < areas.Length; i++)
            {
                if (areas[i] == null) { Error("areas[" + i + "] is null"); continue; }
                CheckArea("areas", i, areas[i].x, areas[i].y, areas[i].w, areas[i].h);
                if (string.IsNullOrEmpty(areas[i].name)) Warn("areas[" + i + "] has no \"name\" (scripts cannot use it)");
            }

            // ---- object names (map scripts)
            var objectNames = new List<string>();
            void Named(string list, int i, string name)
            {
                if (string.IsNullOrEmpty(name)) return;
                if (!IsIdentifier(name)) { Error(list + "[" + i + "].name '" + name + "' must use letters, digits and _ only and not start with a digit"); return; }
                if (MdsLibrary.Signatures.ContainsKey(name) || ReservedScriptWords.Contains(name)) { Error(list + "[" + i + "].name '" + name + "' is a reserved script word"); return; }
                if (objectNames.Contains(name)) { Error(list + "[" + i + "].name '" + name + "' is used twice (names must be unique in a map)"); return; }
                objectNames.Add(name);
            }
            for (int i = 0; i < solids.Length; i++) if (solids[i] != null) Named("solids", i, solids[i].name);
            for (int i = 0; i < boxes.Length; i++) if (boxes[i] != null) Named("boxes", i, boxes[i].name);
            for (int i = 0; i < movers.Length; i++) if (movers[i] != null) Named("movers", i, movers[i].name);
            for (int i = 0; i < pads.Length; i++) if (pads[i] != null) Named("pads", i, pads[i].name);
            for (int i = 0; i < hazards.Length; i++) if (hazards[i] != null) Named("hazards", i, hazards[i].name);
            for (int i = 0; i < winds.Length; i++) if (winds[i] != null) Named("winds", i, winds[i].name);
            for (int i = 0; i < areas.Length; i++) if (areas[i] != null) Named("areas", i, areas[i].name);
            if (objectNames.Count > MapRoot.MaxNamedObjects) Error("at most " + MapRoot.MaxNamedObjects + " named objects per map (network snapshot size), got " + objectNames.Count);

            MdsProgram program = null;
            var scriptLines = d.script ?? new string[0];
            if (scriptLines.Length > 0)
            {
                var scriptErrors = new List<MdsError>();
                program = MdsProgram.Parse(scriptLines, objectNames, scriptErrors);
                foreach (var e in scriptErrors) Error(e.ToString() + " → " + ScriptLine(scriptLines, e.Line));
                if (program != null && program.Handlers.Count == 0) Warn("script has no handlers (on start / every / on enter …), it does nothing");
            }
            var behaviors = d.behaviors ?? new MapBehaviorSpec[0];
            for (int i = 0; i < behaviors.Length; i++)
            {
                string err = MapBehaviorRegistry.Validate(behaviors[i], objectNames);
                if (err != null) Error("behaviors[" + i + "]: " + err);
            }

            for (int s = 0; s < spawns.Length; s++)
            {
                var p = spawns[s];
                if (p == null) { Error("spawns[" + s + "] is null"); continue; }
                if (Mathf.Abs(p.x) > ArenaHalfWidth - 1.5f || Mathf.Abs(p.y) > ArenaHalfHeight - 1.5f)
                    Error("spawns[" + s + "] (" + p.x + ", " + p.y + ") is outside or too close to the arena edge");
                bool ground = false;
                var sp = new Vector2(p.x, p.y);
                for (int i = 0; i < solids.Length; i++)
                {
                    var r = solids[i];
                    if (r == null) continue;
                    var block = new MapBlock(r.x, r.y, r.w, r.h, r.r);
                    if (block.Contains(sp, 0.62f)) Error("spawns[" + s + "] overlaps solids[" + i + "] (a player is ~1.24 wide)");
                    if (Mathf.Abs(p.x - r.x) < Mathf.Max(r.w, Mathf.Abs(r.r) > 1f ? r.h : 0f) / 2f && r.y < p.y) ground = true;
                }
                foreach (var m in movers) if (m != null && Mathf.Abs(p.x - m.x) < m.w / 2f + 2f && m.y < p.y) ground = true;
                if (!ground) Warn("spawns[" + s + "] has no solid below it: players will fall off the map");
            }
            if (!ok) return null;

            MapZone Zone(ZoneKind kind, ZoneData z) => new MapZone
            {
                Kind = kind, Block = new MapBlock(z.x, z.y, z.w, z.h, z.r), Power = z.power, Dps = z.dps, Force = new Vector2(z.fx, z.fy), Name = z.name ?? ""
            };
            var zones = new List<MapZone>();
            foreach (var z in pads) zones.Add(Zone(ZoneKind.JumpPad, z));
            foreach (var z in hazards) zones.Add(Zone(ZoneKind.Hazard, z));
            foreach (var z in winds) zones.Add(Zone(ZoneKind.Wind, z));
            var def = new MapDefinition
            {
                Id = d.id,
                Name = string.IsNullOrWhiteSpace(d.name) ? d.id : d.name,
                TutorialOnly = d.tutorialOnly,
                Solids = Array.ConvertAll(solids, r => new MapBlock(r.x, r.y, r.w, r.h, r.r)),
                Boxes = Array.ConvertAll(boxes, r => new MapBlock(r.x, r.y, r.w, r.h, r.r)),
                Spawns = Array.ConvertAll(spawns, p => new Vector2(p.x, p.y)),
                Movers = Array.ConvertAll(movers, m => new MapMover
                {
                    Block = new MapBlock(m.x, m.y, m.w, m.h, m.r),
                    Path = Array.ConvertAll(m.path, p => new Vector2(p.x, p.y)),
                    Speed = m.speed,
                    Wait = m.wait,
                    Name = m.name ?? ""
                }),
                Zones = zones.ToArray(),
                Areas = Array.ConvertAll(areas, a => new MapArea
                {
                    Name = a.name ?? "",
                    Block = new MapBlock(a.x, a.y, a.w, a.h, a.r),
                    Show = a.show,
                    Color = ColorUtility.TryParseHtmlString(a.color ?? "", out var ac) ? ac : new Color(1f, 1f, 1f, 0.15f)
                }),
                SolidNames = Array.ConvertAll(solids, r => r.name ?? ""),
                BoxNames = Array.ConvertAll(boxes, r => r.name ?? ""),
                ObjectNames = objectNames.ToArray(),
                Script = program,
                ScriptSource = scriptLines,
                Behaviors = behaviors,
                Source = source
            };
            return def;
        }

        private static readonly HashSet<string> ReservedScriptWords = new HashSet<string>
        {
            "on", "every", "after", "var", "if", "elif", "else", "end", "repeat", "foreach", "stop", "and", "or", "not",
            "true", "false", "none", "player", "time", "dt", "event_x", "event_y", "start", "tick", "enter", "exit", "inside", "death", "shot"
        };

        private static bool IsIdentifier(string s)
        {
            if (string.IsNullOrEmpty(s) || char.IsDigit(s[0])) return false;
            foreach (char c in s) if (!char.IsLetterOrDigit(c) && c != '_') return false;
            return true;
        }

        private static string ScriptLine(string[] lines, int line) => line >= 1 && line <= lines.Length ? "\"" + (lines[line - 1] ?? "").Trim() + "\"" : "";

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

