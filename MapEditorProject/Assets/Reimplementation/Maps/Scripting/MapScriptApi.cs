using System.Collections.Generic;
using UnityEngine;
using Metadream.Shoot.Abilities;
using Metadream.Shoot.Cards;
using Metadream.Shoot.Combat;
using Metadream.Shoot.Player;
using Metadream.Shoot.Presentation;

namespace Metadream.Shoot.Maps.Scripting
{
    /// <summary>
    /// The only things a map script (text script or C# MapBehavior) is allowed to do: move/show/colour named map
    /// objects, affect fighters, spawn explosions/projectiles/effects/sounds and show text. Every value is clamped,
    /// so a broken script cannot crash the match, flood the network or reach anything outside the game.
    /// Runs only where the map is simulated (offline game, host, dedicated server).
    /// </summary>
    public sealed class MapScriptApi
    {
        public const float MaxCoordinate = 200f, MaxAmount = 10000f, MaxSeconds = 600f;
        public const int MaxScriptProjectiles = 60;

        private readonly MapRoot _map;
        private readonly List<Projectile> _shots = new List<Projectile>();
        private readonly List<Fighter> _players = new List<Fighter>();

        internal MapScriptApi(MapRoot map) { _map = map; }

        /// <summary>Seconds since the map started being simulated.</summary>
        public float Time { get; internal set; }
        public string HudText { get; private set; } = "";
        public MapRoot Map => _map;

        public MapObject Find(string name) => _map.FindObject(name);

        /// <summary>Fighters that are alive and playing right now.</summary>
        public IReadOnlyList<Fighter> Players
        {
            get
            {
                _players.Clear();
                foreach (var f in Fighter.Active)
                    if (f != null && !f.IsDead && f.IsPlaying) _players.Add(f);
                return _players;
            }
        }

        public Vector2 ToLocal(Vector2 world) => world - (Vector2)_map.transform.position;
        public Vector2 ToWorld(Vector2 local) => local + (Vector2)_map.transform.position;

        public Fighter Nearest(Vector2 local)
        {
            Fighter best = null;
            float bestD = float.MaxValue;
            foreach (var f in Players)
            {
                float d = (ToLocal(f.Position) - local).sqrMagnitude;
                if (d < bestD) { bestD = d; best = f; }
            }
            return best;
        }

        public int AliveInTeam(int team)
        {
            int n = 0;
            foreach (var f in Players) if (f.TeamId == team) n++;
            return n;
        }

        // ------------------------------------------------------------ objects

        public void Move(MapObject o, Vector2 to, float seconds)
        {
            if (o == null) return;
            to = Clamp(to);
            seconds = Seconds(seconds);
            if (o.Kind == MapObjectKind.Box)
            {
                // Boxes are physics bodies: teleport them (tweening a dynamic body would fight the physics).
                if (o.Body != null) { o.Body.position = ToWorld(to); o.Body.linearVelocity = Vector2.zero; o.Body.angularVelocity = 0f; }
                return;
            }
            if (seconds <= 0f) { o.MoveTween.Active = false; o.Center = to; _map.PlaceObject(o); return; }
            o.MoveTween = new Tween2 { Active = true, From = o.Center, To = to, Duration = seconds };
        }

        public void MoveBy(MapObject o, Vector2 delta, float seconds)
        {
            if (o == null) return;
            Vector2 start = o.MoveTween.Active ? o.MoveTween.To : o.Position;
            Move(o, start + delta, seconds);
        }

        public void Rotate(MapObject o, float degrees, float seconds)
        {
            if (o == null || o.Kind == MapObjectKind.Box) return;
            degrees = Mathf.Clamp(Safe(degrees), -36000f, 36000f);
            seconds = Seconds(seconds);
            if (seconds <= 0f) { o.TurnTween.Active = false; o.Rotation = degrees; _map.PlaceObject(o); return; }
            o.TurnTween = new Tween1 { Active = true, From = o.Rotation, To = degrees, Duration = seconds };
        }

        public void RotateBy(MapObject o, float degrees, float seconds)
        {
            if (o == null) return;
            float start = o.TurnTween.Active ? o.TurnTween.To : o.Rotation;
            Rotate(o, start + degrees, seconds);
        }

        public void Resize(MapObject o, Vector2 size, float seconds)
        {
            if (o == null || o.Kind == MapObjectKind.Box) return;
            size = new Vector2(Mathf.Clamp(Safe(size.x), 0.05f, 150f), Mathf.Clamp(Safe(size.y), 0.05f, 150f));
            seconds = Seconds(seconds);
            if (seconds <= 0f) { o.SizeTween.Active = false; o.Size = size; _map.PlaceObject(o); return; }
            o.SizeTween = new Tween2 { Active = true, From = o.Size, To = size, Duration = seconds };
        }

        public void SetVisible(MapObject o, bool visible)
        {
            if (o == null || o.Visible == visible) return;
            o.Visible = visible;
            _map.ApplyVisibility(o);
        }

        public void SetColor(MapObject o, Color c)
        {
            if (o == null) return;
            o.HasTint = true;
            o.Tint = new Color(Mathf.Clamp01(c.r), Mathf.Clamp01(c.g), Mathf.Clamp01(c.b), Mathf.Clamp01(c.a));
            _map.ApplyColor(o);
        }

        public void ResetColor(MapObject o)
        {
            if (o == null) return;
            o.HasTint = false;
            _map.ApplyColor(o);
        }

