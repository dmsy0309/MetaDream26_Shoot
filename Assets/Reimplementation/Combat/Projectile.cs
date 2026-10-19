using System.Collections.Generic;
using UnityEngine;
using Metadream.Shoot.Cards;
using Metadream.Shoot.Player;
using Metadream.Shoot.Presentation;

namespace Metadream.Shoot.Combat
{
    /// <summary>
    /// Projectile movement after MoveTransform.Update (0x06000362): velocity += down * gravity * dt, position += v * dt.
    /// Hits are swept with a circle cast each frame. Block handling follows Block.blocked (0x0600006c).
    /// </summary>
    public sealed class Projectile : MonoBehaviour
    {
        public const float MoveTransformGravity = 30f; // MoveTransform..ctor
        public const float MaxLifetime = 8f;
        public static readonly List<Projectile> Active = new List<Projectile>();
        private static int _nextId = 1;

        private static readonly RaycastHit2D[] Hits = new RaycastHit2D[24];
        private readonly HashSet<Fighter> _ignore = new HashSet<Fighter>();

        private float _damage;
        private float _gravity;
        private int _reflectsLeft;
        private float _speedMOnBounce;
        private float _dmgMOnBounce;
        private float _knockback;
        private bool _unblockable;
        private bool _ignoreWalls;
        private float _radius;
        private float _life;
        private ContactFilter2D _filter;

        public Fighter Owner { get; private set; }
        public int Id { get; private set; }
        public float Radius => _radius;
        public float Gravity => _gravity;
        public Vector2 Velocity { get; private set; }
        public Vector2 Position => transform.position;
        public float Damage => _damage;
        public int Bounces { get; private set; }
        public bool WasBlocked { get; private set; }

        public bool CanHit(Fighter f) => f != null && !_ignore.Contains(f);

        /// <summary>No friendly fire: teammates' bullets pass through (development decision for 2v2).</summary>
        public bool Threatens(Fighter f) => CanHit(f) && (Owner == null || Owner == f || Owner.TeamId != f.TeamId);

        public static Projectile Spawn(Fighter owner, Vector2 origin, Vector2 direction, GunStats g)
        {
            var go = new GameObject("Projectile");
            go.transform.position = origin;
            var p = go.AddComponent<Projectile>();
            p.Id = _nextId++;
            p.Owner = owner;
            p.Velocity = direction.normalized * g.ProjectileSpeed;
            p._damage = g.ProjectileDamage;
            p._gravity = MoveTransformGravity * g.gravity;
            p._reflectsLeft = g.reflects;
            p._speedMOnBounce = g.speedMOnBounce;
            p._dmgMOnBounce = g.dmgMOnBounce;
            p._knockback = g.knockback;
            p._unblockable = g.unblockable;
            p._ignoreWalls = g.ignoreWalls;
            p._radius = Mathf.Clamp(0.12f + p._damage / 55f * 0.1f, 0.1f, 0.65f) * g.projectileSize;
            if (owner != null) p._ignore.Add(owner);
            BuildVisual(go, owner != null ? owner.Color : Color.white, p._radius);
            return p;
        }

        private void Awake()
        {
            _filter = new ContactFilter2D { useTriggers = false, useLayerMask = false };
        }

        private void OnEnable() => Active.Add(this);
        private void OnDisable() => Active.Remove(this);

