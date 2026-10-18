using System.Collections.Generic;
using UnityEngine;
using Metadream.Shoot.Presentation;

namespace Metadream.Shoot.Maps
{
    /// <summary>A built map instance. Boxes are dynamic bodies that players and bullets can push.</summary>
    public sealed class MapRoot : MonoBehaviour
    {
        private readonly List<Rigidbody2D> _bodies = new List<Rigidbody2D>();
        public MapDefinition Definition { get; private set; }

        public IReadOnlyList<Rigidbody2D> Bodies => _bodies;

        /// <summary>Team spawn; additional teammates step 2.6 units toward the arena centre.</summary>
        public Vector2 SpawnPoint(int team, int indexInTeam = 0)
        {
            var spawns = Definition.Spawns;
            Vector2 p = spawns[team % spawns.Length];
            if (indexInTeam > 0) p.x += (p.x < 0f ? 1f : -1f) * 2.6f * indexInTeam;
            return (Vector2)transform.position + p;
        }

        /// <summary>Client mirror: place boxes from host data (local space).</summary>
        public void ApplyBoxes(IReadOnlyList<Network.BoxState> boxes)
        {
            int n = Mathf.Min(boxes.Count, _bodies.Count);
            for (int i = 0; i < n; i++)
            {
                var t = _bodies[i].transform;
                t.localPosition = boxes[i].Position;
                t.localRotation = Quaternion.Euler(0f, 0f, boxes[i].Rotation);
            }
        }

        public static MapRoot Build(MapDefinition def, Vector2 offset)
        {
            var root = new GameObject("Map_" + def.Name);
            root.transform.position = offset;
            var map = root.AddComponent<MapRoot>();
            map.Definition = def;
            foreach (var s in def.Solids) map.AddBlock(s, false);
            foreach (var b in def.Boxes) map.AddBlock(b, true);
            return map;
        }

        private void AddBlock(MapBlock block, bool dynamic)
        {
            var go = new GameObject(dynamic ? "Box" : "Solid");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = block.Center;
            go.transform.localScale = new Vector3(block.Size.x, block.Size.y, 1f);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = SpriteFactory.Square;
            sr.sharedMaterial = SpriteFactory.Material;
            sr.color = dynamic ? Palette.Box : Palette.Solid;
            sr.sortingOrder = dynamic ? 2 : 1;
            var col = go.AddComponent<BoxCollider2D>();
            col.size = Vector2.one;
            if (dynamic)
            {
                col.sharedMaterial = PhysicsMaterials.Box;
                var rb = go.AddComponent<Rigidbody2D>();
                rb.mass = 2f * block.Size.x * block.Size.y;
                rb.gravityScale = 3f;
                rb.linearDamping = 0.2f;
                rb.angularDamping = 0.5f;
                _bodies.Add(rb);
            }
        }

        public void SetSimulated(bool simulated)
        {
            foreach (var rb in _bodies)
                if (rb != null) rb.simulated = simulated;
        }
    }
}
