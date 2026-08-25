using System;

namespace CTXD.Client.Networking
{
    [Serializable] public class LevelRankEntry { public long playerId; public string playerName; public int playerLv; }
    [Serializable] public class LevelRankView { public LevelRankEntry[] rankList; }
}
