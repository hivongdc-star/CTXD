using System;
using System.Linq;
using System.Threading.Tasks;
using CTXD.Client.Features.FirstPlayable;
using CTXD.Client.Networking;
using UnityEngine;
using UnityEngine.UI;

namespace CTXD.Client.Features.World
{
    public sealed class SlaveActivityPanel : MonoBehaviour
    {
        ApiClient _api;Action<string> _status;RectTransform _window;SlaveActivityView _view;bool _busy;

        public static SlaveActivityPanel Open(RectTransform host,ApiClient api,Action<string> status,SlaveActivityView view)
        {
            var go=new GameObject("SlaveActivityPanel");go.transform.SetParent(host,false);var panel=go.AddComponent<SlaveActivityPanel>();panel._api=api;panel._status=status;panel._view=view;panel.Build();panel.Draw();return panel;
        }
        void Build(){var blocker=LegacyUiFactory.Panel(transform,"SlaveActivityBlocker",Vector2.zero,Vector2.one,new Color(0,0,0,.76f));_window=LegacyUiFactory.PixelPanel(blocker,"SlaveActivityWindow",330,130,620,470,new Color(.045f,.036f,.025f,.99f));}
        void Draw()
        {
            LegacyUiFactory.DestroyChildren(_window);LegacyUiFactory.PixelLabel(_window,"SỰ KIỆN NÔ LỆ",21,TextAnchor.MiddleCenter,new Color(1,.82f,.35f),150,10,320,32);LegacyUiFactory.PixelButton(_window,"Đóng",530,12,70,27,()=>Destroy(gameObject));
            LegacyUiFactory.PixelLabel(_window,$"EXP cộng thêm mỗi lần quất: +{_view.bonusExp:N0}   ·   Còn bắt: {_view.remainingCaptures}",14,TextAnchor.MiddleLeft,Color.white,22,55,560,28);
            var rows=_view.rewards??Array.Empty<SlaveActivityRewardView>();foreach(var reward in rows.OrderBy(x=>x.position))
            {
                var y=95+(reward.position-1)*72;var name=reward.kind=="iron"?$"{reward.value:N0} Sắt":$"{reward.value:N0} EXP";var state=reward.state switch{0=>"Chưa mở",1=>"Có thể bắt",2=>"Có thể quất",_=>"Hoàn tất"};
                LegacyUiFactory.PixelLabel(_window,$"Nô lệ {reward.position}   {name}   ·   {state}",14,TextAnchor.MiddleLeft,Color.white,24,y,390,30);
                if(reward.state==1)LegacyUiFactory.PixelButton(_window,"Bắt",445,y,110,28,async()=>await Act(()=>_api.CaptureSlaveActivityAsync(reward.position),$"Đã nhận {name}."));
                if(reward.state==2)LegacyUiFactory.PixelButton(_window,"Quất",445,y,110,28,async()=>await Act(()=>_api.LashSlaveActivityAsync(reward.position),"Đã tăng bonus quất roi."));
            }
        }
        async Task Act(Func<Task<SlaveActivityActionResult>> action,string message){if(_busy)return;_busy=true;try{await action();_status(message);_view=await _api.GetSlaveActivityAsync();Draw();}catch(Exception e){_status(e.Message);}finally{_busy=false;}}
    }
}
