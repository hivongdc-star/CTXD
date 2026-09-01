using System;

namespace CTXD.Client.Features.Expedition
{
    // Presentation-only state. These types intentionally are not transport DTOs and do not define any server API.
    [Serializable]
    public sealed class ExpeditionLegacyViewState
    {
        public int mapId = 1;
        public int currentPage = 1;
        public int pageCount = 1;
        public string previousPageText = string.Empty;
        public string nextPageText = string.Empty;
        public string pageText = string.Empty;
        public bool showBackButton;
        public bool showPreviousButton = true;
        public bool showNextButton = true;
        public bool showDramaButton = true;
        public bool showHelpButton = true;
        public bool showExtraButton;
        public string extraLabel = string.Empty;
        public string extraText = string.Empty;
        public string extraPortraitResourcePath = string.Empty;
        public ExpeditionEnemyVisualState[] enemies = Array.Empty<ExpeditionEnemyVisualState>();
    }

    [Serializable]
    public sealed class ExpeditionEnemyVisualState
    {
        // Legacy enemyN suffix from expedition*.swf.
        public int index;
        public bool visible = true;
        public bool attackable = true;
        public bool attacked;
        public bool elite;
        public bool showRequiredLevel;
        public int requiredLevel;
        public string npcName = string.Empty;
    }

    [Serializable]
    public sealed class ExpeditionGuildLegacyViewState
    {
        // The caller supplies which legacy guide cards are visible; the surface does not infer unlock/gameplay rules.
        public int firstGuideIndex = 1;
        public int selectedGuideIndex = 1;
        public int targetPage = 1;
        public int targetPageCount = 1;
        public int selectedTargetSlot = -1;
        public string selectedTargetName = string.Empty;
        public string selectedTargetDescription = string.Empty;
        public bool waiting;
        public ExpeditionGuildGuideVisualState[] guides = Array.Empty<ExpeditionGuildGuideVisualState>();
        public ExpeditionGuildTargetVisualState[] targets = Array.Empty<ExpeditionGuildTargetVisualState>();
    }

    [Serializable]
    public sealed class ExpeditionGuildGuideVisualState
    {
        public int index;
        public bool open = true;
        public int level;
    }

    [Serializable]
    public sealed class ExpeditionGuildTargetVisualState
    {
        public int slot;
        public string name = string.Empty;
        public string description = string.Empty;
        public string portraitResourcePath = string.Empty;
        public bool completed;
        public bool mainTarget;
        public int level;
    }
}
