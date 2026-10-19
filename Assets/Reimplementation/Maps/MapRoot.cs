using System.Collections.Generic;
using UnityEngine;
using Metadream.Shoot.Presentation;

namespace Metadream.Shoot.Maps
{
    /// <summary>
    /// A built map instance. Boxes are dynamic bodies that players and bullets can push. Movers are kinematic
    /// platforms appended after the boxes, so the existing box snapshot keeps them in sync on clients.
    /// Zones (jump pads, hazards, wind) act on fighters only where the map is simulated (offline / host / server).
    /// </summary>
    public sealed class MapRoot : MonoBehaviour
    {
        private readonly List<Rigidbody2D> _bodies = new List<Rigidbody2D>();
        private readonly List<KeyValuePair<MapMover, Rigidbody2D>> _movers = new List<KeyValuePair<MapMover, Rigidbody2D>>();
        private readonly List<KeyValuePair<MapZone, SpriteRenderer>> _zones = new List<KeyValuePair<MapZone, SpriteRenderer>>();
        private readonly Dictionary<Player.Fighter, float> _padCooldown = new Dictionary<Player.Fighter, float>();
        private bool _simulated = true;
        private float _age;
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
            foreach (var m in def.Movers) map.AddMover(m);
            foreach (var z in def.Zones) map.AddZone(z);
            return map;
        }

        private GameObject AddBlock(MapBlock block, bool dynamic)
        {
            var go = new GameObject(dynamic ? "Box" : "Solid");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = block.Center;
            go.transform.localRotation = Quaternion.Euler(0f, 0f, block.Rotation);
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
                // Light enough that a fighter (mass ≈ 1) and bullets visibly shove it, as in ROUNDS.
                rb.mass = Mathf.Clamp(0.18f * block.Size.x * block.Size.y, 0.25f, 6f);
                rb.gravityScale = 3f;
                rb.linearDamping = 0.1f;
                rb.angularDamping = 0.3f;
                _bodies.Add(rb);
            }
            return go;
        }

        private void AddMover(MapMover m)
        {
            var go = AddBlock(m.Block, false);
            go.name = "Mover";
            go.GetComponent<SpriteRenderer>().color = Palette.Mover;
            var rb = go.AddComponent<Rigidbody2D>();
            rb.bodyType = RigidbodyType2D.Kinematic;
            rb.interpolation = RigidbodyInterpolation2D.Interpolate;
            rb.useFullKinematicContacts = true;
            _bodies.Add(rb);
            _movers.Add(new KeyValuePair<MapMover, Rigidbody2D>(m, rb));
        }

        private void AddZone(MapZone z)
        {
            var go = new GameObject("Zone_" + z.Kind);
            go.transform.SetParent(transform, false);
            go.transform.localPosition = z.Block.Center;
            go.transform.localRotation = Quaternion.Euler(0f, 0f, z.Block.Rotation);
            go.transform.localScale = new Vector3(z.Block.Size.x, z.Block.Size.y, 1f);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = SpriteFactory.Square;
            sr.sharedMaterial = SpriteFactory.Material;
            sr.sortingOrder = z.Kind == ZoneKind.Wind ? -1 : 3;
            sr.color = ZoneColor(z.Kind, 0f);
            _zones.Add(new KeyValuePair<MapZone, SpriteRenderer>(z, sr));
        }

        private static Color ZoneColor(ZoneKind kind, float pulse)
        {
            switch (kind)
            {
                case ZoneKind.JumpPad: return Color.Lerp(Palette.JumpPad, Color.white, 0.25f * pulse);
                case ZoneKind.Hazard: return Color.Lerp(Palette.Hazard, new Color(1f, 0.85f, 0.3f), 0.45f * pulse);
                default: return new Color(0.55f, 0.8f, 1f, 0.12f + 0.08f * pulse);
            }
        }

        private void Update()
        {
            float t = Time.time;
            foreach (var kv in _zones)
                if (kv.Value != null) kv.Value.color = ZoneColor(kv.Key.Kind, 0.5f + 0.5f * Mathf.Sin(t * (kv.Key.Kind == ZoneKind.Hazard ? 6f : 3f)));
        }

        private void FixedUpdate()
        {
            if (!_simulated) return;
            _age += Time.fixedDeltaTime;
            Vector2 origin = transform.position;
            foreach (var kv in _movers)
                if (kv.Value != null) kv.Value.MovePosition(origin + kv.Key.Block.Center + kv.Key.OffsetAt(_age));
            if (_zones.Count == 0) return;
            float dt = Time.fixedDeltaTime;
            foreach (var f in Player.Fighter.Active.ToArray())
            {
                if (f == null || f.IsDead || !f.IsPlaying) continue;
                Vector2 p = f.Position - origin;
                foreach (var kv in _zones)
                {
                    var z = kv.Key;
                    if (!z.Block.Contains(p, f.Radius * 0.8f)) continue;
                    switch (z.Kind)
                    {
                        case ZoneKind.JumpPad:
                            if (_padCooldown.TryGetValue(f, out float until) && Time.time < until) break;
                            _padCooldown[f] = Time.time + 0.35f;
                            Vector2 up = Quaternion.Euler(0f, 0f, z.Block.Rotation) * Vector2.up;
                            float along = Vector2.Dot(f.Velocity, up);
                            if (along < z.Power) f.AddImpulse(up * (z.Power - along));
                            Fx.Ring(f.Position, Palette.JumpPad, 2.2f);
                            break;
                        case ZoneKind.Hazard:
                            f.TakeDamage(z.Dps * dt, null, Vector2.up, true, 0f, true);
                            if (Random.value < dt * 6f) Fx.Burst(f.Position, Palette.Hazard, 3, 2f);
                            break;
                        case ZoneKind.Wind:
                            f.AddImpulse(z.Force * dt);
                            break;
                    }
                }
            }
        }

        public void SetSimulated(bool simulated)
        {
            _simulated = simulated;
            foreach (var rb in _bodies)
                if (rb != null) rb.simulated = simulated;
        }
    }
}
