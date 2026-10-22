using System;
using System.Collections.Generic;
using UnityEngine;
using Metadream.Shoot.Combat;
using Metadream.Shoot.Maps.Scripting;
using Metadream.Shoot.Network;
using Metadream.Shoot.Player;
using Metadream.Shoot.Presentation;

namespace Metadream.Shoot.Maps
{
    /// <summary>
    /// A built map instance. Boxes are dynamic bodies that players and bullets can push. Movers are kinematic
    /// platforms appended after the boxes, so the existing box snapshot keeps them in sync on clients.
    /// Zones (jump pads, hazards, wind) act on fighters only where the map is simulated (offline / host / server).
    /// Map scripts ("script" text and C# "behaviors") also run only there; the state of named objects is sent to
    /// clients and replays through MatchSnapshot.MapObjects.
    /// </summary>
    public sealed class MapRoot : MonoBehaviour
    {
        public const int MaxNamedObjects = 64;

        private readonly List<Rigidbody2D> _bodies = new List<Rigidbody2D>();
        private readonly List<MapObject> _movers = new List<MapObject>();
        private readonly List<MapObject> _zones = new List<MapObject>();
        private readonly Dictionary<string, MapObject> _byName = new Dictionary<string, MapObject>(StringComparer.Ordinal);
        private readonly Dictionary<Collider2D, MapObject> _byCollider = new Dictionary<Collider2D, MapObject>();
        private readonly Dictionary<Fighter, float> _padCooldown = new Dictionary<Fighter, float>();
        private readonly List<MapBehavior> _behaviors = new List<MapBehavior>();
        private readonly Dictionary<MapObject, HashSet<Fighter>> _behaviorInside = new Dictionary<MapObject, HashSet<Fighter>>();
        private MapObject[] _named = new MapObject[0];
        private bool _simulated = true;
        private float _age;
        private bool _scriptsStarted;
        private MapScriptApi _api;
        private MdsRuntime _script;
        private string _behaviorError;
        private string _bannerText;
        private float _bannerUntil;

        public MapDefinition Definition { get; private set; }
        public IReadOnlyList<Rigidbody2D> Bodies => _bodies;
        public IReadOnlyList<MapObject> NamedObjects => _named;
        public MapScriptApi ScriptApi => _api;
        public MdsRuntime Script => _script;
        public bool HasScripts => Definition != null && (Definition.Script != null || Definition.Behaviors.Length > 0);
        public string HudText => _api != null ? _api.HudText : "";

        public MapObject FindObject(string name) => name != null && _byName.TryGetValue(name, out var o) ? o : null;
        public MapObject NamedObject(int index) => index >= 0 && index < _named.Length ? _named[index] : null;

        /// <summary>
        /// Team spawn. The n-th teammate (n ≥ 1) uses spawns[team + 2n] when the map has it
        /// (spawns[2] = orange #2, spawns[3] = blue #2); otherwise it steps 2.6 units toward the arena centre.
        /// </summary>
        public Vector2 SpawnPoint(int team, int indexInTeam = 0)
        {
            var spawns = Definition.Spawns;
            int custom = team + 2 * indexInTeam;
            if (indexInTeam > 0 && team >= 0 && custom < spawns.Length)
                return (Vector2)transform.position + spawns[custom];
            Vector2 p = spawns[team % spawns.Length];
            if (indexInTeam > 0) p.x += (p.x < 0f ? 1f : -1f) * 2.6f * indexInTeam;
            return (Vector2)transform.position + p;
        }

