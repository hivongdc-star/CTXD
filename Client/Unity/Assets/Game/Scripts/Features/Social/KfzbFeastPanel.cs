using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CTXD.Client.Features.FirstPlayable;
using CTXD.Client.Networking;
using UnityEngine;
using UnityEngine.UI;

namespace CTXD.Client.Features.Social
{
    public sealed class KfzbFeastPanel : MonoBehaviour
    {
        const string RulesText = "1. Người chơi trong top 16 mở tiệc, kéo dài 1 ngày.\n2. Toàn bộ người chơi đều có thể dùng Thiệp Mời tham dự tiệc, có thể nhận Điểm. Mỗi lần tốn 1 tấm.\n3. Vào phòng tiệc đợi 1 thời gian sẽ tự động mở tiệc, nếu không đủ 10 người sẽ giải tán.\n4. Vào phòng tiệc có rượu sẽ nhận được nhiều Điểm hơn.\n5. Sau khi mở tiệc, dựa vào quốc gia của người dự tiệc có thể kích hoạt phần thưởng đặc biệt.\n6. Người chơi mở tiệc có thể mua rượu, số lượng rượu tùy theo số người dự tiệc, dù tiệc giải tán vẫn tốn rượu.\n7. Tiệc kết thúc, dựa vào số người tham dự sẽ chọn ra chủ tiệc được mến mộ nhất.\n8. Người chơi cùng quốc gia, cùng server với chủ tiệc được mến mộ nhất đều nhận được phần thưởng.";
        const float LegacyFrameRate = 25f;

        static readonly Vector2[] OrganizerPoints =
        {
            new Vector2(513,183), new Vector2(160,350), new Vector2(836,193), new Vector2(1221,363)
        };

        static readonly Vector2[] ParticipantPoints =
        {
            new Vector2(236,435),new Vector2(702,184),new Vector2(365,496),new Vector2(840,246),new Vector2(488,560),
            new Vector2(958,310),new Vector2(615,626),new Vector2(1090,377),new Vector2(760,698),new Vector2(1216,440)
        };

        sealed class LoopVisual
        {
            public Image image;
            public string sheet;
            public int frameCount;
            public int holdFrames;
            public int cellWidth;
            public int cellHeight;
            public int lastFrame = -1;
        }

        static KfzbFeastPanel _open;
        readonly List<LoopVisual> _loopVisuals = new();
        ApiClient _api;
        Action<string> _status;
        RectTransform _window;
        PlayerView _player;
        KfzbFeastPublicInfoView _info;
        KfzbFeastCurrentRoomInfoView _room;
        Text _countdown;
        Text _resultLeaveCountdown;
        Image _resultTitle;
        Image _resultFx;
        float _resultVisualStartedAt = -1f;
        long _resultVisualRoomId;
        int _floor = 1;
        int _selectedRank;
        int _pendingBuyType;
        int _buyThenJoinRank;
        bool _showRules;
        bool _refreshing;
        float _nextPoll;
        int _pushPending;
        bool _expiryRefreshQueued;
        bool _pendingBuyDrink;
        float _roomCountdownSyncedAt;
        long _roomCountdownMs;

        public static KfzbFeastPanel Open(RectTransform host, ApiClient api, Action<string> status)
        {
            if (_open != null)
            {
                _ = _open.Refresh();
                return _open;
            }
            var go = new GameObject("KfzbFeastPanel");
            go.transform.SetParent(host, false);
            var panel = go.AddComponent<KfzbFeastPanel>();
            _open = panel;
            panel._api = api;
            panel._status = status;
            panel.Build();
            RealtimeClient.MessageObserved += panel.OnRealtimeMessage;
            _ = panel.Refresh();
            return panel;
        }

        public static void RefreshOpenFromPush()
        {
            if (_open != null) Interlocked.Exchange(ref _open._pushPending, 1);
        }

        void Build()
        {
            var blocker = LegacyUiFactory.Panel(transform, "BanquetSceneBlocker", Vector2.zero, Vector2.one, new Color(0,0,0,.96f));
            _window = LegacyUiFactory.PixelPanel(blocker, "BanquetScene", 0, 0, 1280, 768, Color.black);
        }

