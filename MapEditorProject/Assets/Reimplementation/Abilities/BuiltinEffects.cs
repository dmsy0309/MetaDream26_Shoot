using UnityEngine;
using UnityEngine.Scripting;
using Metadream.Shoot.Combat;
using Metadream.Shoot.Player;

namespace Metadream.Shoot.Abilities
{
    // Built-in abilities. Parameters come from the card JSON "effects" entry (EffectSpec fields).

    /// <summary>radius, damage, amount = knockback. Explodes where an owner projectile hits a fighter or stops on terrain.</summary>
    [Preserve, CardEffect("explosion_on_hit", "명중/착탄 지점 폭발: radius, damage, amount(넉백)")]
    public sealed class ExplosionOnHit : CardEffect
    {
        public override string Validate()
        {
            if (!(Spec.radius > 0f)) return "explosion_on_hit needs radius > 0";
            if (Spec.damage < 0f) return "explosion_on_hit needs damage >= 0";
            return null;
        }

        public override void OnHitFighter(Projectile projectile, Fighter target)
            => Explosion.Detonate(projectile.Position, Spec.radius, Spec.damage, Owner, Spec.amount);

        public override void OnHitSurface(Projectile projectile, Vector2 point, Vector2 normal, bool consumed)
        {
            if (consumed) Explosion.Detonate(point, Spec.radius, Spec.damage, Owner, Spec.amount);
        }
    }

    /// <summary>radius, amount = push strength. Pushes enemies away whenever the owner blocks.</summary>
    [Preserve, CardEffect("shockwave_on_block", "방어 시 충격파: radius, amount(밀어내는 속도)")]
    public sealed class ShockwaveOnBlock : CardEffect
    {
        public override string Validate()
        {
            if (!(Spec.radius > 0f)) return "shockwave_on_block needs radius > 0";
            if (!(Spec.amount > 0f)) return "shockwave_on_block needs amount > 0";
            return null;
        }

        public override void OnBlock() => Explosion.Push(Owner.Position, Spec.radius, Spec.amount, Owner);
    }

    /// <summary>damage = damage per second, duration = seconds. Poisons fighters hit by owner projectiles.</summary>
    [Preserve, CardEffect("poison_on_hit", "명중 시 중독: damage(초당 피해), duration(초)")]
    public sealed class PoisonOnHit : CardEffect
    {
        public override string Validate()
        {
            if (!(Spec.damage > 0f)) return "poison_on_hit needs damage > 0 (per second)";
            if (!(Spec.duration > 0f)) return "poison_on_hit needs duration > 0";
            return null;
        }

        public override void OnHitFighter(Projectile projectile, Fighter target)
            => target.AddDamageOverTime(Spec.damage, Spec.duration, Owner);
    }
}

