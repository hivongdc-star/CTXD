using System;
using System.Linq;
using System.Threading.Tasks;
using CTXD.Client.Features.FirstPlayable;
using CTXD.Client.Networking;
using UnityEngine;
using UnityEngine.UI;

namespace CTXD.Client.Features.World
{
    public sealed class FarmPanel : MonoBehaviour
    {
        static FarmPanel _open;
        ApiClient _api;
        Action<string> _status;
        RectTransform _window;
        FarmView _farm;
        GeneralRosterResponse _roster;
        int _generalId;
        int _type=1;
        int _claimPreviewGeneralId;
        int _claimPreviewGold=-1;
        bool _busy;

        public static FarmPanel Open(RectTransform host,ApiClient api,Action<string> status,int selectedGeneralId=0)
        {
            if(_open!=null)Destroy(_open.gameObject);
            var go=new GameObject("FarmPanel");
            go.transform.SetParent(host,false);
            var panel=go.AddComponent<FarmPanel>();
            panel._api=api;panel._status=status;panel._generalId=selectedGeneralId;
            _open=panel;panel.Build();_=panel.LoadAsync();return panel;
        }

        public static void RefreshOpenFromPush(){if(_open!=null&&!_open._busy)_=_open.LoadAsync();}
        void OnDestroy(){if(_open==this)_open=null;}

        void Build()
        {
            var blocker=LegacyUiFactory.Panel(transform,"FarmBlocker",Vector2.zero,Vector2.one,new Color(0,0,0,.72f));
            _window=LegacyUiFactory.PixelPanel(blocker,"FarmWindow",330,80,620,510,new Color(.045f,.036f,.025f,.98f));
            LegacyUiFactory.PixelLabel(_window,"TRUÂN ĐIỀN",22,TextAnchor.MiddleCenter,new Color(1f,.82f,.35f),155,10,310,36);
            LegacyUiFactory.PixelButton(_window,"Đóng",532,13,72,28,()=>Destroy(gameObject));
            LegacyUiFactory.PixelLabel(_window,"Đang tải dữ liệu Farm...",16,TextAnchor.MiddleCenter,Color.white,90,210,440,40);
        }

        async Task LoadAsync()
        {
            if(_busy)return;
            _busy=true;
            try
            {
                _farm=await _api.GetFarmAsync();
                _roster=await _api.GetGeneralsAsync();
                var military=_roster?.military??Array.Empty<GeneralView>();
                if(_generalId==0&&military.Length>0)_generalId=military[0].id;
                Draw();
            }
            catch(Exception ex){_status(ex.Message);}
            finally{_busy=false;}
        }

        void Draw()
        {
            if(_window==null||_farm==null)return;
            LegacyUiFactory.DestroyChildren(_window);
            LegacyUiFactory.PixelLabel(_window,"TRUÂN ĐIỀN",22,TextAnchor.MiddleCenter,new Color(1f,.82f,.35f),155,10,310,36);
            LegacyUiFactory.PixelButton(_window,"Đóng",532,13,72,28,()=>Destroy(gameObject));

            var next=_farm.nextUpCopper>0?$"{_farm.investSum:N0}/{_farm.nextUpCopper:N0}":"Tối đa";
            var cd=Math.Max(0,_farm.investCd/1000L);
            LegacyUiFactory.PixelLabel(_window,$"Thành #{_farm.farmCityId} · Quốc gia Lv.{_farm.nationLevel} · Farm Lv.{_farm.farmLevel}\nĐầu tư: {next} · CD {cd/60:00}:{cd%60:00} · Vé: {_farm.itemNumber}\nHệ số cấp: {_farm.coefficient}% · Bonus thành: {_farm.cityExpBonus}%",15,
                TextAnchor.UpperLeft,new Color(.92f,.88f,.76f),20,54,580,72);

            LegacyUiFactory.PixelButton(_window,"Đầu tư 10.000 bạc",20,130,185,34,async()=>await InvestAsync());
            if(_farm.investCd>0)
                LegacyUiFactory.PixelButton(_window,$"Xóa CD ({_farm.recoverGold} vàng)",215,130,185,34,async()=>await RecoverAsync());
            LegacyUiFactory.PixelButton(_window,"Dừng tất cả",410,130,190,34,async()=>await StopAllAsync());

            var options=_farm.options??Array.Empty<FarmOptionView>();
            LegacyUiFactory.PixelLabel(_window,"Phương án",16,TextAnchor.MiddleLeft,new Color(1f,.82f,.35f),20,172,100,24);
            for(var i=0;i<Math.Min(4,options.Length);i++)
            {
                var option=options[i];
                var selected=option.type==_type?"▶ ":"";
                var text=$"{selected}Loại {option.type} · {option.minutes}p · Lương {option.food:N0} · Thưởng {option.reward:N0}";
                LegacyUiFactory.PixelButton(_window,text,20,198+i*36,580,30,()=>{_type=option.type;Draw();});
            }

            LegacyUiFactory.PixelLabel(_window,"Võ tướng",16,TextAnchor.MiddleLeft,new Color(1f,.82f,.35f),20,346,100,24);
            var farmGenerals=_farm.generals??Array.Empty<FarmGeneralView>();
            var military=_roster?.military??Array.Empty<GeneralView>();
            var shown=military.Take(3).ToArray();
            for(var i=0;i<shown.Length;i++)
            {
                var general=shown[i];
                var state=farmGenerals.FirstOrDefault(x=>x.generalId==general.id);
                var active=state!=null&&state.state>=25&&state.state<=28;
                var buff=state!=null&&state.buffCd>0;
                var suffix=active?$" [Farm loại {state.type}]":state!=null&&state.state==24?" [tại Farm]":buff?" [+50% EXP]":"";
                LegacyUiFactory.PixelButton(_window,(general.id==_generalId?"▶ ":"")+general.name+suffix,20,372+i*35,305,29,()=>{_generalId=general.id;_claimPreviewGeneralId=0;_claimPreviewGold=-1;Draw();});
                if(general.id!=_generalId)continue;
                if(active)
                {
                    var ended=Ended(state.endsAt);
                    if(ended)
                    {
                        LegacyUiFactory.PixelButton(_window,"Nhận thưởng",335,372+i*35,265,29,async()=>await ClaimAsync(general.id));
                    }
                    else
                    {
                        LegacyUiFactory.PixelButton(_window,"Dừng",335,372+i*35,90,29,async()=>await StopAsync(general.id));
                        var previewed=_claimPreviewGeneralId==general.id&&_claimPreviewGold>=0;
                        var label=previewed?$"Nhận sớm ({_claimPreviewGold} vàng)":"Xem giá nhận sớm";
                        LegacyUiFactory.PixelButton(_window,label,430,372+i*35,170,29,
                            async()=>{if(previewed)await ClaimAsync(general.id);else await PreviewClaimAsync(general.id);});
                    }
                }
                else LegacyUiFactory.PixelButton(_window,"Bắt đầu",335,372+i*35,265,29,async()=>await StartAsync(general.id));
            }
            LegacyUiFactory.PixelButton(_window,"Làm mới",475,450,125,32,async()=>await LoadAsync());
        }

