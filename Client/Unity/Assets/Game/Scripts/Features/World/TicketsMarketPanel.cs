using System;
using System.Linq;
using System.Threading.Tasks;
using CTXD.Client.Features.FirstPlayable;
using CTXD.Client.Networking;
using UnityEngine;
using UnityEngine.UI;

namespace CTXD.Client.Features.World
{
    public sealed class TicketsMarketPanel : MonoBehaviour
    {
        ApiClient _api;Action<string> _status;RectTransform _window;TicketsMarketView _view;bool _busy;
        public static TicketsMarketPanel Open(RectTransform host,ApiClient api,Action<string> status)
        {
            var go=new GameObject("TicketsMarketPanel");go.transform.SetParent(host,false);var p=go.AddComponent<TicketsMarketPanel>();p._api=api;p._status=status;p.Build();_=p.Load();return p;
        }
        void Build(){var b=LegacyUiFactory.Panel(transform,"TicketsMarketBlocker",Vector2.zero,Vector2.one,new Color(0,0,0,.78f));_window=LegacyUiFactory.PixelPanel(b,"TicketsMarketWindow",330,115,620,500,new Color(.045f,.036f,.025f,.99f));}
        async Task Load(){if(_busy)return;_busy=true;try{_view=await _api.GetTicketsMarketAsync();Draw();}catch(Exception e){_status(e.Message);}finally{_busy=false;}}
        void Draw()
        {
            LegacyUiFactory.DestroyChildren(_window);LegacyUiFactory.PixelLabel(_window,"ĐIỂM KHOÁN THƯƠNG THÀNH",20,TextAnchor.MiddleCenter,new Color(1,.82f,.35f),145,10,330,32);LegacyUiFactory.PixelButton(_window,"Đóng",535,13,70,27,()=>Destroy(gameObject));
            if(_view==null)return;LegacyUiFactory.PixelLabel(_window,$"Điểm Khoán: {_view.tickets:N0}",15,TextAnchor.MiddleLeft,Color.white,18,50,250,25);
            var goods=(_view.goods??Array.Empty<TicketsMarketItemView>()).OrderBy(x=>x.id).Take(8).ToArray();
            for(var i=0;i<goods.Length;i++)
            {
                var g=goods[i];var y=82+i*48;var need=g.buyLevel>0?$" · Lv.{g.buyLevel}":"";
                LegacyUiFactory.PixelLabel(_window,$"{g.name}  —  {g.tickets:N0} điểm{need}",13,TextAnchor.MiddleLeft,g.buyable?Color.white:new Color(.6f,.6f,.6f),20,y,430,30);
                if(g.buyable)LegacyUiFactory.PixelButton(_window,"Mua",470,y,70,27,async()=>await Buy(g));
            }
        }
        async Task Buy(TicketsMarketItemView item)
        {
            if(_busy)return;_busy=true;try{var r=await _api.BuyTicketsMarketAsync(item.id);_status($"Đã mua {item.name}.");_view=await _api.GetTicketsMarketAsync();Draw();PrisonPanel.RefreshOpenFromPush();}catch(Exception e){_status(e.Message);}finally{_busy=false;}
        }
    }
}
