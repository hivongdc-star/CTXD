#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace CTXD.Client.Editor
{
    [InitializeOnLoad]
    public static class CTXDProjectSetup
    {
        const string ScenePath = "Assets/Game/Scenes/FirstPlayable.unity";
        const string DoneKey = "CTXD.Remake.InitialSetup.V1";

        static CTXDProjectSetup()
        {
            EditorApplication.delayCall += SetupOnce;
        }

        static void SetupOnce()
        {
            ImportLegacySprites();
            if (!File.Exists(ScenePath))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(ScenePath) ?? "Assets/Game/Scenes");
                var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                EditorSceneManager.SaveScene(scene, ScenePath);
            }
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };

            PlayerSettings.productName = "Công Thành Xưng Đế";
            PlayerSettings.companyName = "CTXD Remake";
            PlayerSettings.SetApplicationIdentifier(BuildTargetGroup.Standalone, "com.ctxd.remake.windows");
            PlayerSettings.SetApplicationIdentifier(BuildTargetGroup.Android, "com.ctxd.remake.android");
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.SetScriptingBackend(BuildTargetGroup.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.SetScriptingBackend(BuildTargetGroup.Standalone, ScriptingImplementation.IL2CPP);
            PlayerSettings.defaultScreenWidth = 1280;
            PlayerSettings.defaultScreenHeight = 768;
            PlayerSettings.resizableWindow = true;
#if UNITY_2022_2_OR_NEWER
            PlayerSettings.insecureHttpOption = InsecureHttpOption.AlwaysAllowed; // DEV LAN only; production uses HTTPS.
#endif
            EditorPrefs.SetBool(DoneKey, true);
            AssetDatabase.SaveAssets();
        }

        static void ImportLegacySprites()
        {
            var guids = AssetDatabase.FindAssets("t:Texture2D", new[] { "Assets/Game/Resources/LegacyVisual" });
            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (AssetImporter.GetAtPath(path) is not TextureImporter ti) continue;
                var dirty = false;
                if (ti.textureType != TextureImporterType.Sprite) { ti.textureType = TextureImporterType.Sprite; dirty = true; }
                if (ti.mipmapEnabled) { ti.mipmapEnabled = false; dirty = true; }
                if (ti.alphaIsTransparency == false && path.EndsWith(".png")) { ti.alphaIsTransparency = true; dirty = true; }
                if (dirty) ti.SaveAndReimport();
            }
        }

        [MenuItem("CTXD/Run Initial Setup")]
        public static void RunFromMenu() => SetupOnce();
    }
}
#endif
