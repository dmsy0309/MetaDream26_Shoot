using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.Scripting;
using Metadream.Shoot.Cards;
using Metadream.Shoot.Combat;
using Metadream.Shoot.Player;

namespace Metadream.Shoot.Abilities
{
    /// <summary>Marks a CardEffect subclass and gives it the id used in card JSON ("effects": [{"type": id}]).</summary>
    [AttributeUsage(AttributeTargets.Class, Inherited = false)]
    public sealed class CardEffectAttribute : Attribute
    {
        public CardEffectAttribute(string id, string summary = "")
        {
            Id = id;
            Summary = summary;
        }

        public string Id { get; }
        public string Summary { get; }
    }

    /// <summary>
    /// Base class for card abilities beyond plain stat changes. One instance per owned card copy.
    /// All hooks run on the authoritative simulation (offline game or host/server), never on network clients;
    /// use Fx.* for visuals so clients see them. Add a new ability = one file with [CardEffect("id")].
    /// </summary>
    [Preserve]
    public abstract class CardEffect
    {
        public EffectSpec Spec { get; internal set; }
        public Fighter Owner { get; internal set; }
        public CardDefinition Card { get; internal set; }

        /// <summary>Return an error message for bad parameters, or null when valid (checked at content load).</summary>
        public virtual string Validate() => null;
        public virtual void OnAttach() { }
        public virtual void OnDetach() { }
        /// <summary>Every frame while the owner is alive and playing.</summary>
        public virtual void OnTick(float deltaTime) { }
        public virtual void OnRevive() { }
        /// <summary>A projectile of the owner was spawned (modify it here).</summary>
        public virtual void OnShoot(Projectile projectile) { }
        /// <summary>An owner projectile damaged an enemy fighter (after damage was applied).</summary>
        public virtual void OnHitFighter(Projectile projectile, Fighter target) { }
        /// <summary>An owner projectile touched terrain or a box. consumed = the projectile is destroyed (no bounce left).</summary>
        public virtual void OnHitSurface(Projectile projectile, Vector2 point, Vector2 normal, bool consumed) { }
        /// <summary>The owner pressed block successfully (window opened).</summary>
        public virtual void OnBlock() { }
        /// <summary>The owner blocked an incoming projectile.</summary>
        public virtual void OnBlockedProjectile(Projectile projectile) { }
        public virtual void OnDamaged(float amount, Fighter source) { }
        public virtual void OnDealtDamage(Fighter target, float amount) { }
    }

    /// <summary>Finds every [CardEffect] class in this assembly and creates instances by id.</summary>
    public static class EffectRegistry
    {
        private static Dictionary<string, Type> _types;
        private static Dictionary<string, string> _summaries;

        public static IEnumerable<string> Ids
        {
            get
            {
                Ensure();
                return _types.Keys;
            }
        }

        public static string SummaryOf(string id)
        {
            Ensure();
            return _summaries.TryGetValue(id, out var s) ? s : "";
        }

        private static void Ensure()
        {
            if (_types != null) return;
            _types = new Dictionary<string, Type>(StringComparer.Ordinal);
            _summaries = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var t in typeof(CardEffect).Assembly.GetTypes())
            {
                if (t.IsAbstract || !typeof(CardEffect).IsAssignableFrom(t)) continue;
                var attr = t.GetCustomAttribute<CardEffectAttribute>();
                if (attr == null) continue;
                if (_types.ContainsKey(attr.Id))
                {
                    Debug.LogError("[Effects] duplicate effect id '" + attr.Id + "' on " + t.FullName + " and " + _types[attr.Id].FullName);
                    continue;
                }
                _types[attr.Id] = t;
                _summaries[attr.Id] = attr.Summary;
            }
        }

        public static bool IsKnown(string id)
        {
            Ensure();
            return id != null && _types.ContainsKey(id);
        }

        /// <summary>Error message or null.</summary>
        public static string Validate(EffectSpec spec)
        {
            if (spec == null) return "effect is null";
            if (string.IsNullOrWhiteSpace(spec.type)) return "missing \"type\"";
            if (!IsKnown(spec.type)) return "unknown effect type '" + spec.type + "' (known: " + string.Join(", ", Ids) + ")";
            var probe = (CardEffect)Activator.CreateInstance(_types[spec.type]);
            probe.Spec = spec;
            return probe.Validate();
        }