        /// <summary>Client mirror: place boxes from host data (local space).</summary>
        public void ApplyBoxes(IReadOnlyList<BoxState> boxes)
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
            map._named = new MapObject[def.ObjectNames.Length];
            for (int i = 0; i < def.Solids.Length; i++) map.AddBlock(def.Solids[i], MapObjectKind.Solid, NameAt(def.SolidNames, i));
            for (int i = 0; i < def.Boxes.Length; i++) map.AddBlock(def.Boxes[i], MapObjectKind.Box, NameAt(def.BoxNames, i));
            foreach (var m in def.Movers) map.AddMover(m);
            foreach (var z in def.Zones) map.AddZone(z);
            foreach (var a in def.Areas) map.AddArea(a);
            map._api = new MapScriptApi(map);
            if (def.Script != null) map._script = new MdsRuntime(def.Script, map, map._api);
            foreach (var spec in def.Behaviors)
            {
                var b = MapBehaviorRegistry.Create(spec, map._api);
                if (b != null) map._behaviors.Add(b);
            }
            return map;
        }

        private static string NameAt(string[] names, int i) => names != null && i < names.Length ? names[i] : "";

        private void Register(MapObject o)
        {
            if (o.Collider != null) _byCollider[o.Collider] = o;
            if (string.IsNullOrEmpty(o.Name)) return;
            int index = Array.IndexOf(Definition.ObjectNames, o.Name);
            if (index < 0 || _byName.ContainsKey(o.Name)) return;
            o.NamedIndex = index;
            _named[index] = o;
            _byName[o.Name] = o;
        }

        private MapObject AddBlock(MapBlock block, MapObjectKind kind, string name, bool register = true)
        {
            bool dynamic = kind == MapObjectKind.Box;
            var go = new GameObject(dynamic ? "Box" : "Solid");
            go.transform.SetParent(transform, false);
            var o = new MapObject
            {
                Name = name ?? "", Kind = kind, Transform = go.transform, Center = block.Center, Rotation = block.Rotation, Size = block.Size,
                BaseColor = dynamic ? Palette.Box : Palette.Solid
            };
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = SpriteFactory.Square;
            sr.sharedMaterial = SpriteFactory.Material;
            sr.sortingOrder = dynamic ? 2 : 1;
            o.Renderer = sr;
            // Top-edge highlight (child sprite, not a collider) for contrast on the dark background.
            var edge = new GameObject("Edge");
            edge.transform.SetParent(go.transform, false);
            var es = edge.AddComponent<SpriteRenderer>();
            es.sprite = SpriteFactory.Square;
            es.sharedMaterial = SpriteFactory.Material;
            es.sortingOrder = sr.sortingOrder + 1;
            o.Edge = es;
            var col = go.AddComponent<BoxCollider2D>();
            col.size = Vector2.one;
            o.Collider = col;
            if (dynamic)
            {
                col.sharedMaterial = PhysicsMaterials.Box;
                var rb = go.AddComponent<Rigidbody2D>();
                // Light enough that a fighter (mass ≈ 1) and bullets visibly shove it, as in ROUNDS.
                rb.mass = Mathf.Clamp(0.18f * block.Size.x * block.Size.y, 0.25f, 6f);
                rb.gravityScale = 3f;
                rb.linearDamping = 0.1f;
                rb.angularDamping = 0.3f;
                o.Body = rb;
                _bodies.Add(rb);
            }
            else if (!string.IsNullOrEmpty(name) && kind == MapObjectKind.Solid)
            {
                // Named solids may be moved by scripts: a kinematic body carries players standing on them.
                var rb = go.AddComponent<Rigidbody2D>();
                rb.bodyType = RigidbodyType2D.Kinematic;
                rb.interpolation = RigidbodyInterpolation2D.Interpolate;
                rb.useFullKinematicContacts = true;
                o.Body = rb;
            }
            Layout(o, true);
            ApplyColor(o);
            if (register) Register(o);
            return o;
        }

        private void AddMover(MapMover m)
        {
            var o = AddBlock(m.Block, MapObjectKind.Mover, m.Name ?? "", false);
            o.Transform.name = "Mover";
            o.BaseColor = Palette.Mover;
            o.Path = m;
            var rb = o.Transform.gameObject.AddComponent<Rigidbody2D>();
            rb.bodyType = RigidbodyType2D.Kinematic;
            rb.interpolation = RigidbodyInterpolation2D.Interpolate;
            rb.useFullKinematicContacts = true;
            o.Body = rb;
            _bodies.Add(rb);
            _movers.Add(o);
            ApplyColor(o);
            Register(o);
        }

