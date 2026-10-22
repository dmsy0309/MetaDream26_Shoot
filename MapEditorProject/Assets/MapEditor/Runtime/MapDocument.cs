using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;
using Metadream.Shoot.Content;
using Metadream.Shoot.Maps;
using Metadream.Shoot.Maps.Scripting;

namespace Metadream.MapEditor
{
    /// <summary>Object categories of the game's map JSON (ContentDatabase.MapData).</summary>
    public enum ObjKind { None, Solid, Box, Spawn, Mover, Pad, Hazard, Wind, Area }

    public readonly struct ObjRef : IEquatable<ObjRef>
    {
        public readonly ObjKind Kind;
        public readonly int Index;
        public ObjRef(ObjKind kind, int index) { Kind = kind; Index = index; }
        public static readonly ObjRef None = new ObjRef(ObjKind.None, -1);
        public bool IsNone => Kind == ObjKind.None || Index < 0;
        public bool Equals(ObjRef o) => o.Kind == Kind && o.Index == Index;
        public override bool Equals(object obj) => obj is ObjRef o && Equals(o);
        public override int GetHashCode() => ((int)Kind * 397) ^ Index;
        public static bool operator ==(ObjRef a, ObjRef b) => a.Equals(b);
        public static bool operator !=(ObjRef a, ObjRef b) => !a.Equals(b);
        public override string ToString() => Kind + "[" + Index + "]";
    }

    /// <summary>Centre, size and rotation (degrees, counter-clockwise) shared by every rectangle object.</summary>
    public struct Shape
    {
        public float x, y, w, h, r;
        public Vector2 Center { get => new Vector2(x, y); set { x = value.x; y = value.y; } }
        public Vector2 Size { get => new Vector2(w, h); set { w = value.x; h = value.y; } }
        public MapBlock Block => new MapBlock(x, y, w, h, r);
    }

    /// <summary>
    /// Editor-only background of a map. The current game build draws one fixed background colour
    /// (Palette.Background) for every map and ignores this field (JsonUtility skips unknown keys).
    /// </summary>
    [Serializable]
    public sealed class MapBackground
    {
        public bool enabled;
        public string top = "#10121A";
        public string bottom = "#10121A";

        public static Color GameDefault => Metadream.Shoot.Presentation.Palette.Background;

        public Color TopColor => enabled ? ParseHex(top, GameDefault) : GameDefault;
        public Color BottomColor => enabled ? ParseHex(bottom, GameDefault) : GameDefault;

        public static Color ParseHex(string hex, Color fallback)
        {
            if (string.IsNullOrEmpty(hex)) return fallback;
            string h = hex.Trim();
            if (!h.StartsWith("#")) h = "#" + h;
            return ColorUtility.TryParseHtmlString(h, out var c) ? new Color(c.r, c.g, c.b, 1f) : fallback;
        }

        public static string ToHex(Color c) => "#" + ColorUtility.ToHtmlStringRGB(c);
    }

    [Serializable]
    internal sealed class BackgroundHolder
    {
        public MapBackground background;
    }

    /// <summary>
    /// An editable map. The data object is the game's own MapData class, so every field the game reads is
    /// edited directly; the JSON written by ToJson is the game's map format (plus the optional editor-only
    /// "background" object).
    /// </summary>
    public sealed class MapDocument
    {
        public MapData Data = new MapData();
        public MapBackground Background = new MapBackground();
        /// <summary>File this document was opened from or saved to (null = never saved).</summary>
        public string FilePath;

        public static MapDocument CreateDefault()
        {
            var doc = new MapDocument();
            doc.Data.id = "my_map";
            doc.Data.name = "새 맵";
            doc.Data.solids = new[]
            {
                new RectData { x = 0f, y = -14f, w = 50f, h = 6f },
                new RectData { x = 0f, y = -2f, w = 10f, h = 1.2f }
            };
            doc.Data.spawns = new[] { new PointData { x = -15f, y = -9f }, new PointData { x = 15f, y = -9f } };
            doc.Normalize();
            return doc;
        }

        /// <summary>Reads a game map JSON file (or a file written by this editor).</summary>
        public static MapDocument FromJson(string json, string fileKey = null)
        {
            if (string.IsNullOrWhiteSpace(json)) throw new FormatException("빈 파일입니다");
            MapData data;
            try { data = JsonUtility.FromJson<MapData>(json); }
            catch (Exception e) { throw new FormatException("JSON 형식 오류: " + e.Message); }
            if (data == null) throw new FormatException("맵 JSON이 아닙니다");
            var doc = new MapDocument { Data = data };
            try
            {
                var holder = JsonUtility.FromJson<BackgroundHolder>(json);
                if (holder != null && holder.background != null) doc.Background = holder.background;
            }
            catch (Exception) { /* background is optional */ }
            if (string.IsNullOrWhiteSpace(doc.Data.id) && !string.IsNullOrEmpty(fileKey)) doc.Data.id = fileKey;
            doc.Normalize();
            return doc;
        }