        void OnDestroy()
        {
            RealtimeClient.MessageObserved -= OnRealtimeMessage;
            if (_open == this) _open = null;
        }

        void OnRealtimeMessage(string message)
        {
            if (!string.IsNullOrEmpty(message) && message.Contains("\"type\":\"kfzb.feast\""))
                Interlocked.Exchange(ref _pushPending, 1);
        }

        void Update()
        {
            if (Interlocked.Exchange(ref _pushPending, 0) != 0) _ = Refresh();
            if (Time.unscaledTime >= _nextPoll)
            {
                _nextPoll = Time.unscaledTime + 5f;
                _ = Refresh(false);
            }
            UpdateCountdown();
            UpdateLegacyAnimations();
            UpdateResultVisual();
        }

        async Task Refresh(bool showError = true)
        {
            if (_refreshing) return;
            _refreshing = true;
            try
            {
                if (_player == null) _player = await _api.GetPlayerAsync();
                var previousRoomId = _room != null ? _room.roomId : 0;
                var previousResult = _room != null && _room.result;
                _info = await _api.GetKfzbFeastInfoAsync();
                var current = _info != null ? _info.currentRoom : null;
                _room = current != null && (_info.inRoom || current.result) ? current : null;
                if (_room != null && _room.state == 1)
                {
                    _roomCountdownMs = Math.Max(0L, _room.cd);
                    _roomCountdownSyncedAt = Time.unscaledTime;
                }
                else
                {
                    _roomCountdownMs = 0;
                    _roomCountdownSyncedAt = 0f;
                }
                if (_room != null && _room.result)
                {
                    if (_room.roomId != previousRoomId || !previousResult || _resultVisualStartedAt < 0f)
                    {
                        _resultVisualRoomId = _room.roomId;
                        _resultVisualStartedAt = Time.unscaledTime;
                    }
                }
                else
                {
                    _resultVisualRoomId = 0;
                    _resultVisualStartedAt = -1f;
                }
                _expiryRefreshQueued = false;
                Draw();
            }
            catch (ApiException ex) when (ex.Code == "KFZB_FEAST_CLOSED" || ex.Code == "KFZB_INACTIVE")
            {
                if (showError) _status(ex.Message);
                Destroy(gameObject);
            }
            catch (Exception ex)
            {
                if (showError) _status(ex.Message);
            }
            finally
            {
                _refreshing = false;
                _nextPoll = Time.unscaledTime + 5f;
            }
        }

        void Draw()
        {
            LegacyUiFactory.DestroyChildren(_window);
            _loopVisuals.Clear();
            _countdown = null;
            _resultLeaveCountdown = null;
            _resultTitle = null;
            _resultFx = null;
            if (_room == null) DrawLobby(); else DrawRoom();
            if (_showRules) DrawRules();
            if (_selectedRank > 0 && _room == null) DrawEnter();
            if (_pendingBuyType > 0 && _room == null) DrawBuyConfirm();
            if (_pendingBuyDrink && _room == null) DrawDrinkConfirm();
            UpdateLegacyAnimations();
            UpdateResultVisual();
        }

