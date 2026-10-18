using System;
using UnityEngine;

namespace Metadream.Shoot.Cards
{
    /// <summary>Gun stats. Multipliers default to the Gun..ctor (0x060002a3) value 1; base magnitudes are PROVISIONAL.</summary>
    [Serializable]
    public sealed class GunStats
    {
        public float damage = 1f;
        public float baseDamage = 55f;            // PROVISIONAL (projectile prefab value is UNKNOWN-01)
        public float attackSpeed = 0.3f;          // PROVISIONAL seconds; used as cooldown (Gun.get_usedCooldown)
        public float attackSpeedMultiplier = 1f;  // Gun.Update: sinceAttack += dt * attackSpeedMultiplier
        public float projectileSpeed = 1f;
        public float baseProjectileSpeed = 70f;   // PROVISIONAL
        public float gravity = 1f;                // multiplies MoveTransform.gravity (ctor 30)
        public int numberOfProjectiles = 1;
        public float spread;
        public int bursts;                        // FireBurst loops clamp(bursts, 1, 100) times
        public float timeBetweenBullets;
        public int reflects;
        public float speedMOnBounce = 1f;
        public float dmgMOnBounce = 1f;
        public float knockback = 1f;
        public float projectileSize = 1f;
        public bool unblockable;
        public bool ignoreWalls;

        public GunStats Clone() => (GunStats)MemberwiseClone();
        public float ProjectileDamage => baseDamage * damage;
        public float ProjectileSpeed => baseProjectileSpeed * projectileSpeed;
        public int BurstCount => Mathf.Clamp(bursts, 1, 100);
    }

    [Serializable]
    public sealed class CharacterStats
    {
        public const float BaseMaxHealth = 100f; // CharacterData..ctor
        public float maxHealth = BaseMaxHealth;
        public float sizeMultiplier = 1f;
        public float movementSpeed = 1f;
        public float jump = 1f;
        public int jumps = 1;                     // PROVISIONAL (CharacterData.jumps is serialized)
        public float lifeSteal;
        public float regeneration;
        public int respawns;
        public bool automaticReload = true;       // CharacterStatModifiers..ctor
        public float scale = 1f;
        public float mass = 100f;

        public CharacterStats Clone() => (CharacterStats)MemberwiseClone();
    }

    [Serializable]
    public sealed class BlockStats
    {
        public float cooldown = 4f;               // PROVISIONAL (Block.cooldown is serialized)
        public float cdMultiplier = 1f;           // Block..ctor
        public float cdAdd;
        public int additionalBlocks;
        public float healing;

        public BlockStats Clone() => (BlockStats)MemberwiseClone();
        public float EffectiveCooldown => Combat.CombatRules.EffectiveBlockCooldown(cooldown, cdAdd, cdMultiplier);
    }

    [Serializable]
    public sealed class AmmoStats
    {
        public int maxAmmo = 3;                   // GunAmmo..ctor
        public float reloadTime = 1.5f;           // GunAmmo..ctor (prefab value UNKNOWN-01)
        public float reloadTimeAdd;
        public float reloadTimeMultiplier = 1f;   // GunAmmo..ctor

        public AmmoStats Clone() => (AmmoStats)MemberwiseClone();
        public float EffectiveReloadTime => Combat.CombatRules.EffectiveReloadTime(reloadTime, reloadTimeAdd, reloadTimeMultiplier);
    }

    [Serializable]
    public sealed class FighterStats
    {
        public GunStats gun = new GunStats();
        public CharacterStats character = new CharacterStats();
        public BlockStats block = new BlockStats();
        public AmmoStats ammo = new AmmoStats();

        public FighterStats Clone() => new FighterStats
        {
            gun = gun.Clone(),
            character = character.Clone(),
            block = block.Clone(),
            ammo = ammo.Clone()
        };

        public static FighterStats CreateDefault(Core.BaseStats b = null)
        {
            var s = new FighterStats();
            if (b != null)
            {
                s.character.maxHealth = Mathf.Max(1f, b.maxHealth);
                s.gun.baseDamage = Mathf.Max(0f, b.bulletDamage);
                s.gun.baseProjectileSpeed = Mathf.Max(1f, b.bulletSpeed);
                s.gun.gravity = Mathf.Max(0f, b.bulletGravity);
                s.gun.attackSpeed = Mathf.Max(0.02f, b.attackCooldown);
                s.ammo.maxAmmo = Mathf.Clamp(b.maxAmmo, Combat.CombatRules.MinAmmo, Combat.CombatRules.MaxAmmo);
                s.ammo.reloadTime = Mathf.Max(0f, b.reloadTime);
                s.block.cooldown = Mathf.Max(0f, b.blockCooldown);
                s.character.movementSpeed = Mathf.Max(0.05f, b.movementSpeed);
                s.character.jumps = Mathf.Max(0, b.jumps);
            }
            StatComposer.ConfigureMassAndSize(s.character);
            return s;
        }
    }
}

