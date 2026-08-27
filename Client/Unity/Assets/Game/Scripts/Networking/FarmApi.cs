using System;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace CTXD.Client.Networking
{
    [Serializable] public sealed class FarmOptionView
    {
        public int type;
        public int generalState;
        public int minutes;
        public int food;
        public int reward;
    }

    [Serializable] public sealed class FarmGeneralView
    {
        public int generalId;
        public int state;
        public int locationId;
        public int type;
        public string endsAt;
        public int reward;
        public long buffCd;
    }

    [Serializable] public sealed class FarmView
    {
        public int forceId;
        public int farmCityId;
        public int nationLevel;
        public int farmLevel;
        public long investSum;
        public int nextUpCopper;
        public bool canInvest;
        public long investCd;
        public int recoverGold;
        public int itemNumber;
        public int coefficient;
        public int cityExpBonus;
        public FarmOptionView[] options;
        public FarmGeneralView[] generals;
    }

    [Serializable] public sealed class FarmInvestResult
    {
        public int farmLevel;
        public long investSum;
        public long cd;
        public int exp;
    }

    [Serializable] public sealed class FarmStartRequest { public int generalId; public int type; }
    [Serializable] public sealed class FarmGoldRequest { public string requestKey; }
    [Serializable] public sealed class FarmGoldResult { public int gold; }

    [Serializable] public sealed class FarmStartResult
    {
        public int generalId;
        public int type;
        public int food;
        public int reward;
        public string endsAt;
    }

    [Serializable] public sealed class FarmRewardResult
    {
        public int generalId;
        public int type;
        public int reward;
        public int gold;
        public long buffCd;
    }

    [Serializable] public sealed class FarmStopAllResult { public FarmRewardResult[] items; }

    public static class FarmApi
    {
        public static Task<FarmView> GetFarmAsync(this ApiClient api) =>
            SendAsync<FarmView>(api,"GET","/api/world/farm",null);

        public static Task<FarmInvestResult> InvestFarmAsync(this ApiClient api) =>
            SendAsync<FarmInvestResult>(api,"POST","/api/world/farm/invest",null);

        public static Task<FarmGoldResult> RecoverFarmInvestAsync(this ApiClient api,string requestKey) =>
            SendAsync<FarmGoldResult>(api,"POST","/api/world/farm/invest/recover",new FarmGoldRequest{requestKey=requestKey});

        public static Task<FarmStartResult> StartFarmAsync(this ApiClient api,int generalId,int type) =>
            SendAsync<FarmStartResult>(api,"POST","/api/world/farm/start",new FarmStartRequest{generalId=generalId,type=type});

        public static Task<FarmGoldResult> GetFarmClaimCostAsync(this ApiClient api,int generalId) =>
            SendAsync<FarmGoldResult>(api,"GET",$"/api/world/farm/{generalId}/claim-cost",null);

        public static Task<FarmRewardResult> StopFarmAsync(this ApiClient api,int generalId) =>
            SendAsync<FarmRewardResult>(api,"POST",$"/api/world/farm/{generalId}/stop",null);

        public static Task<FarmRewardResult> ClaimFarmAsync(this ApiClient api,int generalId,string requestKey) =>
            SendAsync<FarmRewardResult>(api,"POST",$"/api/world/farm/{generalId}/claim",new FarmGoldRequest{requestKey=requestKey});

        public static Task<FarmStopAllResult> StopAllFarmAsync(this ApiClient api) =>
            SendAsync<FarmStopAllResult>(api,"POST","/api/world/farm/stop-all",null);

        static async Task<T> SendAsync<T>(ApiClient api,string method,string path,object body)
        {
            using var request=new UnityWebRequest(api.BaseUrl.TrimEnd('/')+path,method);
            request.downloadHandler=new DownloadHandlerBuffer();
            if(body!=null)
            {
                request.uploadHandler=new UploadHandlerRaw(Encoding.UTF8.GetBytes(JsonUtility.ToJson(body)));
                request.SetRequestHeader("Content-Type","application/json");
            }
            if(!string.IsNullOrEmpty(api.Token))request.SetRequestHeader("Authorization","Bearer "+api.Token);
            var op=request.SendWebRequest();
            while(!op.isDone)await Task.Yield();
            if(request.result!=UnityWebRequest.Result.Success)
            {
                ApiError error=null;
                try{if(!string.IsNullOrWhiteSpace(request.downloadHandler.text))error=JsonUtility.FromJson<ApiError>(request.downloadHandler.text);}catch{}
                throw new ApiException(error!=null&&!string.IsNullOrEmpty(error.code)?error.code:"NETWORK",
                    error!=null&&!string.IsNullOrEmpty(error.message)?error.message:request.error);
            }
            return JsonUtility.FromJson<T>(request.downloadHandler.text);
        }
    }
}