        void DrawLobby()
        {
            // banquet.banquetMove.bg frame 1 -> bitmap 475. Frame 2 is also recovered but its AS switch condition is not guessed here.
            BanquetLegacyVisuals.FullImage(_window, "lobby_bg_1", 0, 0, 1280, 768);
            LegacyUiFactory.PixelLabel(_window, "Thịnh Yến", 30, TextAnchor.MiddleCenter, new Color(1f,.82f,.35f), 500, 18, 280, 42);
            LegacyUiFactory.PixelButton(_window, "×", 1218, 18, 42, 32, () => Destroy(gameObject));
            LegacyUiFactory.PixelButton(_window, "Quy tắc tiệc", 1080, 18, 125, 32, () => { _showRules = true; Draw(); });

            DrawBuffIcons();
            DrawHotRooms();
            DrawCardShop();

            // banquet.currentStage frame 1..4, placed at (322,465), has bounds y=-98..0.
            BanquetLegacyVisuals.CellImage(_window, "floor_titles", _floor - 1, 585, 98, 322, 367, 585, 98);
            if (_floor > 1) LegacyUiFactory.PixelButton(_window, "‹", 445, 468, 42, 32, () => { _floor--; Draw(); });
            if (_floor < 4) LegacyUiFactory.PixelButton(_window, "›", 910, 468, 42, 32, () => { _floor++; Draw(); });

            var start = (_floor - 1) * 4 + 1;
            for (var i = 0; i < 4; i++)
            {
                var rank = start + i;
                var point = OrganizerPoints[i];
                var room = FindOrganizer(rank);
                BanquetLegacyVisuals.TopLeftCellImage(_window, "rank_titles", RankTitleIndex(rank), 100, 40, 91, 29, point.x - 66, point.y - 87);
                var organizer = AddLoop("organizer_lobby", point.x - 32, point.y - 64, 64, 80, 16, 3);
                organizer.raycastTarget = room != null;
                if (room != null)
                {
                    var button = organizer.gameObject.AddComponent<Button>();
                    button.targetGraphic = organizer;
                    button.transition = Selectable.Transition.None;
                    var selectedRank = room.pos;
                    button.onClick.AddListener(() => { _selectedRank = selectedRank; Draw(); });
                    LegacyUiFactory.PixelLabel(_window, room.playerName, 14, TextAnchor.MiddleCenter, new Color(1f,.95f,.81f), point.x - 68, point.y + 10, 136, 20).raycastTarget = false;
                    LegacyUiFactory.PixelLabel(_window, "Tổng:" + room.peopleNum + "  " + room.weiNum + "/" + room.shuNum + "/" + room.wuNum, 11, TextAnchor.MiddleCenter, new Color(.91f,.89f,.85f), point.x - 78, point.y + 30, 156, 18).raycastTarget = false;
                }
            }
        }

        void DrawBuffIcons()
        {
            if (_info == null) return;
            BanquetLegacyVisuals.TopLeftCellImage(_window, "item_icons", 0, 80, 80, 27, 31, 20, 12);
            LegacyUiFactory.PixelLabel(_window, "×" + _info.freeCard, 14, TextAnchor.MiddleLeft, new Color(1f,.95f,.81f), 50, 20, 90, 24);
            BanquetLegacyVisuals.TopLeftCellImage(_window, "item_icons", 1, 80, 80, 27, 31, 20, 50);
            LegacyUiFactory.PixelLabel(_window, "×" + _info.goldCard, 14, TextAnchor.MiddleLeft, new Color(1f,.95f,.81f), 50, 58, 90, 24);
            if (_info.drink > 0)
            {
                BanquetLegacyVisuals.TopLeftCellImage(_window, "item_icons", 2, 80, 80, 29, 37, 20, 85);
                LegacyUiFactory.PixelLabel(_window, "×" + _info.drink, 14, TextAnchor.MiddleLeft, new Color(1f,.95f,.81f), 50, 95, 90, 24);
            }
        }

        void DrawHotRooms()
        {
            var hot = _info?.hotRooms ?? Array.Empty<KfzbFeastOrganizerInfoView>();
            // Legacy BanquetScene.xml hotList is one column with three visible rows. Preserve server order; never sort here.
            for (var i = 0; i < Math.Min(3, hot.Length); i++)
            {
                var room = hot[i];
                var y = 160 + i * 30;
                LegacyUiFactory.PixelLabel(_window, "#" + room.pos, 13, TextAnchor.MiddleCenter, new Color(1f,.74f,.11f), 200, y, 111, 20);
                LegacyUiFactory.PixelLabel(_window, room.peopleNum + " người", 13, TextAnchor.MiddleCenter, new Color(1f,.95f,.81f), 330, y, 118, 20);
                LegacyUiFactory.PixelLabel(_window, room.playerName, 13, TextAnchor.MiddleCenter, new Color(1f,.95f,.81f), 450, y, 135, 20);
            }
        }