        public static CardEffect Create(EffectSpec spec, Fighter owner, CardDefinition card)
        {
            if (!IsKnown(spec?.type))
            {
                Debug.LogError("[Effects] cannot create unknown effect '" + spec?.type + "' for card " + card?.Id);
                return null;
            }
            var e = (CardEffect)Activator.CreateInstance(_types[spec.type]);
            e.Spec = spec;
            e.Owner = owner;
            e.Card = card;
            return e;
        }
    }

    /// <summary>Area damage/knockback helper shared by effects.</summary>
    public static class Explosion
    {
        private static readonly Collider2D[] Overlap = new Collider2D[32];

        /// <summary>Damages enemies of source inside radius (linear falloff to 50%). Returns fighters hit.</summary>
        public static int Detonate(Vector2 center, float radius, float damage, Fighter source, float knockback, bool blockable = true)
        {
            int hits = 0;
            Color color = source != null ? source.Color : Color.white;
            foreach (var f in Fighter.Active.ToArray())
            {
                if (f == null || f.IsDead || !f.IsPlaying || f == source) continue;
                if (source != null && f.TeamId == source.TeamId) continue;
                Vector2 delta = f.Position - center;
                float dist = Mathf.Max(0f, delta.magnitude - f.Radius);
                if (dist > radius) continue;
                float falloff = 1f - 0.5f * (dist / Mathf.Max(0.01f, radius));
                Vector2 dir = delta.sqrMagnitude > 0.0001f ? delta.normalized : Vector2.up;
                if (f.TakeDamage(damage * falloff, source, dir, !blockable, knockback * falloff)) hits++;
            }
            PushBodies(center, radius, knockback);
            Presentation.Fx.Ring(center, color, radius * 2f);
            Presentation.Fx.Burst(center, color, 10, radius * 3f);
            Presentation.Fx.Shake(0.2f);
            DevLog.Verbose("[Effect] explosion at " + center.ToString("F1") + " r=" + radius + " dmg=" + damage + " hits=" + hits);
            return hits;
        }

        /// <summary>Knockback only (no damage) away from center.</summary>
        public static int Push(Vector2 center, float radius, float strength, Fighter source)
        {
            int hits = 0;
            foreach (var f in Fighter.Active.ToArray())
            {
                if (f == null || f.IsDead || !f.IsPlaying || f == source) continue;
                if (source != null && f.TeamId == source.TeamId) continue;
                Vector2 delta = f.Position - center;
                if (delta.magnitude - f.Radius > radius) continue;
                float k = 1f - 0.5f * Mathf.Clamp01(delta.magnitude / Mathf.Max(0.01f, radius));
                f.AddImpulse((delta.sqrMagnitude > 0.0001f ? delta.normalized : Vector2.up) * strength * k);
                hits++;
            }
            PushBodies(center, radius, strength * 0.5f);
            Presentation.Fx.Ring(center, Color.white, radius * 2f);
            DevLog.Verbose("[Effect] push at " + center.ToString("F1") + " r=" + radius + " hits=" + hits);
            return hits;
        }

        private static void PushBodies(Vector2 center, float radius, float strength)
        {
            int n = Physics2D.OverlapCircle(center, radius, new ContactFilter2D { useTriggers = false }, Overlap);
            for (int i = 0; i < n; i++)
            {
                var rb = Overlap[i].attachedRigidbody;
                if (rb == null || rb.bodyType != RigidbodyType2D.Dynamic || Overlap[i].GetComponentInParent<Fighter>() != null) continue;
                Vector2 d = (Vector2)rb.worldCenterOfMass - center;
                rb.AddForce((d.sqrMagnitude > 0.0001f ? d.normalized : Vector2.up) * strength * rb.mass, ForceMode2D.Impulse);
            }
        }
    }

    /// <summary>Logs only when developer debug mode (client) or server debug logging is on.</summary>
    public static class DevLog
    {
        public static bool Enabled { get; set; }

        public static void Verbose(string message)
        {
            if (Enabled) Debug.Log(message);
        }
    }
}