        private void AddZone(MapZone z)
        {
            var go = new GameObject("Zone_" + z.Kind);
            go.transform.SetParent(transform, false);
            var kind = z.Kind == ZoneKind.JumpPad ? MapObjectKind.JumpPad : z.Kind == ZoneKind.Hazard ? MapObjectKind.Hazard : MapObjectKind.Wind;
            var o = new MapObject
            {
                Name = z.Name ?? "", Kind = kind, Transform = go.transform, Center = z.Block.Center, Rotation = z.Block.Rotation, Size = z.Block.Size,
                Power = z.Power, Dps = z.Dps, Force = z.Force
            };
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = SpriteFactory.Square;
            sr.sharedMaterial = SpriteFactory.Material;
            sr.sortingOrder = kind == MapObjectKind.Wind ? -1 : 3;
            o.Renderer = sr;
            Layout(o, true);
            o.BaseColor = ZoneColor(kind, 0f);
            ApplyColor(o);
            _zones.Add(o);
            Register(o);
        }

        private void AddArea(MapArea a)
        {
            var go = new GameObject("Area_" + a.Name);
            go.transform.SetParent(transform, false);
            var o = new MapObject
            {
                Name = a.Name ?? "", Kind = MapObjectKind.Area, Transform = go.transform, Center = a.Block.Center, Rotation = a.Block.Rotation, Size = a.Block.Size,
                Visible = a.Show, BaseColor = a.Color
            };
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = SpriteFactory.Square;
            sr.sharedMaterial = SpriteFactory.Material;
            sr.sortingOrder = 0;
            o.Renderer = sr;
            Layout(o, true);
            ApplyColor(o);
            ApplyVisibility(o);
            Register(o);
        }

        // ------------------------------------------------------------ object state (scripts / network)

        /// <summary>Puts the object's transform where its state says.</summary>
        internal void PlaceObject(MapObject o) => Layout(o, false);

        private void Layout(MapObject o, bool initial)
        {
            if (o.Transform == null) return;
            o.Transform.localScale = new Vector3(o.Size.x, o.Size.y, 1f);
            if (o.Edge != null)
            {
                float edgeHeight = Mathf.Min(0.18f, o.Size.y * 0.35f);
                var et = o.Edge.transform;
                et.localScale = new Vector3(1f, edgeHeight / Mathf.Max(0.01f, o.Size.y), 1f);
                et.localPosition = new Vector3(0f, 0.5f - et.localScale.y * 0.5f, 0f);
            }
            if (o.Kind == MapObjectKind.Box && !initial) return;
            if (o.Kind == MapObjectKind.Mover && !initial)
            {
                o.Transform.localRotation = Quaternion.Euler(0f, 0f, o.Rotation);
                return;
            }
            if (!initial && o.Body != null && o.Body.bodyType == RigidbodyType2D.Kinematic && _simulated && o.Body.simulated)
            {
                o.Body.MovePosition((Vector2)transform.position + o.Center);
                o.Body.MoveRotation(o.Rotation);
                return;
            }
            o.Transform.localPosition = o.Center;
            o.Transform.localRotation = Quaternion.Euler(0f, 0f, o.Rotation);
        }

        internal void ApplyVisibility(MapObject o)
        {
            if (o.Renderer != null) o.Renderer.enabled = o.Visible;
            if (o.Edge != null) o.Edge.enabled = o.Visible;
            if (o.Collider != null) o.Collider.enabled = o.Visible;
            if (o.Kind == MapObjectKind.Box && o.Body != null) o.Body.simulated = o.Visible && _simulated;
        }

