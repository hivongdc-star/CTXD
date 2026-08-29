using UnityEditor;
using UnityEngine;

namespace CTXD.Client.Editor
{
    sealed class EntryLegacyTextureImporter : AssetPostprocessor
    {
        void OnPreprocessTexture()
        {
            if (!assetPath.StartsWith("Assets/Game/Resources/LegacyVisual/Entry/")) return;

            var importer=(TextureImporter)assetImporter;
            importer.textureType=TextureImporterType.Sprite;
            importer.spriteImportMode=SpriteImportMode.Single;
            importer.alphaIsTransparency=true;
            importer.mipmapEnabled=false;
            importer.textureCompression=TextureImporterCompression.Uncompressed;
            importer.filterMode=FilterMode.Bilinear;
            importer.maxTextureSize=2048;
            importer.isReadable=assetPath.Contains("_hit.png") || assetPath.Contains("/start_");

            if (assetPath.Contains("/Login/button_"))
                importer.spriteBorder=new Vector4(4,6,18,4);
        }
    }
}