        void DrawCardShop()
        {
            if (_info == null) return;
            DrawCardShopEntry(230, 1, _info.goldCard1, () => StartBuy(1, 0));
            DrawCardShopEntry(352, 10, _info.goldCard10, () => StartBuy(2, 0));
            DrawDrinkShopEntry(474, _info.goldDrink, _info.isOrganizer && _info.isTop16);
        }

        void DrawCardShopEntry(float x, int count, int gold, Action buy)
        {
            BanquetLegacyVisuals.TopLeftCellImage(_window, "item_icons", 1, 80, 80, 27, 31, x + 13, 258);
            LegacyUiFactory.PixelLabel(_window, "×" + count, 20, TextAnchor.MiddleCenter, Color.yellow, x + 20, 288, 50, 26);
            LegacyUiFactory.PixelButton(_window, "Mua", x, 324, 90, 28, () => buy());
            BanquetLegacyVisuals.TopLeftCellImage(_window, "item_icons", 4, 80, 80, 20, 12, x + 22, 351);
            LegacyUiFactory.PixelLabel(_window, gold.ToString(), 14, TextAnchor.MiddleLeft, new Color(1f,.95f,.81f), x + 43, 349, 58, 20);
        }

        void DrawDrinkShopEntry(float x, int gold, bool enabled)
        {
            var icon = BanquetLegacyVisuals.TopLeftCellImage(_window, "item_icons", 8, 80, 80, 50, 71, x + 13, 252);
            LegacyUiFactory.PixelLabel(_window, "×1", 20, TextAnchor.MiddleCenter, enabled ? Color.yellow : Color.gray, x + 20, 288, 50, 26);
            var buy = LegacyUiFactory.PixelButton(_window, "Mua", x, 324, 90, 28, () => { _pendingBuyDrink = true; Draw(); });
            buy.interactable = enabled;
            if (!enabled && icon != null) icon.color = Color.gray;
            BanquetLegacyVisuals.TopLeftCellImage(_window, "item_icons", 4, 80, 80, 20, 12, x + 22, 351);
            LegacyUiFactory.PixelLabel(_window, gold.ToString(), 14, TextAnchor.MiddleLeft, enabled ? new Color(1f,.95f,.81f) : Color.gray, x + 43, 349, 58, 20);
        }

        void DrawEnter()
        {
            var modal = LegacyUiFactory.PixelPanel(_window, "BanquetEnter", 0, 0, 1280, 768, new Color(0,0,0,.2f));
            // banquet.banquetMove.enterBG -> shape 451; bitmap 450 is rendered to the exact 596x263 shape bounds.
            BanquetLegacyVisuals.FullImage(modal, "enter_bg", 222, 138, 596, 263);
            LegacyUiFactory.PixelButton(modal, "×", 724, 251, 36, 30, () => { _selectedRank = 0; Draw(); });
            var selected = FindOrganizer(_selectedRank);
            LegacyUiFactory.PixelLabel(modal, selected != null ? selected.playerName : "#" + _selectedRank, 18, TextAnchor.MiddleLeft, new Color(1f,.95f,.81f), 406, 281, 320, 20).raycastTarget = false;
            var enterInfo = selected != null && selected.haveDrink > 0
                ? "Tướng quân mời vào! Chủ nhân đã chuẩn bị sẵn mỹ tửu ở bên trong."
                : "Tướng quân mời vào!";
            LegacyUiFactory.PixelLabel(modal, enterInfo, 14, TextAnchor.UpperLeft, new Color(1f,.95f,.81f), 452, 305, 288, 50).raycastTarget = false;

            BanquetLegacyVisuals.TopLeftCellImage(modal, "item_icons", 7, 80, 80, 23, 26, 429, 325);
            var normal = LegacyUiFactory.PixelButton(modal, "", 439, 337, 309, 21, () => Join(_selectedRank, 1));
            ApplyEnterButtonSkin(normal);
            normal.interactable = _info != null && _info.freeCard > 0;
            LegacyUiFactory.PixelLabel(modal, "Vào Tiệc", 15, TextAnchor.MiddleLeft, new Color(.31f,.93f,.22f), 459, 339, 65, 20).raycastTarget = false;
            LegacyUiFactory.PixelLabel(modal, "×" + (_info != null ? _info.freeCard : 0), 15, TextAnchor.MiddleLeft, new Color(.77f,.67f,.49f), 524, 339, 90, 20).raycastTarget = false;

            BanquetLegacyVisuals.TopLeftCellImage(modal, "item_icons", 1, 80, 80, 27, 31, 429, 365);
            var vip = LegacyUiFactory.PixelButton(modal, "", 439, 369, 309, 21, () =>
            {
                if (_info != null && _info.goldCard > 0) Join(_selectedRank, 2);
                else StartBuy(1, _selectedRank);
            });
            ApplyEnterButtonSkin(vip);
            LegacyUiFactory.PixelLabel(modal, "Lối đi VIP", 15, TextAnchor.MiddleLeft, new Color(.31f,.93f,.22f), 459, 373, 80, 20).raycastTarget = false;
            LegacyUiFactory.PixelLabel(modal, "×" + (_info != null ? _info.goldCard : 0), 15, TextAnchor.MiddleLeft, new Color(.77f,.67f,.49f), 544, 373, 90, 20).raycastTarget = false;
        }

