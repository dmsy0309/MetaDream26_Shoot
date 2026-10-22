using UnityEditor;
using UnityEngine;
namespace Metadream.Shoot.EditorTools
{
    public sealed class CharacterArtImport : AssetPostprocessor
    {
        private void OnPreprocessTexture()
        {
            if (!assetPath.StartsWith("Assets/Reimplementation/Resources/CharacterArt/")) return;
            var t = (TextureImporter)assetImporter;
            t.textureType = TextureImporterType.Default;
            t.textureCompression = TextureImporterCompression.Uncompressed;
            t.alphaIsTransparency = true;
            t.mipmapEnabled = false;
            t.npotScale = TextureImporterNPOTScale.None;
            t.maxTextureSize = 2048;
            t.filterMode = FilterMode.Bilinear;
            t.wrapMode = TextureWrapMode.Clamp;
        }
    }
}