        public void SetZone(MapObject o, float? power, float? dps, Vector2? force)
        {
            if (o == null) return;
            if (power.HasValue) o.Power = Mathf.Clamp(Safe(power.Value), 0f, 200f);
            if (dps.HasValue) o.Dps = Mathf.Clamp(Safe(dps.Value), 0f, MaxAmount);
            if (force.HasValue) o.Force = new Vector2(Mathf.Clamp(Safe(force.Value.x), -500f, 500f), Mathf.Clamp(Safe(force.Value.y), -500f, 500f));
        }

        // ------------------------------------------------------------ fighters

        public void Damage(Fighter f, float amount)
        {
            if (!Valid(f)) return;
            f.TakeDamage(Mathf.Clamp(Safe(amount), 0f, MaxAmount), null, Vector2.up, true, 0f);
        }

        public void Heal(Fighter f, float amount)
        {
            if (!Valid(f)) return;
            f.Heal(Mathf.Clamp(Safe(amount), 0f, MaxAmount));
        }

        public void Push(Fighter f, Vector2 velocity)
        {
            if (!Valid(f)) return;
            f.AddImpulse(new Vector2(Mathf.Clamp(Safe(velocity.x), -300f, 300f), Mathf.Clamp(Safe(velocity.y), -300f, 300f)));
        }

        public void Teleport(Fighter f, Vector2 local)
        {
            if (!Valid(f)) return;
            f.Teleport(ToWorld(Clamp(local)));
        }

        public void Kill(Fighter f)
        {
            if (!Valid(f)) return;
            f.TakeDamage(f.Health + f.MaxHealth + 1f, null, Vector2.up, true, 0f);
        }

        public void Slow(Fighter f, float factor, float seconds)
        {
            if (!Valid(f)) return;
            f.AddSpeedModifier(Mathf.Clamp(Safe(factor), 0.1f, 3f), Seconds(seconds));
        }

        public void Poison(Fighter f, float dps, float seconds)
        {
            if (!Valid(f)) return;
            f.AddDamageOverTime(Mathf.Clamp(Safe(dps), 0f, MaxAmount), Seconds(seconds), null);
        }

        private static bool Valid(Fighter f) => f != null && !f.IsDead && f.IsPlaying;

        // ------------------------------------------------------------ area / projectiles / effects

        public void Explode(Vector2 local, float radius, float damage, float knockback)
        {
            Explosion.Detonate(ToWorld(Clamp(local)), Mathf.Clamp(Safe(radius), 0.1f, 40f), Mathf.Clamp(Safe(damage), 0f, MaxAmount), null, Mathf.Clamp(Safe(knockback), 0f, 200f), false);
        }

        public void Shockwave(Vector2 local, float radius, float strength)
        {
            Explosion.Push(ToWorld(Clamp(local)), Mathf.Clamp(Safe(radius), 0.1f, 40f), Mathf.Clamp(Safe(strength), 0f, 300f), null);
        }

        /// <summary>Ownerless bullet (hurts everyone). At most MaxScriptProjectiles alive per map; returns false when capped.</summary>
        public bool Shoot(Vector2 local, Vector2 direction, float speed, float damage, float gravity = 0f)
        {
            _shots.RemoveAll(p => p == null);
            if (_shots.Count >= MaxScriptProjectiles) return false;
            if (direction.sqrMagnitude < 1e-6f) direction = Vector2.right;
            var g = new GunStats();
            g.baseDamage = Mathf.Clamp(Safe(damage), 0f, 500f);
            g.damage = 1f;
            g.baseProjectileSpeed = Mathf.Clamp(Safe(speed), 1f, 300f);
            g.projectileSpeed = 1f;
            g.gravity = Mathf.Clamp(Safe(gravity), 0f, 5f);
            var p = Projectile.Spawn(null, ToWorld(Clamp(local)), direction, g);
            _shots.Add(p);
            Fx.Muzzle(ToWorld(Clamp(local)), Color.white, Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg);
            return true;
        }

        public void Effect(string kind, Vector2 local, float size, Color color)
        {
            Vector2 p = ToWorld(Clamp(local));
            size = Mathf.Clamp(Safe(size), 0.1f, 40f);
            switch (kind)
            {
                case "ring": Fx.Ring(p, color, size); break;
                case "burst": Fx.Burst(p, color, Mathf.Clamp(Mathf.RoundToInt(size * 4f), 3, 40), size * 3f); break;
                case "spark": Fx.Spark(p, color); break;
                case "shake": Fx.Shake(Mathf.Clamp(size * 0.1f, 0.02f, 1f)); break;
                case "death": Fx.Death(p, color); break;
            }
        }

        public void Sound(string id, Vector2? local)
        {
            if (string.IsNullOrEmpty(id)) return;
            Fx.Sound(id, local.HasValue ? ToWorld(Clamp(local.Value)) : Vector2.zero);
        }

        public void Banner(string text, float seconds, Color color) => _map.ShowScriptBanner(Trim(text, 60), Mathf.Clamp(Safe(seconds), 0.2f, 30f), color);

        public void Hud(string text) => HudText = Trim(text, 80);

        public void Log(string text) => DevLog.Verbose("[MapScript " + _map.Definition.Id + "] " + Trim(text, 200));

        // ------------------------------------------------------------ helpers

        public static float Safe(float v) => float.IsNaN(v) || float.IsInfinity(v) ? 0f : v;
        private static float Seconds(float s) => Mathf.Clamp(Safe(s), 0f, MaxSeconds);
        private static Vector2 Clamp(Vector2 v) => new Vector2(Mathf.Clamp(Safe(v.x), -MaxCoordinate, MaxCoordinate), Mathf.Clamp(Safe(v.y), -MaxCoordinate, MaxCoordinate));
        private static string Trim(string s, int max) => string.IsNullOrEmpty(s) ? "" : s.Length <= max ? s : s.Substring(0, max);
    }
}