        static void ApplyEnterButtonSkin(Button button)
        {
            var image = button.GetComponent<Image>();
            image.sprite = BanquetLegacyVisuals.TopLeftCellSprite("enter_buttons", 0, 320, 24, 309, 21);
            image.type = Image.Type.Simple;
            image.color = Color.white;
            var state = button.spriteState;
            state.highlightedSprite = BanquetLegacyVisuals.TopLeftCellSprite("enter_buttons", 1, 320, 24, 309, 21);
            state.pressedSprite = BanquetLegacyVisuals.TopLeftCellSprite("enter_buttons", 2, 320, 24, 309, 21);
            state.selectedSprite = state.highlightedSprite;
            button.spriteState = state;
        }

        void StartBuy(int type, int joinAfterBuyRank)
        {
            if (_info == null) return;
            _pendingBuyType = type;
            _buyThenJoinRank = joinAfterBuyRank;
            Draw();
        }

        void DrawBuyConfirm()
        {
            var price = _pendingBuyType == 1 ? _info.goldCard1 : _info.goldCard10;
            var count = _pendingBuyType == 1 ? 1 : 10;
            var modal = LegacyUiFactory.PixelPanel(_window, "BanquetBuyConfirm", 0, 0, 1280, 768, new Color(0,0,0,.2f));
            BanquetLegacyVisuals.FullImage(modal, "dialog_bg", 322, 210, 635, 351);
            LegacyUiFactory.PixelLabel(modal, "Bạn chắc chắn dùng " + price + " vàng mua thiệp mời VIP?", 17, TextAnchor.MiddleCenter, Color.white, 475, 320, 380, 70);
            BanquetLegacyVisuals.TopLeftCellImage(modal, "item_icons", 1, 80, 80, 27, 31, 600, 395);
            LegacyUiFactory.PixelLabel(modal, "×" + count, 18, TextAnchor.MiddleLeft, new Color(1f,.82f,.35f), 635, 398, 70, 28);
            LegacyUiFactory.PixelButton(modal, "Mua", 540, 455, 120, 36, () => BuyCards(_pendingBuyType));
            LegacyUiFactory.PixelButton(modal, "×", 680, 455, 120, 36, () => { _pendingBuyType = 0; _buyThenJoinRank = 0; Draw(); });
        }

