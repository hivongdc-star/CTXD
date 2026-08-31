#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;

namespace CTXD.Client.Editor
{
    static class LegacyGeneralPortraitImporter
    {
        const string LegacyRoot = @"D:\Sever";

        [MenuItem("CTXD/Legacy Visual/Import General Portraits")]
        static void ImportGeneralPortraits()
        {
            var source = Path.Combine(LegacyRoot, "GCLDServer", "wwwroot", "assets", "zh_CN", "img", "generalPicMax");
            if (!Directory.Exists(source))
            {
                Debug.LogError("CTXD: authoritative generalPicMax source not found under D:\\Sever.");
                return;
            }

            var destination = Path.Combine(Application.dataPath, "Game", "Resources", "LegacyVisual", "GeneralPicMax");
            Directory.CreateDirectory(destination);
            var copied = 0;

            foreach (var sourceFile in Directory.GetFiles(source, "*.png", SearchOption.TopDirectoryOnly))
            {
                var destinationFile = Path.Combine(destination, Path.GetFileName(sourceFile));
                if (File.Exists(destinationFile)) continue;
                File.Copy(sourceFile, destinationFile, false);
                copied++;
            }

            if (copied > 0) AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
            Debug.Log($"CTXD: imported {copied} authoritative generalPicMax portrait(s).");
        }
    }
}
#endif