        /// <summary>Replace nulls left by JsonUtility so the editor can index every list.</summary>
        public void Normalize()
        {
            Data.id = Data.id ?? "";
            Data.name = Data.name ?? "";
            Data.origin = Data.origin ?? "";
            Data.solids = Data.solids ?? new RectData[0];
            Data.boxes = Data.boxes ?? new RectData[0];
            Data.spawns = Data.spawns ?? new PointData[0];
            Data.movers = Data.movers ?? new MoverData[0];
            Data.pads = Data.pads ?? new ZoneData[0];
            Data.hazards = Data.hazards ?? new ZoneData[0];
            Data.winds = Data.winds ?? new ZoneData[0];
            Data.areas = Data.areas ?? new AreaData[0];
            Data.script = Data.script ?? new string[0];
            Data.behaviors = Data.behaviors ?? new MapBehaviorSpec[0];
            foreach (var m in Data.movers) if (m != null && m.path == null) m.path = new PointData[0];
            foreach (var r in Data.solids) if (r != null && r.name == null) r.name = "";
            foreach (var r in Data.boxes) if (r != null && r.name == null) r.name = "";
            foreach (var m in Data.movers) if (m != null && m.name == null) m.name = "";
            foreach (var list in new[] { Data.pads, Data.hazards, Data.winds }) foreach (var z in list) if (z != null && z.name == null) z.name = "";
            foreach (var a in Data.areas) if (a != null) { a.name = a.name ?? ""; a.color = a.color ?? "#ffffff26"; }
            for (int i = 0; i < Data.script.Length; i++) Data.script[i] = Data.script[i] ?? "";
            if (Background == null) Background = new MapBackground();
        }

        public MapDocument Clone()
        {
            var copy = FromJson(ToJson(true));
            copy.FilePath = FilePath;
            return copy;
        }

        // ------------------------------------------------------------------ JSON (game format)

        /// <summary>
        /// Game map JSON. includeEditorData adds the editor-only "background" object (ignored by the game).
        /// Only keys of ContentDatabase.MapData are written otherwise; type-specific values are always explicit
        /// so the game's field defaults never change the meaning.
        /// </summary>
        public string ToJson(bool includeEditorData)
        {
            var sb = new StringBuilder();
            sb.Append("{\n");
            Prop(sb, "id", Str(Data.id)).Append(",\n");
            Prop(sb, "name", Str(Data.name)).Append(",\n");
            Prop(sb, "tutorialOnly", Data.tutorialOnly ? "true" : "false");
            if (Data.disabled) { sb.Append(",\n"); Prop(sb, "disabled", "true"); }
            if (!string.IsNullOrEmpty(Data.origin)) { sb.Append(",\n"); Prop(sb, "origin", Str(Data.origin)); }
            Array(sb, "solids", Data.solids, r => Rect(r), true);
            Array(sb, "boxes", Data.boxes, r => Rect(r), true);
            Array(sb, "spawns", Data.spawns, p => "{ \"x\": " + Num(p.x) + ", \"y\": " + Num(p.y) + " }", true);
            Array(sb, "movers", Data.movers, Mover, false);
            Array(sb, "pads", Data.pads, z => Zone(z, "\"power\": " + Num(z.power)), false);
            Array(sb, "hazards", Data.hazards, z => Zone(z, "\"dps\": " + Num(z.dps)), false);
            Array(sb, "winds", Data.winds, z => Zone(z, "\"fx\": " + Num(z.fx) + ", \"fy\": " + Num(z.fy)), false);
            Array(sb, "areas", Data.areas, AreaJson, false);
            Array(sb, "behaviors", Data.behaviors, BehaviorJson, false);
            if (Data.script != null && Data.script.Length > 0)
            {
                sb.Append(",\n    \"script\": [\n");
                for (int i = 0; i < Data.script.Length; i++)
                    sb.Append("        ").Append(Str(Data.script[i] ?? "")).Append(i < Data.script.Length - 1 ? ",\n" : "\n");
                sb.Append("    ]");
            }
            if (includeEditorData && Background != null && Background.enabled)
            {
                sb.Append(",\n");
                Prop(sb, "background", "{ \"enabled\": true, \"top\": " + Str(Background.top) + ", \"bottom\": " + Str(Background.bottom) + " }");
            }
            sb.Append("\n}\n");
            return sb.ToString();
        }

        private static StringBuilder Prop(StringBuilder sb, string key, string value) => sb.Append("    \"").Append(key).Append("\": ").Append(value);

        private static void Array<T>(StringBuilder sb, string key, T[] items, Func<T, string> write, bool always)
        {
            if (!always && (items == null || items.Length == 0)) return;
            sb.Append(",\n");
            if (items == null || items.Length == 0) { Prop(sb, key, "[]"); return; }
            sb.Append("    \"").Append(key).Append("\": [\n");
            int written = 0;
            for (int i = 0; i < items.Length; i++)
            {
                if (items[i] == null) continue;
                if (written++ > 0) sb.Append(",\n");
                sb.Append("        ").Append(write(items[i]));
            }
            sb.Append("\n    ]");
        }

        /// <summary>"name" (map scripts) is written first and only when set.</summary>
        private static string NamePart(string name) => string.IsNullOrEmpty(name) ? "" : "\"name\": " + Str(name) + ", ";

