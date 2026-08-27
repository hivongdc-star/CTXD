using System;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace CTXD.Client.Networking
{
    [Serializable] public sealed class BlacksmithSmithView
    {
        public int smithId;
        public bool unlocked;
        public int level,dailyUsed,dailyLimit,blueprintItemId,blueprintItemType,blueprintCount,stoneItemId,stoneItemType,stoneCount,ironPerDissolve;
    }

    [Serializable] public sealed class BlacksmithView
    {
        public bool functionOpen;
        public int playerLevel;
        public long iron;
        public BlacksmithSmithView smith1;
    }

    [Serializable] public sealed class BlacksmithUnlockResult
    {
        public int smithId,level,blueprintItemId,blueprintItemType,blueprintConsumed;
    }

    [Serializable] public sealed class BlacksmithDissolveResult
    {
        public int smithId,ironAdded,dailyUsed,dailyLimit;
        public long iron;
    }

    public static class BlacksmithApi
    {
        public static Task<BlacksmithView> GetBlacksmithAsync(this ApiClient api) =>
            SendAsync<BlacksmithView>(api,"GET","/api/blacksmith",null);

        public static Task<BlacksmithUnlockResult> UnlockBlacksmithSmith1Async(this ApiClient api) =>
            SendAsync<BlacksmithUnlockResult>(api,"POST","/api/blacksmith/smiths/1/unlock",null);

        public static Task<BlacksmithDissolveResult> DissolveBlacksmithSmith1Async(this ApiClient api) =>
            SendAsync<BlacksmithDissolveResult>(api,"POST","/api/blacksmith/smiths/1/dissolve",null);

        static async Task<T> SendAsync<T>(ApiClient api,string method,string path,string body)
        {
            using var request=new UnityWebRequest(api.BaseUrl.TrimEnd('/')+path,method);
            request.downloadHandler=new DownloadHandlerBuffer();
            if(body!=null)
            {
                request.uploadHandler=new UploadHandlerRaw(Encoding.UTF8.GetBytes(body));
                request.SetRequestHeader("Content-Type","application/json");
            }
            if(!string.IsNullOrEmpty(api.Token))request.SetRequestHeader("Authorization","Bearer "+api.Token);
            var op=request.SendWebRequest();while(!op.isDone)await Task.Yield();
            if(request.result!=UnityWebRequest.Result.Success)
            {
                ApiError error=null;try{error=JsonUtility.FromJson<ApiError>(request.downloadHandler.text);}catch{}
                throw new ApiException(error!=null&&!string.IsNullOrEmpty(error.code)?error.code:"NETWORK",error!=null&&!string.IsNullOrEmpty(error.message)?error.message:request.error);
            }
            return JsonUtility.FromJson<T>(request.downloadHandler.text);
        }
    }
}
