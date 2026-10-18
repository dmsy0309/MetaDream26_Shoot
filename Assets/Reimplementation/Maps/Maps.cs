using System.Collections.Generic;
using UnityEngine;
using Metadream.Shoot.Content;

namespace Metadream.Shoot.Maps
{
    public readonly struct MapBlock
    {
        public readonly Vector2 Center;
        public readonly Vector2 Size;
        public MapBlock(float x, float y, float w, float h)
        {
            Center = new Vector2(x, y);
            Size = new Vector2(w, h);
        }
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

