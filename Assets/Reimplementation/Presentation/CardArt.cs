using System.Collections.Generic;
using System.IO;
using UnityEngine;
using Metadream.Shoot.Cards;
using Metadream.Shoot.Content;

namespace Metadream.Shoot.Presentation
{
    /// <summary>
    /// Card face layout (1920×1080 reference pixels) and optional card images. See Docs/CARD_ART.md.
    ///
    /// Art area = the rectangle under the rarity band (Fill mode replaces it), Emblem = the circle in its centre
    /// (Icon mode draws a transparent PNG inside it). Images are looked up by card JSON "image" or, when empty,
    /// by the card id:
    ///   1. game folder  Content/CardArt/&lt;name&gt;.png / .jpg   (no build needed)
    ///   2. built-in     Assets/Reimplementation/Resources/Content/CardArt/&lt;name&gt;.png
    /// No image → the procedural two-letter emblem (unchanged look).
    /// </summary>
    public static class CardArt
    {
        // ---- card face layout (keep GameHud.DrawCard and Docs/CARD_ART.md in sync with these)
        public const float CardWidth = 300f, CardHeight = 480f;
        public const float Border = 6f, BandHeight = 44f;
        public const float ArtInsetX = 14f, ArtGapY = 10f;
        public const float ArtWidth = CardWidth - 2f * Border - 2f * ArtInsetX;   // 260
        public const float ArtHeight = 112f;
        public const float EmblemOuterRadius = 48f, EmblemInnerRadius = 40f;      // ring 96 Ø, disc 80 Ø
        public const float IconSize = 2f * EmblemInnerRadius;                     // 80

        /// <summary>Recommended source sizes: 4× the reference size so the card stays sharp at 4K and when enlarged (×1.15).</summary>
        public const int RecommendedFillWidth = 1040, RecommendedFillHeight = 448, RecommendedIconSize = 320;
        public const string Folder = "CardArt";
        public const string ResourceFolder = "Content/CardArt";

        public const string ModeFill = "fill", ModeIcon = "icon";

        /// <summary>Art area of a card drawn in rect r (card-local layout scaled to r).</summary>
        public static Rect ArtRect(Rect r)
        {
            float sx = r.width / CardWidth, sy = r.height / CardHeight;
            return new Rect(r.x + (Border + ArtInsetX) * sx, r.y + (Border + BandHeight + ArtGapY) * sy, ArtWidth * sx, ArtHeight * sy);
        }

        private static readonly Dictionary<string, Texture2D> Cache = new Dictionary<string, Texture2D>();
        private static readonly Dictionary<string, string> Origins = new Dictionary<string, string>();
        private static bool _hooked;

        /// <summary>The image name for a card: JSON "image" or the card id.</summary>
        public static string NameOf(CardDefinition card) => card == null ? null : string.IsNullOrWhiteSpace(card.Image) ? card.Id : card.Image;

        public static bool IsIcon(CardDefinition card) => card != null && card.ImageMode == ModeIcon;

        /// <summary>Texture for the card, or null (procedural emblem). Cached; cleared on content reload.</summary>
        public static Texture2D Get(CardDefinition card)
        {
            string name = NameOf(card);
            if (string.IsNullOrEmpty(name) || !IsSafeName(name)) return null;
            Hook();
            if (Cache.TryGetValue(name, out var cached)) return cached;
            var tex = LoadExternal(name, out string origin) ?? LoadBuiltin(name, out origin);
            Cache[name] = tex;
            Origins[name] = origin;
            return tex;
        }

        /// <summary>Where the card's image came from ("외부 …" / "내장 …"), or null when it has none.</summary>
        public static string OriginOf(CardDefinition card)
        {
            if (Get(card) == null) return null;
            return Origins.TryGetValue(NameOf(card), out var o) ? o : null;
        }

        /// <summary>True when an image file for name exists in externalRoot/CardArt or the built-in folder (content validation).</summary>
        public static bool Exists(string name, string externalRoot)
        {
            if (!IsSafeName(name)) return false;
            if (ExternalPath(name, externalRoot) != null) return true;
            return Resources.Load<Texture2D>(ResourceFolder + "/" + name) != null;
        }

        /// <summary>Image names are plain file names (no folders), so a card can never read files outside CardArt.</summary>
        public static bool IsSafeName(string name)
        {
            if (string.IsNullOrEmpty(name) || name.Length > 64) return false;
            foreach (char c in name) if (!(char.IsLetterOrDigit(c) || c == '_' || c == '-')) return false;
            return true;
        }

        public static void ClearCache()
        {
            foreach (var kv in Cache)
                if (kv.Value != null && Origins.TryGetValue(kv.Key, out var o) && o != null && o.StartsWith("외부"))
                {
                    if (Application.isPlaying) Object.Destroy(kv.Value);
                    else Object.DestroyImmediate(kv.Value);
                }
            Cache.Clear();
            Origins.Clear();
        }

        private static void Hook()
        {
            if (_hooked) return;
            _hooked = true;
            ContentDatabase.Reloaded += ClearCache;
        }

        private static string ExternalPath(string name, string root)
        {
            if (root == null) return null;
            foreach (var ext in new[] { ".png", ".jpg", ".jpeg" })
            {
                string p = Path.Combine(root, Folder, name + ext);
                if (File.Exists(p)) return p;
            }
            return null;
        }

        private static Texture2D LoadExternal(string name, out string origin)
        {
            origin = null;
            var _ = ContentDatabase.Cards; // resolves the external Content folder
            string path = ExternalPath(name, ContentDatabase.LoadedExternalDirectory);
            if (path == null) return null;
            try
            {
                var tex = new Texture2D(2, 2, TextureFormat.RGBA32, true, false) { name = "CardArt_" + name, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Trilinear };
                if (!tex.LoadImage(File.ReadAllBytes(path)))
                {
                    Object.Destroy(tex);
                    Debug.LogWarning("[CardArt] cannot decode " + path + " (PNG or JPG only)");
                    return null;
                }
                origin = "외부 " + path + " (" + tex.width + "×" + tex.height + ")";
                return tex;
            }
            catch (IOException e)
            {
                Debug.LogWarning("[CardArt] cannot read " + path + ": " + e.Message);
                return null;
            }
        }

        private static Texture2D LoadBuiltin(string name, out string origin)
        {
            var tex = Resources.Load<Texture2D>(ResourceFolder + "/" + name);
            origin = tex != null ? "내장 Resources/" + ResourceFolder + "/" + name + " (" + tex.width + "×" + tex.height + ")" : null;
            return tex;
        }
    }
}

