using System.Collections;
using UnityEngine;
using UnityEngine.Scripting;
using Metadream.Shoot.Cards;
using Metadream.Shoot.Combat;
using Metadream.Shoot.Player;
using Metadream.Shoot.Presentation;

namespace Metadream.Shoot.Abilities
{
    // Gimmick effects modelled on the original ROUNDS card abilities (approximations; see Docs/CONTENT_JSON_GUIDE.md).
    // Every parameter comes from the card JSON "effects" entry: type, radius, damage, amount, duration, chance, interval.

    internal static class EffectUtil
    {
        public static bool Roll(EffectSpec s) => s.chance >= 1f || Random.value < s.chance;

        public static void ForEnemiesNear(Fighter owner, Vector2 center, float radius, System.Action<Fighter> action)
        {
            foreach (var f in Fighter.Active.ToArray())
            {
                if (f == null || f == owner || f.IsDead || !f.IsPlaying) continue;
                if (owner != null && f.TeamId == owner.TeamId) continue;
                if (Vector2.Distance(f.Position, center) - f.Radius > radius) continue;
                action(f);
            }
        }
    }

    [Preserve, CardEffect("homing", "탄이 가까운 적을 따라감: amount(초당 회전 각도, 기본 240), radius(탐지 거리)")]
    public sealed class Homing : CardEffect
    {
        public override string Validate() => Spec.amount < 0f ? "homing needs amount >= 0" : null;

        public override void OnShoot(Projectile p)
        {
            p.HomingRate = Spec.amount > 0f ? Spec.amount : 240f;
            if (Spec.radius > 0f) p.HomingRadius = Spec.radius;
        }
    }

    [Preserve, CardEffect("grow_damage", "날아갈수록 피해 증가: amount(초당 기본 피해의 비율, 0.5 = 초당 +50%)")]
    public sealed class GrowDamage : CardEffect
    {
        public override string Validate() => Spec.amount > 0f ? null : "grow_damage needs amount > 0";
        public override void OnShoot(Projectile p) => p.GrowPerSecond += Spec.amount;
    }

    [Preserve, CardEffect("slow_on_hit", "명중 시 감속: amount(감속 비율 0~0.95), duration(초)")]
    public sealed class SlowOnHit : CardEffect
    {
        public override string Validate() => Spec.amount > 0f && Spec.amount < 1f && Spec.duration > 0f ? null : "slow_on_hit needs 0 < amount < 1 and duration > 0";

        public override void OnHitFighter(Projectile p, Fighter target)
        {
            if (!EffectUtil.Roll(Spec)) return;
            target.AddSpeedModifier(1f - Spec.amount, Spec.duration);
            Fx.Ring(target.Position, new Color(0.6f, 0.85f, 1f), 1.8f);
        }
    }

    [Preserve, CardEffect("push_on_hit", "명중 시 탄 방향으로 밀어냄: amount(속도)")]
    public sealed class PushOnHit : CardEffect
    {
        public override string Validate() => Spec.amount > 0f ? null : "push_on_hit needs amount > 0";
        public override void OnHitFighter(Projectile p, Fighter target) => target.AddImpulse(p.Velocity.normalized * Spec.amount);
    }

    [Preserve, CardEffect("timed_bomb", "명중/착탄 지점에 시한폭탄: duration(지연 초), radius, damage, amount(넉백)")]
    public sealed class TimedBomb : CardEffect
    {
        public override string Validate() => Spec.radius > 0f && Spec.duration >= 0f ? null : "timed_bomb needs radius > 0 and duration >= 0";

        public override void OnHitFighter(Projectile p, Fighter target) => Arm(p.Position);

        public override void OnHitSurface(Projectile p, Vector2 point, Vector2 normal, bool consumed)
        {
            if (consumed) Arm(point);
        }

        private void Arm(Vector2 at)
        {
            if (Owner == null) return;
            Fx.Ring(at, Owner.Color, Spec.radius * 0.6f);
            Owner.StartCoroutine(Detonate(at));
        }

        private IEnumerator Detonate(Vector2 at)
        {
            yield return new WaitForSeconds(Spec.duration);
            if (Owner != null) Explosion.Detonate(at, Spec.radius, Spec.damage, Owner, Spec.amount);
        }
    }