        internal void ApplyColor(MapObject o)
        {
            Color c = o.HasTint ? o.Tint : o.BaseColor;
            if (o.Renderer != null) o.Renderer.color = c;
            if (o.Edge != null)
                o.Edge.color = o.HasTint ? Color.Lerp(o.Tint, Color.white, 0.45f)
                    : o.Kind == MapObjectKind.Box ? Color.Lerp(Palette.Box, Color.white, 0.45f)
                    : o.Kind == MapObjectKind.Mover ? Color.Lerp(Palette.Mover, Color.white, 0.45f) : Palette.SolidEdge;
        }

        internal void ShowScriptBanner(string text, float seconds, Color color)
        {
            var flow = GameFlow.Instance;
            if (flow == null || string.IsNullOrEmpty(text)) return;
            flow.ShowBanner(text, null, color);
            _bannerText = text;
            _bannerUntil = (_api != null ? _api.Time : 0f) + seconds;
        }

        /// <summary>Host: state of every named object, in ObjectNames order.</summary>
        public void CaptureObjects(List<MapObjectState> list)
        {
            for (int i = 0; i < _named.Length; i++)
            {
                var o = _named[i];
                if (o == null) { list.Add(default); continue; }
                list.Add(new MapObjectState
                {
                    Position = o.Kind == MapObjectKind.Mover ? o.Center : o.Position,
                    Rotation = o.Rotation,
                    Size = o.Size,
                    Visible = o.Visible,
                    HasTint = o.HasTint,
                    Tint = o.Tint
                });
            }
        }

        /// <summary>Client / replay: apply named object states (boxes and movers are positioned by ApplyBoxes).</summary>
        public void ApplyObjects(IReadOnlyList<MapObjectState> states)
        {
            int n = Mathf.Min(states.Count, _named.Length);
            for (int i = 0; i < n; i++)
            {
                var o = _named[i];
                if (o == null) continue;
                var s = states[i];
                if (o.Kind != MapObjectKind.Box && (o.Center != s.Position || !Mathf.Approximately(o.Rotation, s.Rotation) || o.Size != s.Size))
                {
                    o.Center = s.Position;
                    o.Rotation = s.Rotation;
                    o.Size = s.Size;
                    Layout(o, o.Kind != MapObjectKind.Mover);
                }
                if (o.Visible != s.Visible) { o.Visible = s.Visible; ApplyVisibility(o); }
                if (o.HasTint != s.HasTint || (s.HasTint && !SameColor(o.Tint, s.Tint)))
                {
                    o.HasTint = s.HasTint;
                    o.Tint = s.Tint;
                    ApplyColor(o);
                }
            }
        }

        // ------------------------------------------------------------ simulation

        private static bool SameColor(Color a, Color32 b)
        {
            Color32 c = a;
            return c.r == b.r && c.g == b.g && c.b == b.b && c.a == b.a;
        }

        private static Color ZoneColor(MapObjectKind kind, float pulse)
        {
            switch (kind)
            {
                case MapObjectKind.JumpPad: return Color.Lerp(Palette.JumpPad, Color.white, 0.25f * pulse);
                case MapObjectKind.Hazard: return Color.Lerp(Palette.Hazard, new Color(1f, 0.85f, 0.3f), 0.45f * pulse);
                default: return new Color(0.55f, 0.8f, 1f, 0.12f + 0.08f * pulse);
            }
        }

        private void Update()
        {
            float t = Time.time;
            foreach (var o in _zones)
            {
                if (o.Renderer == null || o.HasTint) continue;
                o.Renderer.color = ZoneColor(o.Kind, 0.5f + 0.5f * Mathf.Sin(t * (o.Kind == MapObjectKind.Hazard ? 6f : 3f)));
            }
        }

