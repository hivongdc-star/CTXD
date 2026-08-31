using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CTXD.Client.Core;
using CTXD.Client.Features.Tavern;
using CTXD.Client.Features.Equipment;
using CTXD.Client.Features.Technology;
using CTXD.Client.Features.World;
using CTXD.Client.Features.Nation;
using CTXD.Client.Features.Battle;
using CTXD.Client.Features.Mail;
using CTXD.Client.Features.Market;
using CTXD.Client.Features.Social;
using CTXD.Client.Features.Activity;
using CTXD.Client.Features.Rank;
using CTXD.Client.Networking;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace CTXD.Client.Features.FirstPlayable
{
    public sealed class FirstPlayableApp : MonoBehaviour
    {
        ApiClient _api;
        Canvas _canvas;
        RectTransform _screen;
        Text _status;
        string _username = "";
        string _password = "";
        MainCityResponse _city;
        bool _busy;
        RealtimeClient _realtime;
        bool _refreshQueued;
        InputField _loginUserInput;
        InputField _loginPasswordInput;
        int _selectedForceId = 1;
        readonly Image[] _forceVisuals = new Image[4];
        readonly bool[] _forceHovered = new bool[4];
        readonly bool[] _forcePressed = new bool[4];

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Bootstrap()
        {
            if (FindFirstObjectByType<FirstPlayableApp>() == null)
                new GameObject("CTXD_FirstPlayable").AddComponent<FirstPlayableApp>();
        }

        void Update()
        {
            if (_loginUserInput != null && (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter)))
            {
                var selected = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
                if (selected == _loginUserInput.gameObject)
                    _loginPasswordInput.Select();
                else if (selected == _loginPasswordInput.gameObject && !_busy)
                {
                    _username = _loginUserInput.text.Trim();
                    _password = _loginPasswordInput.text;
                    _ = Auth(false);
                }
            }
            if (_realtime == null) return;
            while (_realtime.TryDequeue(out var message))
            {
                if (!_refreshQueued && message.Contains("\"type\":\"maincity.updated\""))
                {
                    _refreshQueued = true;
                    _ = RefreshFromPush();
                }
                if (message.Contains("\"type\":\"world.updated\"")) WorldPanel.RefreshOpenFromPush();
                if (message.Contains("\"type\":\"auto-battle.updated\"")) AutoBattlePanel.RefreshOpenFromPush();
                if (message.Contains("\"type\":\"mine.updated\"")) MinePanel.RefreshOpenFromPush();
                if (message.Contains("\"type\":\"treasure.updated\"")) TreasurePanel.RefreshOpenFromPush();
                if (message.Contains("\"type\":\"battle.updated\"")) BattlePanel.RefreshOpenFromPush();
                if (message.Contains("\"type\":\"chat.message\"")) ChatPanel.RefreshOpenFromPush();
            }
        }

        async Task RefreshFromPush()
        {
            try { if (!string.IsNullOrEmpty(_api?.Token)) await LoadMainCity(); }
            catch { }
            finally { _refreshQueued = false; }
        }

        async Task EnsureRealtime()
        {
            if (string.IsNullOrEmpty(_api.Token) || _realtime != null) return;
            try
            {
                _realtime = new RealtimeClient();
                await _realtime.ConnectAsync(_api.BaseUrl, _api.Token);
            }
            catch
            {
                _realtime?.Dispose(); _realtime = null;
                // HTTP remains authoritative; realtime failure does not block gameplay.
            }
        }

        void OnDestroy() { _realtime?.Dispose(); }

        async void Start()
        {
            DontDestroyOnLoad(gameObject);
            _api = new ApiClient { BaseUrl = ClientConfig.ServerUrl, Token = PlayerPrefs.GetString("ctxd.session", "") };
            _canvas = LegacyUiFactory.CreateCanvas();
            DontDestroyOnLoad(_canvas.gameObject);
            _screen = LegacyUiFactory.Panel(_canvas.transform, "Screen", Vector2.zero, Vector2.one, Color.black);
            if (!string.IsNullOrEmpty(_api.Token))
            {
                try
                {
                    var p = await _api.GetPlayerAsync();
                    await RouteAfterAuth(p);
                    return;
                }
                catch { PlayerPrefs.DeleteKey("ctxd.session"); _api.Token = ""; }
            }
            ShowLogin();
        }

        void ShowLogin()
        {
            LegacyUiFactory.DestroyChildren(_screen);
            _screen.GetComponent<Image>().color = new Color(51f/255f,51f/255f,51f/255f,1f);
            const float left=510f, top=279f;
            var box=LegacyUiFactory.PixelPanel(_screen,"LoginScene",left,top,260,210,Color.clear);
            var panel=LegacyUiFactory.PixelImage(box,"LegacyVisual/Entry/Login/panel",0,0,260,210);
            panel.raycastTarget=false;
            var title=LegacyUiFactory.PixelLabel(box,"Đăng ký và đăng nhập",14,TextAnchor.MiddleCenter,new Color(1f,1f,.8f),0,13,260,20);
            title.fontStyle=FontStyle.Bold;
            LegacyUiFactory.PixelLabel(box,"TK:",14,TextAnchor.MiddleLeft,Color.white,25,50,42,20);
            LegacyUiFactory.PixelLabel(box,"MK:",14,TextAnchor.MiddleLeft,Color.white,25,80,42,20);
            LegacyUiFactory.PixelLabel(box,"Server:",12,TextAnchor.MiddleLeft,Color.white,25,110,42,20);
            var user=LegacyUiFactory.PixelInput(box,"Username",67,48,155,20,Color.white,Color.black,14);
            var pass=LegacyUiFactory.PixelInput(box,"Password",67,78,155,20,Color.white,Color.black,14,true);
            user.text=_username; pass.text=_password;
            _loginUserInput=user; _loginPasswordInput=pass;

            var combo=LegacyUiFactory.PixelImage(box,"LegacyVisual/Entry/Login/combo",67,108,130,22);
            combo.raycastTarget=false;
            LegacyUiFactory.PixelLabel(box,ClientConfig.ServerUrl,11,TextAnchor.MiddleLeft,new Color(.2f,.2f,.2f),70,109,102,20);
            var socket=LegacyUiFactory.PixelImage(box,"LegacyVisual/Entry/Login/radio_up",67,139,14,15);
            var http=LegacyUiFactory.PixelImage(box,"LegacyVisual/Entry/Login/radio_selected",147,139,14,15);
            socket.raycastTarget=false; http.raycastTarget=false;
            LegacyUiFactory.PixelLabel(box,"SOCKET",11,TextAnchor.MiddleLeft,Color.white,84,137,56,20);
            LegacyUiFactory.PixelLabel(box,"HTTP",11,TextAnchor.MiddleLeft,Color.white,164,137,45,20);

            var connect=LegacyUiFactory.PixelButton(box,">",202,108,20,24,()=>{},
                "LegacyVisual/Entry/Login/button_up","LegacyVisual/Entry/Login/button_over","LegacyVisual/Entry/Login/button_down");
            connect.GetComponent<Image>().type=Image.Type.Sliced;
            connect.GetComponentInChildren<Text>().fontSize=12;
            var register=LegacyUiFactory.PixelButton(box,"Đăng ký",115,173,50,24,async()=>
            {
                _username=user.text.Trim();_password=pass.text;await Auth(true);
            },"LegacyVisual/Entry/Login/button_up","LegacyVisual/Entry/Login/button_over","LegacyVisual/Entry/Login/button_down");
            register.GetComponent<Image>().type=Image.Type.Sliced;
            ConfigureCompactButtonText(register.GetComponentInChildren<Text>());
            var login=LegacyUiFactory.PixelButton(box,"Đăng nhập",172,173,50,24,async()=>
            {
                _username=user.text.Trim();_password=pass.text;await Auth(false);
            },"LegacyVisual/Entry/Login/button_up","LegacyVisual/Entry/Login/button_over","LegacyVisual/Entry/Login/button_down");
            login.GetComponent<Image>().type=Image.Type.Sliced;
            ConfigureCompactButtonText(login.GetComponentInChildren<Text>());
            _status=LegacyUiFactory.PixelLabel(_screen,"",13,TextAnchor.MiddleCenter,new Color(1f,.82f,.35f),430,497,420,24);
            user.Select();
        }

        async Task Auth(bool register)
        {
            if (_busy) return; _busy=true; SetStatus("Đang kết nối...");
            try
            {
                var auth = register ? await _api.RegisterAsync(_username,_password) : await _api.LoginAsync(_username,_password);
                _api.Token=auth.token; PlayerPrefs.SetString("ctxd.session",auth.token); PlayerPrefs.Save();
                await RouteAfterAuth(auth.player);
            }
            catch(Exception ex) { SetStatus(ex.Message); }
            finally { _busy=false; }
        }

        async Task RouteAfterAuth(PlayerView player)
        {
            await EnsureRealtime();
            if (player.forceId == 0) { ShowForceSelection(); return; }
            await LoadMainCity();
        }

        void ShowForceSelection()
        {
            LegacyUiFactory.DestroyChildren(_screen);
            _screen.GetComponent<Image>().color=Color.black;
            _loginUserInput=null; _loginPasswordInput=null;
            _selectedForceId=1;
            Array.Clear(_forceVisuals,0,_forceVisuals.Length);
            Array.Clear(_forceHovered,0,_forceHovered.Length);
            Array.Clear(_forcePressed,0,_forcePressed.Length);
            var baseImage=LegacyUiFactory.PixelImage(_screen,"LegacyVisual/Entry/Force/scene_up",0,0,1280,768);
            baseImage.raycastTarget=false;

            // FFDec preserves each symbol's common timeline canvas. These top-left offsets are
            // the exact exported-canvas origins resolved from the SWF symbol bounds.
            AddForceChoice(1,"wei",405,12,915,540,502.95f,111.5f,534,340);
            AddForceChoice(2,"shu",17,105,723,562,196.3f,173.55f,470,422);
            AddForceChoice(3,"wu",482,193,831,530,582.8f,349.95f,513,236);
            RefreshForceVisuals();

            var start=LegacyUiFactory.PixelImage(_screen,"LegacyVisual/Entry/Force/start_up",489,606,313,163);
            start.raycastTarget=false;
            Action<string> setStartState=state=>start.sprite=Resources.Load<Sprite>("LegacyVisual/Entry/Force/start_"+state);
            var startHit=LegacyUiFactory.PixelImage(_screen,"LegacyVisual/Entry/Force/start_down",489,606,313,163);
            startHit.color=new Color(1,1,1,.001f);
            startHit.alphaHitTestMinimumThreshold=.1f;
            var startPointer=startHit.gameObject.AddComponent<LegacyForceHitArea>();
            startPointer.Setup(
                ()=>setStartState("over"),
                ()=>setStartState("up"),
                ()=>setStartState("down"),
                ()=>setStartState("over"),
                async()=>await ChooseSelectedForce());
            _status=LegacyUiFactory.PixelLabel(_screen,"",14,TextAnchor.MiddleCenter,new Color(1f,.8f,.35f),430,736,420,25);
        }

        async Task ChooseSelectedForce()
        {
            if(_busy)return;
            _busy=true;
            try
            {
                SetStatus("Đang vào thành...");
                await _api.ChooseForceAsync(_selectedForceId);
                await LoadMainCity();
            }
            catch(Exception ex){SetStatus(ex.Message);}
            finally{_busy=false;}
        }

        static void ConfigureCompactButtonText(Text text)
        {
            text.fontSize=11;
            text.resizeTextForBestFit=true;
            text.resizeTextMinSize=7;
            text.resizeTextMaxSize=11;
            text.horizontalOverflow=HorizontalWrapMode.Overflow;
        }

        void AddForceChoice(int id,string key,float x,float y,float width,float height,float hitX,float hitY,float hitWidth,float hitHeight)
        {
            var visual=LegacyUiFactory.PixelImage(_screen,"LegacyVisual/Entry/Force/"+key+"_selected",x,y,width,height);
            visual.raycastTarget=false;
            _forceVisuals[id]=visual;
            var hit=LegacyUiFactory.PixelImage(_screen,"LegacyVisual/Entry/Force/"+key+"_hit",hitX,hitY,hitWidth,hitHeight);
            hit.color=new Color(1,1,1,0.001f);
            hit.alphaHitTestMinimumThreshold=.1f;
            var pointer=hit.gameObject.AddComponent<LegacyForceHitArea>();
            pointer.Setup(
                ()=>{_forceHovered[id]=true;RefreshForceVisuals();},
                ()=>{_forceHovered[id]=false;_forcePressed[id]=false;RefreshForceVisuals();},
                ()=>{_forcePressed[id]=true;RefreshForceVisuals();},
                ()=>{_forcePressed[id]=false;RefreshForceVisuals();},
                ()=>{_selectedForceId=id;RefreshForceVisuals();});
        }

        void RefreshForceVisuals()
        {
            var keys=new[]{"","wei","shu","wu"};
            for(var id=1;id<=3;id++)
            {
                var image=_forceVisuals[id];
                if(image==null)continue;
                var selected=id==_selectedForceId;
                var visible=selected||_forceHovered[id];
                image.enabled=visible;
                if(!visible)continue;
                var state=selected
                    ?(_forcePressed[id]?"selected_down":_forceHovered[id]?"selected_over":"selected")
                    :(_forcePressed[id]?"down":"over");
                image.sprite=Resources.Load<Sprite>("LegacyVisual/Entry/Force/"+keys[id]+"_"+state);
            }
        }

        async Task LoadMainCity()
        {
            try { _city = await _api.GetMainCityAsync(); ShowMainCity(); }
            catch(Exception ex) { ShowLogin(); SetStatus(ex.Message); }
        }

        void ShowMainCity()
        {
            LegacyUiFactory.DestroyChildren(_screen);
            LegacyUiFactory.ResourceImage(_screen,"LegacyVisual/MainCity/legacy_maincity_regional_00001",Vector2.zero,Vector2.one);

            var top = LegacyUiFactory.Panel(_screen,"ResourceBar",new Vector2(0,.925f),new Vector2(1,1),new Color(.035f,.025f,.018f,.91f));
            var r=_city.resources; var p=_city.player;
            LegacyUiFactory.Label(top,$"{(string.IsNullOrEmpty(p.name)?"Chưa đặt tên":p.name)}  Lv.{p.level}  Xây:{p.constructionSlots}",18,TextAnchor.MiddleLeft,new Color(1f,.84f,.45f),new Vector2(.015f,0),new Vector2(.22f,1));
            LegacyUiFactory.Label(top,$"Bạc {r.copper}/{r.copperMax}  +{r.copperPerHour}/h",16,TextAnchor.MiddleCenter,Color.white,new Vector2(.20f,0),new Vector2(.40f,1));
            LegacyUiFactory.Label(top,$"Gỗ {r.wood}/{r.woodMax}  +{r.woodPerHour}/h",16,TextAnchor.MiddleCenter,Color.white,new Vector2(.39f,0),new Vector2(.58f,1));
            LegacyUiFactory.Label(top,$"Lương {r.food}/{r.foodMax}  +{r.foodPerHour}/h",16,TextAnchor.MiddleCenter,Color.white,new Vector2(.57f,0),new Vector2(.77f,1));
            LegacyUiFactory.Label(top,$"Sắt {r.iron}/{r.ironMax}  +{r.ironPerHour}/h",16,TextAnchor.MiddleCenter,Color.white,new Vector2(.76f,0),new Vector2(.96f,1));

            var side=LegacyUiFactory.Panel(_screen,"BuildingList",new Vector2(.77f,.08f),new Vector2(.99f,.90f),new Color(.035f,.025f,.018f,.78f));
            LegacyUiFactory.Label(side,"CÔNG TRÌNH",20,TextAnchor.MiddleCenter,new Color(1f,.82f,.35f),new Vector2(.05f,.91f),new Vector2(.95f,.99f));
            BuildBuildingRows(side);
            _status=LegacyUiFactory.Label(_screen,TaskText(),15,TextAnchor.MiddleLeft,new Color(1f,.88f,.55f),new Vector2(.015f,.005f),new Vector2(.58f,.06f));
            if (HasFunction(p, 44) || HasFunction(p, 45))
                LegacyUiFactory.Button(_screen,"Tửu Quán",new Vector2(.59f,.008f),new Vector2(.68f,.058f),()=>OpenTavern());
            if (HasFunction(p, 18) || HasFunction(p, 17))
                LegacyUiFactory.Button(_screen,"Trang bị",new Vector2(.685f,.008f),new Vector2(.765f,.058f),()=>OpenEquipment());
            if (HasFunction(p, 19))
                LegacyUiFactory.Button(_screen,"Khoa Kỹ",new Vector2(.77f,.008f),new Vector2(.845f,.058f),()=>OpenTechnology());
            if (HasFunction(p, 10))
                LegacyUiFactory.Button(_screen,"Thế Giới",new Vector2(.85f,.008f),new Vector2(.94f,.058f),()=>OpenWorld());
            if (HasFunction(p, 10))
                LegacyUiFactory.Button(_screen,"Quốc Gia",new Vector2(.65f,.865f),new Vector2(.76f,.915f),()=>NationPanel.Open(_screen,_api,SetStatus));

            LegacyUiFactory.Button(_screen,"Mail",new Vector2(.77f,.865f),new Vector2(.84f,.915f),()=>MailPanel.Open(_screen,_api,SetStatus));
            if(HasFunction(p,27)) LegacyUiFactory.Button(_screen,"Market",new Vector2(.85f,.865f),new Vector2(.94f,.915f),()=>MarketPanel.Open(_screen,_api,SetStatus));
            LegacyUiFactory.Button(_screen,"Chat",new Vector2(.55f,.865f),new Vector2(.63f,.915f),()=>ChatPanel.Open(_screen,_api,SetStatus));
            if(HasFunction(p,59)) LegacyUiFactory.Button(_screen,"Team",new Vector2(.38f,.865f),new Vector2(.46f,.915f),()=>TeamPanel.Open(_screen,_api,SetStatus));
            if(HasFunction(p,39)) LegacyUiFactory.Button(_screen,"Gift",new Vector2(.47f,.865f),new Vector2(.54f,.915f),()=>OnlineGiftPanel.Open(_screen,_api,SetStatus));
            if(HasFunction(p,38)) LegacyUiFactory.Button(_screen,"Daily",new Vector2(.30f,.865f),new Vector2(.37f,.915f),()=>DailyGiftPanel.Open(_screen,_api,SetStatus));
            LegacyUiFactory.Button(_screen,"EXP Event",new Vector2(.20f,.865f),new Vector2(.29f,.915f),()=>BattleExpActivityPanel.Open(_screen,_api,SetStatus));
            LegacyUiFactory.Button(_screen,"Level Event",new Vector2(.10f,.865f),new Vector2(.19f,.915f),()=>LevelExpActivityPanel.Open(_screen,_api,SetStatus));
            LegacyUiFactory.Button(_screen,"Seasonal",new Vector2(.01f,.865f),new Vector2(.095f,.915f),()=>SeasonalActivityPanel.Open(_screen,_api,SetStatus));
            LegacyUiFactory.Button(_screen,"VIP",new Vector2(.01f,.805f),new Vector2(.095f,.855f),()=>VipPanel.Open(_screen,_api,SetStatus));
            LegacyUiFactory.Button(_screen,"KFWD",new Vector2(.105f,.805f),new Vector2(.19f,.855f),()=>KfwdPanel.Open(_screen,_api,SetStatus));
            LegacyUiFactory.Button(_screen,"KFZB",new Vector2(.2f,.805f),new Vector2(.285f,.855f),()=>KfzbPanel.Open(_screen,_api,SetStatus));
            LegacyUiFactory.Button(_screen,"Xếp hạng",new Vector2(.295f,.805f),new Vector2(.38f,.855f),()=>RankPanel.Open(_screen,_api,SetStatus));
            if(p.canChooseName && p.currentTaskId==8) ShowCreateRoleOverlay();
        }

        string TaskText()
        {
            switch(_city.player.currentTaskId)
            {
                case 1:return "Nhiệm vụ: Chọn thế lực";
                case 2:return "Nhiệm vụ: Nâng Dân Cư 1 lên cấp 2";
                case 3:return "Nhiệm vụ: Nâng Dân Cư 1 lên cấp 3";
                case 4:return "Nhiệm vụ: Nâng Dân Cư 2 lên cấp 2";
                case 8:return "Nhiệm vụ: Tôn Tính Đại Danh";
                case 64:return "Nhiệm vụ: Chú tư Nhật Lý Vạn Cơ";
                case 65:return "Nhiệm vụ: Nghiên cứu thành công Nhật Lý Vạn Cơ";
                case 71:return "Nhiệm vụ: Chú tư Lịch Luyện 1";
                case 72:return "Nhiệm vụ: Nghiên cứu Lịch Luyện 1";
                case 74:return "Nhiệm vụ: Chú tư Binh Chủng Thăng Cấp 1";
                case 75:return "Nhiệm vụ: Nghiên cứu Binh Chủng Thăng Cấp 1";
                default:return "Nhiệm vụ hiện tại: " + _city.player.currentTaskId;
            }
        }

        void BuildBuildingRows(Transform parent)
        {
            var items = _city.buildings ?? Array.Empty<BuildingView>();
            var max=Math.Min(items.Length,9);
            for(var i=0;i<max;i++)
            {
                var b=items[i];
                var top=.88f-i*.092f; var bottom=top-.082f;
                var text=b.state==1 ? $"{b.name} Lv.{b.level}  (đang xây)" : $"{b.name} Lv.{b.level}";
                LegacyUiFactory.Button(parent,text,new Vector2(.04f,bottom),new Vector2(.96f,top),()=>ShowBuildingInfo(b));
            }
        }

        void ShowBuildingInfo(BuildingView b)
        {
            var overlay=LegacyUiFactory.Panel(_screen,"BuildingInfo",new Vector2(.29f,.29f),new Vector2(.70f,.60f),new Color(.035f,.025f,.018f,.95f));
            LegacyUiFactory.Label(overlay,$"{b.name}  Lv.{b.level}",23,TextAnchor.MiddleCenter,new Color(1f,.82f,.35f),new Vector2(.05f,.76f),new Vector2(.95f,.96f));
            LegacyUiFactory.Label(overlay,$"Sản lượng: {b.outputPerHour}/giờ\nNâng cấp cần: Bạc {b.nextCopperCost} · Gỗ {b.nextWoodCost}\nThời gian: {Math.Max(0,b.nextDurationMs)/1000f:0.#} giây",17,TextAnchor.MiddleLeft,Color.white,new Vector2(.08f,.28f),new Vector2(.92f,.73f));
            LegacyUiFactory.SpriteButton(overlay,"",new Vector2(.24f,.05f),new Vector2(.38f,.24f),async()=>
            {
                if(_busy)return; _busy=true;
                try { await _api.UpgradeAsync(b.id); Destroy(overlay.gameObject); await LoadMainCity(); }
                catch(Exception ex){SetStatus(ex.Message);} finally{_busy=false;}
            },"LegacyVisual/MainCity/UI/00053","LegacyVisual/MainCity/UI/00055","LegacyVisual/MainCity/UI/00057");
            LegacyUiFactory.Label(overlay,"Nâng cấp",15,TextAnchor.MiddleLeft,new Color(1f,.86f,.5f),new Vector2(.39f,.05f),new Vector2(.58f,.24f));
            LegacyUiFactory.Button(overlay,"Đóng",new Vector2(.60f,.07f),new Vector2(.83f,.24f),()=>Destroy(overlay.gameObject));
        }

        void ShowCreateRoleOverlay()
        {
            var modal=LegacyUiFactory.Panel(_screen,"CreateRoleModal",Vector2.zero,Vector2.one,new Color(0,0,0,.4f));
            var overlay=LegacyUiFactory.PixelPanel(modal,"CreateRole",309,164,662,440,Color.clear);
            var background=LegacyUiFactory.PixelImage(overlay,"LegacyVisual/CreateRole/00002",0,0,662,440);
            background.raycastTarget=false;

            var portrait=LegacyUiFactory.PixelImage(overlay,"LegacyVisual/Entry/CreateRole/Big/1",8,71,273,345);
            portrait.raycastTarget=false;
            var selectedPic=1;
            var selectedFrames=new Image[7];
            var lights=new Image[7];
            Action<int> selectRole=null;
            selectRole=pic=>
            {
                selectedPic=pic;
                var widths=new[]{0,273,247,263,290,305,320};
                var heights=new[]{0,345,338,342,344,395,398};
                var ys=new[]{0,71,78,74,71,21,18};
                portrait.sprite=Resources.Load<Sprite>("LegacyVisual/Entry/CreateRole/Big/"+pic);
                portrait.rectTransform.anchoredPosition=new Vector2(8,-ys[pic]);
                portrait.rectTransform.sizeDelta=new Vector2(widths[pic],heights[pic]);
                for(var j=1;j<=6;j++)
                {
                    selectedFrames[j].enabled=j==pic;
                    lights[j].enabled=j!=pic;
                }
            };
            for(var i=1;i<=6;i++)
            {
                var pic=i;
                var x=344+((i-1)%3)*100;
                var y=53+((i-1)/3)*100;
                var frame=LegacyUiFactory.PixelImage(overlay,"LegacyVisual/Entry/CreateRole/thumb_frame",x,y,100,100);
                var head=LegacyUiFactory.PixelImage(overlay,"LegacyVisual/Entry/CreateRole/Thumb/"+i,x+14,y+14,72,72);
                var selected=LegacyUiFactory.PixelImage(overlay,"LegacyVisual/Entry/CreateRole/thumb_selected",x,y,100,100);
                var light=LegacyUiFactory.PixelImage(overlay,"LegacyVisual/Entry/CreateRole/thumb_light",x,y,100,100);
                frame.raycastTarget=false;head.raycastTarget=false;selected.raycastTarget=false;light.raycastTarget=false;
                selectedFrames[i]=selected;lights[i]=light;
                var hit=LegacyUiFactory.PixelButton(overlay,"",x,y,100,100,()=>selectRole(pic));
                hit.image.color=Color.clear;
            }

            LegacyUiFactory.PixelLabel(overlay,"Vui lòng nhập tên nhân vật",12,TextAnchor.MiddleCenter,new Color(.8f,.73f,.53f),347,270,280,40);
            var name=LegacyUiFactory.PixelInput(overlay,"RoleName",400,300,150,20,Color.clear,new Color(1f,1f,.8f),14,false,false);
            name.characterLimit=14;
            var inputTask=LegacyUiFactory.PixelImage(overlay,"LegacyVisual/Entry/CreateRole/InputTask/01",247,289,150,39);
            inputTask.raycastTarget=false;
            var inputTaskAnimation=inputTask.gameObject.AddComponent<LegacyFrameLoop>();
            inputTaskAnimation.Initialize(inputTask,"LegacyVisual/Entry/CreateRole/InputTask",20,24f);
            Action hideInputTask=inputTaskAnimation.StopAndHide;
            name.gameObject.AddComponent<LegacyPointerClick>().Initialize(hideInputTask);
            LegacyUiFactory.PixelButton(overlay,"",556,294,25,26,async()=>
            {
                hideInputTask();
                if(_busy)return; _busy=true;
                try { var list=await _api.RandomNamesAsync(selectedPic>=4,5); if(list.list!=null&&list.list.Length>0) name.text=list.list[UnityEngine.Random.Range(0,list.list.Length)]; }
                catch(Exception ex){SetStatus(ex.Message);} finally{_busy=false;}
            },"LegacyVisual/CreateRole/00005");

            _status=LegacyUiFactory.PixelLabel(overlay,"",13,TextAnchor.MiddleCenter,new Color(1f,.75f,.45f),340,397,290,25);
            LegacyUiFactory.PixelButton(overlay,"",395,338,191,53,async()=>
            {
                if(_busy)return; _busy=true;
                try { await _api.SetNameAsync(name.text.Trim(),selectedPic); Destroy(modal.gameObject); await LoadMainCity(); }
                catch(Exception ex){SetStatus(ex.Message);} finally{_busy=false;}
            },"LegacyVisual/CreateRole/00012","LegacyVisual/CreateRole/00014","LegacyVisual/CreateRole/00016");
            selectRole(UnityEngine.Random.Range(1,7));
            name.Select();
        }


        static bool HasFunction(PlayerView player, int id) => player?.functionIds != null && Array.IndexOf(player.functionIds, id) >= 0;

        void OpenTavern()
        {
            if (_city?.player == null) return;
            TavernPanel.Open(_screen, _api, _city.player, SetStatus, async () => { await LoadMainCity(); });
        }

        void OpenEquipment()
        {
            if (_city?.player == null) return;
            EquipmentPanel.Open(_screen, _api, _city.player, SetStatus, async () => { await LoadMainCity(); });
        }

        void OpenTechnology()
        {
            if (_city?.player == null) return;
            TechnologyPanel.Open(_screen, _api, SetStatus, async () => { await LoadMainCity(); });
        }

        void OpenWorld()
        {
            if (_city?.player == null) return;
            WorldPanel.Open(_screen, _api, SetStatus);
        }

        void SetStatus(string value) { if(_status!=null) _status.text=value; }
    }

    sealed class LegacyForceHitArea : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler,
        IPointerDownHandler, IPointerUpHandler, IPointerClickHandler
    {
        Action _enter;
        Action _exit;
        Action _down;
        Action _up;
        Action _click;

        public void Setup(Action enter,Action exit,Action down,Action up,Action click)
        {
            _enter=enter;_exit=exit;_down=down;_up=up;_click=click;
        }

        public void OnPointerEnter(PointerEventData eventData)=>_enter?.Invoke();
        public void OnPointerExit(PointerEventData eventData)=>_exit?.Invoke();
        public void OnPointerDown(PointerEventData eventData){if(eventData.button==PointerEventData.InputButton.Left)_down?.Invoke();}
        public void OnPointerUp(PointerEventData eventData){if(eventData.button==PointerEventData.InputButton.Left)_up?.Invoke();}
        public void OnPointerClick(PointerEventData eventData){if(eventData.button==PointerEventData.InputButton.Left)_click?.Invoke();}
    }

    sealed class LegacyPointerClick : MonoBehaviour, IPointerClickHandler
    {
        Action _click;
        public void Initialize(Action click) { _click=click; }
        public void OnPointerClick(PointerEventData eventData) { _click?.Invoke(); }
    }

    sealed class LegacyFrameLoop : MonoBehaviour
    {
        Image _image;
        Sprite[] _frames;
        float _frameRate;
        float _startedAt;

        public void Initialize(Image image,string resourceFolder,int frameCount,float frameRate)
        {
            _image=image;
            _frames=new Sprite[frameCount];
            for(var i=0;i<frameCount;i++)
                _frames[i]=Resources.Load<Sprite>($"{resourceFolder}/{i+1:00}");
            _frameRate=frameRate;
            _startedAt=Time.unscaledTime;
            if(_frames.Length>0)_image.sprite=_frames[0];
        }

        void Update()
        {
            if(_image==null||_frames==null||_frames.Length==0)return;
            var frame=(int)((Time.unscaledTime-_startedAt)*_frameRate)%_frames.Length;
            _image.sprite=_frames[frame];
        }

        public void StopAndHide()
        {
            enabled=false;
            if(_image!=null)_image.enabled=false;
        }
    }
}
