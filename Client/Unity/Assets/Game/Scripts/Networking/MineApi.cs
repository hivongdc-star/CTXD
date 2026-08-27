using System;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace CTXD.Client.Networking
{
    [Serializable] public sealed class MineSlotView { public int id,type,position,output,stone,currentOutput,currentStone; public string name,pic,operation,ownerName; public bool canDo,myself,@double; public long ownerPlayerId,cd; public int ownerForceId; }
    [Serializable] public sealed class MineView { public int style,currentPage,totalPage; public bool haveSmith; public MineSlotView[] mines; }
    [Serializable] public sealed class MineOccupyRequest { public int mineId,generalId; }
    [Serializable] public sealed class MineOccupyResult { public int mineId; public bool captured; public long battleId; }
    [Serializable] public sealed class MineRewardResult { public int mineId,type,output,stone; }
    public static class MineApi
    {
        public static Task<MineView> GetMinesAsync(this ApiClient api,int page,int style)=>SendAsync<MineView>(api,"GET",$"/api/world/mines?page={page}&style={style}",null);
        public static Task<MineOccupyResult> OccupyMineAsync(this ApiClient api,int mineId,int generalId)=>SendAsync<MineOccupyResult>(api,"POST","/api/world/mines/occupy",new MineOccupyRequest{mineId=mineId,generalId=generalId});
        public static Task<object> RushMineAsync(this ApiClient api,int style)=>SendAsync<object>(api,"POST",$"/api/world/mines/rush/{style}",null);
        public static Task<MineRewardResult> AbandonMineAsync(this ApiClient api,int style)=>SendAsync<MineRewardResult>(api,"POST",$"/api/world/mines/abandon/{style}",null);
        public static Task<MineRewardResult> HarvestForceMineAsync(this ApiClient api,int style)=>SendAsync<MineRewardResult>(api,"POST",$"/api/world/mines/harvest/{style}",null);
        static async Task<T> SendAsync<T>(ApiClient api,string method,string path,object body){using var request=new UnityWebRequest(api.BaseUrl.TrimEnd('/')+path,method);request.downloadHandler=new DownloadHandlerBuffer();if(body!=null){request.uploadHandler=new UploadHandlerRaw(Encoding.UTF8.GetBytes(JsonUtility.ToJson(body)));request.SetRequestHeader("Content-Type","application/json");}if(!string.IsNullOrEmpty(api.Token))request.SetRequestHeader("Authorization","Bearer "+api.Token);var op=request.SendWebRequest();while(!op.isDone)await Task.Yield();if(request.result!=UnityWebRequest.Result.Success){ApiError error=null;try{error=JsonUtility.FromJson<ApiError>(request.downloadHandler.text);}catch{}throw new ApiException(error!=null&&!string.IsNullOrEmpty(error.code)?error.code:"NETWORK",error!=null&&!string.IsNullOrEmpty(error.message)?error.message:request.error);}if(typeof(T)==typeof(object))return default;return JsonUtility.FromJson<T>(request.downloadHandler.text);}
    }
}