    [Preserve, CardEffect("health_cost_on_shoot", "쏠 때마다 체력 소모: damage(발당, 체력 1 아래로는 안 내려감)")]
    public sealed class HealthCostOnShoot : CardEffect
    {
        public override string Validate() => Spec.damage > 0f ? null : "health_cost_on_shoot needs damage > 0";

        public override void OnShoot(Projectile p)
        {
            float cost = Mathf.Min(Spec.damage, Owner.Health - 1f);
            if (cost > 0f) Owner.TakeDamage(cost, null, Vector2.zero, true, 0f, true);
        }
    }

    [Preserve, CardEffect("slow_aura", "주변 적 지속 감속: radius, amount(감속 비율)")]
    public sealed class SlowAura : CardEffect
    {
        public override string Validate() => Spec.radius > 0f && Spec.amount > 0f && Spec.amount < 1f ? null : "slow_aura needs radius > 0 and 0 < amount < 1";
        public override void OnTick(float dt) => EffectUtil.ForEnemiesNear(Owner, Owner.Position, Spec.radius, f => f.AddSpeedModifier(1f - Spec.amount, 0.25f));
    }

    [Preserve, CardEffect("drain_aura", "주변 적에게 초당 피해, 일부 회복: radius, damage(초당), amount(회복 비율 0~1)")]
    public sealed class DrainAura : CardEffect
    {
        private float _fx;
        public override string Validate() => Spec.radius > 0f && Spec.damage > 0f ? null : "drain_aura needs radius > 0 and damage > 0";

        public override void OnTick(float dt)
        {
            bool any = false;
            EffectUtil.ForEnemiesNear(Owner, Owner.Position, Spec.radius, f =>
            {
                float d = Spec.damage * dt;
                if (f.TakeDamage(d, Owner, Vector2.zero, true, 0f, true)) { Owner.Heal(d * Spec.amount); any = true; }
            });
            _fx -= dt;
            if (any && _fx <= 0f) { _fx = 0.5f; Fx.Ring(Owner.Position, Owner.Color, Spec.radius * 2f); }
        }
    }

    [Preserve, CardEffect("blast_on_block", "방어 시 주변 폭발: radius, damage, amount(넉백)")]
    public sealed class BlastOnBlock : CardEffect
    {
        public override string Validate() => Spec.radius > 0f && Spec.damage >= 0f ? null : "blast_on_block needs radius > 0";
        public override void OnBlock()
        {
            if (EffectUtil.Roll(Spec)) Explosion.Detonate(Owner.Position, Spec.radius, Spec.damage, Owner, Spec.amount, false);
        }
    }

    [Preserve, CardEffect("pull_on_block", "방어 시 주변 적을 끌어당김: radius, amount(끌어당기는 속도)")]
    public sealed class PullOnBlock : CardEffect
    {
        public override string Validate() => Spec.radius > 0f && Spec.amount > 0f ? null : "pull_on_block needs radius > 0 and amount > 0";
        public override void OnBlock()
        {
            EffectUtil.ForEnemiesNear(Owner, Owner.Position, Spec.radius, f => f.AddImpulse((Owner.Position - f.Position).normalized * Spec.amount));
            Fx.Ring(Owner.Position, new Color(0.45f, 0.2f, 0.8f), Spec.radius * 2f);
        }
    }

    [Preserve, CardEffect("slow_on_block", "방어 시 주변 적 감속: radius, amount(감속 비율), duration(초)")]
    public sealed class SlowOnBlock : CardEffect
    {
        public override string Validate() => Spec.radius > 0f && Spec.amount > 0f && Spec.amount < 1f && Spec.duration > 0f ? null : "slow_on_block needs radius > 0, 0 < amount < 1, duration > 0";
        public override void OnBlock()
        {
            EffectUtil.ForEnemiesNear(Owner, Owner.Position, Spec.radius, f => f.AddSpeedModifier(1f - Spec.amount, Spec.duration));
            Fx.Ring(Owner.Position, new Color(0.6f, 0.85f, 1f), Spec.radius * 2f);
        }
    }

