using System;
using System.Threading.Tasks;
using CTXD.Client.Features.FirstPlayable;
using CTXD.Client.Networking;
using UnityEngine;
using UnityEngine.UI;

namespace CTXD.Client.Features.Nation
{
    public sealed class NationProtectionPanel : MonoBehaviour
    {
        ApiClient _api; Action<string> _status; RectTransform _window; NationProtectionView _view; NationRankEntry[] _rank; bool _busy;
        public static NationProtectionPanel Open(RectTransform host,ApiClient api,Action<string> status){var go=new GameObject("NationProtectionPanel");go.transform.SetParent(host,false);var panel=go.AddComponent<NationProtectionPanel>();panel._api=api;panel._status=status;panel.Build();_=panel.Refresh();return panel;}
        void Build(){var blocker=LegacyUiFactory.Panel(transform,"ProtectionBlocker",Vector2.zero,Vector2.one,new Color(0,0,0,.9f));_window=LegacyUiFactory.PixelPanel(blocker,"ProtectionWindow",315,145,650,430,new Color(.055f,.035f,.018f,1));}
        async Task Refresh(){try{_view=await _api.GetNationProtectionAsync();_rank=await _api.GetNationRankAsync("protect");Draw();}catch(Exception ex){_status(ex.Message);Destroy(gameObject);}}
        void Draw(){LegacyUiFactory.DestroyChildren(_window);LegacyUiFactory.PixelLabel(_window,"BẢO VỆ MAN VƯƠNG",23,TextAnchor.MiddleCenter,new Color(1,.8f,.3f),160,14,330,36);LegacyUiFactory.PixelButton(_window,"Đóng",560,16,70,27,()=>Destroy(gameObject));LegacyUiFactory.PixelLabel(_window,"Thành #"+_view.cityId+" · địch quốc "+_view.attackingForceId,16,TextAnchor.MiddleLeft,Color.white,35,70,390,25);LegacyUiFactory.PixelLabel(_window,"Hạ địch "+_view.playerKills+" · hạng "+_view.rank,16,TextAnchor.MiddleLeft,new Color(1,.8f,.4f),35,102,330,25);if(_view.rewardAvailable)LegacyUiFactory.PixelButton(_window,"Nhận thưởng",440,98,150,29,async()=>await Reward());LegacyUiFactory.PixelLabel(_window,"XẾP HẠNG",17,TextAnchor.MiddleLeft,new Color(1,.8f,.4f),35,145,180,25);var rows=_rank??Array.Empty<NationRankEntry>();for(var i=0;i<Math.Min(8,rows.Length);i++){var row=rows[i];LegacyUiFactory.PixelLabel(_window,row.rank+". "+row.name,14,TextAnchor.MiddleLeft,Color.white,45,177+i*27,260,23);LegacyUiFactory.PixelLabel(_window,row.value.ToString(),14,TextAnchor.MiddleRight,new Color(.9f,.8f,.65f),330,177+i*27,150,23);}}
        async Task Reward(){if(_busy)return;_busy=true;try{var reward=await _api.ClaimNationProtectionRewardAsync();_status("Thưởng bảo vệ: EXP "+reward.rankExp+", sắt "+reward.rankIron);await Refresh();}catch(Exception ex){_status(ex.Message);}finally{_busy=false;}}
    }
}
