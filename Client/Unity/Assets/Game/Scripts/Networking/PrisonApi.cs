using System;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace CTXD.Client.Networking
{
    [Serializable] public sealed class PrisonDegreeView { public int level,extraExp,extraCd,cost,freePointCap,totalPoint; }
    [Serializable] public sealed class PrisonerView { public long id,slavePlayerId; public int generalId,forceId,level,slashTimes; public string playerName,generalName,generalPic,grabTime,escapeAt; }
    [Serializable] public sealed class CaptiveGeneralView { public long id,holderPlayerId; public int generalId,slashTimes; public string holderName,generalName,grabTime,escapeAt; }
    [Serializable] public sealed class PrisonView
    {
        public bool built,havePic,canUpdate,haveUpgradePic,haveTech,trialActive,canTrial;
        public int prisonLv,lashLv,effectiveLashLv,maxLashLv,extraExp,extraCd,upgradeGold,trialGold,grabNum,quality,currentFreePoint,maxFreePoint,totalPoint;
        public string trialEndsAt;
        public long autoLashExp;
        public PrisonDegreeView[] lashList;
        public PrisonerView[] generals;
        public CaptiveGeneralView[] captives;
    }
    [Serializable] public sealed class PrisonLashResult { public long slaveId; public int rewardExp,addedEscapeSeconds,lashLevel,currentFreePoint; }
    [Serializable] public sealed class PrisonEscapeResult { public long slaveId; public int generalId,seconds; public string escapeAt; }
    [Serializable] public sealed class PrisonTrialResult { public bool upgraded; public int lashLv,effectiveLashLv,gold; public string trialEndsAt; }

    public static class PrisonApi
    {
        public static Task<PrisonView> GetPrisonAsync(this ApiClient api)=>SendAsync<PrisonView>(api,"GET","/api/prison");
        public static Task<PrisonView> BuildPrisonAsync(this ApiClient api)=>SendAsync<PrisonView>(api,"POST","/api/prison/build");
        public static Task<PrisonView> UpgradePrisonAsync(this ApiClient api)=>SendAsync<PrisonView>(api,"POST","/api/prison/upgrade");
        public static Task<PrisonView> UpgradeLashAsync(this ApiClient api)=>SendAsync<PrisonView>(api,"POST","/api/prison/lash-level/upgrade");
        public static Task<PrisonTrialResult> TrialLashAsync(this ApiClient api)=>SendAsync<PrisonTrialResult>(api,"POST","/api/prison/lash-level/trial");
        public static Task<PrisonLashResult> LashPrisonerAsync(this ApiClient api,long slaveId)=>SendAsync<PrisonLashResult>(api,"POST",$"/api/prison/slaves/{slaveId}/lash");
        public static Task<PrisonEscapeResult> EscapePrisonAsync(this ApiClient api,int generalId)=>SendAsync<PrisonEscapeResult>(api,"POST",$"/api/prison/captive/{generalId}/escape");
        public static Task FreePrisonerAsync(this ApiClient api,long slaveId)=>SendEmptyAsync(api,"POST",$"/api/prison/slaves/{slaveId}/freedom");

        static UnityWebRequest Request(ApiClient api,string method,string path)
        {
            var request=new UnityWebRequest(api.BaseUrl.TrimEnd('/')+path,method);
            request.downloadHandler=new DownloadHandlerBuffer();
            if(method!="GET") request.uploadHandler=new UploadHandlerRaw(Encoding.UTF8.GetBytes("{}"));
            request.SetRequestHeader("Content-Type","application/json");
            if(!string.IsNullOrEmpty(api.Token))request.SetRequestHeader("Authorization","Bearer "+api.Token);
            return request;
        }
        static async Task<T> SendAsync<T>(ApiClient api,string method,string path)
        {
            using var request=Request(api,method,path);var op=request.SendWebRequest();while(!op.isDone)await Task.Yield();ThrowIfFailed(request);return JsonUtility.FromJson<T>(request.downloadHandler.text);
        }
        static async Task SendEmptyAsync(ApiClient api,string method,string path)
        {
            using var request=Request(api,method,path);var op=request.SendWebRequest();while(!op.isDone)await Task.Yield();ThrowIfFailed(request);
        }
        static void ThrowIfFailed(UnityWebRequest request)
        {
            if(request.result==UnityWebRequest.Result.Success)return;
            ApiError error=null;try{error=JsonUtility.FromJson<ApiError>(request.downloadHandler.text);}catch{}
            throw new ApiException(error!=null&&!string.IsNullOrEmpty(error.code)?error.code:"NETWORK",error!=null&&!string.IsNullOrEmpty(error.message)?error.message:request.error);
        }
    }
}
