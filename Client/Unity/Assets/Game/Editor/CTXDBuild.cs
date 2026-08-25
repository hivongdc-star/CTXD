#if UNITY_EDITOR
using System;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;

namespace CTXD.Client.Editor
{
    public static class CTXDBuild
    {
        static string[] Scenes => EditorBuildSettings.scenes.Where(x => x.enabled).Select(x => x.path).ToArray();

        public static void BuildWindows()
        {
            var opt = new BuildPlayerOptions
            {
                scenes = Scenes,
                locationPathName = "Build/Windows/CTXD.exe",
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None
            };
            Finish(BuildPipeline.BuildPlayer(opt));
        }

        public static void BuildAndroid()
        {
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            var opt = new BuildPlayerOptions
            {
                scenes = Scenes,
                locationPathName = "Build/Android/CTXD.apk",
                target = BuildTarget.Android,
                options = BuildOptions.None
            };
            Finish(BuildPipeline.BuildPlayer(opt));
        }

        static void Finish(BuildReport report)
        {
            if (report.summary.result != BuildResult.Succeeded)
                throw new Exception($"CTXD build failed: {report.summary.result}, errors={report.summary.totalErrors}");
            Console.WriteLine($"CTXD BUILD OK: {report.summary.outputPath} ({report.summary.totalSize} bytes)");
        }
    }
}
#endif
