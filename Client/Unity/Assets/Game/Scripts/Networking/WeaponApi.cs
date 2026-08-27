using System;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace CTXD.Client.Networking
{
    [Serializable] public sealed class WeaponItemView { public int id,type,quality,level,times,totalTimes,attribute,nextAttribute,upgradeCost,itemId,itemNum,gemId,gemNum,incense; public long itemOwned; public string name,pic,intro,cost; public bool open; }
    [Serializable] public sealed class WeaponView { public WeaponItemView[] weapons; public ResourceView resources; }
    [Serializable] public sealed class WeaponUpgradeResult { public WeaponItemView weapon; public int crit; public bool levelUp; public ResourceView resources; }
    public static class WeaponApi
    {
        public static Task<WeaponView> GetWeaponsAsync(this ApiClient api)=>SendAsync<WeaponView>(api,"GET","/api/weapons");
        public static Task<WeaponUpgradeResult> UpgradeWeaponAsync(this ApiClient api,int weaponId)=>SendAsync<WeaponUpgradeResult>(api,"POST",$"/api/weapons/{weaponId}/upgrade");
        static async Task<T> SendAsync<T>(ApiClient api,string method,string path){using var request=new UnityWebRequest(api.BaseUrl.TrimEnd('/')+path,method);request.downloadHandler=new DownloadHandlerBuffer();request.uploadHandler=new UploadHandlerRaw(Encoding.UTF8.GetBytes("{}"));request.SetRequestHeader("Content-Type","application/json");if(!string.IsNullOrEmpty(api.Token))request.SetRequestHeader("Authorization","Bearer "+api.Token);var op=request.SendWebRequest();while(!op.isDone)await Task.Yield();if(request.result!=UnityWebRequest.Result.Success){ApiError error=null;try{error=JsonUtility.FromJson<ApiError>(request.downloadHandler.text);}catch{}throw new ApiException(error!=null&&!string.IsNullOrEmpty(error.code)?error.code:"NETWORK",error!=null&&!string.IsNullOrEmpty(error.message)?error.message:request.error);}return JsonUtility.FromJson<T>(request.downloadHandler.text);}
    }
}