        void DrawDrinkConfirm()
        {
            if (_info == null || !_info.isOrganizer || !_info.isTop16)
            {
                _pendingBuyDrink = false;
                return;
            }
            var modal = LegacyUiFactory.PixelPanel(_window, "BanquetDrinkConfirm", 0, 0, 1280, 768, new Color(0,0,0,.2f));
            BanquetLegacyVisuals.FullImage(modal, "dialog_bg", 322, 210, 635, 351);
            LegacyUiFactory.PixelLabel(modal, "Bạn chắc chằn dùng " + _info.goldDrink + " vàng mua Nữ Nhi Hồng", 17, TextAnchor.MiddleCenter, Color.white, 475, 320, 380, 70);
            BanquetLegacyVisuals.TopLeftCellImage(modal, "item_icons", 8, 80, 80, 50, 71, 595, 385);
            LegacyUiFactory.PixelButton(modal, "Mua", 540, 455, 120, 36, () => BuyDrink());
            LegacyUiFactory.PixelButton(modal, "×", 680, 455, 120, 36, () => { _pendingBuyDrink = false; Draw(); });
        }

        async void BuyDrink()
        {
            if (_refreshing || _info == null || !_info.isOrganizer || !_info.isTop16) return;
            _pendingBuyDrink = false;
            try
            {
                await _api.BuyKfzbFeastDrinkAsync();
                await Refresh();
            }
            catch (Exception ex) { _status(ex.Message); }
        }

        async void BuyCards(int type)
        {
            if (_refreshing) return;
            var joinRank = _buyThenJoinRank;
            _pendingBuyType = 0;
            _buyThenJoinRank = 0;
            try
            {
                await _api.BuyKfzbFeastCardsAsync(type);
                await Refresh();
                if (joinRank > 0 && _info != null && _info.goldCard > 0) Join(joinRank, 2);
            }
            catch (Exception ex) { _status(ex.Message); }
        }

        async void Join(int rank, int cardType)
        {
            if (_refreshing) return;
            try
            {
                await _api.JoinKfzbFeastRoomAsync(rank, cardType);
                _selectedRank = 0;
                _pendingBuyType = 0;
                _buyThenJoinRank = 0;
                _pendingBuyDrink = false;
                _expiryRefreshQueued = false;
                await Refresh();
            }
            catch (Exception ex) { _status(ex.Message); }
        }

        void DrawRoom()
        {
            BanquetLegacyVisuals.FullImage(_window, "room_bg", 0, 0, 1280, 768);
            LegacyUiFactory.PixelButton(_window, "×", 1218, 18, 42, 32, () => Destroy(gameObject));

            var participants = _room.participants ?? Array.Empty<KfzbFeastPublicParticipantView>();
            var ordered = participants.Select((p, i) => new { p, i })
                .OrderByDescending(x => _player != null && x.p.playerId == _player.id)
                .ThenBy(x => x.i).Select(x => x.p).ToArray();
            LegacyUiFactory.PixelLabel(_window, "Dự Tiệc", 14, TextAnchor.MiddleLeft, new Color(.88f,.76f,.22f), 11, 6, 100, 25);
            DrawNationCount(1, 115, 152, _room.weiNum);
            DrawNationCount(2, 185, 220, _room.shuNum);
            DrawNationCount(3, 260, 293, _room.wuNum);
            LegacyUiFactory.PixelLabel(_window, "Tổng:" + _room.peopleNum + " người", 14, TextAnchor.MiddleLeft, Color.white, 338, 6, 139, 25);

            BanquetLegacyVisuals.TopLeftCellImage(_window, "rank_titles", RankTitleIndex(_room.pos), 100, 40, 91, 29, 267, 82);
            AddLoop("organizer_room", 280, 106, 80, 112, 20, 2);
            LegacyUiFactory.PixelLabel(_window, _room.organizerName, 14, TextAnchor.MiddleCenter, new Color(.77f,.67f,.46f), 270, 196, 100, 25);
            DrawBanquetType();

            if (_room.drink)
            {
                BanquetLegacyVisuals.TopLeftCellImage(_window, "item_icons", 2, 80, 80, 29, 37, 20, 85);
                LegacyUiFactory.PixelLabel(_window, "Nữ Nhi Hồng", 14, TextAnchor.MiddleLeft, new Color(1f,.95f,.81f), 50, 95, 130, 24);
            }

            for (var i = 0; i < Math.Min(10, ordered.Length); i++)
            {
                var participant = ordered[i];
                var point = ParticipantPoints[i];
                DrawParticipantNation(participant.forceId, point.x - 45, point.y - 63);
                LegacyUiFactory.PixelLabel(_window, participant.playerName, 14, TextAnchor.MiddleCenter, new Color(.91f,.89f,.85f), point.x - 50, point.y - 10, 100, 25);
            }

            // participant_a/participant_b contain the exact two joinMan animation variants, but the legacy BanquetJoinVO selects them by joinPic.
            // The current public room DTO does not expose joinPic, so client does not invent a character variant.

            if (_room.state == 1)
            {
                _countdown = LegacyUiFactory.PixelLabel(_window, "", 17, TextAnchor.MiddleCenter, new Color(.9f,.88f,.83f), 528, 300, 170, 40);
                UpdateCountdown();
            }
            else if (_room.result)
            {
                DrawResult();
            }
        }

