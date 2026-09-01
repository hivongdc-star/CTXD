using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CTXD.Client.Networking;
using CTXD.Client.Features.Tavern;
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
        const string Root="LegacyVisual/MainCity/W1/";
        const string LegacyRoot="LegacyVisual/MainCity/";
        const float SwfFps=24f;

        readonly struct BuildingPlacement
        {
            public readonly int instance,depth,upgradeVariant;
            public readonly float x,y,w,h,rootX,rootY;
            public BuildingPlacement(int instance,int depth,float x,float y,float w,float h,float rootX,float rootY,int upgradeVariant)
            {this.instance=instance;this.depth=depth;this.x=x;this.y=y;this.w=w;this.h=h;this.rootX=rootX;this.rootY=rootY;this.upgradeVariant=upgradeVariant;}
        }
        readonly struct UpgradePart
        {
            public readonly int characterId;public readonly float sx,sy,tx,ty;
            public UpgradePart(int characterId,float sx,float sy,float tx,float ty){this.characterId=characterId;this.sx=sx;this.sy=sy;this.tx=tx;this.ty=ty;}
        }
        readonly struct RegionalPlacement
        {
            public readonly int id;public readonly float x,y,w,h,gx,gy,gw,gh;
            public RegionalPlacement(int id,float x,float y,float w,float h,float gx,float gy,float gw,float gh){this.id=id;this.x=x;this.y=y;this.w=w;this.h=h;this.gx=gx;this.gy=gy;this.gw=gw;this.gh=gh;}
        }

        static readonly BuildingPlacement[] Area1=
        {
            new BuildingPlacement(1,2,559f,196f,148f,84f,559f,196f,0),
            new BuildingPlacement(2,23,652f,242f,148f,84f,652f,242f,0),
            new BuildingPlacement(8,44,472f,233f,149f,91f,472f,233f,1),
            new BuildingPlacement(3,65,742f,286f,148f,84f,742f,286f,0),
            new BuildingPlacement(5,86,377.95f,285f,148f,88f,378f,285f,0),
            new BuildingPlacement(9,107,563f,279f,149f,91f,563f,279f,0),
            new BuildingPlacement(4,128,840f,333f,148f,84f,840f,333f,0),
            new BuildingPlacement(6,149,468.95f,330f,148f,88f,469f,330f,0),
            new BuildingPlacement(12,170,284f,328f,154f,90f,282f,328f,0),
            new BuildingPlacement(10,191,655f,331f,157f,86f,654f,331f,0),
            new BuildingPlacement(11,212,561f,376f,149f,86f,561f,376f,0),
            new BuildingPlacement(14,233,377f,377f,149f,88f,377f,377f,0),
            new BuildingPlacement(16,254,746.15f,377f,151f,85f,746.15f,377f,0),
            new BuildingPlacement(7,275,656f,421f,149f,87f,656f,421f,0),
            new BuildingPlacement(13,296,470f,423f,148f,86f,470f,423f,0),
            new BuildingPlacement(15,317,561f,463f,150f,93f,561f,463f,0)
        };

        static readonly BuildingPlacement[] Area2=
        {
            new BuildingPlacement(1,2,559f,184f,151f,93f,559f,184f,0),
            new BuildingPlacement(2,23,650f,230f,151f,93f,650f,230f,0),
            new BuildingPlacement(8,44,461f,234f,156f,93f,461f,234f,0),
            new BuildingPlacement(3,65,740f,274f,151f,93f,740f,274f,0),
            new BuildingPlacement(5,86,375f,275f,156f,96f,375f,274f,0),
            new BuildingPlacement(9,107,552f,276f,156f,93f,552f,276f,0),
            new BuildingPlacement(4,128,833f,320f,151f,93f,833f,320f,0),
            new BuildingPlacement(6,149,461f,318f,156f,96f,461f,317f,0),
            new BuildingPlacement(12,170,283f,320f,153f,94f,283f,320f,0),
            new BuildingPlacement(10,191,645f,324f,153f,90f,645f,323f,0),
            new BuildingPlacement(11,212,555f,373f,153f,85f,555f,373f,0),
            new BuildingPlacement(14,233,366f,365f,155f,97f,366f,364f,0),
            new BuildingPlacement(16,254,739f,367f,153f,91f,739f,367f,0),
            new BuildingPlacement(7,275,645f,412f,157f,93f,645f,411f,0),
            new BuildingPlacement(13,296,463f,412f,154f,95f,462f,412f,0),
            new BuildingPlacement(15,317,554f,465f,155f,86f,554f,465f,0)
        };

        static readonly BuildingPlacement[] Area3=
        {
            new BuildingPlacement(1,2,553f,190f,165f,91f,553f,190f,0),
            new BuildingPlacement(2,23,650f,233f,165f,91f,650f,233f,0),
            new BuildingPlacement(6,44,473f,236f,151f,82f,473f,236f,0),
            new BuildingPlacement(3,65,745f,275f,165f,91f,745f,275f,0),
            new BuildingPlacement(7,86,564f,283f,151f,82f,564f,283f,0),
            new BuildingPlacement(9,107,376f,266f,158f,100f,371f,266f,0),
            new BuildingPlacement(4,128,839f,320f,165f,91f,839f,320f,0),
            new BuildingPlacement(8,149,654.05f,324f,150f,83f,654.05f,324f,0),
            new BuildingPlacement(10,170,473f,309f,158f,100f,468f,309f,0),
            new BuildingPlacement(13,191,286f,323f,151f,91f,286f,321f,0),
            new BuildingPlacement(5,212,745f,354f,156f,98f,744f,354f,0),
            new BuildingPlacement(11,233,566f,363f,153f,86f,566f,363f,0),
            new BuildingPlacement(14,254,385f,362f,154f,90f,385f,362f,0),
            new BuildingPlacement(15,275,467f,397f,157f,100f,467f,397f,0),
            new BuildingPlacement(16,296,659.05f,403f,146f,92f,658.05f,403f,0),
            new BuildingPlacement(12,317,561f,456f,156f,90f,561f,456f,0)
        };

        static readonly BuildingPlacement[] Area4=
        {
            new BuildingPlacement(1,2,556.55f,184f,143f,95f,556.55f,184f,0),
            new BuildingPlacement(2,23,646.05f,229f,143f,95f,646.05f,229f,0),
            new BuildingPlacement(16,44,739.05f,363f,151f,95f,739.05f,363f,0),
            new BuildingPlacement(3,65,737.05f,272.7f,143f,95f,737.05f,272.7f,0),
            new BuildingPlacement(8,86,464.05f,222f,154f,102f,464.05f,222f,0),
            new BuildingPlacement(5,107,373.55f,270.7f,152f,96f,373.55f,270.7f,0),
            new BuildingPlacement(4,128,832.55f,319f,143f,95f,832.55f,319f,0),
            new BuildingPlacement(9,149,554.05f,268f,154f,102f,554.05f,268f,0),
            new BuildingPlacement(6,170,463.55f,317f,152f,96f,463.55f,317f,0),
            new BuildingPlacement(12,191,281.6f,336f,155f,80f,281.6f,336f,0),
            new BuildingPlacement(10,212,646.05f,318f,156f,95f,646.05f,318f,0),
            new BuildingPlacement(11,233,555.05f,372f,153f,86f,555.05f,372f,0),
            new BuildingPlacement(14,254,373.55f,374.05f,153f,85f,373.55f,374.05f,0),
            new BuildingPlacement(7,275,554.05f,460f,156f,90f,554.05f,460f,0),
            new BuildingPlacement(13,296,464.05f,423f,157f,84f,463.05f,423f,0),
            new BuildingPlacement(15,317,648.05f,418f,153f,84f,648.05f,418f,0)
        };

        static readonly BuildingPlacement[] Area5=
        {
            new BuildingPlacement(1,2,565f,200f,135f,77f,564f,200f,2),
            new BuildingPlacement(2,23,659f,245f,135f,77f,658f,245f,2),
            new BuildingPlacement(16,44,476f,231f,141f,91f,475f,231f,2),
            new BuildingPlacement(3,65,752f,290f,135f,77f,751f,290f,2),
            new BuildingPlacement(8,86,566f,278f,141f,91f,565f,278f,2),
            new BuildingPlacement(5,107,385f,281f,142f,86f,385f,281f,2),
            new BuildingPlacement(4,128,841f,336f,135f,77f,840f,336f,2),
            new BuildingPlacement(9,149,657f,322f,141f,91f,656f,322f,2),
            new BuildingPlacement(6,170,475f,327f,142f,86f,475f,327f,2),
            new BuildingPlacement(12,191,283f,319f,146f,101f,283f,319f,3),
            new BuildingPlacement(10,212,749f,369f,141f,91f,748f,369f,2),
            new BuildingPlacement(11,233,565f,372f,142f,86f,565f,372f,2),
            new BuildingPlacement(14,254,380f,362f,146f,101f,380f,362f,3),
            new BuildingPlacement(7,275,660f,418f,142f,86f,660f,418f,2),
            new BuildingPlacement(13,296,472f,407f,146f,101f,472f,407f,0),
            new BuildingPlacement(15,317,565f,455f,146f,101f,565f,455f,3)
        };

        static readonly UpgradePart[] UpgradeVariant0=
        {
            new UpgradePart(7,1f,1f,0f,0f),
            new UpgradePart(34,0.79424f,0.80727f,85.55f,82.65f),
            new UpgradePart(59,0.79424f,0.80727f,33.1f,67.4f),
            new UpgradePart(76,0.79424f,0.80727f,22.1f,65.2f),
            new UpgradePart(89,0.79424f,0.80727f,76.9f,74f),
            new UpgradePart(89,0.79424f,0.80727f,63.4f,56.4f),
            new UpgradePart(89,0.79424f,0.80727f,45.15f,47.75f)
        };

        static readonly UpgradePart[] UpgradeVariant1=
        {
            new UpgradePart(7,1f,1f,3f,2f),
            new UpgradePart(34,0.79424f,0.80727f,88.55f,84.65f),
            new UpgradePart(59,0.79424f,0.80727f,36.1f,69.4f),
            new UpgradePart(76,0.79424f,0.80727f,25.1f,67.2f),
            new UpgradePart(89,0.79424f,0.80727f,79.9f,76f),
            new UpgradePart(89,0.79424f,0.80727f,66.4f,58.4f),
            new UpgradePart(89,0.79424f,0.80727f,48.15f,49.75f)
        };

        static readonly UpgradePart[] UpgradeVariant2=
        {
            new UpgradePart(7,1f,1f,0f,-9f),
            new UpgradePart(34,0.79424f,0.80727f,85.55f,82.65f),
            new UpgradePart(59,0.79424f,0.80727f,33.1f,67.4f),
            new UpgradePart(76,0.79424f,0.80727f,22.1f,65.2f),
            new UpgradePart(89,0.79424f,0.80727f,76.9f,74f),
            new UpgradePart(89,0.79424f,0.80727f,63.4f,56.4f),
            new UpgradePart(89,0.79424f,0.80727f,45.15f,47.75f)
        };

        static readonly UpgradePart[] UpgradeVariant3=
        {
            new UpgradePart(7,1f,1f,0f,0f),
            new UpgradePart(34,0.79424f,0.80727f,85.55f,91.65f),
            new UpgradePart(59,0.79424f,0.80727f,33.1f,76.4f),
            new UpgradePart(76,0.79424f,0.80727f,22.1f,74.2f),
            new UpgradePart(89,0.79424f,0.80727f,76.9f,83f),
            new UpgradePart(89,0.79424f,0.80727f,63.4f,65.4f),
            new UpgradePart(89,0.79424f,0.80727f,45.15f,56.75f)
        };

        static readonly RegionalPlacement[] Regions=
        {
            new RegionalPlacement(1,547.95f,298.9f,335f,164f,529.95f,280.9f,371f,200f),
            new RegionalPlacement(2,360.95f,224.9f,262f,143f,342.95f,206.9f,298f,179f),
            new RegionalPlacement(3,299.05f,382.7f,263f,146f,281.05f,364.7f,299f,182f),
            new RegionalPlacement(4,750.05f,394.9f,280f,181f,732.05f,376.9f,316f,217f),
            new RegionalPlacement(5,426.25f,453.1f,290f,177f,408.25f,435.1f,326f,213f),
            new RegionalPlacement(6,657.55f,190f,407f,202f,639.55f,172f,443f,238f)
        };

        static readonly Dictionary<int,(int Frames,float OriginX,float OriginY,float Width,float Height)> WorkerInfo =
            new Dictionary<int,(int,float,float,float,float)>
            {
                [34]=(20,-17f,-29f,49f,34f),
                [59]=(20,-11f,-29f,34f,34f),
                [76]=(16,-28f,-29f,37f,34f),
                [89]=(12,-9f,-19f,24f,23f)
            };

        RectTransform _host,_scene,_hud,_events,_popup;
        MainCityResponse _city;ApiClient _api;Action<string> _status;Action<BuildingView> _legacyBuildingCallback;MainCityNavigation _nav;
        int _area=1;bool _regional=true;bool _busy;

        public static void Show(RectTransform host,MainCityResponse city,ApiClient api,Action<string> status,Action<BuildingView> building,MainCityNavigation nav)
        {
            var go=new GameObject("MainCityLegacyVisual",typeof(RectTransform));go.transform.SetParent(host,false);
            var rt=(RectTransform)go.transform;rt.anchorMin=Vector2.zero;rt.anchorMax=Vector2.one;rt.offsetMin=rt.offsetMax=Vector2.zero;
            var v=go.AddComponent<MainCityVisualRenderer>();v._host=host;v._city=city;v._api=api;v._status=status;v._legacyBuildingCallback=building;v._nav=nav;v.Build();
        }

        void Build()
        {
            _scene=LegacyUiFactory.PixelPanel(transform,"MainCityScene",0,0,1280,768,Color.clear);
            _hud=LegacyUiFactory.PixelPanel(transform,"GameUI",0,0,1280,768,Color.clear);
            DrawRegional();
            DrawHud();
            DrawGameUi();
            DrawEventGrid();
            StartCoroutine(HideObsoleteGenericTaskSurface());
        }

        IEnumerator HideObsoleteGenericTaskSurface()
        {
            // FirstPlayableApp currently appends its pre-restoration generic task label after Show() returns.
            // Keep the app/login/force/create-role code untouched and remove only that obsolete Main City surface.
            yield return null;
            for(var i=0;i<_host.childCount;i++)
            {
                var child=_host.GetChild(i);if(child==transform)continue;
                var text=child.GetComponent<Text>();
                if(text!=null&&!string.IsNullOrEmpty(text.text)&&text.text.StartsWith("Nhiệm vụ",StringComparison.Ordinal))child.gameObject.SetActive(false);
            }
        }

        static int AreaForId(int id)=>Mathf.Clamp(((id-1)/16)+1,1,5);
        static int LegacyInstance(int id)=>((id-1)%16)+1;
        static BuildingPlacement[] Placements(int area)=>area switch{1=>Area1,2=>Area2,3=>Area3,4=>Area4,_=>Area5};
        static UpgradePart[] UpgradeParts(int variant)=>variant switch{0=>UpgradeVariant0,1=>UpgradeVariant1,2=>UpgradeVariant2,_=>UpgradeVariant3};

        void DrawRegional()
        {
            _regional=true;_area=0;ClosePopup();LegacyUiFactory.DestroyChildren(_scene);
            var bg=LegacyUiFactory.ResourceImage(_scene,LegacyRoot+"legacy_maincity_regional_00001",Vector2.zero,Vector2.one);bg.raycastTarget=false;
            foreach(var r in Regions)
            {
                // Invisible alpha-tested exact legacy silhouette is the hit target; the already-exported regional frame is the static visual.
                var hit=LegacyUiFactory.PixelImage(_scene,Root+$"Regional/Region{r.id}/base",r.x,r.y,r.w,r.h);hit.color=new Color(1,1,1,.001f);hit.alphaHitTestMinimumThreshold=.1f;
                var glow=LegacyUiFactory.PixelImage(_scene,Root+$"Regional/Region{r.id}/GlowOut/08",r.gx,r.gy,r.gw,r.gh);glow.raycastTarget=false;glow.enabled=false;
                var pointer=hit.gameObject.AddComponent<LegacyRegionalPointer>();
                var region=r.id;
                pointer.Initialize(glow,Root+$"Regional/Region{region}",()=>{if(region<=5)DrawArea(region); /* CONTRACT GAP: region 6 destination unavailable; exact visual remains interactive but action is intentionally no-op. */});
            }
        }

        void DrawArea(int area)
        {
            _regional=false;_area=area;ClosePopup();LegacyUiFactory.DestroyChildren(_scene);
            var bg=LegacyUiFactory.PixelImage(_scene,Root+$"Area{area}_background",0,0,1280,768);bg.raycastTarget=false;
            var buildings=_city.buildings??Array.Empty<BuildingView>();
            foreach(var p in Placements(area).OrderBy(x=>x.depth))
            {
                var b=buildings.FirstOrDefault(x=>AreaForId(x.id)==area&&LegacyInstance(x.id)==p.instance);
                if(b==null)continue;
                DrawBuilding(b,p,area);
            }
        }

        void DrawBuilding(BuildingView b,BuildingPlacement p,int area)
        {
            var path=b.state==1?Root+$"Area{area}/BuildingUpgrade/{p.instance:00}":Root+$"Area{area}/Building/{p.instance:00}";
            var art=LegacyUiFactory.PixelImage(_scene,path,p.x,p.y,p.w,p.h);art.raycastTarget=true;
            var button=art.gameObject.AddComponent<Button>();button.targetGraphic=art;button.transition=Selectable.Transition.None;button.onClick.AddListener(()=>ShowBuildingInfo(b,p));
            if(b.state==1)DrawUpgrade(p);
            // CONTRACT GAP: bandit state unavailable. Server exposes only state 0/1; no bandit visual is inferred or wired.
        }

        void DrawUpgrade(BuildingPlacement p)
        {
            foreach(var u in UpgradeParts(p.upgradeVariant))
            {
                if(u.characterId==7)
                {
                    // mainCity area frame-2 overlay: bitmap 00006, shape bounds x=2..142 y=26..88.
                    var overlay=LegacyUiFactory.PixelImage(_scene,Root+"Upgrade/overlay",p.rootX+u.tx+2f,p.rootY+u.ty+26f,140f,62f);overlay.raycastTarget=false;
                    continue;
                }
                if(!WorkerInfo.TryGetValue(u.characterId,out var info))continue;
                var x=p.rootX+u.tx+info.OriginX*u.sx;var y=p.rootY+u.ty+info.OriginY*u.sy;
                var w=info.Width*Mathf.Abs(u.sx);var h=info.Height*Mathf.Abs(u.sy);
                var image=LegacyUiFactory.PixelImage(_scene,Root+$"Upgrade/Worker{u.characterId}/01",x,y,w,h);image.raycastTarget=false;
                image.gameObject.AddComponent<LegacyFrameLoop>().Initialize(image,Root+$"Upgrade/Worker{u.characterId}",info.Frames,SwfFps);
            }
        }

        void DrawHud()
        {
            LegacyUiFactory.DestroyChildren(_hud);
            var top=LegacyUiFactory.PixelImage(_hud,LegacyRoot+"HUD/top",0,0,1280,94);top.raycastTarget=false;
            var p=_city.player;var r=_city.resources;
            if(p!=null)
            {
                var portrait=LegacyUiFactory.PixelImage(_hud,"LegacyVisual/Entry/CreateRole/Thumb/"+Mathf.Clamp(p.pic,1,6),3,5,72,72,true);portrait.raycastTarget=false;
                Dyn(string.IsNullOrEmpty(p.name)?"":p.name,96,7,93,20,13);
                Dyn(p.level.ToString(),96,33,28,18,12);
                Dyn((p.sysGold+p.userGold).ToString(),325,7,100,18,12);
            }
            if(r!=null)
            {
                Dyn(r.copper.ToString(),463,7,105,18,12);Dyn(r.wood.ToString(),596,7,105,18,12);Dyn(r.food.ToString(),729,7,105,18,12);Dyn(r.iron.ToString(),864,7,105,18,12);
            }
            // gameUI.btn.mail exact roleInfo-local position and Flash up/over states.
            StateButton(_hud,Root+"GameUI/Mail",10,78,15,15,_nav?.mail,true,false,false);
        }

        Text Dyn(string text,float x,float y,float w,float h,int size)
        {
            var t=LegacyUiFactory.PixelLabel(_hud,text,size,TextAnchor.MiddleLeft,new Color(1f,1f,.8f),x,y,w,h);t.gameObject.AddComponent<Outline>().effectColor=new Color(.19f,.125f,.063f);t.raycastTarget=false;return t;
        }

        void DrawGameUi()
        {
            var bottom=LegacyUiFactory.PixelImage(_hud,Root+"GameUI/bottom_bg",695,634,585,134);bottom.raycastTarget=false;
            const float listX=758f,listY=714f,step=51f;
            StateButton(_hud,Root+"GameUI/Equipment",listX+step*0,listY,58,63,_nav?.equipment,true,true);
            StateButton(_hud,Root+"GameUI/General",listX+step*1,listY,58,63,()=>GeneralRosterPanel.Open(_host,_api,_status),true,true);
            StateButton(_hud,Root+"GameUI/Ranking",listX+step*2,listY,58,63,_nav?.rank,true,true);
            StateButton(_hud,Root+"GameUI/Technology",listX+step*3,listY,58,63,_nav?.technology,true,true);
            StateButton(_hud,Root+"GameUI/Soldier",listX+step*4,listY,48,52,null,false,true);
            StateButton(_hud,Root+"GameUI/Nation",listX+step*5,listY,56,61,_nav?.nation,true,true);

            // bottomView origin from GameUI.xml autoAlign: x=563,y=708.
            StateButton(_hud,Root+"GameUI/World",1166,683,38,58,_nav?.world,true,true);
            StateButton(_hud,Root+"GameUI/Expedition",1193,653,59,40,null,true,false); // visual/interactivity retained; no W1 destination semantics invented.
            StateButton(_hud,Root+"GameUI/MainCity",1206,695,66,66,DrawRegional,true,true);
        }

        Button StateButton(Transform parent,string folder,float x,float y,float w,float h,Action action,bool interactable,bool hasDisabled,bool hasDown=true)
        {
            var pressed=folder+(hasDown?"/down":"/over");
            var b=LegacyUiFactory.PixelButton(parent,"",x,y,w,h,()=>action?.Invoke(),folder+"/up",folder+"/over",pressed);
            b.transition=Selectable.Transition.SpriteSwap;b.interactable=interactable;
            if(hasDisabled)
            {
                var state=b.spriteState;var disabled=Resources.Load<Sprite>(folder+"/disabled");if(disabled!=null)state.disabledSprite=disabled;b.spriteState=state;
            }
            return b;
        }

        void DrawEventGrid()
        {
            _events=LegacyUiFactory.PixelPanel(_hud,"LegacyEventGrid",238,33,448,112,Color.clear);
            var items=new (string Asset,Action Click,bool Show)[]
            {
                ("dailayLogin",_nav?.dailyGift,Has(38)),("zeroreward",_nav?.onlineGift,Has(39)),("market",_nav?.market,Has(27)),("51Activity",_nav?.battleExp,true),
                ("sprintLevel",_nav?.levelExp,true),("duanwu",_nav?.seasonal,true),("recharge",_nav?.vip,true),("kfwd",_nav?.kfwd,true),("kfzb",_nav?.kfzb,true)
            };
            var slot=0;
            foreach(var item in items)
            {
                if(!item.Show)continue;
                var col=slot%8;var row=slot/8;if(row>=2)break;
                var path=LegacyRoot+"Icons/"+item.Asset;var sprite=Resources.Load<Sprite>(path);if(sprite==null){slot++;continue;}
                var b=LegacyUiFactory.PixelButton(_events,"",col*56,row*56,56,56,()=>item.Click?.Invoke(),path);b.transition=Selectable.Transition.None;
                slot++;
            }
        }

        bool Has(int id)=>_city.player?.functionIds!=null&&Array.IndexOf(_city.player.functionIds,id)>=0;

        void ShowBuildingInfo(BuildingView b,BuildingPlacement p)
        {
            ClosePopup();
            // mainCity.view.infoPopo exact 127x53 skin. XML fields are intentionally allowed to overflow the skin bounds, matching Flash.
            _popup=LegacyUiFactory.PixelPanel(_scene,"mainCity.view.infoPopo",p.rootX,p.rootY,127,53,Color.clear);
            var bg=LegacyUiFactory.PixelImage(_popup,Root+"Popup/infoPopo",0,0,127,53);bg.raycastTarget=false;
            PopupLabel(_popup,"Bạc: "+b.nextCopperCost,-32,-46,210,20);
            PopupLabel(_popup,"Gỗ: "+b.nextWoodCost,-32,-30,210,20);
            var up=LegacyUiFactory.PixelButton(_popup,"",15,-51,47,43,async()=>await UpgradeBuilding(b.id),Root+"Popup/upgrade_up",Root+"Popup/upgrade_over",Root+"Popup/upgrade_down");
            up.transition=Selectable.Transition.SpriteSwap;
            var label=LegacyUiFactory.PixelLabel(up.transform,"Nâng cấp",10,TextAnchor.MiddleCenter,new Color(1f,1f,.8f),0,0,47,43);label.gameObject.AddComponent<Outline>().effectColor=new Color(.19f,.125f,.063f);label.raycastTarget=false;
        }

        static void PopupLabel(Transform parent,string text,float x,float y,float w,float h)
        {
            var t=LegacyUiFactory.PixelLabel(parent,text,12,TextAnchor.MiddleLeft,new Color(1f,1f,.8f),x,y,w,h);var o=t.gameObject.AddComponent<Outline>();o.effectColor=new Color(.188f,.125f,.063f);t.raycastTarget=false;
        }

        async Task UpgradeBuilding(int buildingId)
        {
            if(_busy)return;_busy=true;
            try
            {
                var result=await _api.UpgradeAsync(buildingId);
                if(result?.resources!=null)_city.resources=result.resources;
                if(result?.building!=null&&_city.buildings!=null)
                {
                    for(var i=0;i<_city.buildings.Length;i++)if(_city.buildings[i].id==buildingId){_city.buildings[i]=result.building;break;}
                }
                ClosePopup();DrawHud();DrawGameUi();DrawEventGrid();if(_regional)DrawRegional();else DrawArea(_area);
            }
            catch(Exception ex){_status?.Invoke(ex.Message);}
            finally{_busy=false;}
        }

        void ClosePopup(){if(_popup!=null){Destroy(_popup.gameObject);_popup=null;}}
    }

    sealed class LegacyRegionalPointer:MonoBehaviour,IPointerEnterHandler,IPointerExitHandler,IPointerDownHandler,IPointerUpHandler,IPointerClickHandler
    {
        Image _glow;string _root;Action _click;LegacyRegionalGlow _timeline;
        public void Initialize(Image glow,string root,Action click){_glow=glow;_root=root;_click=click;_timeline=glow.gameObject.AddComponent<LegacyRegionalGlow>();_timeline.Initialize(glow,root);}
        public void OnPointerEnter(PointerEventData e)=>_timeline.PlayIn();
        public void OnPointerExit(PointerEventData e)=>_timeline.PlayOut();
        public void OnPointerDown(PointerEventData e){if(e.button==PointerEventData.InputButton.Left)_timeline.HoldOver();}
        public void OnPointerUp(PointerEventData e){if(e.button==PointerEventData.InputButton.Left)_timeline.HoldOver();}
        public void OnPointerClick(PointerEventData e){if(e.button==PointerEventData.InputButton.Left)_click?.Invoke();}
    }

    sealed class LegacyRegionalGlow:MonoBehaviour
    {
        Image _image;Sprite[] _in,_out;float _started;bool _playingIn,_playingOut;
        public void Initialize(Image image,string root)
        {
            _image=image;_in=Load(root+"/GlowIn",10);_out=Load(root+"/GlowOut",8);_image.enabled=false;
        }
        static Sprite[] Load(string folder,int count){var a=new Sprite[count];for(var i=0;i<count;i++)a[i]=Resources.Load<Sprite>($"{folder}/{i+1:00}");return a;}
        public void PlayIn(){_playingIn=true;_playingOut=false;_started=Time.unscaledTime;_image.enabled=true;}
        public void PlayOut(){_playingIn=false;_playingOut=true;_started=Time.unscaledTime;_image.enabled=true;}
        public void HoldOver(){_playingIn=_playingOut=false;if(_in!=null&&_in.Length>0){_image.sprite=_in[_in.Length-1];_image.enabled=true;}}
        void Update()
        {
            var frames=_playingIn?_in:_playingOut?_out:null;if(frames==null||frames.Length==0)return;
            var frame=Mathf.Min(frames.Length-1,(int)((Time.unscaledTime-_started)*SwfFps));_image.sprite=frames[frame];
            if(frame!=frames.Length-1)return;
            if(_playingOut)_image.enabled=false;_playingIn=_playingOut=false;
        }
        const float SwfFps=24f;
    }
}