        /// <summary>Scripts run on the simulated live map of a match only (not the title backdrop, clients or replays).</summary>
        private bool ScriptsActive
        {
            get
            {
                if (!_simulated || !HasScripts) return false;
                var flow = GameFlow.Instance;
                return flow != null && flow.CurrentMap == this && flow.HasMatch && !flow.InMenu && !flow.ReplayMode && flow.Role != NetRole.Client;
            }
        }

        private void FixedUpdate()
        {
            if (!_simulated) return;
            float dt = Time.fixedDeltaTime;
            _age += dt;
            Vector2 origin = transform.position;
            foreach (var o in _movers)
                if (o.Body != null) o.Body.MovePosition(origin + o.Center + o.Path.OffsetAt(_age));
            TickTweens(dt);
            if (ScriptsActive) RunScripts(dt);
            if (_zones.Count == 0) return;
            foreach (var f in Fighter.Active.ToArray())
            {
                if (f == null || f.IsDead || !f.IsPlaying) continue;
                Vector2 p = f.Position - origin;
                foreach (var z in _zones)
                {
                    if (!z.Visible || !z.Contains(p, f.Radius * 0.8f)) continue;
                    switch (z.Kind)
                    {
                        case MapObjectKind.JumpPad:
                            if (_padCooldown.TryGetValue(f, out float until) && Time.time < until) break;
                            _padCooldown[f] = Time.time + 0.35f;
                            Vector2 up = Quaternion.Euler(0f, 0f, z.Rotation) * Vector2.up;
                            float along = Vector2.Dot(f.Velocity, up);
                            if (along < z.Power) f.AddImpulse(up * (z.Power - along));
                            Fx.Ring(f.Position, Palette.JumpPad, 2.2f);
                            Fx.Sound("pad", f.Position);
                            break;
                        case MapObjectKind.Hazard:
                            f.TakeDamage(z.Dps * dt, null, Vector2.up, true, 0f, true);
                            if (UnityEngine.Random.value < dt * 6f) Fx.Burst(f.Position, Palette.Hazard, 3, 2f);
                            break;
                        case MapObjectKind.Wind:
                            f.AddImpulse(z.Force * dt);
                            break;
                    }
                }
            }
        }

        private void TickTweens(float dt)
        {
            foreach (var o in _named)
            {
                if (o == null || !(o.MoveTween.Active || o.TurnTween.Active || o.SizeTween.Active)) continue;
                if (o.MoveTween.Active) o.Center = Advance(ref o.MoveTween, dt);
                if (o.TurnTween.Active) o.Rotation = Advance(ref o.TurnTween, dt);
                if (o.SizeTween.Active) o.Size = Advance(ref o.SizeTween, dt);
                Layout(o, false);
            }
        }

        private static Vector2 Advance(ref Tween2 t, float dt)
        {
            t.Elapsed += dt;
            float k = Mathf.Clamp01(t.Elapsed / Mathf.Max(0.0001f, t.Duration));
            if (k >= 1f) t.Active = false;
            return Vector2.Lerp(t.From, t.To, Mathf.SmoothStep(0f, 1f, k));
        }

        private static float Advance(ref Tween1 t, float dt)
        {
            t.Elapsed += dt;
            float k = Mathf.Clamp01(t.Elapsed / Mathf.Max(0.0001f, t.Duration));
            if (k >= 1f) t.Active = false;
            return Mathf.Lerp(t.From, t.To, Mathf.SmoothStep(0f, 1f, k));
        }

        private void RunScripts(float dt)
        {
            _api.Time += dt;
            if (!_scriptsStarted)
            {
                _scriptsStarted = true;
                ForBehaviors(b => b.OnStart());
            }
            _script?.Tick(dt);
            ForBehaviors(b => b.OnTick(dt));
            if (_behaviors.Count > 0) TickBehaviorAreas();
            if (_bannerText != null && _api.Time >= _bannerUntil)
            {
                var flow = GameFlow.Instance;
                if (flow != null && flow.Banner == _bannerText) flow.ShowBanner(null, null, Color.white);
                _bannerText = null;
            }
        }

