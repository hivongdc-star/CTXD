using System;
using System.Collections.Generic;
using UnityEngine;

namespace CTXD.Client.Features.Battle
{
    /// <summary>
    /// Immutable visual mapping loaded from legacy canonical content packaged with W4.
    /// It never derives or mutates battle gameplay state.
    /// </summary>
    public sealed class BattleLegacyCatalog
    {
        const string CatalogPath = "LegacyVisual/Battle/catalog";

        [Serializable] sealed class CatalogData
        {
            public GeneralEntry[] generals;
            public TroopEntry[] troops;
            public TacticEntry[] tactics;
        }

        [Serializable] sealed class GeneralEntry { public int id; public string pic; }
        [Serializable] sealed class TroopEntry { public int id; public int type; }
        [Serializable] sealed class TacticEntry { public int id; public int displayId; public string pic; public int playerTime; }

        readonly Dictionary<int, string> _generalPic = new Dictionary<int, string>();
        readonly Dictionary<int, int> _troopType = new Dictionary<int, int>();
        readonly Dictionary<int, TacticEntry> _tactics = new Dictionary<int, TacticEntry>();

        public static BattleLegacyCatalog Load()
        {
            var catalog = new BattleLegacyCatalog();
            var asset = Resources.Load<TextAsset>(CatalogPath);
            if (asset == null) return catalog;
            var data = JsonUtility.FromJson<CatalogData>(asset.text);
            if (data == null) return catalog;

            if (data.generals != null)
                foreach (var row in data.generals)
                    if (row != null && !catalog._generalPic.ContainsKey(row.id))
                        catalog._generalPic.Add(row.id, row.pic);

            if (data.troops != null)
                foreach (var row in data.troops)
                    if (row != null && !catalog._troopType.ContainsKey(row.id))
                        catalog._troopType.Add(row.id, row.type);

            if (data.tactics != null)
                foreach (var row in data.tactics)
                    if (row != null && !catalog._tactics.ContainsKey(row.id))
                        catalog._tactics.Add(row.id, row);

            return catalog;
        }

        public string GeneralPic(int generalId)
        {
            return _generalPic.TryGetValue(generalId, out var pic) && !string.IsNullOrEmpty(pic) ? pic : null;
        }

        public int TroopType(int troopId)
        {
            return _troopType.TryGetValue(troopId, out var type) ? type : 0;
        }

        public string TacticSkillKey(int tacticId)
        {
            if (!_tactics.TryGetValue(tacticId, out var tactic) || tactic.displayId <= 0) return null;
            // Legacy display 8 is packaged as skill/80.swf; all other active display ids use their display id.
            return tactic.displayId == 8 ? "80" : tactic.displayId.ToString();
        }

        public int TacticDurationMs(int tacticId)
        {
            return _tactics.TryGetValue(tacticId, out var tactic) ? tactic.playerTime : 0;
        }

        public int TacticDisplayId(int tacticId)
        {
            return _tactics.TryGetValue(tacticId, out var tactic) ? tactic.displayId : 0;
        }
    }
}