        void DrawBanquetType()
        {
            if (_room == null) return;
            if (_room.cardType == 1)
                BanquetLegacyVisuals.TopLeftCellImage(_window, "item_icons", 5, 80, 80, 58, 64, 444, 283);
            else if (_room.cardType == 2)
                BanquetLegacyVisuals.TopLeftCellImage(_window, "item_icons", 6, 80, 80, 57, 63, 444, 283);
        }

        void DrawNationCount(int forceId, float iconX, float textX, int count)
        {
            var index = forceId == 1 ? 0 : forceId == 2 ? 1 : 2;
            BanquetLegacyVisuals.TopLeftCellImage(_window, "nation_icons", index, 32, 32, 27, 27, iconX, 2);
            LegacyUiFactory.PixelLabel(_window, count.ToString(), 14, TextAnchor.MiddleLeft, Color.white, textX, 6, 30, 25);
        }

        void DrawParticipantNation(int forceId, float x, float y)
        {
            var index = forceId == 1 ? 0 : forceId == 2 ? 1 : forceId == 3 ? 2 : -1;
            if (index >= 0) BanquetLegacyVisuals.TopLeftCellImage(_window, "nation_icons", index, 32, 32, 27, 27, x, y);
        }

        void DrawResult()
        {
            if (_room == null || !_room.result) return;
            if (_resultVisualStartedAt < 0f || _resultVisualRoomId != _room.roomId)
            {
                _resultVisualRoomId = _room.roomId;
                _resultVisualStartedAt = Time.unscaledTime;
            }

            // banquet.banquetHold.result{1..7}: frame 1 base, frame 9 title, frames 15/17/.../31 effects, frame 33 base+title.
            BanquetLegacyVisuals.FullImage(_window, "result_base", 340, 294, 600, 180);
            if (_room.titleId >= 1 && _room.titleId <= 7)
                _resultTitle = BanquetLegacyVisuals.CellImage(_window, "result_titles", _room.titleId - 1, 600, 180, 340, 294, 600, 180);
            _resultFx = BanquetLegacyVisuals.CellImage(_window, "result_fx", 0, 600, 180, 340, 294, 600, 180);

            BanquetLegacyVisuals.TopLeftCellImage(_window, "item_icons", 3, 80, 80, 31, 30, 600, 350);
            LegacyUiFactory.PixelLabel(_window, "Nhận", 16, TextAnchor.MiddleRight, new Color(.27f,.88f,.22f), 520, 358, 80, 25);
            LegacyUiFactory.PixelLabel(_window, _room.tickets.ToString(), 17, TextAnchor.MiddleLeft, new Color(.27f,.88f,.22f), 634, 358, 100, 25);
            _resultLeaveCountdown = LegacyUiFactory.PixelLabel(_window, "", 14, TextAnchor.MiddleCenter, Color.yellow, 525, 388, 200, 30);
            UpdateResultVisual();
        }