        /// <summary>Shared by the simulated projectile and the client-side ProjectileProxy.</summary>
        public static void BuildVisual(GameObject go, Color color, float radius)
        {
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = SpriteFactory.Circle;
            sr.sharedMaterial = SpriteFactory.Material;
            sr.color = Color.Lerp(color, Color.white, 0.25f);
            sr.sortingOrder = 20;
            go.transform.localScale = Vector3.one * (radius * 2f);
            // Glow halo and a white-hot core so bullets read clearly against any map.
            var halo = new GameObject("Halo");
            halo.transform.SetParent(go.transform, false);
            halo.transform.localScale = Vector3.one * 2.1f;
            var hs = halo.AddComponent<SpriteRenderer>();
            hs.sprite = SpriteFactory.Circle;
            hs.sharedMaterial = SpriteFactory.Material;
            hs.color = new Color(color.r, color.g, color.b, 0.22f);
            hs.sortingOrder = 18;
            var core = new GameObject("Core");
            core.transform.SetParent(go.transform, false);
            core.transform.localScale = Vector3.one * 0.45f;
            var cs = core.AddComponent<SpriteRenderer>();
            cs.sprite = SpriteFactory.Circle;
            cs.sharedMaterial = SpriteFactory.Material;
            cs.color = new Color(1f, 1f, 1f, 0.9f);
            cs.sortingOrder = 21;

            var trail = go.AddComponent<TrailRenderer>();
            trail.sharedMaterial = SpriteFactory.Material;
            trail.time = 0.2f;
            trail.minVertexDistance = 0.1f;
            trail.widthMultiplier = 1f;
            trail.startWidth = radius * 2.2f;
            trail.endWidth = 0f;
            trail.sortingOrder = 19;
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(color, 0f), new GradientColorKey(color, 1f) },
                new[] { new GradientAlphaKey(0.85f, 0f), new GradientAlphaKey(0f, 1f) });
            trail.colorGradient = gradient;
        }

        public static void ClearAll()
        {
            for (int i = Active.Count - 1; i >= 0; i--)
                if (Active[i] != null) Destroy(Active[i].gameObject);
            Active.Clear();
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            _life += dt;
            Velocity += Vector2.down * _gravity * dt;
            ApplyModifiers(dt);

            Vector2 pos = transform.position;
            Vector2 delta = Velocity * dt;
            float dist = delta.magnitude;
            if (dist > 1e-5f && Sweep(pos, delta / dist, dist)) return;

            transform.position = pos + delta;
            if (_life > MaxLifetime || Mathf.Abs(pos.x) > 70f || pos.y < -45f || pos.y > 80f) Destroy(gameObject);
        }

        /// <summary>Returns true when the projectile was consumed or redirected this frame.</summary>
        private bool Sweep(Vector2 pos, Vector2 dir, float dist)
        {
            int n = Physics2D.CircleCast(pos, _radius, dir, _filter, Hits, dist);
            SortByDistance(n);
            for (int i = 0; i < n; i++)
            {
                var hit = Hits[i];
                var fighter = hit.collider.GetComponentInParent<Fighter>();
                if (fighter != null)
                {
                    if (!CanHit(fighter) || fighter.IsDead || !fighter.IsPlaying) continue;
                    if (Owner != null && fighter != Owner && fighter.TeamId == Owner.TeamId) continue;
                    if (!_unblockable && fighter.TryBlockProjectile(this))
                    {
                        // Block.blocked: velocity *= -1, owner removed from the hit list, blocker held.
                        WasBlocked = true;
                        Velocity = -Velocity;
                        _ignore.Add(fighter);
                        if (Owner != null) _ignore.Remove(Owner);
                        _life = 0f;
                        transform.position = hit.centroid;
                        return true;
                    }
                    float knock = _damage / 55f * 6f * _knockback;
                    fighter.TakeDamage(_damage, Owner, Velocity, _unblockable, knock);
                    NotifyHitFighter(fighter);
                    Fx.Spark(hit.point, Owner != null ? Owner.Color : Color.white);
                    Destroy(gameObject);
                    return true;
                }

                var body = hit.rigidbody;
                if (body != null && body.bodyType == RigidbodyType2D.Dynamic)
                    body.AddForceAtPosition(Velocity.normalized * _damage * 0.09f * _knockback, hit.point, ForceMode2D.Impulse);
                if (_ignoreWalls) continue;

                if (_reflectsLeft > 0)
                {
                    _reflectsLeft--;
                    Bounces++;
                    Velocity = Vector2.Reflect(Velocity, hit.normal) * _speedMOnBounce;
                    _damage *= _dmgMOnBounce;
                    // Development decision: bounced bullets may hit their owner (owner leaves the ignore list).
                    if (Owner != null) _ignore.Remove(Owner);
                    transform.position = hit.centroid + hit.normal * 0.02f;
                    Fx.Spark(hit.point, Color.white);
                    Fx.Sound("bounce", hit.point);
                    NotifyHitSurface(hit.point, hit.normal, false);
                    return true;
                }

                Fx.Spark(hit.point, Owner != null ? Owner.Color : Color.white);
                Fx.Sound("impact", hit.point);
                NotifyHitSurface(hit.point, hit.normal, true);
                Destroy(gameObject);
                return true;
            }
            return false;
        }

        // ---------------------------------------------------------------- ability hooks (Abilities/CardEffect.cs)

        /// <summary>Free-form per-projectile data for abilities (e.g. "explosive").</summary>
        public readonly HashSet<string> Tags = new HashSet<string>();

        /// <summary>Homing turn rate in degrees per second toward the nearest visible enemy within HomingRadius (0 = off).</summary>
        public float HomingRate;
        public float HomingRadius = 30f;
        /// <summary>Damage grows by this fraction of the starting damage per second of flight (GROW).</summary>
        public float GrowPerSecond;
        private float _baseDamage = -1f;

        public void ScaleDamage(float factor) => _damage = Mathf.Max(0f, _damage * factor);

        public void SetVelocity(Vector2 velocity) => Velocity = velocity;

        private void ApplyModifiers(float dt)
        {
            if (GrowPerSecond > 0f)
            {
                if (_baseDamage < 0f) _baseDamage = _damage;
                _damage += _baseDamage * GrowPerSecond * dt;
            }
            if (HomingRate <= 0f) return;
            Fighter target = null;
            float best = HomingRadius;
            foreach (var f in Fighter.Active)
            {
                if (f == null || f.IsDead || !f.IsPlaying || !Threatens(f) || f == Owner) continue;
                float d = Vector2.Distance(f.Position, Position);
                if (d < best) { best = d; target = f; }
            }
            if (target == null) return;
            float speed = Velocity.magnitude;
            Vector2 want = (target.Position - Position).normalized * speed;
            float maxRad = HomingRate * Mathf.Deg2Rad * dt;
            Velocity = (Vector2)Vector3.RotateTowards(Velocity, want, maxRad, 0f);
        }

        private void NotifyHitFighter(Fighter target)
        {
            if (Owner == null) return;
            var effects = Owner.Effects;
            for (int i = 0; i < effects.Count; i++)
            {
                try { effects[i].OnHitFighter(this, target); }
                catch (System.Exception e) { Debug.LogException(e); }
            }
        }

        private void NotifyHitSurface(Vector2 point, Vector2 normal, bool consumed)
        {
            if (Owner == null) return;
            var effects = Owner.Effects;
            for (int i = 0; i < effects.Count; i++)
            {
                try { effects[i].OnHitSurface(this, point, normal, consumed); }
                catch (System.Exception e) { Debug.LogException(e); }
            }
        }

        private static void SortByDistance(int n)
        {
            for (int i = 1; i < n; i++)
            {
                var key = Hits[i];
                int j = i - 1;
                while (j >= 0 && Hits[j].distance > key.distance)
                {
                    Hits[j + 1] = Hits[j];
                    j--;
                }
                Hits[j + 1] = key;
            }
        }
    }
}

