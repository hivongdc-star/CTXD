using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace CTXD.Client.EditorTools
{
    public static class BattleLegacyAssetImporter
    {
        const string TargetRoot = "Assets/Game/Resources/LegacyVisual/Battle";
        const string LegacyClient = "LegacyReference/Client";
        const string LegacyRevision = "59e62c8b09ff5612419dbbc5cc129294fec780ef";

        static readonly string[] CommonWarSymbols =
        {
            "ArmyHp", "AttBattleHp", "DefBattleHp", "BattleBlood", "BloodBar", "hpMc",
            "StrategyPlay", "TeacticsPlay", "GeneralRoar", "SoldierDeath", "SoldierDodge", "SoldierHurt",
            "ShootAtSoldiers", "TouXi01", "TouXi02", "TouXi04", "WuShen01", "WuShen02", "WuShen03",
            "WuShen04", "WuShen05", "WuShen06"
        };

        [MenuItem("CTXD/Legacy/Import Battle Visuals")]
        public static void ImportBattleVisuals()
        {
            var selected = EditorUtility.OpenFolderPanel(
                "Select CTXD-Legacy-Reference @ " + LegacyRevision,
                Directory.GetParent(Application.dataPath)?.FullName ?? string.Empty,
                string.Empty);
            if (string.IsNullOrWhiteSpace(selected)) return;

            var sourceRoot = ResolveReferenceRoot(selected);
            if (sourceRoot == null)
            {
                EditorUtility.DisplayDialog(
                    "CTXD Battle Legacy Import",
                    "Selected folder is not CTXD-Legacy-Reference. Expected RemakeInput/GeneratedV5/Workpacks/14_Battle/WORKPACK.md and LegacyReference/Client/AssetsRaw/zh_CN/xml/module/War.xml.",
                    "OK");
                return;
            }

            if (!TryReadGitHead(sourceRoot, out var sourceHead) || !string.Equals(sourceHead, LegacyRevision, StringComparison.OrdinalIgnoreCase))
            {
                EditorUtility.DisplayDialog(
                    "CTXD Battle Legacy Import",
                    "Import refused. The selected reference checkout must be exactly " + LegacyRevision + ".\nCurrent HEAD: " + (sourceHead ?? "UNKNOWN"),
                    "OK");
                return;
            }

            var copied = new List<string>();
            CopyRawDirectory(sourceRoot, "AssetsRaw/zh_CN/img/warBG", "Background", copied);
            CopyRawDirectory(sourceRoot, "AssetsRaw/zh_CN/img/warBuff", "Hud/warBuff", copied);
            CopyRawDirectory(sourceRoot, "AssetsRaw/zh_CN/img/warLock", "Hud/warLock", copied);
            CopyRawDirectory(sourceRoot, "AssetsRaw/zh_CN/img/warTitle", "Hud/warTitle", copied);
            CopyRawDirectory(sourceRoot, "AssetsRaw/zh_CN/img/warvsicon", "Hud/warvsicon", copied);
            CopyRawDirectory(sourceRoot, "AssetsRaw/zh_CN/img/tacticalGeneralPicMax", "Hud/tacticalGeneralPicMax", copied);

            for (var troopId = 1; troopId <= 29; troopId++)
            {
                ImportSoldier(sourceRoot, "att", troopId, copied);
                ImportSoldier(sourceRoot, "def", troopId, copied);
            }

            ImportWarSymbols(sourceRoot, copied);
            WriteEvidence(sourceRoot, copied);
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            Debug.Log($"CTXD Battle legacy import complete: {copied.Count} authoritative files copied from {LegacyRevision}.");
        }

        static string ResolveReferenceRoot(string selected)
        {
            selected = Path.GetFullPath(selected);
            for (var i = 0; i < 4; i++)
            {
                var workpack = Path.Combine(selected, "RemakeInput", "GeneratedV5", "Workpacks", "14_Battle", "WORKPACK.md");
                var warXml = Path.Combine(selected, LegacyClient, "AssetsRaw", "zh_CN", "xml", "module", "War.xml");
                if (File.Exists(workpack) && File.Exists(warXml)) return selected;
                var parent = Directory.GetParent(selected);
                if (parent == null) break;
                selected = parent.FullName;
            }
            return null;
        }

        static bool TryReadGitHead(string root, out string head)
        {
            head = null;
            try
            {
                var start = new ProcessStartInfo
                {
                    FileName = "git",
                    Arguments = "-C \"" + root.Replace("\"", "\\\"") + "\" rev-parse HEAD",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };
                using var process = Process.Start(start);
                if (process == null) return false;
                head = process.StandardOutput.ReadToEnd().Trim();
                process.WaitForExit();
                return process.ExitCode == 0 && head.Length == 40;
            }
            catch
            {
                return false;
            }
        }

        static void CopyRawDirectory(string root, string sourceRelativeToClient, string targetRelative, ICollection<string> copied)
        {
            var source = Path.Combine(root, LegacyClient, sourceRelativeToClient.Replace('/', Path.DirectorySeparatorChar));
            if (!Directory.Exists(source)) return;
            CopyDirectoryFiles(source, Path.Combine(TargetRoot, targetRelative), copied, file =>
                file.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ||
                file.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) ||
                file.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase));
        }

        static void ImportSoldier(string root, string side, int troopId, ICollection<string> copied)
        {
            var soldierName = side + troopId;
            var exportRoot = Path.Combine(root, LegacyClient, "SWF_Export", "GCLDServer", "wwwroot", "assets", "zh_CN", "swf", "soldiers", soldierName);
            var symbols = Path.Combine(exportRoot, "symbolClass", "symbols.csv");
            if (!File.Exists(symbols)) return;

            var symbolMap = ParseSymbols(symbols);
            for (var action = 1; action <= 5; action++)
            {
                var className = $"war.{soldierName}_{action}";
                if (!symbolMap.TryGetValue(className, out var spriteId)) continue;
                var source = Path.Combine(exportRoot, "sprites", $"DefineSprite_{spriteId}_{className}");
                if (!Directory.Exists(source)) continue;
                CopyDirectoryFiles(source, Path.Combine(TargetRoot, "Soldiers", soldierName, "action" + action), copied,
                    file => file.EndsWith(".png", StringComparison.OrdinalIgnoreCase) || file.EndsWith(".xml", StringComparison.OrdinalIgnoreCase));
            }
        }

        static void ImportWarSymbols(string root, ICollection<string> copied)
        {
            var exportRoot = Path.Combine(root, LegacyClient, "SWF_Export", "GCLDServer", "wwwroot", "assets", "zh_CN", "swf", "module", "War");
            var symbols = Path.Combine(exportRoot, "symbolClass", "symbols.csv");
            if (!File.Exists(symbols)) return;
            var symbolMap = ParseSymbols(symbols);

            foreach (var className in CommonWarSymbols)
            {
                if (!symbolMap.TryGetValue(className, out var spriteId)) continue;
                var source = Path.Combine(exportRoot, "sprites", $"DefineSprite_{spriteId}_{className}");
                if (!Directory.Exists(source)) continue;
                CopyDirectoryFiles(source, Path.Combine(TargetRoot, "Common", className), copied,
                    file => file.EndsWith(".png", StringComparison.OrdinalIgnoreCase) || file.EndsWith(".xml", StringComparison.OrdinalIgnoreCase));
            }
        }

        static Dictionary<string, int> ParseSymbols(string path)
        {
            var result = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var raw in File.ReadLines(path))
            {
                var semicolon = raw.IndexOf(';');
                if (semicolon <= 0 || !int.TryParse(raw.Substring(0, semicolon), out var id)) continue;
                var name = raw.Substring(semicolon + 1).Trim().Trim('"');
                if (name.Length != 0) result[name] = id;
            }
            return result;
        }

        static void CopyDirectoryFiles(string source, string targetAssetPath, ICollection<string> copied, Func<string, bool> filter)
        {
            Directory.CreateDirectory(targetAssetPath);
            foreach (var sourceFile in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories).Where(filter))
            {
                var relative = sourceFile.Substring(source.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                var target = Path.Combine(targetAssetPath, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(target) ?? targetAssetPath);
                File.Copy(sourceFile, target, true);
                copied.Add(target.Replace('\\', '/'));
            }
        }

        static void WriteEvidence(string root, IReadOnlyCollection<string> copied)
        {
            Directory.CreateDirectory(TargetRoot);
            var evidence = Path.Combine(TargetRoot, "LEGACY_EVIDENCE.txt");
            File.WriteAllText(evidence,
                "CTXD Battle legacy visual import\n" +
                "Authoritative repo: hivongdc-star/CTXD-Legacy-Reference\n" +
                "Authoritative revision: " + LegacyRevision + "\n" +
                "Selected source root: " + root + "\n" +
                "Evidence: RemakeInput/GeneratedV5/Workpacks/14_Battle/WORKPACK.md\n" +
                "Evidence: RemakeInput/LegacyIndexV4/Modules/14_Battle\n" +
                "Evidence: LegacyReference/Client/AssetsRaw/zh_CN/xml/module/War.xml\n" +
                "Evidence: LegacyReference/Client/SWF_Export/GCLDServer/wwwroot/Game/module/war/view/War/scripts/game/module/war/view/Army.as\n" +
                "Evidence: LegacyReference/Client/SWF_Export/GCLDServer/wwwroot/Game/module/war/view/War/scripts/game/module/war/view/fightView/FightArea.as\n" +
                "Evidence: LegacyReference/Client/SWF_Export/GCLDServer/wwwroot/Game/module/war/view/War/scripts/game/module/war/view/fightView/FightVS.as\n" +
                "Imported files: " + copied.Count + "\n");
        }
    }
}