    [Preserve, CardEffect("heal_on_block", "방어 시 회복: amount(회복량)")]
    public sealed class HealOnBlock : CardEffect
    {
        public override string Validate() => Spec.amount > 0f ? null : "heal_on_block needs amount > 0";
        public override void OnBlock()
        {
            Owner.Heal(Spec.amount);
            Fx.Ring(Owner.Position, new Color(0.35f, 0.95f, 0.5f), 3f);
        }
    }

    [Preserve, CardEffect("teleport_on_block", "방어 시 조준 방향으로 순간이동: amount(거리)")]
    public sealed class TeleportOnBlock : CardEffect
    {
        private static readonly RaycastHit2D[] Hits = new RaycastHit2D[8];
        public override string Validate() => Spec.amount > 0f ? null : "teleport_on_block needs amount > 0";

        public override void OnBlock()
        {
            Vector2 from = Owner.Position, dir = Owner.AimDirection.sqrMagnitude > 0.01f ? Owner.AimDirection.normalized : Vector2.right;
            float dist = Spec.amount;
            int n = Physics2D.CircleCastNonAlloc(from, Owner.Radius * 0.9f, dir, Hits, dist);
            for (int i = 0; i < n; i++)
            {
                var h = Hits[i];
                if (h.collider == null || h.collider.isTrigger || h.collider.GetComponentInParent<Fighter>() != null) continue;
                dist = Mathf.Min(dist, Mathf.Max(0f, h.distance - 0.05f));
            }
            Fx.Burst(from, Owner.Color, 8, 6f);
            Owner.Teleport(from + dir * dist);
            Fx.Ring(Owner.Position, Owner.Color, 2.5f);
        }
    }

    [Preserve, CardEffect("dash_on_block", "방어 시 조준 방향으로 돌진: amount(속도)")]
    public sealed class DashOnBlock : CardEffect
    {
        public override string Validate() => Spec.amount > 0f ? null : "dash_on_block needs amount > 0";
        public override void OnBlock() => Owner.AddImpulse(Owner.AimDirection.normalized * Spec.amount);
    }

    [Preserve, CardEffect("reload_on_block", "방어 시 즉시 재장전")]
    public sealed class ReloadOnBlock : CardEffect
    {
        public override void OnBlock() => Owner.Ammo.Refill();
    }

    [Preserve, CardEffect("empower_on_block", "방어 후 다음 탄 강화: amount(피해 배율, 예 2), duration(유지 초, 0 = 무제한)")]
    public sealed class EmpowerOnBlock : CardEffect
    {
        private float _until = -1f;
        public override string Validate() => Spec.amount > 0f ? null : "empower_on_block needs amount > 0";
        public override void OnBlock() => _until = Spec.duration > 0f ? Time.time + Spec.duration : float.MaxValue;

        public override void OnShoot(Projectile p)
        {
            if (Time.time > _until) return;
            _until = -1f;
            p.ScaleDamage(Spec.amount);
            p.SetVelocity(p.Velocity * 1.5f);
            Fx.Ring(p.Position, Owner.Color, 1.5f);
        }
    }

    [Preserve, CardEffect("block_on_empty", "마지막 탄을 쏘면 자동 방어")]
    public sealed class BlockOnEmpty : CardEffect
    {
        public override void OnShoot(Projectile p)
        {
            if (Owner.Ammo.currentAmmo <= 1) Owner.Block.DoExtraBlock();
        }
    }

    [Preserve, CardEffect("reload_on_damage", "피해를 주면 즉시 재장전")]
    public sealed class ReloadOnDamage : CardEffect
    {
        public override void OnDealtDamage(Fighter target, float amount) => Owner.Ammo.Refill();
    }

    [Preserve, CardEffect("block_on_damage", "피해를 주면 방어 재사용 대기 초기화")]
    public sealed class BlockOnDamage : CardEffect
    {
        public override void OnDealtDamage(Fighter target, float amount) => Owner.RefreshBlock();
    }

    [Preserve, CardEffect("speed_after_damage", "피해를 주면 잠시 빨라짐: amount(+비율, 0.5 = +50%), duration(초)")]
    public sealed class SpeedAfterDamage : CardEffect
    {
        public override string Validate() => Spec.amount > 0f && Spec.duration > 0f ? null : "speed_after_damage needs amount > 0 and duration > 0";
        public override void OnDealtDamage(Fighter target, float amount) => Owner.AddSpeedModifier(1f + Spec.amount, Spec.duration);
    }
}
