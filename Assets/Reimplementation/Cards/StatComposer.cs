using UnityEngine;
using Metadream.Shoot.Combat;

namespace Metadream.Shoot.Cards
{
    /// <summary>Card stat composition following ApplyCardStats.ApplyStats (0x06000032) and CopyGunStats (0x06000033).</summary>
    public static class StatComposer
    {
        public static void Apply(FighterStats stats, CardModifiers m)
        {
            var c = stats.character;
            var a = stats.ammo;
            var b = stats.block;
            var g = stats.gun;

            a.maxAmmo = Mathf.Clamp(a.maxAmmo + m.ammo, CombatRules.MinAmmo, CombatRules.MaxAmmo);
            a.reloadTimeMultiplier *= m.reloadTimeMultiplier;
            a.reloadTimeAdd += m.reloadTimeAdd;

            c.maxHealth *= m.health;
            c.sizeMultiplier *= m.size;
            c.movementSpeed *= m.movementSpeed;
            c.jump *= m.jump;
            c.jumps += m.numberOfJumps;
            c.lifeSteal += m.lifeSteal;
            c.regeneration += m.regen;
            c.respawns += m.respawns;

            b.cdMultiplier *= m.blockCdMultiplier;
            b.cdAdd += m.blockCdAdd;
            b.additionalBlocks += m.additionalBlocks;
            b.healing += m.blockHealing;

            CopyGunStats(g, m);
            ConfigureMassAndSize(c);
        }

        public static void CopyGunStats(GunStats g, CardModifiers m)
        {
            // damage is computed against the projectile count at method entry, before K is added.
            g.damage = ComposeDamage(g.damage, m.damage, g.numberOfProjectiles, m.numberOfProjectiles);
            g.knockback *= m.knockback; // original has a similar correction path; not reproduced (see unknowns)
            g.projectileSpeed *= m.projectileSpeed;
            g.attackSpeed *= m.attackSpeed;
            g.gravity *= m.gravity;
            g.speedMOnBounce *= m.speedMOnBounce;
            g.dmgMOnBounce *= m.dmgMOnBounce;
            g.projectileSize *= m.projectileSize;
            g.numberOfProjectiles += m.numberOfProjectiles;
            g.reflects += m.reflects;
            g.bursts += m.bursts;
            g.spread += m.spread;
            g.timeBetweenBullets += m.timeBetweenBullets;
            if (m.unblockable) g.unblockable = true;
            if (m.ignoreWalls) g.ignoreWalls = true;
        }

        /// <summary>r = (K != 0 &amp;&amp; N != 1) ? K/(K+N) : 1; damage = max(0.25, G * (1 - r * (1 - D))).</summary>
        public static float ComposeDamage(float currentDamage, float cardDamage, int currentProjectiles, int addedProjectiles)
        {
            float r = addedProjectiles != 0 && currentProjectiles != 1
                ? addedProjectiles / (float)(addedProjectiles + currentProjectiles)
                : 1f;
            return Mathf.Max(CombatRules.MinDamageFactor, currentDamage * (1f - r * (1f - cardDamage)));
        }

        /// <summary>CharacterStatModifiers.ConfigureMassAndSize (0x060000ee).</summary>
        public static void ConfigureMassAndSize(CharacterStats c)
        {
            // ROUNDS uses maxHealth / 100; relative to the match base health so the default body size stays the same.
            float q = c.maxHealth / Mathf.Max(1f, c.startHealth) * 1.2f;
            c.scale = 1.2f * Mathf.Pow(q, 0.2f) * c.sizeMultiplier;
            c.mass = 100f * Mathf.Pow(q, 0.8f) * c.sizeMultiplier;
        }
    }
}