        static bool Ended(string value)
        {
            if(string.IsNullOrWhiteSpace(value))return false;
            return DateTimeOffset.TryParse(value,out var ends)&&ends<=DateTimeOffset.UtcNow;
        }

        async Task InvestAsync()
        {
            if(_busy)return;_busy=true;
            try{var r=await _api.InvestFarmAsync();_status($"Đầu tư Farm thành công, nhận {r.exp:N0} EXP.");}
            catch(Exception ex){_status(ex.Message);}finally{_busy=false;}await LoadAsync();
        }

        async Task RecoverAsync()
        {
            if(_busy)return;_busy=true;
            try{var r=await _api.RecoverFarmInvestAsync(Guid.NewGuid().ToString("N"));_status($"Đã xóa CD đầu tư, dùng {r.gold} vàng.");}
            catch(Exception ex){_status(ex.Message);}finally{_busy=false;}await LoadAsync();
        }

        async Task StartAsync(int generalId)
        {
            if(_busy)return;_busy=true;
            try{var r=await _api.StartFarmAsync(generalId,_type);_claimPreviewGeneralId=0;_claimPreviewGold=-1;_status($"Võ tướng bắt đầu Truân Điền loại {r.type}.");}
            catch(Exception ex){_status(ex.Message);}finally{_busy=false;}await LoadAsync();
        }

        async Task StopAsync(int generalId)
        {
            if(_busy)return;_busy=true;
            try{var r=await _api.StopFarmAsync(generalId);_claimPreviewGeneralId=0;_claimPreviewGold=-1;_status($"Đã dừng Truân Điền, nhận {r.reward:N0}.");}
            catch(Exception ex){_status(ex.Message);}finally{_busy=false;}await LoadAsync();
        }

        async Task PreviewClaimAsync(int generalId)
        {
            if(_busy)return;_busy=true;
            try
            {
                var r=await _api.GetFarmClaimCostAsync(generalId);
                _claimPreviewGeneralId=generalId;_claimPreviewGold=r.gold;
                _status(r.gold>0?$"Nhận toàn bộ thưởng ngay cần {r.gold} vàng. Nhấn lại để xác nhận.":"Đã đủ thời gian, nhận thưởng không tốn vàng.");
                Draw();
            }
            catch(Exception ex){_status(ex.Message);}
            finally{_busy=false;}
        }

        async Task ClaimAsync(int generalId)
        {
            if(_busy)return;_busy=true;
            try{var r=await _api.ClaimFarmAsync(generalId,Guid.NewGuid().ToString("N"));_claimPreviewGeneralId=0;_claimPreviewGold=-1;_status($"Đã nhận thưởng {r.reward:N0}, dùng {r.gold} vàng; buff EXP chiến đấu 30 phút.");}
            catch(Exception ex){_status(ex.Message);}finally{_busy=false;}await LoadAsync();
        }

        async Task StopAllAsync()
        {
            if(_busy)return;_busy=true;
            try{var r=await _api.StopAllFarmAsync();_claimPreviewGeneralId=0;_claimPreviewGold=-1;_status($"Đã dừng {r.items?.Length??0} võ tướng Truân Điền.");}
            catch(Exception ex){_status(ex.Message);}finally{_busy=false;}await LoadAsync();
        }
    }
}
