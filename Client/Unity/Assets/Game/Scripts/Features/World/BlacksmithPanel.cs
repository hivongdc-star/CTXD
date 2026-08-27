using System;
using System.Threading.Tasks;
using CTXD.Client.Features.FirstPlayable;
using CTXD.Client.Networking;
using UnityEngine;
using UnityEngine.UI;

namespace CTXD.Client.Features.World
{
    public sealed class BlacksmithPanel : MonoBehaviour
    {
        static BlacksmithPanel _open;
        ApiClient _api;
        Action<string> _status;
        RectTransform _window;
        BlacksmithView _view;
        bool _busy;

        public static BlacksmithPanel Open(RectTransform host,ApiClient api,Action<string> status)
        {
            if(_open!=null)Destroy(_open.gameObject);
            var go=new GameObject("BlacksmithPanel");go.transform.SetParent(host,false);
            var p=go.AddComponent<BlacksmithPanel>();p._api=api;p._status=status;_open=p;p.Build();_=p.Load();return p;
        }

        public static void RefreshOpenFromPush(){if(_open!=null&&!_open._busy)_=_open.Load();}
        void OnDestroy(){if(_open==this)_open=null;}

        void Build()
        {
            var blocker=LegacyUiFactory.Panel(transform,"BlacksmithBlocker",Vector2.zero,Vector2.one,new Color(0,0,0,.78f));
            _window=LegacyUiFactory.PixelPanel(blocker,"BlacksmithWindow",280,105,720,510,new Color(.045f,.036f,.025f,.99f));
            LegacyUiFactory.PixelLabel(_window,"Đang tải Thợ Rèn từ server...",16,TextAnchor.MiddleCenter,Color.white,120,225,480,36);
        }

        async Task Load()
        {
            if(_busy)return;_busy=true;
            try{_view=await _api.GetBlacksmithAsync();Draw();}
            catch(Exception e){_status(e.Message);}
            finally{_busy=false;}
        }

        void Draw()
        {
            LegacyUiFactory.DestroyChildren(_window);
            LegacyUiFactory.PixelLabel(_window,"THỢ RÈN",22,TextAnchor.MiddleCenter,new Color(1,.82f,.35f),215,8,290,34);
            LegacyUiFactory.PixelButton(_window,"Đóng",632,12,70,27,()=>Destroy(gameObject));
            if(_view==null)return;

            LegacyUiFactory.PixelLabel(_window,$"Sắt: {_view.iron:N0}",15,TextAnchor.MiddleLeft,new Color(.94f,.88f,.72f),20,52,260,26);
            if(!_view.functionOpen)
            {
                LegacyUiFactory.PixelLabel(_window,"Thợ Rèn chưa mở.",18,TextAnchor.MiddleCenter,Color.white,100,205,520,38);
                return;
            }

            var smith=_view.smith1;
            if(smith==null)
            {
                LegacyUiFactory.PixelLabel(_window,"Không nhận được trạng thái Thợ Rèn 1 từ server.",16,TextAnchor.MiddleCenter,Color.white,80,205,560,38);
                return;
            }

            LegacyUiFactory.PixelLabel(_window,"THỢ RÈN 1",19,TextAnchor.MiddleLeft,new Color(1,.82f,.35f),20,92,250,30);
            if(!smith.unlocked)
            {
                LegacyUiFactory.PixelPanel(_window,"NoBuildBg",20,130,680,245,new Color(.08f,.065f,.045f,.88f));
                LegacyUiFactory.PixelLabel(_window,$"Bản vẽ Thợ Rèn cấp 1   {smith.blueprintCount}/1",17,TextAnchor.MiddleCenter,Color.white,75,175,570,34);
                LegacyUiFactory.PixelLabel(_window,$"Nhân vật Lv.{_view.playerLevel}",14,TextAnchor.MiddleCenter,new Color(.78f,.74f,.65f),75,215,570,26);
                LegacyUiFactory.PixelLabel(_window,smith.blueprintCount>0?"Đã có bản vẽ, có thể kiến tạo.":"Chưa có bản vẽ Thợ Rèn.",14,TextAnchor.MiddleCenter,smith.blueprintCount>0?new Color(.75f,1f,.58f):new Color(1f,.67f,.45f),75,250,570,27);
                LegacyUiFactory.PixelButton(_window,"Kiến tạo",285,300,150,42,async()=>await Unlock());
                return;
            }

            var remain=Math.Max(0,smith.dailyLimit-smith.dailyUsed);
            LegacyUiFactory.PixelPanel(_window,"SmithBuiltBg",20,130,680,300,new Color(.08f,.065f,.045f,.88f));
            LegacyUiFactory.PixelLabel(_window,$"Thợ Rèn 1 · Lv.{smith.level}",17,TextAnchor.MiddleLeft,Color.white,45,150,310,30);
            LegacyUiFactory.PixelLabel(_window,$"Huyền Thiết Thạch: {smith.stoneCount:N0}",16,TextAnchor.MiddleLeft,Color.white,45,195,300,30);
            LegacyUiFactory.PixelLabel(_window,$"Số lần còn lại hôm nay: {remain}/{smith.dailyLimit}",15,TextAnchor.MiddleLeft,new Color(.92f,.84f,.65f),45,235,380,28);
            LegacyUiFactory.PixelPanel(_window,"MaterialBg",45,285,430,70,new Color(.12f,.095f,.055f,.92f));
            LegacyUiFactory.PixelLabel(_window,$"1 × Huyền Thiết Thạch   →   +{smith.ironPerDissolve:N0} Sắt",17,TextAnchor.MiddleCenter,new Color(1,.82f,.35f),55,302,410,34);
            if(remain<=0)
                LegacyUiFactory.PixelLabel(_window,"Hôm nay đã dùng hết số lần nung chảy.",14,TextAnchor.MiddleCenter,new Color(1f,.62f,.45f),490,195,190,55);
            else if(smith.stoneCount<=0)
                LegacyUiFactory.PixelLabel(_window,"Không có Huyền Thiết Thạch.",14,TextAnchor.MiddleCenter,new Color(1f,.62f,.45f),490,195,190,55);
            else
                LegacyUiFactory.PixelButton(_window,"Nung chảy",505,296,155,44,async()=>await Dissolve());
        }

        async Task Unlock()
        {
            if(_busy)return;_busy=true;
            try
            {
                var r=await _api.UnlockBlacksmithSmith1Async();
                _status($"Đã kiến tạo Thợ Rèn {r.smithId}.");
                _view=await _api.GetBlacksmithAsync();Draw();
                TicketsMarketPanel.RefreshOpenFromPush();
            }
            catch(Exception e){_status(e.Message);}
            finally{_busy=false;}
        }

        async Task Dissolve()
        {
            if(_busy)return;_busy=true;
            try
            {
                var r=await _api.DissolveBlacksmithSmith1Async();
                _status($"Nung chảy thành công: +{r.ironAdded:N0} Sắt.");
                _view=await _api.GetBlacksmithAsync();Draw();
            }
            catch(Exception e){_status(e.Message);}
            finally{_busy=false;}
        }
    }
}
