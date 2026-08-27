using System;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace CTXD.Client.Networking
{
    [Serializable] public sealed class TicketsMarketItemView { public int id,tickets,buyLevel,seeLevel,itemId,itemType; public string reward,pic,name; public bool buyable; }
    [Serializable] public sealed class TicketsMarketView { public long tickets; public TicketsMarketItemView[] goods; }
    [Serializable] public sealed class TicketsBuyRequest { public int quantity=1; }
    [Serializable] public sealed class TicketsBuyResult { public long tickets; public TicketsMarketItemView item; public int quantity; }

    public static class TicketsMarketApi
    {
        public static Task<TicketsMarketView> GetTicketsMarketAsync(this ApiClient api)=>SendAsync<TicketsMarketView>(api,"GET","/api/tickets/market",null);
        public static Task<TicketsBuyResult> BuyTicketsMarketAsync(this ApiClient api,int marketId,int quantity=1)=>SendAsync<TicketsBuyResult>(api,"POST",$"/api/tickets/market/{marketId}/buy",JsonUtility.ToJson(new TicketsBuyRequest{quantity=quantity}));
        static async Task<T> SendAsync<T>(ApiClient api,string method,string path,string body)
        {
            using var request=new UnityWebRequest(api.BaseUrl.TrimEnd('/')+path,method);request.downloadHandler=new DownloadHandlerBuffer();
            if(body!=null){request.uploadHandler=new UploadHandlerRaw(Encoding.UTF8.GetBytes(body));request.SetRequestHeader("Content-Type","application/json");}
            if(!string.IsNullOrEmpty(api.Token))request.SetRequestHeader("Authorization","Bearer "+api.Token);
            var op=request.SendWebRequest();while(!op.isDone)await Task.Yield();
            if(request.result!=UnityWebRequest.Result.Success){ApiError error=null;try{error=JsonUtility.FromJson<ApiError>(request.downloadHandler.text);}catch{}throw new ApiException(error!=null&&!string.IsNullOrEmpty(error.code)?error.code:"NETWORK",error!=null&&!string.IsNullOrEmpty(error.message)?error.message:request.error);}
            return JsonUtility.FromJson<T>(request.downloadHandler.text);
        }
    }
}
