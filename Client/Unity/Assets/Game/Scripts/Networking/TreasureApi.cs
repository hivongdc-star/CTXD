using System;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;
namespace CTXD.Client.Networking
{
    [Serializable]public sealed class TreasureItemView{public int id,position,type;public string name,pic,tips,effect;public bool owned;}
    [Serializable]public sealed class TreasureView{public TreasureItemView[] treasures;}
    public static class TreasureApi
    {
        public static async Task<TreasureView> GetTreasuresAsync(this ApiClient api){using var request=UnityWebRequest.Get(api.BaseUrl.TrimEnd('/')+"/api/treasures");if(!string.IsNullOrEmpty(api.Token))request.SetRequestHeader("Authorization","Bearer "+api.Token);var op=request.SendWebRequest();while(!op.isDone)await Task.Yield();if(request.result!=UnityWebRequest.Result.Success){ApiError error=null;try{error=JsonUtility.FromJson<ApiError>(request.downloadHandler.text);}catch{}throw new ApiException(error!=null&&!string.IsNullOrEmpty(error.code)?error.code:"NETWORK",error!=null&&!string.IsNullOrEmpty(error.message)?error.message:request.error);}return JsonUtility.FromJson<TreasureView>(request.downloadHandler.text);}
    }
}