        private static string Rect(RectData r) =>
            "{ " + NamePart(r.name) + "\"x\": " + Num(r.x) + ", \"y\": " + Num(r.y) + ", \"w\": " + Num(r.w) + ", \"h\": " + Num(r.h) + (r.r != 0f ? ", \"r\": " + Num(r.r) : "") + " }";

        private static string Zone(ZoneData z, string extra) =>
            "{ " + NamePart(z.name) + "\"x\": " + Num(z.x) + ", \"y\": " + Num(z.y) + ", \"w\": " + Num(z.w) + ", \"h\": " + Num(z.h) + (z.r != 0f ? ", \"r\": " + Num(z.r) : "") + ", " + extra + " }";

        private static string AreaJson(AreaData a) =>
            "{ " + NamePart(a.name) + "\"x\": " + Num(a.x) + ", \"y\": " + Num(a.y) + ", \"w\": " + Num(a.w) + ", \"h\": " + Num(a.h) + (a.r != 0f ? ", \"r\": " + Num(a.r) : "")
            + ", \"show\": " + (a.show ? "true" : "false") + ", \"color\": " + Str(a.color ?? "#ffffff26") + " }";

        /// <summary>Behavior spec with the fields that differ from MapBehaviorSpec's defaults (omitted = same value).</summary>
        public static string BehaviorJson(MapBehaviorSpec b)
        {
            var d = new MapBehaviorSpec();
            var parts = new List<string> { "\"type\": " + Str(b.type ?? "") };
            void S(string key, string v, string def) { if ((v ?? "") != def) parts.Add("\"" + key + "\": " + Str(v ?? "")); }
            void F(string key, float v, float def) { if (v != def) parts.Add("\"" + key + "\": " + Num(v)); }
            S("target", b.target, d.target);
            S("area", b.area, d.area);
            F("speed", b.speed, d.speed);
            F("amount", b.amount, d.amount);
            F("interval", b.interval, d.interval);
            F("delay", b.delay, d.delay);
            F("duration", b.duration, d.duration);
            F("damage", b.damage, d.damage);
            F("radius", b.radius, d.radius);
            F("x", b.x, d.x);
            F("y", b.y, d.y);
            S("text", b.text, d.text);
            S("sound", b.sound, d.sound);
            return "{ " + string.Join(", ", parts) + " }";
        }

        private static string Mover(MoverData m)
        {
            var path = new StringBuilder();
            var pts = m.path ?? new PointData[0];
            for (int i = 0; i < pts.Length; i++)
            {
                if (pts[i] == null) continue;
                if (path.Length > 0) path.Append(", ");
                path.Append("{ \"x\": ").Append(Num(pts[i].x)).Append(", \"y\": ").Append(Num(pts[i].y)).Append(" }");
            }
            return "{ \"x\": " + Num(m.x) + ", \"y\": " + Num(m.y) + ", \"w\": " + Num(m.w) + ", \"h\": " + Num(m.h) + (m.r != 0f ? ", \"r\": " + Num(m.r) : "")
                + ", \"path\": [ " + path + " ], \"speed\": " + Num(m.speed) + ", \"wait\": " + Num(m.wait) + " }";
        }

        /// <summary>Shortest text that parses back to the same float (JSON has no NaN/Infinity).</summary>
        public static string Num(float v)
        {
            if (float.IsNaN(v) || float.IsInfinity(v)) v = 0f;
            if (v == 0f) return "0";
            if (Mathf.Abs(v) < 1e7f && v == Mathf.Round(v)) return ((long)v).ToString(CultureInfo.InvariantCulture);
            string s = v.ToString("R", CultureInfo.InvariantCulture);
            if (float.Parse(s, CultureInfo.InvariantCulture) != v) s = v.ToString("G9", CultureInfo.InvariantCulture);
            return s;
        }

