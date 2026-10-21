using UnityEditor;

namespace Metadream.Shoot.EditorTools
{
    /// <summary>
    /// Import settings for card images in Resources/Content/CardArt: keep the original size (no power-of-two
    /// stretching, which would break the aspect ratio used by ScaleAndCrop), sRGB colour, smooth downscaling.
    /// </summary>
    public sealed class CardArtImporter : AssetPostprocessor
    {
        private void OnPreprocessTexture()
        {
            if (!assetPath.Replace('\\', '/').Contains("/Resources/Content/CardArt/")) return;
            var ti = (TextureImporter)assetImporter;
            ti.textureType = TextureImporterType.Default;
            ti.npotScale = TextureImporterNPOTScale.None;
            ti.sRGBTexture = true;
            ti.alphaSource = TextureImporterAlphaSource.FromInput;
            ti.alphaIsTransparency = true;
            ti.mipmapEnabled = true;
            ti.wrapMode = UnityEngine.TextureWrapMode.Clamp;
            ti.filterMode = UnityEngine.FilterMode.Trilinear;
            ti.maxTextureSize = 2048;
            ti.textureCompression = TextureImporterCompression.CompressedHQ;
        }
    }
}

