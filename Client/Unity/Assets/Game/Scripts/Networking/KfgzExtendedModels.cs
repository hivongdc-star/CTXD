using System;

namespace CTXD.Client.Networking
{
    [Serializable] public class KfgzBattleResourceView { public int recruitToken; public int mubing; public int phantomCount; }
    [Serializable] public class KfgzMubingResult { public int generalId; public bool active; public int forces; public int maxForces; public int mubing; public int food; }
    [Serializable] public class KfgzFastRecruitResult { public int generalId; public int forces; public int maxForces; public int healed; public int recruitTokenSpent; public int goldSpent; public int foodSpent; public bool mubingActive; }
    [Serializable] public class KfgzPhantomRequest { public string requestKey; }
    [Serializable] public class KfgzPhantomResult { public long battleId; public long phantomUnitId; public int generalId; public bool usedFree; public int goldCost; public int phantomCount; }
    [Serializable] public class KfgzRushRequest { public int[] generalIds; public int cityId; }
    [Serializable] public class KfgzRushResult { public long sourceBattleId; public int targetCityId; public long targetBattleId; public int[] generalIds; public bool captured; }
    [Serializable] public class KfgzCallGeneralRequest { public int[] generalIds; }
    [Serializable] public class KfgzCallGeneralInfo { public int cityId; public int[] generalIds; }
    [Serializable] public class KfgzCallGeneralFailure { public int generalId; public string code; public string message; }
    [Serializable] public class KfgzCallGeneralResult { public int cityId; public int[] movedGeneralIds; public KfgzCallGeneralFailure[] failed; }
}
