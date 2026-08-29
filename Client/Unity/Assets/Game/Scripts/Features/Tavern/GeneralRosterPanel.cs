using System;
using System.Threading.Tasks;
using CTXD.Client.Features.FirstPlayable;
using CTXD.Client.Networking;
using UnityEngine;
using UnityEngine.EventSystems;
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
            var root=LegacyUiFactory.PixelPanel(parent,$"General_{g.id}",x,y,163,128,Color.clear);
            root.gameObject.GetComponent<Image>().raycastTarget=true;
            LegacyUiFactory.PixelImage(root,"LegacyVisual/Tavern/00903",0,0,163,128);
            var q=Mathf.Clamp(g.quality,1,6);
            LegacyUiFactory.PixelImage(root,$"LegacyVisual/Tavern/{(697+q*2):00000}",8,8,76,76);
            LegacyUiFactory.PixelImage(root,"LegacyVisual/GeneralPic/"+g.pic,10,10,72,72,true);
            LegacyUiFactory.PixelLabel(root,$"{g.name}  Lv.{g.level}",14,TextAnchor.MiddleLeft,QualityColor(q),86,6,71,28);
            var stat=g.type==2?$"Thống {g.leader}\nVõ {g.strength}\nSĩ khí {g.morale}":$"Trí {g.intel}\nChính {g.politics}";
            LegacyUiFactory.PixelLabel(root,stat,12,TextAnchor.UpperLeft,Color.white,88,34,69,63);
            AddHover(root,()=>ShowDetail(parent,g,index),()=>HideDetail(parent));
        }

        static void AddHover(RectTransform target, Action enter, Action exit)
        {
            var trigger=target.gameObject.AddComponent<EventTrigger>();
            trigger.triggers=new System.Collections.Generic.List<EventTrigger.Entry>();
            var over=new EventTrigger.Entry{eventID=EventTriggerType.PointerEnter};
            over.callback.AddListener(_=>enter());
            trigger.triggers.Add(over);
            var outEntry=new EventTrigger.Entry{eventID=EventTriggerType.PointerExit};
            outEntry.callback.AddListener(_=>exit());
            trigger.triggers.Add(outEntry);
        }

        void ShowDetail(RectTransform parent, GeneralView g, int index)
        {
            HideDetail(parent);
            var col=index%3;
            var x=Mathf.Clamp(15+col*181+78,6,368);
            var detail=LegacyUiFactory.PixelPanel(parent,"GeneralHoverDetail",x,23,190,145,new Color(.055f,.035f,.018f,.97f));
            var outline=detail.gameObject.AddComponent<Outline>();
            outline.effectColor=new Color(.72f,.53f,.22f,1f);
            outline.effectDistance=new Vector2(1,-1);
            var q=Mathf.Clamp(g.quality,1,6);
            LegacyUiFactory.PixelImage(detail,$"LegacyVisual/Tavern/{(697+q*2):00000}",8,8,76,76);
            LegacyUiFactory.PixelImage(detail,"LegacyVisual/GeneralPic/"+g.pic,10,10,72,72,true);
            LegacyUiFactory.PixelLabel(detail,$"{g.name}  Lv.{g.level}",16,TextAnchor.MiddleLeft,QualityColor(q),90,8,94,26);
            var stat=g.type==2?$"Thống {g.leader}\nVõ {g.strength}\nSĩ khí {g.morale}":$"Trí {g.intel}\nChính {g.politics}";
            LegacyUiFactory.PixelLabel(detail,stat,13,TextAnchor.UpperLeft,Color.white,90,38,94,62);
            LegacyUiFactory.PixelLabel(detail,g.type==2?"Võ tướng":"Văn quan",12,TextAnchor.MiddleLeft,new Color(1f,.84f,.45f),10,108,170,22);
            detail.SetAsLastSibling();
        }

        static void HideDetail(RectTransform parent)
        {
            var detail=parent.Find("GeneralHoverDetail");
            if(detail!=null)Destroy(detail.gameObject);
        }

        static Color QualityColor(int q) => q switch
        {
            1=>Color.white,2=>new Color(.45f,1f,.45f),3=>new Color(.45f,.75f,1f),4=>new Color(.75f,.45f,1f),5=>new Color(1f,.62f,.25f),_=>new Color(1f,.32f,.25f)
        };
    }
}
