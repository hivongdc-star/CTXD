using System;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace CTXD.Client.Networking
{
    [Serializable] public sealed class SlaveActivityRewardView { public int position,value,state; public string kind; }
    [Serializable] public sealed class SlaveActivityView { public long activityId; public string endsAt; public int bonusExp,remainingCaptures; public SlaveActivityRewardView[] rewards; }
    [Serializable] public sealed class SlaveActivityActionResult { public int position,value,bonusExp; public string kind; }

    public static class SlaveActivityApi
    {
        public static Task<SlaveActivityView> GetSlaveActivityAsync(this ApiClient api)=>SendAsync<SlaveActivityView>(api,"GET","/api/activities/slave");
        public static Task<SlaveActivityActionResult> CaptureSlaveActivityAsync(this ApiClient api,int position)=>SendAsync<SlaveActivityActionResult>(api,"POST",$"/api/activities/slave/capture/{position}");
        public static Task<SlaveActivityActionResult> LashSlaveActivityAsync(this ApiClient api,int position)=>SendAsync<SlaveActivityActionResult>(api,"POST",$"/api/activities/slave/lash/{position}");

        static async Task<T> SendAsync<T>(ApiClient api,string method,string path)
        {
            using var request=new UnityWebRequest(api.BaseUrl.TrimEnd('/')+path,method);
            request.downloadHandler=new DownloadHandlerBuffer();
            if(method!="GET")request.uploadHandler=new UploadHandlerRaw(Encoding.UTF8.GetBytes("{}"));
            request.SetRequestHeader("Content-Type","application/json");if(!string.IsNullOrEmpty(api.Token))request.SetRequestHeader("Authorization","Bearer "+api.Token);
            var op=request.SendWebRequest();while(!op.isDone)await Task.Yield();
            if(request.result==UnityWebRequest.Result.Success)return JsonUtility.FromJson<T>(request.downloadHandler.text);
            ApiError error=null;try{error=JsonUtility.FromJson<ApiError>(request.downloadHandler.text);}catch{}
            throw new ApiException(error!=null&&!string.IsNullOrEmpty(error.code)?error.code:"NETWORK",error!=null&&!string.IsNullOrEmpty(error.message)?error.message:request.error);
        }
    }
}
