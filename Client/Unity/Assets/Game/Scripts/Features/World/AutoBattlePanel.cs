using System;
using System.Threading.Tasks;
using CTXD.Client.Features.FirstPlayable;
using CTXD.Client.Networking;
using UnityEngine;
using UnityEngine.UI;

namespace CTXD.Client.Features.World
{
    public sealed class AutoBattlePanel : MonoBehaviour
    {
        static AutoBattlePanel _open;
        ApiClient _api;
        Action<string> _status;
        RectTransform _window;
        AutoBattleView _state;
        int _selectedCityId;
        bool _busy;

        public static AutoBattlePanel Open(RectTransform host,ApiClient api,Action<string> status,int selectedCityId)
        {
            if(_open!=null)Destroy(_open.gameObject);
            var go=new GameObject("AutoBattlePanel");
            go.transform.SetParent(host,false);
            var panel=go.AddComponent<AutoBattlePanel>();
            panel._api=api;panel._status=status;panel._selectedCityId=selectedCityId;
            _open=panel;panel.Build();_=panel.LoadAsync();return panel;
        }

        public static void RefreshOpenFromPush(){if(_open!=null&&!_open._busy)_=_open.LoadAsync();}
        void OnDestroy(){if(_open==this)_open=null;}

        void Build()
        {
            var blocker=LegacyUiFactory.Panel(transform,"AutoBattleBlocker",Vector2.zero,Vector2.one,new Color(0,0,0,.70f));
            _window=LegacyUiFactory.PixelPanel(blocker,"AutoBattleWindow",410,175,460,365,new Color(.045f,.036f,.025f,.98f));
            LegacyUiFactory.PixelLabel(_window,"TỰ ĐỘNG QUỐC CHIẾN",22,TextAnchor.MiddleCenter,new Color(1f,.82f,.35f),65,12,330,36);
            LegacyUiFactory.PixelButton(_window,"Đóng",374,13,70,28,()=>Destroy(gameObject));
            LegacyUiFactory.PixelLabel(_window,"Đang tải...",16,TextAnchor.MiddleCenter,Color.white,40,130,380,40);
        }

        async Task LoadAsync()
        {
            if(_busy)return;
            _busy=true;
            try{_state=await _api.GetAutoBattleAsync();Draw();}
            catch(Exception ex){_status(ex.Message);}
            finally{_busy=false;}
        }

        void Draw()
        {
            if(_window==null||_state==null)return;
            LegacyUiFactory.DestroyChildren(_window);
            LegacyUiFactory.PixelLabel(_window,"TỰ ĐỘNG QUỐC CHIẾN",22,TextAnchor.MiddleCenter,new Color(1f,.82f,.35f),65,12,330,36);
            LegacyUiFactory.PixelButton(_window,"Đóng",374,13,70,28,()=>Destroy(gameObject));

            if(!_state.techUnlocked)
            {
                LegacyUiFactory.PixelLabel(_window,"Chưa mở tính năng.\nCần hoàn thành khoa kỹ tương ứng của legacy (TechEffect 59).",16,
                    TextAnchor.MiddleCenter,new Color(.92f,.82f,.64f),45,95,370,100);
                return;
            }

            if(_state.state==1)
            {
                var mode=_state.autoType==1?"Công thành":"Phòng thủ";
                var seconds=Math.Max(0,_state.cd/1000L);
                LegacyUiFactory.PixelLabel(_window,$"{mode} · Thành #{_state.targetCityId}\nCòn lại: {seconds/60:00}:{seconds%60:00}\nEXP: {_state.exp:N0}    Tổn thất: {_state.lost:N0}",17,
                    TextAnchor.MiddleCenter,Color.white,45,78,370,125);
                LegacyUiFactory.PixelButton(_window,"Dừng tự động quốc chiến",95,230,270,44,async()=>await StopAsync());
                return;
            }

            var result=ResultText(_state.result);
            LegacyUiFactory.PixelLabel(_window,$"Mục tiêu đang chọn: Thành #{_selectedCityId}\nChi phí legacy: 50.000 lương"+
                (string.IsNullOrEmpty(result)?"":"\nKết quả lần trước: "+result+$" · EXP {_state.exp:N0} · Tổn thất {_state.lost:N0}"),16,
                TextAnchor.MiddleCenter,new Color(.92f,.88f,.78f),35,76,390,130);
            LegacyUiFactory.PixelButton(_window,"Bắt đầu",115,230,230,44,async()=>await StartAsync());
        }

        async Task StartAsync()
        {
            if(_busy||_selectedCityId<=0)return;
            _busy=true;
            try
            {
                _state=await _api.StartAutoBattleAsync(_selectedCityId);
                Draw();
                _status(_state.autoType==1?"Đã bắt đầu tự động công thành.":"Đã bắt đầu tự động phòng thủ.");
            }
            catch(Exception ex){_status(ex.Message);}
            finally{_busy=false;}
        }

        async Task StopAsync()
        {
            if(_busy)return;
            _busy=true;
            try{_state=await _api.StopAutoBattleAsync();Draw();_status("Đã dừng tự động quốc chiến.");}
            catch(Exception ex){_status(ex.Message);}
            finally{_busy=false;}
        }

        static string ResultText(int result)
        {
            switch(result)
            {
                case 1:return "Công thành thành công";
                case 2:return "Công thành hết thời gian";
                case 3:return "Phòng thủ thành công";
                case 4:return "Mất thành";
                case 5:return "Phòng thủ hết thời gian";
                default:return "";
            }
        }
    }
}
