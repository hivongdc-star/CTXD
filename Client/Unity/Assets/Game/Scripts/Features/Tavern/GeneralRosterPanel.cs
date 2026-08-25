using System;
using System.Threading.Tasks;
using CTXD.Client.Features.FirstPlayable;
using CTXD.Client.Networking;
using UnityEngine;
using UnityEngine.UI;

namespace CTXD.Client.Features.Tavern
{
    public sealed class GeneralRosterPanel : MonoBehaviour
    {
        ApiClient _api;
        RectTransform _window;
        Action<string> _status;
        GeneralRosterResponse _data;
        int _type = 2;
        bool _busy;

        public static GeneralRosterPanel Open(RectTransform host, ApiClient api, Action<string> status, int initialType = 2)
        {
            var go = new GameObject("GeneralRosterPanel");
            go.transform.SetParent(host, false);
            var p = go.AddComponent<GeneralRosterPanel>();
            p._api = api; p._status = status; p._type = initialType;
            p.BuildFrame(); _ = p.LoadAsync();
            return p;
        }

        void BuildFrame()
        {
            var blocker = LegacyUiFactory.Panel(transform,"GeneralBlocker",Vector2.zero,Vector2.one,new Color(0,0,0,.55f));
            _window = LegacyUiFactory.PixelPanel(blocker,"GeneralWindow",356,207,567,353,Color.white);
            LegacyUiFactory.PixelImage(_window,"LegacyVisual/Tavern/00780",0,0,567,353);
            LegacyUiFactory.PixelLabel(_window,"TƯỚNG LĨNH",22,TextAnchor.MiddleCenter,new Color(1f,.82f,.35f),185,7,195,31);
            LegacyUiFactory.PixelButton(_window,"Võ tướng",12,10,83,27,()=>Switch(2));
            LegacyUiFactory.PixelButton(_window,"Văn quan",100,10,83,27,()=>Switch(1));
            LegacyUiFactory.PixelButton(_window,"Đóng",497,9,58,27,()=>Destroy(gameObject));
        }

        async void Switch(int type) { if (_busy) return; _type=type; if (_data==null) await LoadAsync(); else Render(); }
        async Task LoadAsync()
        {
            if(_busy)return; _busy=true;
            try { _data=await _api.GetGeneralsAsync(); Render(); }
            catch(Exception ex){_status?.Invoke(ex.Message);} finally{_busy=false;}
        }

        void Render()
        {
            var old=_window.Find("Content"); if(old!=null)Destroy(old.gameObject);
            var c=LegacyUiFactory.PixelPanel(_window,"Content",0,41,567,312,Color.clear);
            var list=_type==2 ? (_data.military??Array.Empty<GeneralView>()) : (_data.civil??Array.Empty<GeneralView>());
            var max=_type==2 ? _data.militaryMax : _data.civilMax;
            LegacyUiFactory.PixelLabel(c,$"{(_type==2?"Võ tướng":"Văn quan")}  {list.Length}/{max}",14,TextAnchor.MiddleLeft,new Color(1f,.88f,.55f),12,0,180,23);
            for(var i=0;i<Math.Min(list.Length,6);i++) DrawGeneral(c,list[i],i);
            if(list.Length==0) LegacyUiFactory.PixelLabel(c,"Chưa chiêu mộ tướng.",17,TextAnchor.MiddleCenter,new Color(.85f,.8f,.7f),120,110,327,50);
        }

        void DrawGeneral(RectTransform parent, GeneralView g, int index)
        {
            var col=index%3; var row=index/3; var x=15+col*181; var y=30+row*135;
            LegacyUiFactory.PixelImage(parent,"LegacyVisual/Tavern/00903",x,y,163,128);
            var q=Mathf.Clamp(g.quality,1,6);
            LegacyUiFactory.PixelImage(parent,$"LegacyVisual/Tavern/{(697+q*2):00000}",x+8,y+8,76,76);
            LegacyUiFactory.PixelImage(parent,"LegacyVisual/GeneralPic/"+g.pic,x+10,y+10,72,72,true);
            LegacyUiFactory.PixelLabel(parent,$"{g.name}  Lv.{g.level}",14,TextAnchor.MiddleLeft,QualityColor(q),x+86,y+6,71,28);
            var stat=g.type==2?$"Thống {g.leader}\nVõ {g.strength}\nSĩ khí {g.morale}":$"Trí {g.intel}\nChính {g.politics}";
            LegacyUiFactory.PixelLabel(parent,stat,12,TextAnchor.UpperLeft,Color.white,x+88,y+34,69,63);
        }

        static Color QualityColor(int q) => q switch
        {
            1=>Color.white,2=>new Color(.45f,1f,.45f),3=>new Color(.45f,.75f,1f),4=>new Color(.75f,.45f,1f),5=>new Color(1f,.62f,.25f),_=>new Color(1f,.32f,.25f)
        };
    }
}
