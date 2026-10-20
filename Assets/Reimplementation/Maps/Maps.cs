using System.Collections.Generic;
using UnityEngine;
using Metadream.Shoot.Content;

namespace Metadream.Shoot.Maps
{
    public readonly struct MapBlock
    {
        public readonly Vector2 Center;
        public readonly Vector2 Size;
        /// <summary>Rotation around the centre in degrees (counter-clockwise).</summary>
        public readonly float Rotation;
        public MapBlock(float x, float y, float w, float h, float rotation = 0f)
        {
            Center = new Vector2(x, y);
            Size = new Vector2(w, h);
            Rotation = rotation;
        }

        /// <summary>Point in block-local coordinates (unrotated, centre at 0).</summary>
        public Vector2 ToLocal(Vector2 p)
        {
            Vector2 d = p - Center;
            float a = -Rotation * Mathf.Deg2Rad, c = Mathf.Cos(a), s = Mathf.Sin(a);
            return new Vector2(d.x * c - d.y * s, d.x * s + d.y * c);
        }

        public bool Contains(Vector2 p, float margin = 0f)
        {
            var l = ToLocal(p);
            return Mathf.Abs(l.x) <= Size.x * 0.5f + margin && Mathf.Abs(l.y) <= Size.y * 0.5f + margin;
        }
    }

    /// <summary>Kinematic platform that loops through Path (offsets from its start), pausing Wait seconds at each point.</summary>
    public sealed class MapMover
    {
        public MapBlock Block;
        public string Name = "";
        public Vector2[] Path = new Vector2[0];
        public float Speed = 6f;
        public float Wait = 1f;

        public float CycleTime
        {
            get
            {
                float t = 0f;
                for (int i = 0; i < Path.Length; i++) t += Vector2.Distance(Path[i], Path[(i + 1) % Path.Length]) / Mathf.Max(0.01f, Speed) + Wait;
                return Mathf.Max(0.01f, t);
            }
        }

        /// <summary>Offset from the start position at time t (deterministic, loops).</summary>
        public Vector2 OffsetAt(float t)
        {
            if (Path.Length == 0) return Vector2.zero;
            if (Path.Length == 1) return Path[0];
            t %= CycleTime;
            for (int i = 0; i < Path.Length; i++)
            {
                if (t < Wait) return Path[i];
                t -= Wait;
                Vector2 a = Path[i], b = Path[(i + 1) % Path.Length];
                float move = Vector2.Distance(a, b) / Mathf.Max(0.01f, Speed);
                if (t < move) return Vector2.Lerp(a, b, Mathf.SmoothStep(0f, 1f, t / Mathf.Max(0.0001f, move)));
                t -= move;
            }
            return Path[0];
        }
    }

    public enum ZoneKind { JumpPad, Hazard, Wind }

    /// <summary>Area gimmick: jump pad (Power = launch speed), hazard (Dps), wind (Force = acceleration).</summary>
    public sealed class MapZone
    {
        public ZoneKind Kind;
        public MapBlock Block;
        public string Name = "";
        public float Power;
        public float Dps;
        public Vector2 Force;
    }

    /// <summary>Named trigger rectangle for map scripts ("areas" in map JSON). Invisible unless Show.</summary>
    public sealed class MapArea
    {
        public string Name = "";
        public MapBlock Block;
        public bool Show;
        public Color Color = new Color(1f, 1f, 1f, 0.15f);
    }

    /// <summary>
    /// A map loaded from JSON (Resources/Content/Maps). Layouts are original content inside the observed
    /// arena (±35.56 × ±20). Network id = Index in the id-sorted map list.
    /// </summary>
    public sealed class MapDefinition
    {
        public string Id = "";
        public string Name = "";
        public int Index = -1;
        public bool TutorialOnly;
        public string Source = "";
        public MapBlock[] Solids = new MapBlock[0];
        public MapBlock[] Boxes = new MapBlock[0];
        public Vector2[] Spawns = new Vector2[0];
        public MapMover[] Movers = new MapMover[0];
        public MapZone[] Zones = new MapZone[0];
        public MapArea[] Areas = new MapArea[0];
        /// <summary>Object "name" per solid / box (empty = unnamed).</summary>
        public string[] SolidNames = new string[0];
        public string[] BoxNames = new string[0];
        /// <summary>Every named object in build order (solids, boxes, movers, zones, areas). Index = network/script id.</summary>
        public string[] ObjectNames = new string[0];
        /// <summary>Parsed "script" (null when the map has none). See Docs/MAP_SCRIPTING.md.</summary>
        public Scripting.MdsProgram Script;
        public string[] ScriptSource = new string[0];
        public Scripting.MapBehaviorSpec[] Behaviors = new Scripting.MapBehaviorSpec[0];
    }

    public static class MapCatalog
    {
        public static IReadOnlyList<MapDefinition> All => ContentDatabase.Maps;

        /// <summary>Playable maps allowed by a pool of ids (empty pool = every non-tutorial map).</summary>
        public static List<MapDefinition> Pool(string[] ids)
        {
            var list = new List<MapDefinition>();
            foreach (var m in All)
            {
                if (m.TutorialOnly) continue;
                if (ids == null || ids.Length == 0 || System.Array.IndexOf(ids, m.Id) >= 0) list.Add(m);
            }
            if (list.Count == 0)
            {
                Debug.LogWarning("[Content] map pool [" + string.Join(",", ids ?? new string[0]) + "] matched no playable map; using all maps");
                foreach (var m in All) if (!m.TutorialOnly) list.Add(m);
            }
            return list;
        }
    }

    /// <summary>Map order: shuffled rotation over a pool without immediate repeats (development decision).</summary>
    public sealed class MapRotation
    {
        private readonly System.Random _rng;
        private readonly List<MapDefinition> _pool;
        private readonly List<int> _order = new List<int>();
        private int _cursor;
        private int _last = -1;

        public MapRotation(System.Random rng, List<MapDefinition> pool = null)
        {
            _rng = rng;
            _pool = pool ?? MapCatalog.Pool(null);
        }

        public MapDefinition Next()
        {
            if (_cursor >= _order.Count) Reshuffle();
            int index = _order[_cursor++];
            _last = index;
            return _pool[index];
        }

        private void Reshuffle()
        {
            _order.Clear();
            for (int i = 0; i < _pool.Count; i++) _order.Add(i);
            for (int i = _order.Count - 1; i > 0; i--)
            {
                int j = _rng.Next(i + 1);
                (_order[i], _order[j]) = (_order[j], _order[i]);
            }
            if (_order.Count > 1 && _order[0] == _last) (_order[0], _order[1]) = (_order[1], _order[0]);
            _cursor = 0;
        }
    }
}

