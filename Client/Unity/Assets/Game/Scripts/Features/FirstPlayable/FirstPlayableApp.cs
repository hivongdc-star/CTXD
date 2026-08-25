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
using CTXD.Client.Networking;
using UnityEngine;
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

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Bootstrap()
        {
            if (FindFirstObjectByType<FirstPlayableApp>() == null)
                new GameObject("CTXD_FirstPlayable").AddComponent<FirstPlayableApp>();
        }

        void Update()
        {
            if (_realtime == null) return;
            while (_realtime.TryDequeue(out var message))
            {
                if (!_refreshQueued && message.Contains("\"type\":\"maincity.updated\""))
                {
                    _refreshQueued = true;
                    _ = RefreshFromPush();
                }
                if (message.Contains("\"type\":\"world.updated\"")) WorldPanel.RefreshOpenFromPush();
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
            LegacyUiFactory.ResourceImage(_screen, "LegacyVisual/RoleScene/background", Vector2.zero, Vector2.one);
            var box = LegacyUiFactory.Panel(_screen, "LoginBox", new Vector2(.36f,.27f), new Vector2(.64f,.68f), new Color(.04f,.025f,.015f,.84f));
            LegacyUiFactory.Label(box, "CÔNG THÀNH XƯNG ĐẾ", 28, TextAnchor.MiddleCenter, new Color(1f,.82f,.35f), new Vector2(.05f,.79f), new Vector2(.95f,.98f));
            var user = LegacyUiFactory.Input(box, "Tài khoản", new Vector2(.12f,.58f), new Vector2(.88f,.7f));
            var pass = LegacyUiFactory.Input(box, "Mật khẩu", new Vector2(.12f,.42f), new Vector2(.88f,.54f), true);
            user.text = _username; pass.text = _password;
            _status = LegacyUiFactory.Label(box, "", 15, TextAnchor.MiddleCenter, new Color(1f,.75f,.45f), new Vector2(.05f,.05f), new Vector2(.95f,.19f));
            LegacyUiFactory.SpriteButton(box, "", new Vector2(.12f,.23f), new Vector2(.48f,.36f), async () =>
            {
                _username=user.text.Trim(); _password=pass.text; await Auth(false);
            }, "LegacyVisual/RoleScene/enter", "LegacyVisual/RoleScene/enter_over", "LegacyVisual/RoleScene/enter_down");
            LegacyUiFactory.Button(box, "Tạo tài khoản", new Vector2(.52f,.23f), new Vector2(.88f,.36f), async () =>
            {
                _username=user.text.Trim(); _password=pass.text; await Auth(true);
            });
            LegacyUiFactory.Label(_screen, "Server: " + ClientConfig.ServerUrl, 13, TextAnchor.LowerRight, new Color(1,1,1,.65f), new Vector2(.55f,.005f), new Vector2(.995f,.045f));
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
            LegacyUiFactory.ResourceImage(_screen, "LegacyVisual/RoleScene/background", Vector2.zero, Vector2.one);
            var panel = LegacyUiFactory.Panel(_screen,"ForceSelection",new Vector2(.18f,.18f),new Vector2(.82f,.82f),new Color(.04f,.025f,.015f,.74f));
            LegacyUiFactory.Label(panel,"LỰA CHỌN THẾ LỰC",30,TextAnchor.MiddleCenter,new Color(1f,.83f,.38f),new Vector2(.05f,.78f),new Vector2(.95f,.96f));
            LegacyUiFactory.Label(panel,"Giữ đúng bước mở đầu của legacy: chọn phe trước khi vào thành.",17,TextAnchor.MiddleCenter,Color.white,new Vector2(.08f,.65f),new Vector2(.92f,.76f));
            MakeForceButton(panel,1,"Ngụy",.16f,.38f);
            MakeForceButton(panel,2,"Thục",.40f,.62f);
            MakeForceButton(panel,3,"Ngô",.64f,.86f);
            _status=LegacyUiFactory.Label(panel,"",16,TextAnchor.MiddleCenter,new Color(1f,.72f,.4f),new Vector2(.1f,.08f),new Vector2(.9f,.2f));
        }

        void MakeForceButton(Transform parent,int id,string name,float x1,float x2)
        {
            LegacyUiFactory.Button(parent,name,new Vector2(x1,.35f),new Vector2(x2,.58f),async()=>
            {
                if(_busy)return; _busy=true;
                try { SetStatus("Đang vào thành..."); await _api.ChooseForceAsync(id); await LoadMainCity(); }
                catch(Exception ex){SetStatus(ex.Message);} finally{_busy=false;}
            });
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
            var overlay=LegacyUiFactory.Panel(_screen,"CreateRole",new Vector2(.241f,.206f),new Vector2(.759f,.782f),Color.clear);
            LegacyUiFactory.ResourceImage(overlay,"LegacyVisual/CreateRole/00002",Vector2.zero,Vector2.one);
            LegacyUiFactory.Label(overlay,"TÔN TÍNH ĐẠI DANH",23,TextAnchor.MiddleCenter,new Color(1f,.82f,.35f),new Vector2(.08f,.83f),new Vector2(.92f,.98f));

            LegacyUiFactory.ResourceImage(overlay,"LegacyVisual/CreateRole/00009",new Vector2(.08f,.47f),new Vector2(.23f,.70f),true);
            var portrait=LegacyUiFactory.ResourceImage(overlay,"LegacyVisual/RolePortraits/Big/1",new Vector2(.075f,.38f),new Vector2(.245f,.77f),true);
            portrait.color=Color.white;

            var name=LegacyUiFactory.Input(overlay,"Tên nhân vật",new Vector2(.38f,.56f),new Vector2(.82f,.68f));
            var dice=LegacyUiFactory.SpriteButton(overlay,"",new Vector2(.835f,.575f),new Vector2(.88f,.655f),async()=>
            {
                if(_busy)return; _busy=true;
                try { var list=await _api.RandomNamesAsync(true,5); if(list.list!=null&&list.list.Length>0) name.text=list.list[UnityEngine.Random.Range(0,list.list.Length)]; }
                catch(Exception ex){SetStatus(ex.Message);} finally{_busy=false;}
            },"LegacyVisual/CreateRole/00005");

            _status=LegacyUiFactory.Label(overlay,"",14,TextAnchor.MiddleCenter,new Color(1f,.75f,.45f),new Vector2(.34f,.19f),new Vector2(.92f,.31f));
            LegacyUiFactory.SpriteButton(overlay,"",new Vector2(.47f,.30f),new Vector2(.76f,.43f),async()=>
            {
                if(_busy)return; _busy=true;
                try { await _api.SetNameAsync(name.text.Trim(),1); Destroy(overlay.gameObject); await LoadMainCity(); }
                catch(Exception ex){SetStatus(ex.Message);} finally{_busy=false;}
            },"LegacyVisual/CreateRole/00012","LegacyVisual/CreateRole/00014","LegacyVisual/CreateRole/00016");
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
}
