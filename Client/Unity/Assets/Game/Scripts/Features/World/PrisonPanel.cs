using System;
using System.Linq;
using System.Threading.Tasks;
using CTXD.Client.Features.FirstPlayable;
using CTXD.Client.Networking;
using UnityEngine;
using UnityEngine.UI;

namespace CTXD.Client.Features.World
{
    public sealed class PrisonPanel : MonoBehaviour
    {
        static PrisonPanel _open;
        ApiClient _api;
        Action<string> _status;
        RectTransform _window;
        PrisonView _view;
        SlaveActivityView _slaveActivity;
        bool _busy;

        public static PrisonPanel Open(RectTransform host,ApiClient api,Action<string> status)
        {
            if(_open!=null)Destroy(_open.gameObject);
            var go=new GameObject("PrisonPanel");go.transform.SetParent(host,false);
            var p=go.AddComponent<PrisonPanel>();p._api=api;p._status=status;_open=p;p.Build();_=p.Load();return p;
        }
        public static void RefreshOpenFromPush(){if(_open!=null&&!_open._busy)_=_open.Load();}
        void OnDestroy(){if(_open==this)_open=null;}

        void Build()
        {
            var blocker=LegacyUiFactory.Panel(transform,"PrisonBlocker",Vector2.zero,Vector2.one,new Color(0,0,0,.76f));
            _window=LegacyUiFactory.PixelPanel(blocker,"PrisonWindow",265,80,750,590,new Color(.045f,.036f,.025f,.99f));
        }

        async Task Load()
        {
            if(_busy)return;_busy=true;
            try
            {
                _view=await _api.GetPrisonAsync();
                try{_slaveActivity=await _api.GetSlaveActivityAsync();}catch(ApiException e)when(e.Code=="SLAVE_ACTIVITY_UNAVAILABLE"||e.Code=="PRISON_MISSING"){_slaveActivity=null;}
                Draw();
            }
            catch(Exception e){_status(e.Message);}
            finally{_busy=false;}
        }

        void Draw()
        {
            LegacyUiFactory.DestroyChildren(_window);
            LegacyUiFactory.PixelLabel(_window,"LAO PHÒNG",22,TextAnchor.MiddleCenter,new Color(1,.82f,.35f),225,8,300,34);
            LegacyUiFactory.PixelButton(_window,"Điểm Khoán",548,12,105,27,()=>TicketsMarketPanel.Open((RectTransform)transform.parent,_api,_status));
            LegacyUiFactory.PixelButton(_window,"Đóng",660,12,70,27,()=>Destroy(gameObject));
            if(_view==null)return;
            if(!_view.built)
            {
                LegacyUiFactory.PixelLabel(_window,_view.havePic?"Đã có Lao Phòng Kiến Thiết Đồ Chỉ (601).":"Cần Lao Phòng Kiến Thiết Đồ Chỉ (601).",17,TextAnchor.MiddleCenter,Color.white,90,185,570,60);
                if(_view.havePic)LegacyUiFactory.PixelButton(_window,"Kiến tạo",300,270,150,42,async()=>await BuildPrison());
                DrawCaptives(55);
                return;
            }

            var tech=_view.haveTech?$"Điểm tự do {_view.currentFreePoint}/{_view.maxFreePoint}":"Chưa có kỹ thuật điểm tự do";
            var lashText=_view.trialActive?$"Roi Lv.{_view.effectiveLashLv} (thử)":$"Roi Lv.{_view.lashLv}";
            LegacyUiFactory.PixelLabel(_window,$"Lao Phòng Lv.{_view.prisonLv}   {lashText}/{_view.maxLashLv}   Bắt: {_view.grabNum}   Phẩm chất: {_view.quality}\nEXP tự quất: {_view.autoLashExp:N0}   {tech}",14,TextAnchor.UpperLeft,new Color(.92f,.86f,.68f),18,48,520,56);
            if(_view.canUpdate)
            {
                var text=_view.haveUpgradePic?"Nâng Lao Phòng":"Thiếu bản vẽ";
                LegacyUiFactory.PixelButton(_window,text,548,52,178,28,async()=>await UpgradePrison());
            }
            if(_view.lashLv<_view.maxLashLv)
                LegacyUiFactory.PixelButton(_window,$"Nâng roi ({_view.upgradeGold}v)",548,85,178,28,async()=>await UpgradeLash());
            if(_view.canTrial)
                LegacyUiFactory.PixelButton(_window,$"Thử roi ({_view.trialGold}v)",548,116,178,28,async()=>await TrialLash());
            if(_slaveActivity!=null)
                LegacyUiFactory.PixelButton(_window,$"Sự kiện +{_slaveActivity.bonusExp} EXP",548,147,178,28,()=>SlaveActivityPanel.Open((RectTransform)transform.parent,_api,_status,_slaveActivity));

            LegacyUiFactory.PixelLabel(_window,"TÙ NHÂN",16,TextAnchor.MiddleLeft,new Color(1,.82f,.35f),18,118,240,24);
            var prisoners=(_view.generals??Array.Empty<PrisonerView>()).Take(5).ToArray();
            for(var i=0;i<prisoners.Length;i++)
            {
                var s=prisoners[i];var y=145+i*62;
                var escape=string.IsNullOrEmpty(s.escapeAt)?"":$" · đang vượt ngục tới {ShortTime(s.escapeAt)}";
                LegacyUiFactory.PixelLabel(_window,$"{s.playerName} · {s.generalName} Lv.{s.level}{escape}",13,TextAnchor.MiddleLeft,Color.white,20,y,410,25);
                if(s.slashTimes<=0)LegacyUiFactory.PixelButton(_window,"Quất",438,y,75,25,async()=>await Lash(s));
                LegacyUiFactory.PixelButton(_window,"Thả 5v",520,y,90,25,async()=>await Free(s));
            }
            if(prisoners.Length==0)LegacyUiFactory.PixelLabel(_window,"Chưa có tù nhân.",13,TextAnchor.MiddleLeft,new Color(.65f,.65f,.65f),20,145,300,25);
            DrawCaptives(470);
        }

