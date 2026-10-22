using System;
using UnityEngine;

namespace Metadream.Shoot.Combat
{
    /// <summary>Formulas taken from CIL; each cites the metadata token of ROUNDS 1.1.2 Assembly-CSharp.dll.</summary>
    public static class CombatRules
    {
        /// <summary>Block.IsBlocking (0x06000070): sinceBlock &lt; 0.3.</summary>
        public const float BlockWindow = 0.3f;
        /// <summary>ProjectileHit..ctor holdPlayerFor = 0.5 (blocker immunity after a block).</summary>
        public const float HoldPlayerFor = 0.5f;
        /// <summary>Block..ctor timeBetweenBlocks = 0.2.</summary>
        public const float TimeBetweenBlocks = 0.2f;
        /// <summary>DoAttacks waits 0.3 / attacks between repeated attacks.</summary>
        public const float AttackRepeatWindow = 0.3f;
        public const int MinAmmo = 1;
        public const int MaxAmmo = 90;
        public const float MinDamageFactor = 0.25f;

        /// <summary>Block.Cooldown (0x0600005e).</summary>
        public static float EffectiveBlockCooldown(float cooldown, float cdAdd, float cdMultiplier)
            => (cooldown + cdAdd) * cdMultiplier;

        public static bool IsBlocking(float sinceBlock) => sinceBlock < BlockWindow;

        /// <summary>GunAmmo.ReloadTime (0x06000790).</summary>
        public static float EffectiveReloadTime(float reloadTime, float reloadTimeAdd, float reloadTimeMultiplier)
            => (reloadTime + reloadTimeAdd) * reloadTimeMultiplier;

        /// <summary>Gun.Attack (0x06000298): attacks = clamp(round(0.5 * charge / attackSpeed), 1, 10), 1 when locked.</summary>
        public static int AttackCount(float charge, float attackSpeed, bool lockGunToDefault)
        {
            if (lockGunToDefault) return 1;
            if (attackSpeed <= 0f) return 10;
            return Mathf.Clamp(Mathf.RoundToInt(0.5f * charge / attackSpeed), 1, 10);
        }

        /// <summary>Gun.Attack gate: sinceAttack must reach usedCooldown and the gun must not be reloading, unless forced.</summary>
        public static bool CanAttack(float sinceAttack, float usedCooldown, bool isReloading, bool forceAttack)
        {
            if (sinceAttack < usedCooldown && !forceAttack) return false;
            if (isReloading && !forceAttack) return false;
            return true;
        }

        /// <summary>
        /// HealthHandler.DoDamage (0x060002b4): health -= |damage|; the death branch is health &lt; 0 (not &lt;= 0).
        /// </summary>
        public static DamageOutcome ApplyDamage(float health, float damage)
        {
            float next = health - Mathf.Abs(damage);
            return new DamageOutcome(next, next < 0f);
        }

        /// <summary>
        /// MoveTransform.GetUpwardsCompensation (0x06000363): aim offset that compensates bullet drop.
        /// Used by the bot. Both y values are set to 0.5 before the distance is taken.
        /// </summary>
        public static float UpwardsCompensation(Vector2 from, Vector2 to, float gravity, float speed)
        {
            if (speed <= 0f) return 0f;
            from.y = 0.5f;
            to.y = 0.5f;
            return Mathf.Pow(Vector2.Distance(from, to), 2.06f) * gravity / speed * 0.012f;
        }
    }

    public readonly struct DamageOutcome
    {
        public readonly float Health;
        public readonly bool Died;

        public DamageOutcome(float health, bool died)
        {
            Health = health;
            Died = died;
        }
    }

    /// <summary>GunAmmo state (0x06000791 Update, 0x06000793 Shoot, 0x06000792 ReloadAmmo).</summary>
    [Serializable]
    public sealed class AmmoState
    {
        public int maxAmmo = 3;
        public int currentAmmo = 3;
        public bool isReloading;
        public float reloadCounter;
        public float freeReloadCounter;

        public void Refill()
        {
            currentAmmo = maxAmmo;
            isReloading = false;
            reloadCounter = 0f;
            freeReloadCounter = 0f;
        }

        /// <summary>One call per spawned projectile when useAmmo is true (Gun.BulletInit -> GunAmmo.Shoot).</summary>
        public void Shoot(float reloadTime)
        {
            currentAmmo--;
            freeReloadCounter = 0f;
            if (currentAmmo <= 0)
            {
                reloadCounter = reloadTime;
                isReloading = true;
            }
        }

        /// <summary>Returns true when the magazine was refilled this tick.</summary>
        public bool Tick(float deltaTime, float reloadTime, bool automaticReload)
        {
            if (isReloading)
            {
                reloadCounter -= deltaTime;
                if (reloadCounter < 0f)
                {
                    Refill();
                    return true;
                }
                return false;
            }
            if (currentAmmo != maxAmmo)
            {
                freeReloadCounter += deltaTime;
                if (freeReloadCounter > reloadTime && automaticReload)
                {
                    currentAmmo = maxAmmo;
                    freeReloadCounter = 0f;
                    return true;
                }
            }
            return false;
        }

        public float ReloadProgress(float reloadTime)
            => isReloading && reloadTime > 0f ? Mathf.Clamp01(1f - reloadCounter / reloadTime) : 1f;
    }

    /// <summary>Block counters (0x06000061 Update, 0x06000069 TryBlock, 0x0600006e TryBlockMe).</summary>
    [Serializable]
    public sealed class BlockState
    {
        public float counter = 1000f; // Block..ctor
        public float sinceBlock = 1000f;

        public bool IsBlocking => CombatRules.IsBlocking(sinceBlock);

        public void Tick(float deltaTime)
        {
            sinceBlock += deltaTime;
            counter += deltaTime;
        }

        public bool IsReady(float cooldown) => counter >= cooldown;

        public bool TryBlock(float cooldown)
        {
            if (counter < cooldown) return false;
            counter = 0f;
            sinceBlock = 0f;
            return true;
        }

        /// <summary>Extra blocks reset only the active window (DoBlock), not the cooldown counter.</summary>
        public void DoExtraBlock() => sinceBlock = 0f;

        /// <summary>TryBlockMe: a hit inside the window is blocked and re-opens the window.</summary>
        public bool TryBlockMe()
        {
            if (!IsBlocking) return false;
            sinceBlock = 0f;
            return true;
        }

        public void ResetCooldown(float cooldown) => counter = cooldown + 1f;

        public float CooldownProgress(float cooldown) => cooldown <= 0f ? 1f : Mathf.Clamp01(counter / cooldown);
    }
}