        void UpdateResultVisual()
        {
            if (_room == null || !_room.result || _resultVisualStartedAt < 0f) return;
            var frame = (int)((Time.unscaledTime - _resultVisualStartedAt) * LegacyFrameRate) + 1;
            if (_resultTitle != null) _resultTitle.enabled = frame >= 9;
            if (_resultLeaveCountdown != null && _room.resultLeaveCountdownMs > 0)
            {
                var elapsedMs = (Time.unscaledTime - _resultVisualStartedAt) * 1000f;
                var remainingMs = Math.Max(0, _room.resultLeaveCountdownMs - (int)elapsedMs);
                var seconds = (int)Math.Ceiling(remainingMs / 1000d);
                _resultLeaveCountdown.text = seconds + " giây sau tự động thoát";
            }
            if (_resultFx == null) return;
            if (frame >= 15 && frame <= 32)
            {
                var index = Math.Min(8, (frame - 15) / 2);
                BanquetLegacyVisuals.SetCell(_resultFx, "result_fx", index, 600, 180);
                _resultFx.enabled = true;
            }
            else
            {
                _resultFx.enabled = false;
            }
        }

        void UpdateCountdown()
        {
            if (_countdown == null || _room == null || _room.state != 1) return;
            long remainingMs;
            if (_roomCountdownMs > 0)
            {
                remainingMs = Math.Max(0L, _roomCountdownMs - (long)((Time.unscaledTime - _roomCountdownSyncedAt) * 1000f));
            }
            else if (DateTimeOffset.TryParse(_room.expiresAt, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var expires))
            {
                remainingMs = Math.Max(0L, (long)(expires - DateTimeOffset.UtcNow).TotalMilliseconds);
            }
            else
            {
                _countdown.text = "";
                return;
            }
            var remaining = TimeSpan.FromMilliseconds(remainingMs);
            _countdown.text = "Nếu không đủ 10 người thì chúng ta sau " + ((int)remaining.TotalMinutes).ToString("00") + ":" + remaining.Seconds.ToString("00") + " giải tán thôi";
            if (remainingMs == 0 && !_expiryRefreshQueued)
            {
                _expiryRefreshQueued = true;
                _ = Refresh(false);
            }
        }

        void DrawRules()
        {
            var modal = LegacyUiFactory.PixelPanel(_window, "BanquetRules", 0, 0, 1280, 768, new Color(0,0,0,.2f));
            BanquetLegacyVisuals.FullImage(modal, "rules_bg", 158, 55, 685, 352);
            LegacyUiFactory.PixelButton(modal, "×", 776, 147, 42, 32, () => { _showRules = false; Draw(); });
            LegacyUiFactory.PixelLabel(modal, RulesText, 16, TextAnchor.UpperLeft, new Color(.9f,.88f,.83f), 358, 190, 440, 200);
        }

        Image AddLoop(string sheet, float x, float y, int width, int height, int frameCount, int holdFrames)
        {
            var image = BanquetLegacyVisuals.CellImage(_window, sheet, 0, width, height, x, y, width, height);
            _loopVisuals.Add(new LoopVisual
            {
                image = image,
                sheet = sheet,
                frameCount = frameCount,
                holdFrames = holdFrames,
                cellWidth = width,
                cellHeight = height
            });
            return image;
        }

        void UpdateLegacyAnimations()
        {
            if (_loopVisuals.Count == 0) return;
            var tick = (int)(Time.unscaledTime * LegacyFrameRate);
            foreach (var visual in _loopVisuals)
            {
                if (visual.image == null || visual.frameCount <= 0) continue;
                var frame = (tick / Math.Max(1, visual.holdFrames)) % visual.frameCount;
                if (frame == visual.lastFrame) continue;
                BanquetLegacyVisuals.SetCell(visual.image, visual.sheet, frame, visual.cellWidth, visual.cellHeight);
                visual.lastFrame = frame;
            }
        }

        KfzbFeastOrganizerInfoView FindOrganizer(int pos)
        {
            var rooms = _info?.rooms;
            if (rooms == null) return null;
            for (var i = 0; i < rooms.Length; i++)
                if (rooms[i] != null && rooms[i].pos == pos) return rooms[i];
            return null;
        }

        static int RankTitleIndex(int rank)
        {
            if (rank <= 1) return 0;
            if (rank == 2) return 1;
            if (rank <= 4) return 2;
            if (rank <= 8) return 3;
            return 4;
        }
    }
}
