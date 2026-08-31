using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CTXD.Client.Networking;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace CTXD.Client.Features.FirstPlayable
{
    sealed class MainCityNavigation
    {
        public Action tavern,equipment,technology,world,nation,mail,market,chat,team;
        public Action onlineGift,dailyGift,battleExp,levelExp,seasonal,vip,kfwd,kfzb,rank;
    }

    sealed class MainCityVisualRenderer : MonoBehaviour
    {
        const string Root="LegacyVisual/MainCity/";
        static readonly int[] Slot={1,2,3,4,5,6,7,16,8,9,11,12,13,14,15,10};
        // Recovered from the 1280x768 BuildingList placement. All five area SWFs
        // share the same sixteen-slot diamond.
        static readonly Vector2[] Pos={Vector2.zero,new Vector2(550,196),new Vector2(462,233),new Vector2(645,233),new Vector2(550,284),new Vector2(369,285),new Vector2(738,285),new Vector2(647,421),new Vector2(463,328),new Vector2(552,328),new Vector2(645,331),new Vector2(552,376),new Vector2(273,328),new Vector2(461,423),new Vector2(368,377),new Vector2(552,463),new Vector2(738,377)};

        RectTransform _host,_scene,_events; MainCityResponse _city; ApiClient _api;
        Action<string> _status; Action<BuildingView> _building; MainCityNavigation _nav; int _area;
        readonly Dictionary<string,MainCityEventIcon> _icons=new Dictionary<string,MainCityEventIcon>();

        public static void Show(RectTransform host,MainCityResponse city,ApiClient api,Action<string> status,Action<BuildingView> building,MainCityNavigation nav)
        {
            var go=new GameObject("MainCityLegacyVisual",typeof(RectTransform));go.transform.SetParent(host,false);
            var rt=(RectTransform)go.transform;rt.anchorMin=Vector2.zero;rt.anchorMax=Vector2.one;rt.offsetMin=rt.offsetMax=Vector2.zero;
            var v=go.AddComponent<MainCityVisualRenderer>();v._host=host;v._city=city;v._api=api;v._status=status;v._building=building;v._nav=nav;v.Build();
        }

        void Build()
        {
            _scene=LegacyUiFactory.PixelPanel(transform,"BuildingScene",0,0,1280,768,Color.clear);
            var first=(_city.buildings??Array.Empty<BuildingView>()).OrderBy(x=>x.id).FirstOrDefault();
            DrawArea(first==null?1:Area(first.id));DrawHud();DrawAreaButtons();DrawBottom();DrawEvents();_=RefreshEvents();
        }

        void DrawArea(int area)
        {
            _area=area;LegacyUiFactory.DestroyChildren(_scene);
            LegacyUiFactory.ResourceImage(_scene,Root+"legacy_maincity_bg_1",Vector2.zero,Vector2.one).raycastTarget=false;
            foreach(var b in (_city.buildings??Array.Empty<BuildingView>()).Where(x=>Area(x.id)==area).OrderBy(x=>Position(x.id)))DrawBuilding(b);
        }

        void DrawBuilding(BuildingView b)
        {
            var slot=Position(b.id);var p=Pos[slot];var path=Root+"Area/"+_area+"/pos"+slot.ToString("00");var sprite=Resources.Load<Sprite>(path);if(sprite==null)return;
            var w=sprite.rect.width;var h=sprite.rect.height;var button=LegacyUiFactory.PixelButton(_scene,"",p.x,p.y,w,h,()=>_building(b),path);
            button.transition=Selectable.Transition.None;button.gameObject.AddComponent<MainCityPointerState>().Init(button.image);
            var label=LegacyUiFactory.PixelLabel(_scene,b.name+"  Lv."+b.level,13,TextAnchor.MiddleCenter,new Color(1f,.92f,.67f),p.x+10,p.y+h-23,w-20,22);
            label.gameObject.AddComponent<Outline>().effectColor=Color.black;label.raycastTarget=false;
            if(b.state!=1)return;
            button.image.color=new Color(.72f,.72f,.72f,1);
            var fx=LegacyUiFactory.PixelImage(_scene,Root+"Effects/Upgrade/01",p.x-75,p.y-62,320,240);fx.raycastTarget=false;
            fx.gameObject.AddComponent<LegacyFrameLoop>().Initialize(fx,Root+"Effects/Upgrade",10,12f);
            var cd=LegacyUiFactory.PixelLabel(_scene,"",14,TextAnchor.MiddleCenter,new Color(1f,.84f,.25f),p.x+15,p.y+h-2,w-30,22);
            cd.gameObject.AddComponent<MainCityCountdown>().Init(b.completeAt,"Đang xây ");cd.raycastTarget=false;
        }

        void DrawHud()
        {
            var p=_city.player;var r=_city.resources;
            LegacyUiFactory.PixelImage(transform,"LegacyVisual/Entry/CreateRole/Thumb/"+Mathf.Clamp(p.pic,1,6),5,5,72,72,true).raycastTarget=false;
            LegacyUiFactory.PixelImage(transform,Root+"HUD/top",0,0,1280,94).raycastTarget=false;
            Text(string.IsNullOrEmpty(p.name)?"Chưa đặt tên":p.name,86,7,205,24,15,new Color(1,.86f,.45f));Text("Lv."+p.level+"   EXP "+p.exp,86,31,205,20,13,Color.white);
            Text((p.sysGold+p.userGold).ToString(),322,8,96,18,13,new Color(1,.9f,.45f));Resource(r.copper,r.copperPerHour,458);Resource(r.wood,r.woodPerHour,595);Resource(r.food,r.foodPerHour,732);Resource(r.iron,r.ironPerHour,869);
            Text("Đội xây: "+p.freeConstructionNum+"/"+p.constructionSlots,1015,7,170,22,13,new Color(1,.86f,.45f));
        }
        void Resource(long value,int rate,float x){Text(value.ToString(),x,8,96,18,13,Color.white);Text("+"+rate+"/h",x,28,96,16,11,new Color(.76f,1,.55f));}
        void Text(string value,float x,float y,float w,float h,int size,Color color){var t=LegacyUiFactory.PixelLabel(transform,value,size,TextAnchor.MiddleCenter,color,x,y,w,h);t.gameObject.AddComponent<Outline>().effectColor=Color.black;t.raycastTarget=false;}

        void DrawAreaButtons()
        {
            var names=new[]{"","Dân Cư","Lâm Mộc","Nông Điền","Khoáng","Quân Doanh"};
            for(var i=1;i<=5;i++){var a=i;var open=(_city.buildings??Array.Empty<BuildingView>()).Any(x=>Area(x.id)==a);var b=LegacyUiFactory.PixelButton(transform,names[i],10,112+(i-1)*31,126,27,()=>{if(open){DrawArea(a);SelectArea();}});b.name="Area"+i;b.interactable=open;b.image.color=open?new Color(.10f,.055f,.02f,.78f):new Color(.04f,.04f,.04f,.58f);b.gameObject.AddComponent<Outline>().effectColor=new Color(.72f,.52f,.18f,1);}
            SelectArea();
        }
        void SelectArea(){for(var i=1;i<=5;i++){var t=transform.Find("Area"+i);if(t!=null&&t.GetComponent<Button>().interactable)t.GetComponent<Image>().color=i==_area?new Color(.48f,.25f,.035f,.94f):new Color(.10f,.055f,.02f,.78f);}}

        void DrawBottom()
        {
            LegacyUiFactory.PixelImage(transform,Root+"HUD/bottom",695,634,585,134).raycastTarget=false;var x=710f;
            Menu("Tửu Quán",Root+"Icons/dinner",ref x,_nav.tavern,Has(44)||Has(45));Menu("Trang Bị",Root+"Menu/equipment",ref x,_nav.equipment,Has(18)||Has(17));Menu("Khoa Kỹ",Root+"Menu/technology",ref x,_nav.technology,Has(19));Menu("Thế Giới",Root+"Menu/world",ref x,_nav.world,Has(10));Menu("Quốc Gia",Root+"Menu/nation",ref x,_nav.nation,Has(10));Menu("Xếp Hạng",Root+"Menu/ranking",ref x,_nav.rank,true);
            Utility(Root+"Menu/mail",1160,674,34,34,_nav.mail,true,"Thư");Utility(Root+"Menu/settings",1202,674,34,34,_nav.chat,true,"Chat");Utility(Root+"Menu/gift",1240,674,28,28,_nav.team,Has(59),"Đội");
        }
        void Menu(string title,string path,ref float x,Action click,bool show){if(!show)return;var s=Resources.Load<Sprite>(path);if(s==null)return;var w=Mathf.Max(50,s.rect.width);var h=Mathf.Max(58,s.rect.height);var b=LegacyUiFactory.PixelButton(transform,"",x,665,w,h,()=>click?.Invoke(),path);b.transition=Selectable.Transition.None;b.gameObject.AddComponent<MainCityPointerState>().Init(b.image);Text(title,x-5,724,w+10,17,11,new Color(1,.9f,.62f));x+=70;}
        void Utility(string path,float x,float y,float w,float h,Action click,bool show,string title){if(!show)return;var b=LegacyUiFactory.PixelButton(transform,"",x,y,w,h,()=>click?.Invoke(),path);b.transition=Selectable.Transition.None;b.gameObject.AddComponent<MainCityPointerState>().Init(b.image);Text(title,x-4,y+h,w+8,15,10,Color.white);}

        void DrawEvents()
        {
            _events=LegacyUiFactory.PixelPanel(transform,"LegacyEventIcons",318,96,950,70,Color.clear);var x=0f;
            Icon("daily","dailayLogin",ref x,_nav.dailyGift,Has(38));Icon("online","zeroreward",ref x,_nav.onlineGift,Has(39));Icon("market","market",ref x,_nav.market,Has(27));Icon("battle","51Activity",ref x,_nav.battleExp,true);Icon("level","sprintLevel",ref x,_nav.levelExp,true);Icon("seasonal","duanwu",ref x,_nav.seasonal,true);Icon("vip","recharge",ref x,_nav.vip,true);Icon("kfwd","kfwd",ref x,_nav.kfwd,true);Icon("kfzb","kfzb",ref x,_nav.kfzb,true);
        }
        void Icon(string key,string asset,ref float x,Action click,bool show){if(!show)return;var path=Root+"Icons/"+asset;if(Resources.Load<Sprite>(path)==null)return;var b=LegacyUiFactory.PixelButton(_events,"",x,0,56,56,()=>click?.Invoke(),path);b.transition=Selectable.Transition.None;var icon=b.gameObject.AddComponent<MainCityEventIcon>();icon.Init(b.image);_icons[key]=icon;x+=58;}

        async Task RefreshEvents(){await Daily();await Online();await Battle();await Level();await Seasonal();}
        async Task Daily(){if(!_icons.ContainsKey("daily"))return;try{var v=await _api.GetDailyGiftAsync();_icons["daily"].Badge(v.available?1:0,null);}catch(Exception){}}
        async Task Online(){if(!_icons.ContainsKey("online"))return;try{var v=await _api.GetOnlineGiftAsync();_icons["online"].Badge(v.available,v.remaining>0?v.remaining.ToString():null);}catch(Exception){}}
        async Task Battle(){try{var v=await _api.GetBattleExpActivityAsync();if(!v.active)_icons["battle"].gameObject.SetActive(false);else _icons["battle"].Badge(v.activated?0:1,v.endsAt);}catch(Exception){}}
        async Task Level(){try{var v=await _api.GetLevelExpActivityAsync();if(!v.active)_icons["level"].gameObject.SetActive(false);else _icons["level"].Badge(v.rewardAvailable?1:0,v.endsAt);}catch(Exception){}}
        async Task Seasonal(){var n=0;string ends=null;try{var v=await _api.GetDragonActivityAsync();if(v.active){n+=v.dragonNum;ends=v.endsAt;}}catch(Exception){}try{var v=await _api.GetIronActivityAsync();n+=v.rewardTimes;if(string.IsNullOrEmpty(ends))ends=v.endsAt;}catch(Exception){}try{var v=await _api.GetDstqActivityAsync();if(string.IsNullOrEmpty(ends))ends=v.endsAt;}catch(Exception){}if(_icons.ContainsKey("seasonal"))_icons["seasonal"].Badge(n,ends);}

        bool Has(int id)=>_city.player?.functionIds!=null&&Array.IndexOf(_city.player.functionIds,id)>=0;
        static int Area(int id)=>Mathf.Clamp(((id-1)/16)+1,1,5);
        static int Position(int id){var n=(id-1)%16;return n>=0&&n<Slot.Length?Slot[n]:1;}
    }

    sealed class MainCityPointerState:MonoBehaviour,IPointerEnterHandler,IPointerExitHandler,IPointerDownHandler,IPointerUpHandler
    {
        Image _image;Vector3 _scale;Color _color;public void Init(Image image){_image=image;_scale=transform.localScale;_color=image.color;}
        public void OnPointerEnter(PointerEventData e){_image.color=_color*1.18f;transform.localScale=_scale*1.035f;}public void OnPointerExit(PointerEventData e){_image.color=_color;transform.localScale=_scale;}public void OnPointerDown(PointerEventData e){if(e.button==PointerEventData.InputButton.Left)transform.localScale=_scale*.97f;}public void OnPointerUp(PointerEventData e){if(e.button==PointerEventData.InputButton.Left)transform.localScale=_scale*1.035f;}
    }
    sealed class MainCityCountdown:MonoBehaviour
    {
        Text _text;DateTimeOffset _end;string _prefix;bool _valid;public void Init(string value,string prefix){_text=GetComponent<Text>();_prefix=prefix;_valid=DateTimeOffset.TryParse(value,out _end);Refresh();}void Update(){Refresh();}void Refresh(){if(_text==null)return;if(!_valid){_text.text=_prefix;return;}var left=_end-DateTimeOffset.UtcNow;if(left<TimeSpan.Zero)left=TimeSpan.Zero;_text.text=_prefix+(left.TotalHours>=1?((int)left.TotalHours)+":"+left.Minutes.ToString("00")+":"+left.Seconds.ToString("00"):left.Minutes.ToString("00")+":"+left.Seconds.ToString("00"));}
    }
    sealed class MainCityEventIcon:MonoBehaviour
    {
        Image _image;Text _badge,_timer;bool _attention;float _start;public void Init(Image image){_image=image;_start=Time.unscaledTime;var dot=LegacyUiFactory.PixelPanel(transform,"RedDot",39,-2,18,18,new Color(.78f,.03f,.015f,1));dot.gameObject.AddComponent<Outline>().effectColor=new Color(1,.65f,.2f,1);_badge=LegacyUiFactory.PixelLabel(dot,"!",11,TextAnchor.MiddleCenter,Color.white,0,0,18,18);dot.gameObject.SetActive(false);_timer=LegacyUiFactory.PixelLabel(transform,"",11,TextAnchor.MiddleCenter,new Color(1,.88f,.42f),-5,51,66,16);_timer.gameObject.AddComponent<Outline>().effectColor=Color.black;}
        public void Badge(int count,string countdown){_badge.transform.parent.gameObject.SetActive(count>0);_badge.text=count>1?count.ToString():"!";_attention=count>0;var cd=_timer.GetComponent<MainCityCountdown>();if(cd==null)cd=_timer.gameObject.AddComponent<MainCityCountdown>();cd.Init(countdown,"");}
        void Update(){if(!_attention){transform.localScale=Vector3.one;transform.localRotation=Quaternion.identity;return;}var t=Time.unscaledTime-_start;transform.localScale=Vector3.one*(1+.055f*Mathf.Sin(t*5.5f));transform.localRotation=Quaternion.Euler(0,0,Mathf.Sin(t*9)*2.2f);}
    }
}
