using System;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace CTXD.Client.Networking
{
    [Serializable] public sealed class AutoBattleStartRequest { public int cityId; }
    [Serializable] public sealed class AutoBattleView
    {
        public bool techUnlocked;
        public int state;
        public int targetCityId;
        public int autoType;
        public long cd;
        public long exp;
        public long lost;
        public int result;
    }

    public static class AutoBattleApi
    {
        public static Task<AutoBattleView> GetAutoBattleAsync(this ApiClient api) =>
            SendAsync<AutoBattleView>(api,"GET","/api/world/auto-battle",null);

        public static Task<AutoBattleView> StartAutoBattleAsync(this ApiClient api,int cityId) =>
            SendAsync<AutoBattleView>(api,"POST","/api/world/auto-battle/start",new AutoBattleStartRequest{cityId=cityId});

        public static Task<AutoBattleView> StopAutoBattleAsync(this ApiClient api) =>
            SendAsync<AutoBattleView>(api,"POST","/api/world/auto-battle/stop",null);

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
