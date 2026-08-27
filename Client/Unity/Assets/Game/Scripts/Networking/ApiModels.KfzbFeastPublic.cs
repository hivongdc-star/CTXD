using System;

namespace CTXD.Client.Networking
{
    [Serializable] public class KfzbFeastOrganizerInfoView
    {
        public int pos;
        public long playerId;
        public string playerName;
        public int weiNum;
        public int shuNum;
        public int wuNum;
        public int peopleNum;
        public int haveDrink;
    }

    [Serializable] public class KfzbFeastPublicParticipantView
    {
        public long playerId;
        public string playerName;
        public int forceId;
        public int titleId;
        public int tickets;
    }

    [Serializable] public class KfzbFeastCurrentRoomInfoView
    {
        public long roomId;
        public int pos;
        public string organizerName;
        public int state;
        public bool result;
        public bool drink;
        public string expiresAt;
        public string resolvedAt;
        public long cd;
        public int cardType;
        public KfzbFeastPublicParticipantView[] participants;
        public int weiNum;
        public int shuNum;
        public int wuNum;
        public int peopleNum;
        public int titleId;
        public int tickets;
        public int resultLeaveCountdownMs;
    }

    [Serializable] public class KfzbFeastPublicInfoView
    {
        public long seasonId;
        public KfzbFeastOrganizerInfoView[] rooms;
        public KfzbFeastOrganizerInfoView[] hotRooms;
        public bool inRoom;
        public bool isOrganizer;
        public bool isTop16;
        public int freeCard;
        public int goldCard;
        public int drink;
        public int goldCard1;
        public int goldCard10;
        public int goldDrink;
        public KfzbFeastCurrentRoomInfoView currentRoom;
    }
}
