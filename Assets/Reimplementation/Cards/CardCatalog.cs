using System.Collections.Generic;
using Metadream.Shoot.Content;

namespace Metadream.Shoot.Cards
{
    /// <summary>
    /// Card list. Cards are data: one JSON file per card in Resources/Content/Cards (see Docs/EXTENDING.md).
    /// Network ids are positions in this id-sorted list.
    /// </summary>
    public static class CardCatalog
    {
        public const string CatBounce = "bounce";
        public const string CatPierce = "pierce";
        public const string CatMultishot = "multishot";
        public const string CatBlock = "block";
        public const string CatSurvival = "survival";

        public static IReadOnlyList<CardDefinition> All => ContentDatabase.Cards;

        public static CardDefinition Find(string id) => ContentDatabase.FindCard(id);

        /// <summary>Cards allowed by a pool of ids (empty pool = every card).</summary>
        public static List<CardDefinition> Pool(string[] ids)
        {
            var list = new List<CardDefinition>();
            foreach (var c in All)
                if (ids == null || ids.Length == 0 || System.Array.IndexOf(ids, c.Id) >= 0) list.Add(c);
            return list.Count > 0 ? list : new List<CardDefinition>(All);
        }
    }
}

