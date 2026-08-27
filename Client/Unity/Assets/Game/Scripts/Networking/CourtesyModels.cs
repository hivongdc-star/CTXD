using System;

namespace CTXD.Client.Networking
{
    [Serializable]
    public sealed class CourtesyEventView
    {
        public long id;
        public int type;
        public long playerId;
        public string playerName;
        public int playerPic;
        public int playerLv;
        public int eventId;
        public int state;
    }

    [Serializable]
    public sealed class CourtesyStateView
    {
        public bool open;
        public int liYiDu;
        public int maxLiYiDu;
        public bool liShangWangLai;
        public CourtesyEventView[] events;
    }
}
