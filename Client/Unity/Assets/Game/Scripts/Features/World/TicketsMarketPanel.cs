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
        const int PageSize=8;
        static TicketsMarketPanel _open;
        ApiClient _api;Action<string> _status;RectTransform _window;TicketsMarketView _view;bool _busy;int _page;
        public static TicketsMarketPanel Open(RectTransform host,ApiClient api,Action<string> status)
        {
            if(_open!=null)Destroy(_open.gameObject);
            var go=new GameObject("TicketsMarketPanel");go.transform.SetParent(host,false);var p=go.AddComponent<TicketsMarketPanel>();p._api=api;p._status=status;_open=p;p.Build();_=p.Load();return p;
        }
        public static void RefreshOpenFromPush(){if(_open!=null&&!_open._busy)_=_open.Load();}
        void OnDestroy(){if(_open==this)_open=null;}
        void Build(){var b=LegacyUiFactory.Panel(transform,"TicketsMarketBlocker",Vector2.zero,Vector2.one,new Color(0,0,0,.78f));_window=LegacyUiFactory.PixelPanel(b,"TicketsMarketWindow",330,115,620,500,new Color(.045f,.036f,.025f,.99f));}
        async Task Load(){if(_busy)return;_busy=true;try{_view=await _api.GetTicketsMarketAsync();Draw();}catch(Exception e){_status(e.Message);}finally{_busy=false;}}
        void Draw()
        {
            LegacyUiFactory.DestroyChildren(_window);LegacyUiFactory.PixelLabel(_window,"ĐIỂM KHOÁN THƯƠNG THÀNH",20,TextAnchor.MiddleCenter,new Color(1,.82f,.35f),145,10,330,32);LegacyUiFactory.PixelButton(_window,"Đóng",535,13,70,27,()=>Destroy(gameObject));
            if(_view==null)return;LegacyUiFactory.PixelLabel(_window,$"Điểm Khoán: {_view.tickets:N0}",15,TextAnchor.MiddleLeft,Color.white,18,50,250,25);
            var all=(_view.goods??Array.Empty<TicketsMarketItemView>()).OrderBy(x=>x.id).ToArray();
            var pageCount=Math.Max(1,(all.Length+PageSize-1)/PageSize);if(_page>=pageCount)_page=pageCount-1;if(_page<0)_page=0;
            var goods=all.Skip(_page*PageSize).Take(PageSize).ToArray();
            for(var i=0;i<goods.Length;i++)
            {
                var g=goods[i];var y=82+i*45;var need=g.buyLevel>0?$" · Lv.{g.buyLevel}":"";
                LegacyUiFactory.PixelLabel(_window,$"{g.name}  —  {g.tickets:N0} điểm{need}",13,TextAnchor.MiddleLeft,g.buyable?Color.white:new Color(.6f,.6f,.6f),20,y,430,28);
                if(g.buyable)LegacyUiFactory.PixelButton(_window,"Mua",470,y,70,27,async()=>await Buy(g));
            }
            if(pageCount>1)
            {
                if(_page>0)LegacyUiFactory.PixelButton(_window,"<",205,452,54,27,()=>{_page--;Draw();});
                LegacyUiFactory.PixelLabel(_window,$"Trang {_page+1}/{pageCount}",13,TextAnchor.MiddleCenter,new Color(.82f,.78f,.67f),265,452,90,27);
                if(_page+1<pageCount)LegacyUiFactory.PixelButton(_window,">",360,452,54,27,()=>{_page++;Draw();});
            }
        }
        async Task Buy(TicketsMarketItemView item)
        {
            if(_busy)return;_busy=true;try{await _api.BuyTicketsMarketAsync(item.id);_status($"Đã mua {item.name}.");_view=await _api.GetTicketsMarketAsync();Draw();PrisonPanel.RefreshOpenFromPush();BlacksmithPanel.RefreshOpenFromPush();}catch(Exception e){_status(e.Message);}finally{_busy=false;}
        }
    }
}
