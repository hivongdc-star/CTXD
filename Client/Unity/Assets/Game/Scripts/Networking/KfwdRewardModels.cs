using System;

namespace CTXD.Client.Networking
{
    [Serializable]
    public sealed class KfwdDayRewardView
    {
        public int day;
        public int rank;
        public int tickets;
        public bool granted;
    }

    [Serializable]
    public sealed class KfwdGeneralTreasureView
    {
        public long instanceId;
        public int treasureId;
        public string name;
        public int goodsType;
        public int quality;
        public int leadership;
        public int strength;
        public bool overflow;
    }

    [Serializable]
    public sealed class KfwdRewardView
    {
        public long seasonId;
        public int globalState;
        public KfwdDayRewardView[] days;
        public bool treasureClaimed;
        public bool treasureClaimAvailable;
        public KfwdGeneralTreasureView treasure;
    }
}
