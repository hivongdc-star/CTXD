using System;
using System.Linq;
using CTXD.Client.Features.FirstPlayable;
using CTXD.Client.Networking;
using UnityEngine;
using UnityEngine.UI;
namespace CTXD.Client.Features.Equipment
{
    public sealed class TreasurePanel:MonoBehaviour
    {
        static TreasurePanel _open;ApiClient _api;Action<string> _status;RectTransform _window;TreasureView _view;
        public static TreasurePanel Open(RectTransform host,ApiClient api,Action<string> status){if(_open!=null)Destroy(_open.gameObject);var go=new GameObject("TreasurePanel");go.transform.SetParent(host,false);var p=go.AddComponent<TreasurePanel>();p._api=api;p._status=status;_open=p;p.Build();_=p.Load();return p;}
        public static void RefreshOpenFromPush(){if(_open!=null)_=_open.Load();}void OnDestroy(){if(_open==this)_open=null;}
        void Build(){var b=LegacyUiFactory.Panel(transform,"TreasureBlocker",Vector2.zero,Vector2.one,new Color(0,0,0,.72f));_window=LegacyUiFactory.PixelPanel(b,"TreasureWindow",330,150,620,420,new Color(.045f,.036f,.025f,.99f));LegacyUiFactory.PixelLabel(_window,"BẢO VẬT",22,TextAnchor.MiddleCenter,new Color(1,.82f,.35f),175,10,270,34);}
        async System.Threading.Tasks.Task Load(){try{_view=await _api.GetTreasuresAsync();Draw();}catch(Exception e){_status(e.Message);}}
        void Draw(){LegacyUiFactory.DestroyChildren(_window);LegacyUiFactory.PixelLabel(_window,"BẢO VẬT",22,TextAnchor.MiddleCenter,new Color(1,.82f,.35f),175,10,270,34);LegacyUiFactory.PixelButton(_window,"Đóng",535,13,70,27,()=>Destroy(gameObject));var items=(_view?.treasures??Array.Empty<TreasureItemView>()).OrderBy(x=>x.position).ToArray();for(var i=0;i<items.Length;i++){var item=items[i];var col=i%2;var row=i/2;var x=18+col*300;var y=58+row*66;var color=item.owned?new Color(1,.82f,.35f):new Color(.6f,.6f,.6f);LegacyUiFactory.PixelLabel(_window,$"{(item.owned?"◆":"◇")} {item.name}\n{item.tips}",13,TextAnchor.UpperLeft,color,x,y,285,60);}}
    }
}