        void DrawCaptives(float y)
        {
            var captives=_view?.captives??Array.Empty<CaptiveGeneralView>();
            LegacyUiFactory.PixelLabel(_window,"VÕ TƯỚNG CỦA BẠN BỊ BẮT",15,TextAnchor.MiddleLeft,new Color(1,.67f,.35f),18,y,350,24);
            y+=27;
            if(captives.Length==0){LegacyUiFactory.PixelLabel(_window,"Không có.",12,TextAnchor.MiddleLeft,new Color(.65f,.65f,.65f),20,y,200,22);return;}
            foreach(var c in captives.Take(2))
            {
                var state=string.IsNullOrEmpty(c.escapeAt)?$"Bị {c.holderName} giam":$"Vượt ngục tới {ShortTime(c.escapeAt)}";
                LegacyUiFactory.PixelLabel(_window,$"{c.generalName} · {state}",12,TextAnchor.MiddleLeft,Color.white,20,y,480,24);
                if(string.IsNullOrEmpty(c.escapeAt))LegacyUiFactory.PixelButton(_window,"Vượt ngục",515,y,95,24,async()=>await Escape(c));
                y+=28;
            }
        }

        static string ShortTime(string value)
        {
            if(DateTimeOffset.TryParse(value,out var dt))return dt.ToLocalTime().ToString("HH:mm:ss");
            return value;
        }
        async Task BuildPrison()=>await RefreshAction(async()=>{_view=await _api.BuildPrisonAsync();_status("Đã kiến tạo Lao Phòng.");});
        async Task UpgradePrison()=>await RefreshAction(async()=>{_view=await _api.UpgradePrisonAsync();_status($"Lao Phòng lên Lv.{_view.prisonLv}.");});
        async Task UpgradeLash()=>await RefreshAction(async()=>{_view=await _api.UpgradeLashAsync();_status($"Roi lên Lv.{_view.lashLv}.");});
        async Task TrialLash()=>await RefreshAction(async()=>{var r=await _api.TrialLashAsync();_status(r.upgraded?$"Roi lên Lv.{r.lashLv}.":$"Dùng thử Roi Lv.{r.effectiveLashLv} trong 24 giờ.");_view=await _api.GetPrisonAsync();});
        async Task Lash(PrisonerView slave)=>await RefreshAction(async()=>{var r=await _api.LashPrisonerAsync(slave.id);_status($"Quất {slave.generalName}: +{r.rewardExp:N0} EXP.");_view=await _api.GetPrisonAsync();});
        async Task Free(PrisonerView slave)=>await RefreshAction(async()=>{await _api.FreePrisonerAsync(slave.id);_status($"Đã phóng thích {slave.generalName}.");_view=await _api.GetPrisonAsync();});
        async Task Escape(CaptiveGeneralView captive)=>await RefreshAction(async()=>{var r=await _api.EscapePrisonAsync(captive.generalId);_status($"{captive.generalName} bắt đầu vượt ngục ({r.seconds}s).");_view=await _api.GetPrisonAsync();});
        async Task RefreshAction(Func<Task> action)
        {
            if(_busy)return;_busy=true;
            try{await action();Draw();}
            catch(Exception e){_status(e.Message);}
            finally{_busy=false;}
        }
    }
}
