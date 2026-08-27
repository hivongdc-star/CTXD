using System;

namespace CTXD.Client.Networking
{
    [Serializable] public class KfzbRewardView
    {
        public long seasonId;
        public int[] rewardInfo;
        public int doneNum;
        public int totalTickets;
        public int pendingTickets;
        public string title;
        public int eliminatedLayer;
        public bool eliminated;
        public bool eventEnded;
        public GeneralTreasureView treasure;
    }

    [Serializable] public class KfzbRewardClaimResult
    {
        public long seasonId;
        public int ticketsGranted;
        public int doneNum;
        public int rewardCount;
        public int ticketBalance;
    }

    [Serializable] public class GeneralTreasureListResponse
    {
        public GeneralTreasureView[] items;
    }

    [Serializable] public class GeneralTreasureView
    {
        public long id;
        public int treasureId;
        public string name;
        public int quality;
        public int lea;
        public int str;
        public int ownerGeneralId;
        public bool equipped;
        public string source;
        public string acquiredAt;
    }

    [Serializable] public class GeneralTreasureEquipRequest
    {
        public int generalId;
    }
}