        public static string Str(string s)
        {
            var sb = new StringBuilder("\"");
            foreach (char c in s ?? "")
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else sb.Append(c);
                        break;
                }
            }
            return sb.Append('"').ToString();
        }

        // ------------------------------------------------------------------ preview definition

        /// <summary>
        /// Same conversion as ContentDatabase.ParseMap but without validation, so a half-finished map can still
        /// be drawn by the game's MapRoot. Play tests and exports always go through the game's ParseMap.
        /// </summary>
        public MapDefinition ToPreviewDefinition()
        {
            Normalize();
            var zones = new List<MapZone>();
            MapZone Zone(ZoneKind kind, ZoneData z) => new MapZone
            {
                Kind = kind, Block = new MapBlock(z.x, z.y, z.w, z.h, z.r), Power = z.power, Dps = z.dps, Force = new Vector2(z.fx, z.fy), Name = z.name ?? ""
            };
            foreach (var z in Data.pads) if (z != null) zones.Add(Zone(ZoneKind.JumpPad, z));
            foreach (var z in Data.hazards) if (z != null) zones.Add(Zone(ZoneKind.Hazard, z));
            foreach (var z in Data.winds) if (z != null) zones.Add(Zone(ZoneKind.Wind, z));
            var movers = new List<MapMover>();
            foreach (var m in Data.movers)
            {
                if (m == null) continue;
                movers.Add(new MapMover
                {
                    Block = new MapBlock(m.x, m.y, Mathf.Max(0.01f, m.w), Mathf.Max(0.01f, m.h), m.r),
                    Name = m.name ?? "",
                    Path = System.Array.ConvertAll(m.path ?? new PointData[0], p => p != null ? new Vector2(p.x, p.y) : Vector2.zero),
                    Speed = m.speed,
                    Wait = m.wait
                });
            }
            MapBlock Block(RectData r) => new MapBlock(r.x, r.y, Mathf.Max(0.01f, r.w), Mathf.Max(0.01f, r.h), r.r);
            var solids = new List<MapBlock>();
            var solidNames = new List<string>();
            foreach (var r in Data.solids) if (r != null) { solids.Add(Block(r)); solidNames.Add(r.name ?? ""); }
            var boxes = new List<MapBlock>();
            var boxNames = new List<string>();
            foreach (var r in Data.boxes) if (r != null) { boxes.Add(Block(r)); boxNames.Add(r.name ?? ""); }
            var areas = new List<MapArea>();
            foreach (var a in Data.areas)
            {
                if (a == null) continue;
                areas.Add(new MapArea
                {
                    Name = a.name ?? "",
                    Block = new MapBlock(a.x, a.y, Mathf.Max(0.01f, a.w), Mathf.Max(0.01f, a.h), a.r),
                    Show = a.show,
                    Color = ColorUtility.TryParseHtmlString(a.color ?? "", out var ac) ? ac : new Color(1f, 1f, 1f, 0.15f)
                });
            }
            var spawns = new List<Vector2>();
            foreach (var p in Data.spawns) if (p != null) spawns.Add(new Vector2(p.x, p.y));
            if (spawns.Count == 0) spawns.Add(Vector2.zero);
            return new MapDefinition
            {
                Id = Data.id,
                Name = string.IsNullOrWhiteSpace(Data.name) ? Data.id : Data.name,
                TutorialOnly = Data.tutorialOnly,
                Solids = solids.ToArray(),
                Boxes = boxes.ToArray(),
                Spawns = spawns.ToArray(),
                Movers = movers.ToArray(),
                Zones = zones.ToArray(),
                Areas = areas.ToArray(),
                SolidNames = solidNames.ToArray(),
                BoxNames = boxNames.ToArray(),
                ObjectNames = NamesInGameOrder().ToArray(),
                // The preview never runs map scripts or C# behaviors; play tests use the game's loader.
                Script = null,
                Behaviors = new MapBehaviorSpec[0],
                Source = "editor preview"
            };
        }

        /// <summary>Distinct object names in the order the game numbers them (solids, boxes, movers, pads, hazards, winds, areas).</summary>
        public List<string> NamesInGameOrder()
        {
            var names = new List<string>();
            foreach (var o in All())
            {
                if (o.Kind == ObjKind.Spawn) continue;
                string n = GetName(o);
                if (!string.IsNullOrEmpty(n) && !names.Contains(n)) names.Add(n);
            }
            return names;
        }

        public string GetName(ObjRef o)
        {
            if (!Exists(o)) return "";
            switch (o.Kind)
            {
                case ObjKind.Solid:
                case ObjKind.Box: return RectAt(o).name ?? "";
                case ObjKind.Mover: return MoverAt(o).name ?? "";
                case ObjKind.Area: return AreaAt(o).name ?? "";
                case ObjKind.Spawn: return "";
                default: return ZoneAt(o).name ?? "";
            }
        }

        public void SetName(ObjRef o, string name)
        {
            if (!Exists(o)) return;
            name = name ?? "";
            switch (o.Kind)
            {
                case ObjKind.Solid:
                case ObjKind.Box: RectAt(o).name = name; break;
                case ObjKind.Mover: MoverAt(o).name = name; break;
                case ObjKind.Area: AreaAt(o).name = name; break;
                case ObjKind.Spawn: break;
                default: ZoneAt(o).name = name; break;
            }
        }

        public ObjRef FindByName(string name)
        {
            if (string.IsNullOrEmpty(name)) return ObjRef.None;
            foreach (var o in All()) if (GetName(o) == name) return o;
            return ObjRef.None;
        }

        /// <summary>Name not used yet in this map (base, base_2, base_3 …).</summary>
        public string UniqueName(string baseName)
        {
            if (string.IsNullOrEmpty(baseName)) return "";
            var used = new HashSet<string>(NamesInGameOrder());
            if (!used.Contains(baseName)) return baseName;
            string stem = baseName;
            int us = baseName.LastIndexOf('_');
            if (us > 0 && int.TryParse(baseName.Substring(us + 1), out _)) stem = baseName.Substring(0, us);
            for (int i = 2; i < 1000; i++) if (!used.Contains(stem + "_" + i)) return stem + "_" + i;
            return "";
        }

        // ------------------------------------------------------------------ object access

        public static bool IsRect(ObjKind k) => k != ObjKind.Spawn && k != ObjKind.None;
        public static bool CanBeNamed(ObjKind k) => k != ObjKind.Spawn && k != ObjKind.None;
        public static bool IsZone(ObjKind k) => k == ObjKind.Pad || k == ObjKind.Hazard || k == ObjKind.Wind;

        public int Count(ObjKind k)
        {
            switch (k)
            {
                case ObjKind.Solid: return Data.solids.Length;
                case ObjKind.Box: return Data.boxes.Length;
                case ObjKind.Spawn: return Data.spawns.Length;
                case ObjKind.Mover: return Data.movers.Length;
                case ObjKind.Pad: return Data.pads.Length;
                case ObjKind.Hazard: return Data.hazards.Length;
                case ObjKind.Wind: return Data.winds.Length;
                case ObjKind.Area: return Data.areas.Length;
                default: return 0;
            }
        }

        public bool Exists(ObjRef o) => !o.IsNone && o.Index < Count(o.Kind) && Raw(o) != null;

        private object Raw(ObjRef o)
        {
            switch (o.Kind)
            {
                case ObjKind.Solid: return Data.solids[o.Index];
                case ObjKind.Box: return Data.boxes[o.Index];
                case ObjKind.Spawn: return Data.spawns[o.Index];
                case ObjKind.Mover: return Data.movers[o.Index];
                case ObjKind.Pad: return Data.pads[o.Index];
                case ObjKind.Hazard: return Data.hazards[o.Index];
                case ObjKind.Wind: return Data.winds[o.Index];
                case ObjKind.Area: return Data.areas[o.Index];
                default: return null;
            }
        }

        public RectData RectAt(ObjRef o) => o.Kind == ObjKind.Solid ? Data.solids[o.Index] : o.Kind == ObjKind.Box ? Data.boxes[o.Index] : null;
        public ZoneData ZoneAt(ObjRef o) => o.Kind == ObjKind.Pad ? Data.pads[o.Index] : o.Kind == ObjKind.Hazard ? Data.hazards[o.Index] : o.Kind == ObjKind.Wind ? Data.winds[o.Index] : null;
        public MoverData MoverAt(ObjRef o) => o.Kind == ObjKind.Mover ? Data.movers[o.Index] : null;
        public PointData SpawnAt(ObjRef o) => o.Kind == ObjKind.Spawn ? Data.spawns[o.Index] : null;
        public AreaData AreaAt(ObjRef o) => o.Kind == ObjKind.Area ? Data.areas[o.Index] : null;

        public Shape GetShape(ObjRef o)
        {
            var s = new Shape();
            if (!Exists(o)) return s;
            switch (o.Kind)
            {
                case ObjKind.Solid:
                case ObjKind.Box: { var r = RectAt(o); s.x = r.x; s.y = r.y; s.w = r.w; s.h = r.h; s.r = r.r; break; }
                case ObjKind.Mover: { var m = MoverAt(o); s.x = m.x; s.y = m.y; s.w = m.w; s.h = m.h; s.r = m.r; break; }
                case ObjKind.Spawn: { var p = SpawnAt(o); s.x = p.x; s.y = p.y; s.w = 1.24f; s.h = 1.24f; break; }
                case ObjKind.Area: { var a = AreaAt(o); s.x = a.x; s.y = a.y; s.w = a.w; s.h = a.h; s.r = a.r; break; }
                default: { var z = ZoneAt(o); s.x = z.x; s.y = z.y; s.w = z.w; s.h = z.h; s.r = z.r; break; }
            }
            return s;
        }

        public void SetShape(ObjRef o, Shape s)
        {
            if (!Exists(o)) return;
            switch (o.Kind)
            {
                case ObjKind.Solid:
                case ObjKind.Box: { var r = RectAt(o); r.x = s.x; r.y = s.y; r.w = s.w; r.h = s.h; r.r = s.r; break; }
                case ObjKind.Mover: { var m = MoverAt(o); m.x = s.x; m.y = s.y; m.w = s.w; m.h = s.h; m.r = s.r; break; }
                case ObjKind.Spawn: { var p = SpawnAt(o); p.x = s.x; p.y = s.y; break; }
                case ObjKind.Area: { var a = AreaAt(o); a.x = s.x; a.y = s.y; a.w = s.w; a.h = s.h; a.r = s.r; break; }
                default: { var z = ZoneAt(o); z.x = s.x; z.y = s.y; z.w = s.w; z.h = s.h; z.r = s.r; break; }
            }
        }

        public IEnumerable<ObjRef> All()
        {
            foreach (ObjKind k in new[] { ObjKind.Solid, ObjKind.Box, ObjKind.Mover, ObjKind.Pad, ObjKind.Hazard, ObjKind.Wind, ObjKind.Area, ObjKind.Spawn })
                for (int i = 0; i < Count(k); i++)
                    if (Raw(new ObjRef(k, i)) != null) yield return new ObjRef(k, i);
        }

        /// <summary>Default object for a creation click (sizes follow the examples in CONTENT_JSON_GUIDE.md).</summary>
        public ObjRef Add(ObjKind kind, Vector2 center, Vector2? size = null)
        {
            Normalize();
            Vector2 sz = size ?? DefaultSize(kind);
            switch (kind)
            {
                case ObjKind.Solid: Data.solids = Append(Data.solids, new RectData { x = center.x, y = center.y, w = sz.x, h = sz.y }); return new ObjRef(kind, Data.solids.Length - 1);
                case ObjKind.Box: Data.boxes = Append(Data.boxes, new RectData { x = center.x, y = center.y, w = sz.x, h = sz.y }); return new ObjRef(kind, Data.boxes.Length - 1);
                case ObjKind.Spawn: Data.spawns = Append(Data.spawns, new PointData { x = center.x, y = center.y }); return new ObjRef(kind, Data.spawns.Length - 1);
                case ObjKind.Mover:
                    Data.movers = Append(Data.movers, new MoverData
                    {
                        x = center.x, y = center.y, w = sz.x, h = sz.y,
                        path = new[] { new PointData { x = 0f, y = 0f }, new PointData { x = 0f, y = 8f } }, speed = 5f, wait = 1f
                    });
                    return new ObjRef(kind, Data.movers.Length - 1);
                case ObjKind.Pad: Data.pads = Append(Data.pads, new ZoneData { x = center.x, y = center.y, w = sz.x, h = sz.y, power = 34f }); return new ObjRef(kind, Data.pads.Length - 1);
                case ObjKind.Hazard: Data.hazards = Append(Data.hazards, new ZoneData { x = center.x, y = center.y, w = sz.x, h = sz.y, dps = 60f }); return new ObjRef(kind, Data.hazards.Length - 1);
                case ObjKind.Wind: Data.winds = Append(Data.winds, new ZoneData { x = center.x, y = center.y, w = sz.x, h = sz.y, fx = 0f, fy = 26f }); return new ObjRef(kind, Data.winds.Length - 1);
                case ObjKind.Area:
                {
                    var a = new AreaData { x = center.x, y = center.y, w = sz.x, h = sz.y, show = true, color = "#40ff9060" };
                    Data.areas = Append(Data.areas, a);
                    a.name = UniqueName("area");
                    return new ObjRef(kind, Data.areas.Length - 1);
                }
                default: return ObjRef.None;
            }
        }

        public static Vector2 DefaultSize(ObjKind kind)
        {
            switch (kind)
            {
                case ObjKind.Solid: return new Vector2(10f, 1.2f);
                case ObjKind.Box: return new Vector2(2f, 2f);
                case ObjKind.Mover: return new Vector2(7f, 1.2f);
                case ObjKind.Pad: return new Vector2(3f, 0.6f);
                case ObjKind.Hazard: return new Vector2(10f, 2f);
                case ObjKind.Wind: return new Vector2(8f, 16f);
                case ObjKind.Area: return new Vector2(4f, 2f);
                default: return new Vector2(1.24f, 1.24f);
            }
        }

        public void Remove(ObjRef o)
        {
            if (!Exists(o)) return;
            switch (o.Kind)
            {
                case ObjKind.Solid: Data.solids = RemoveAt(Data.solids, o.Index); break;
                case ObjKind.Box: Data.boxes = RemoveAt(Data.boxes, o.Index); break;
                case ObjKind.Spawn: Data.spawns = RemoveAt(Data.spawns, o.Index); break;
                case ObjKind.Mover: Data.movers = RemoveAt(Data.movers, o.Index); break;
                case ObjKind.Pad: Data.pads = RemoveAt(Data.pads, o.Index); break;
                case ObjKind.Hazard: Data.hazards = RemoveAt(Data.hazards, o.Index); break;
                case ObjKind.Wind: Data.winds = RemoveAt(Data.winds, o.Index); break;
                case ObjKind.Area: Data.areas = RemoveAt(Data.areas, o.Index); break;
            }
        }

        /// <summary>Copy of one object as standalone JSON (clipboard).</summary>
        public string CopyObject(ObjRef o)
        {
            if (!Exists(o)) return null;
            return o.Kind + "|" + JsonUtility.ToJson(Raw(o));
        }

        public ObjRef PasteObject(string clip, Vector2 offset)
        {
            if (string.IsNullOrEmpty(clip)) return ObjRef.None;
            int bar = clip.IndexOf('|');
            if (bar <= 0 || !Enum.TryParse(clip.Substring(0, bar), out ObjKind kind)) return ObjRef.None;
            string json = clip.Substring(bar + 1);
            try
            {
                ObjRef added;
                switch (kind)
                {
                    case ObjKind.Solid: Data.solids = Append(Data.solids, JsonUtility.FromJson<RectData>(json)); added = new ObjRef(kind, Data.solids.Length - 1); break;
                    case ObjKind.Box: Data.boxes = Append(Data.boxes, JsonUtility.FromJson<RectData>(json)); added = new ObjRef(kind, Data.boxes.Length - 1); break;
                    case ObjKind.Spawn: Data.spawns = Append(Data.spawns, JsonUtility.FromJson<PointData>(json)); added = new ObjRef(kind, Data.spawns.Length - 1); break;
                    case ObjKind.Mover: Data.movers = Append(Data.movers, JsonUtility.FromJson<MoverData>(json)); added = new ObjRef(kind, Data.movers.Length - 1); break;
                    case ObjKind.Pad: Data.pads = Append(Data.pads, JsonUtility.FromJson<ZoneData>(json)); added = new ObjRef(kind, Data.pads.Length - 1); break;
                    case ObjKind.Hazard: Data.hazards = Append(Data.hazards, JsonUtility.FromJson<ZoneData>(json)); added = new ObjRef(kind, Data.hazards.Length - 1); break;
                    case ObjKind.Wind: Data.winds = Append(Data.winds, JsonUtility.FromJson<ZoneData>(json)); added = new ObjRef(kind, Data.winds.Length - 1); break;
                    case ObjKind.Area: Data.areas = Append(Data.areas, JsonUtility.FromJson<AreaData>(json)); added = new ObjRef(kind, Data.areas.Length - 1); break;
                    default: return ObjRef.None;
                }
                Normalize();
                // Names must stay unique in a map: a copy gets name_2, name_3 …
                string pastedName = GetName(added);
                if (!string.IsNullOrEmpty(pastedName))
                {
                    SetName(added, "");
                    SetName(added, UniqueName(pastedName));
                }
                var s = GetShape(added);
                s.Center += offset;
                SetShape(added, s);
                return added;
            }
            catch (Exception) { return ObjRef.None; }
        }

        public ObjRef Duplicate(ObjRef o, Vector2 offset) => PasteObject(CopyObject(o), offset);

        // ------------------------------------------------------------------ 2v2 teammate spawns

        public const string TeamSpawnBegin = "# [Map Maker] 2대2 팀원 개별 스폰 — 자동 생성: spawns[2]·spawns[3] 위치로 갱신됩니다 (직접 고치지 마세요)";
        public const string TeamSpawnEnd = "# [Map Maker] 2대2 팀원 개별 스폰 끝";
        public static readonly float TeammateOffset = 2.6f;

        public static string SpawnRole(int index)
        {
            switch (index)
            {
                case 0: return "주황 1";
                case 1: return "파랑 1";
                case 2: return "주황 2 (2대2)";
                case 3: return "파랑 2 (2대2)";
                default: return "미사용";
            }
        }

        /// <summary>Where the game puts the second player of a team without a custom spawn (MapRoot.SpawnPoint).</summary>
        public Vector2 DefaultTeammateSpawn(int team)
        {
            var p = Data.spawns[team];
            return new Vector2(p.x + (p.x < 0f ? 1f : -1f) * TeammateOffset, p.y);
        }

        public bool HasTeamSpawnScript => System.Array.IndexOf(Data.script ?? new string[0], TeamSpawnBegin) >= 0;

        /// <summary>Tests only: force the native-support answer.</summary>
        public static bool? NativeTeamSpawnsOverride;
        private static bool? _nativeTeamSpawns;

        /// <summary>
        /// True when the synced game runtime's MapRoot.SpawnPoint itself puts the second teammate on spawns[2]/[3]
        /// (Docs/GAME_PATCH_TEAM_SPAWNS.md). Then the game places teammates from the "준비" moment and no script is needed.
        /// Probed once with a throwaway map built by the game's own code.
        /// </summary>
        public static bool GameNativeTeamSpawns
        {
            get
            {
                if (NativeTeamSpawnsOverride.HasValue) return NativeTeamSpawnsOverride.Value;
                if (_nativeTeamSpawns == null) _nativeTeamSpawns = ProbeNativeTeamSpawns();
                return _nativeTeamSpawns.Value;
            }
        }

        private static bool ProbeNativeTeamSpawns()
        {
            MapRoot root = null;
            try
            {
                var def = new MapDefinition
                {
                    Id = "mapmaker_probe", Name = "probe",
                    Spawns = new[] { new Vector2(-10f, 0f), new Vector2(10f, 0f), new Vector2(-3f, 5f), new Vector2(3f, 5f) }
                };
                root = MapRoot.Build(def, Vector2.zero);
                return (root.SpawnPoint(0, 1) - new Vector2(-3f, 5f)).sqrMagnitude < 1e-4f && (root.SpawnPoint(1, 1) - new Vector2(3f, 5f)).sqrMagnitude < 1e-4f;
            }
            catch (Exception) { return false; }
            finally { if (root != null) UnityEngine.Object.DestroyImmediate(root.gameObject); }
        }

        /// <summary>2v2 teammate spawns in effect: natively (spawns[2]/[3] exist) or through the generated script block.</summary>
        public bool TeamSpawnsEnabled => GameNativeTeamSpawns ? Data.spawns.Length >= 3 : HasTeamSpawnScript;

        /// <summary>
        /// The game places a team's second player 2.6 beside the team spawn (spawns[2] and later are not used for
        /// placement). This generated map-script block moves player 2 (orange #2) and player 3 (blue #2) to spawns[2] /
        /// spawns[3] once per point (each point builds a new map, so the flags start at 0 again). It does not react to
        /// deaths: a "respawns" card revives a player where they died (Fighter.PhoenixRevive), as in the game.
        /// </summary>
        public List<string> BuildTeamSpawnBlock()
        {
            var lines = new List<string>();
            if (GameNativeTeamSpawns) return lines;
            var ids = new List<int>();
            for (int id = 2; id <= 3; id++) if (id < Data.spawns.Length && Data.spawns[id] != null) ids.Add(id);
            if (ids.Count == 0) return lines;
            lines.Add(TeamSpawnBegin);
            foreach (int id in ids) lines.Add("var mm_spawn" + id + " = 0");
            lines.Add("on tick");
            lines.Add("  foreach player");
            foreach (int id in ids)
            {
                var p = Data.spawns[id];
                lines.Add("    if player.id == " + id + " and mm_spawn" + id + " == 0");
                lines.Add("      mm_spawn" + id + " = 1");
                lines.Add("      teleport(player, " + Num(p.x) + ", " + Num(p.y) + ")");
                lines.Add("    end");
            }
            lines.Add("  end");
            lines.Add("end");
            lines.Add(TeamSpawnEnd);
            return lines;
        }

        private List<string> ScriptWithoutTeamBlock()
        {
            var result = new List<string>();
            var src = Data.script ?? new string[0];
            bool inside = false;
            foreach (var l in src)
            {
                if (l == TeamSpawnBegin) { inside = true; continue; }
                if (inside) { if (l == TeamSpawnEnd) inside = false; continue; }
                result.Add(l);
            }
            while (result.Count > 0 && string.IsNullOrWhiteSpace(result[result.Count - 1])) result.RemoveAt(result.Count - 1);
            return result;
        }

        /// <summary>Turns the generated block on (adding missing teammate spawns at the game's default spot) or off.</summary>
        public void SetTeamSpawns(bool enabled, bool addMissing = true)
        {
            Normalize();
            if (enabled && addMissing && Data.spawns.Length >= 2)
            {
                var spawns = new List<PointData>(Data.spawns);
                for (int team = 0; team < 2 && spawns.Count < 4; team++)
                    if (spawns.Count == 2 + team)
                    {
                        var d = DefaultTeammateSpawn(team);
                        spawns.Add(new PointData { x = d.x, y = d.y });
                    }
                Data.spawns = spawns.ToArray();
            }
            var script = ScriptWithoutTeamBlock();
            if (enabled)
            {
                var block = BuildTeamSpawnBlock();
                if (block.Count > 0)
                {
                    if (script.Count > 0) script.Add("");
                    script.AddRange(block);
                }
            }
            Data.script = script.ToArray();
            if (!enabled && GameNativeTeamSpawns && Data.spawns.Length > 2)
            {
                // With native support every spawns[2]/[3] is used, so "off" means removing them.
                var keep = new List<PointData>(Data.spawns);
                keep.RemoveRange(2, keep.Count - 2);
                Data.spawns = keep.ToArray();
            }
        }

        /// <summary>Keeps the generated block in step with spawns[2]/[3]. Returns true when the script changed.</summary>
        public bool SyncTeamSpawnScript()
        {
            if (!HasTeamSpawnScript) return false;
            string before = string.Join("\n", Data.script);
            var script = ScriptWithoutTeamBlock();
            var block = BuildTeamSpawnBlock();
            if (block.Count > 0)
            {
                if (script.Count > 0) script.Add("");
                script.AddRange(block);
            }
            Data.script = script.ToArray();
            return string.Join("\n", Data.script) != before;
        }

        public void SwapSpawns(int a, int b)
        {
            if (a < 0 || b < 0 || a >= Data.spawns.Length || b >= Data.spawns.Length || a == b) return;
            (Data.spawns[a], Data.spawns[b]) = (Data.spawns[b], Data.spawns[a]);
        }

        private static T[] Append<T>(T[] list, T item)
        {
            var l = new List<T>(list ?? new T[0]) { item };
            return l.ToArray();
        }

        private static T[] RemoveAt<T>(T[] list, int index)
        {
            var l = new List<T>(list);
            l.RemoveAt(index);
            return l.ToArray();
        }

        public static string KindName(ObjKind k)
        {
            switch (k)
            {
                case ObjKind.Solid: return "지형";
                case ObjKind.Box: return "물리 상자";
                case ObjKind.Spawn: return "스폰";
                case ObjKind.Mover: return "움직이는 발판";
                case ObjKind.Pad: return "점프대";
                case ObjKind.Hazard: return "피해 구역";
                case ObjKind.Wind: return "바람 구역";
                case ObjKind.Area: return "트리거 영역";
                default: return "";
            }
        }

        /// <summary>JSON list name used by the game's validation messages (e.g. "solids[3]").</summary>
        public static string JsonList(ObjKind k)
        {
            switch (k)
            {
                case ObjKind.Solid: return "solids";
                case ObjKind.Box: return "boxes";
                case ObjKind.Spawn: return "spawns";
                case ObjKind.Mover: return "movers";
                case ObjKind.Pad: return "pads";
                case ObjKind.Hazard: return "hazards";
                case ObjKind.Wind: return "winds";
                case ObjKind.Area: return "areas";
                default: return "";
            }
        }

        public static ObjKind KindFromJsonList(string list)
        {
            foreach (ObjKind k in Enum.GetValues(typeof(ObjKind))) if (JsonList(k) == list && k != ObjKind.None) return k;
            return ObjKind.None;
        }
    }
}