        private void TickBehaviorAreas()
        {
            foreach (var b in _behaviors.ToArray())
            {
                var area = b.Area;
                if (area == null) continue;
                if (!_behaviorInside.TryGetValue(area, out var set)) _behaviorInside[area] = set = new HashSet<Fighter>();
                var now = new HashSet<Fighter>();
                foreach (var f in _api.Players)
                    if ((area.Kind == MapObjectKind.Area || area.Visible) && area.Contains(_api.ToLocal(f.Position), f.Radius * 0.8f)) now.Add(f);
                foreach (var f in now) if (!set.Contains(f)) Guard(b, () => b.OnEnter(area, f));
                foreach (var f in set) if (!now.Contains(f)) Guard(b, () => b.OnExit(area, f));
                _behaviorInside[area] = now;
            }
        }

        private void ForBehaviors(Action<MapBehavior> call)
        {
            for (int i = _behaviors.Count - 1; i >= 0; i--)
            {
                var b = _behaviors[i];
                Guard(b, () => call(b));
            }
        }

        /// <summary>A C# behavior that throws is removed (the match continues) and the error is shown in debug mode.</summary>
        private void Guard(MapBehavior b, Action call)
        {
            try { call(); }
            catch (Exception e)
            {
                _behaviors.Remove(b);
                _behaviorError = b.Spec.type + ": " + e.Message;
                Debug.LogWarning("[MapBehavior] " + Definition.Id + " '" + b.Spec.type + "' stopped — " + e);
            }
        }

        private void OnEnable() => Projectile.SurfaceHit += OnSurfaceHit;

        private void OnDisable() => Projectile.SurfaceHit -= OnSurfaceHit;

        private void OnSurfaceHit(Projectile p, Collider2D collider, Vector2 point)
        {
            if (collider == null || !_byCollider.TryGetValue(collider, out var o) || o.NamedIndex < 0 || !ScriptsActive) return;
            _script?.OnShot(o, p, point);
            ForBehaviors(b => b.OnShot(o, p, point));
        }

        private void OnDestroy()
        {
            var flow = GameFlow.Instance;
            if (_bannerText != null && flow != null && flow.Banner == _bannerText) flow.ShowBanner(null, null, Color.white);
        }

        /// <summary>One line for the developer debug overlay (null when the map has no scripts).</summary>
        public string ScriptStatus()
        {
            if (!HasScripts) return null;
            var parts = new List<string>();
            if (_script != null)
            {
                if (_script.Error != null) parts.Add("script STOPPED " + _script.Error);
                else parts.Add("script " + Definition.Script.LineCount + " lines · " + Definition.Script.Handlers.Count + " handlers · runs " + _script.Runs
                               + (Definition.Script.VarNames.Length > 0 ? " · " + _script.VariableSummary() : ""));
            }
            if (Definition.Behaviors.Length > 0)
            {
                var ids = new List<string>();
                foreach (var b in Definition.Behaviors) ids.Add(b.type);
                parts.Add("behaviors " + string.Join(",", ids) + (_behaviorError != null ? " · STOPPED " + _behaviorError : ""));
            }
            parts.Add("t=" + (_api != null ? _api.Time.ToString("0.0") : "0") + (ScriptsActive ? "" : " (paused)"));
            if (!string.IsNullOrEmpty(HudText)) parts.Add("hud \"" + HudText + "\"");
            return string.Join("  ·  ", parts);
        }

        public void SetSimulated(bool simulated)
        {
            _simulated = simulated;
            foreach (var rb in _bodies)
                if (rb != null) rb.simulated = simulated;
            foreach (var o in _named)
            {
                if (o == null || o.Body == null) continue;
                if (o.Kind == MapObjectKind.Box) o.Body.simulated = simulated && o.Visible;
                else if (o.Kind == MapObjectKind.Solid) o.Body.simulated = simulated; // clients place named solids directly
            }
        }
    }
}

