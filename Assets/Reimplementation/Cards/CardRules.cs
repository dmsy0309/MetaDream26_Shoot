using System;
using System.Collections.Generic;

namespace Metadream.Shoot.Cards
{
    /// <summary>Numeric value of CardInfo.rarity; weights from CardChoice.GetRandomCard (0x060000ad).</summary>
    public enum CardRarity
    {
        Common = 0,
        Uncommon = 1,
        Rare = 2
    }

    /// <summary>Identity values compose to "no change" under StatComposer.</summary>
    [Serializable]
    public sealed class CardModifiers
    {
        // character
        public int ammo;
        public float reloadTimeMultiplier = 1f;
        public float reloadTimeAdd;
        public float health = 1f;
        public float size = 1f;
        public float movementSpeed = 1f;
        public float jump = 1f;
        public int numberOfJumps;
        public float lifeSteal;
        public float regen;
        public int respawns;
        // block
        public float blockCdMultiplier = 1f;
        public float blockCdAdd;
        public int additionalBlocks;
        public float blockHealing;
        // gun
        public float damage = 1f;
        public float attackSpeed = 1f;
        public float projectileSpeed = 1f;
        public float gravity = 1f;
        public float speedMOnBounce = 1f;
        public float dmgMOnBounce = 1f;
        public float knockback = 1f;
        public float projectileSize = 1f;
        public int numberOfProjectiles;
        public int reflects;
        public int bursts;
        public float spread;
        public float timeBetweenBullets;
        public bool unblockable;
        public bool ignoreWalls;
    }

    public readonly struct CardStatLine
    {
        public readonly string Label;
        public readonly string Amount;
        public readonly bool Positive;

        public CardStatLine(string label, string amount, bool positive)
        {
            Label = label;
            Amount = amount;
            Positive = positive;
        }
    }

    public sealed class CardDefinition
    {
        public string Id;
        /// <summary>Position in CardCatalog.All; used as the network id.</summary>
        public int Index = -1;
        public string Title;
        public string Subtitle;
        public string Description;
        public CardRarity Rarity;
        public bool AllowMultiple = true;
        public string[] Categories = Array.Empty<string>();
        public string[] BlacklistedCategories = Array.Empty<string>();
        public CardModifiers Modifiers = new CardModifiers();
        /// <summary>Abilities beyond stat changes (Abilities/*.cs, matched by EffectSpec.type).</summary>
        public EffectSpec[] Effects = Array.Empty<EffectSpec>();
        public CardStatLine[] Lines = Array.Empty<CardStatLine>();
        /// <summary>File the card was loaded from (for error messages).</summary>
        public string Source = "";

        public override string ToString() => Id;
    }

    /// <summary>
    /// One "effects" entry of a card JSON. A flat set of common parameters; each effect documents which it reads.
    /// </summary>
    [Serializable]
    public sealed class EffectSpec
    {
        public string type = "";
        public float radius;
        public float damage;
        public float amount;
        public float duration;
        public float chance = 1f;
        public float interval;
    }

    public static class CardRules
    {
        public static int RarityWeight(CardRarity rarity)
        {
            switch (rarity)
            {
                case CardRarity.Common: return 10;
                case CardRarity.Uncommon: return 4;
                case CardRarity.Rare: return 1;
                default: return 0;
            }
        }

        /// <summary>
        /// SpawnUniqueCard (0x060000ae) constraints: not already offered, owned !allowMultiple cards excluded,
        /// and no owned card may blacklist one of the candidate's categories.
        /// </summary>
        public static bool IsEligible(CardDefinition candidate, IReadOnlyList<CardDefinition> owned, IReadOnlyList<CardDefinition> offered)
        {
            if (candidate == null) return false;
            if (offered != null)
                foreach (var o in offered)
                    if (o.Id == candidate.Id) return false;
            if (owned != null)
            {
                foreach (var o in owned)
                {
                    if (o.Id == candidate.Id && !candidate.AllowMultiple) return false;
                    foreach (var banned in o.BlacklistedCategories)
                        foreach (var cat in candidate.Categories)
                            if (banned == cat) return false;
                }
            }
            return true;
        }

        /// <summary>Per-card weighted pick; roll01 in [0,1). Denominator is the sum of candidate weights.</summary>
        public static CardDefinition PickWeighted(IReadOnlyList<CardDefinition> candidates, double roll01)
        {
            if (candidates == null || candidates.Count == 0) return null;
            int total = 0;
            foreach (var c in candidates) total += RarityWeight(c.Rarity);
            if (total <= 0) return null;
            double target = Math.Max(0.0, Math.Min(roll01, 0.999999999)) * total;
            int cumulative = 0;
            foreach (var c in candidates)
            {
                cumulative += RarityWeight(c.Rarity);
                if (target < cumulative) return c;
            }
            return candidates[candidates.Count - 1];
        }

        /// <summary>
        /// Builds an offer by recomputing the eligible set before every draw (development decision; the original
        /// re-rolls recursively). If candidates run out the offer is shorter (original exhaustion behaviour unknown).
        /// </summary>
        public static List<CardDefinition> DrawOffer(IReadOnlyList<CardDefinition> pool, IReadOnlyList<CardDefinition> owned, int count, Random rng)
        {
            var offer = new List<CardDefinition>(count);
            var eligible = new List<CardDefinition>();
            for (int i = 0; i < count; i++)
            {
                eligible.Clear();
                foreach (var c in pool)
                    if (IsEligible(c, owned, offer)) eligible.Add(c);
                var pick = PickWeighted(eligible, rng.NextDouble());
                if (pick == null) break;
                offer.Add(pick);
            }
            return offer;
        }
    }
}

