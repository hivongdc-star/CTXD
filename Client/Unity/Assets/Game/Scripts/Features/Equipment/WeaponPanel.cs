using System;
using System.Linq;
using System.Threading.Tasks;
using CTXD.Client.Features.FirstPlayable;
using CTXD.Client.Networking;
using UnityEngine;
using UnityEngine.UI;

namespace CTXD.Client.Features.Equipment
{
    public sealed class WeaponPanel : MonoBehaviour
    {
        static WeaponPanel _open;
        ApiClient _api;
        Action<string> _status;
        RectTransform _window;
        WeaponView _view;
        bool _busy;

        public static WeaponPanel Open(RectTransform host,ApiClient api,Action<string> status)
        {
            if(_open!=null)Destroy(_open.gameObject);
            var go=new GameObject("WeaponPanel");go.transform.SetParent(host,false);
            var p=go.AddComponent<WeaponPanel>();p._api=api;p._status=status;_open=p;p.Build();_=p.Load();return p;
        }
        public static void RefreshOpenFromPush(){if(_open!=null&&!_open._busy)_=_open.Load();}
        void OnDestroy(){if(_open==this)_open=null;}

        void Build()
        {
            var blocker=LegacyUiFactory.Panel(transform,"WeaponBlocker",Vector2.zero,Vector2.one,new Color(0,0,0,.76f));
            _window=LegacyUiFactory.PixelPanel(blocker,"WeaponWindow",270,95,740,565,new Color(.045f,.036f,.025f,.99f));
            LegacyUiFactory.PixelLabel(_window,"BINH KHÍ",22,TextAnchor.MiddleCenter,new Color(1,.82f,.35f),225,10,290,34);
        }

        async Task Load()
        {
            if(_busy)return;_busy=true;
            try{_view=await _api.GetWeaponsAsync();Draw();}
            catch(Exception e){_status(e.Message);}
            finally{_busy=false;}
        }

        void Draw()
        {
            if(_view==null)return;
            LegacyUiFactory.DestroyChildren(_window);
            LegacyUiFactory.PixelLabel(_window,"BINH KHÍ",22,TextAnchor.MiddleCenter,new Color(1,.82f,.35f),225,8,290,34);
            var iron=_view.resources==null?0:_view.resources.iron;
            LegacyUiFactory.PixelLabel(_window,$"Sắt: {iron:N0}",14,TextAnchor.MiddleLeft,new Color(.9f,.86f,.75f),18,18,180,26);
            LegacyUiFactory.PixelButton(_window,"Đóng",650,13,70,27,()=>Destroy(gameObject));

            var weapons=(_view.weapons??Array.Empty<WeaponItemView>()).OrderBy(x=>x.id).ToArray();
            for(var i=0;i<weapons.Length;i++)
            {
                var w=weapons[i];var col=i%2;var row=i/2;var x=18+col*355;var y=58+row*155;
                var title=w.open?w.name:w.name+" · Chưa mở";
                var state=w.level<=0?$"Bản vẽ {w.itemOwned}/{w.itemNum}":$"Lv.{w.level} · {w.times}/{w.totalTimes}";
                var stat=w.type==1?"Công":w.type==2?"Thủ":"Binh lực";
                LegacyUiFactory.PixelLabel(_window,$"{title}\n{stat}: +{w.attribute:N0}\n{state}",15,TextAnchor.UpperLeft,w.open?new Color(1,.86f,.55f):new Color(.6f,.6f,.6f),x,y,230,86);
                if(!w.open)continue;
                var label=w.level<=0?"Rèn":"Cường hóa";
                var cost=w.level<=0?"5 mảnh + tài nguyên":$"{w.upgradeCost:N0} sắt";
                LegacyUiFactory.PixelLabel(_window,cost,12,TextAnchor.MiddleLeft,Color.white,x,y+88,210,24);
                LegacyUiFactory.PixelButton(_window,label,x+238,y+76,98,34,async()=>await Upgrade(w));
            }
        }

        async Task Upgrade(WeaponItemView weapon)
        {
            if(_busy)return;_busy=true;
            try
            {
                var result=await _api.UpgradeWeaponAsync(weapon.id);
                _status(result.levelUp?$"{weapon.name} đã lên Lv.{result.weapon.level}.":$"Cường hóa {weapon.name}: x{result.crit}.");
                _view=await _api.GetWeaponsAsync();Draw();
            }
            catch(Exception e){_status(e.Message);}
            finally{_busy=false;}
        }
    }
}
