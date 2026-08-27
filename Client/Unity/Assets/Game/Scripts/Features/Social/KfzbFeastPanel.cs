using System;
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
        static readonly Vector2[] ParticipantPoints =
        {
            new Vector2(236,435),new Vector2(702,184),new Vector2(365,496),new Vector2(840,246),new Vector2(488,560),
            new Vector2(958,310),new Vector2(615,626),new Vector2(1090,377),new Vector2(760,698),new Vector2(1216,440)
        };

        static KfzbFeastPanel _open;
        ApiClient _api;
        Action<string> _status;
        RectTransform _window;
        PlayerView _player;
        KfzbFeastCardView _cards;
        KfzbFeastRoomView _room;
        Text _countdown;
        int _floor = 1;
        int _selectedRank;
        int _pendingBuyType;
        int _buyThenJoinRank;
        bool _showRules;
        bool _refreshing;
        float _nextPoll;
        int _pushPending;
        bool _expiryRefreshQueued;

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
            _window = LegacyUiFactory.PixelPanel(blocker, "BanquetScene", 0, 0, 1280, 768, new Color(.035f,.022f,.012f,1));
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
        }

        async Task Refresh(bool showError = true)
        {
            if (_refreshing) return;
            _refreshing = true;
            try
            {
                if (_player == null) _player = await _api.GetPlayerAsync();
                _cards = await _api.GetKfzbFeastCardsAsync();
                try
                {
                    var room = await _api.GetKfzbFeastRoomAsync();
                    _room = room != null && room.state != 3 ? room : null;
                }
                catch (ApiException ex) when (ex.Code == "KFZB_FEAST_ROOM_MISSING")
                {
                    _room = null;
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
            _countdown = null;
            if (_room == null) DrawLobby(); else DrawRoom();
            if (_showRules) DrawRules();
            if (_selectedRank > 0 && _room == null) DrawEnter();
            if (_pendingBuyType > 0 && _room == null) DrawBuyConfirm();
        }

        void DrawLobby()
        {
            LegacyUiFactory.PixelLabel(_window, "Thịnh Yến", 30, TextAnchor.MiddleCenter, new Color(1f,.82f,.35f), 500, 18, 280, 42);
            LegacyUiFactory.PixelButton(_window, "×", 1218, 18, 42, 32, () => Destroy(gameObject));
            LegacyUiFactory.PixelButton(_window, "Quy tắc tiệc", 1080, 18, 125, 32, () => { _showRules = true; Draw(); });

            LegacyUiFactory.PixelLabel(_window, "Thiệp mời, tham dự tiệc tốn 1 tờ", 14, TextAnchor.MiddleLeft, Color.white, 20, 66, 320, 24);
            LegacyUiFactory.PixelLabel(_window, "×" + (_cards != null ? _cards.freeCards : 0), 18, TextAnchor.MiddleLeft, new Color(1f,.88f,.45f), 25, 92, 100, 28);
            LegacyUiFactory.PixelLabel(_window, "Lối đi VIP", 16, TextAnchor.MiddleLeft, Color.white, 20, 126, 140, 24);
            LegacyUiFactory.PixelLabel(_window, "×" + (_cards != null ? _cards.goldCards : 0), 18, TextAnchor.MiddleLeft, new Color(1f,.88f,.45f), 25, 152, 100, 28);
            LegacyUiFactory.PixelLabel(_window, "Nữ Nhi Hồng", 16, TextAnchor.MiddleLeft, Color.white, 20, 188, 140, 24);
            LegacyUiFactory.PixelLabel(_window, "×" + (_cards != null ? _cards.drinkNum : 0), 18, TextAnchor.MiddleLeft, new Color(1f,.88f,.45f), 25, 214, 100, 28);

            if (_cards != null)
            {
                LegacyUiFactory.PixelLabel(_window, "1", 18, TextAnchor.MiddleCenter, Color.white, 22, 270, 70, 24);
                LegacyUiFactory.PixelLabel(_window, _cards.goldCard1.ToString(), 15, TextAnchor.MiddleCenter, new Color(1f,.82f,.35f), 22, 294, 70, 22);
                LegacyUiFactory.PixelButton(_window, "Mua", 18, 322, 78, 30, () => StartBuy(1, 0));
                LegacyUiFactory.PixelLabel(_window, "10", 18, TextAnchor.MiddleCenter, Color.white, 112, 270, 70, 24);
                LegacyUiFactory.PixelLabel(_window, _cards.goldCard10.ToString(), 15, TextAnchor.MiddleCenter, new Color(1f,.82f,.35f), 112, 294, 70, 22);
                LegacyUiFactory.PixelButton(_window, "Mua", 108, 322, 78, 30, () => StartBuy(2, 0));
            }

            LegacyUiFactory.PixelLabel(_window, "Tầng " + _floor, 22, TextAnchor.MiddleCenter, new Color(1f,.82f,.35f), 500, 90, 280, 32);
            if (_floor > 1) LegacyUiFactory.PixelButton(_window, "‹", 450, 90, 42, 32, () => { _floor--; Draw(); });
            if (_floor < 4) LegacyUiFactory.PixelButton(_window, "›", 788, 90, 42, 32, () => { _floor++; Draw(); });

            // The current public API exposes room join by rank but not the legacy GetBanquetInfo organizer metadata.
            // These are protocol positions only: no organizer name, people count or drink state is inferred client-side.
            var start = (_floor - 1) * 4 + 1;
            for (var i = 0; i < 4; i++)
            {
                var rank = start + i;
                var x = 315 + i * 175;
                LegacyUiFactory.PixelButton(_window, "#" + rank, x, 180, 145, 210, () => { _selectedRank = rank; Draw(); });
            }
        }

        void DrawEnter()
        {
            var modal = LegacyUiFactory.PixelPanel(_window, "BanquetEnter", 390, 175, 500, 400, new Color(.055f,.035f,.018f,.98f));
            LegacyUiFactory.PixelButton(modal, "×", 448, 10, 36, 30, () => { _selectedRank = 0; Draw(); });
            LegacyUiFactory.PixelLabel(modal, "#" + _selectedRank, 24, TextAnchor.MiddleCenter, new Color(1f,.82f,.35f), 180, 35, 140, 34);
            LegacyUiFactory.PixelLabel(modal, "Vào Tiệc", 19, TextAnchor.MiddleCenter, Color.white, 55, 105, 160, 30);
            LegacyUiFactory.PixelLabel(modal, "(Tốn 1 thiệp mời thường, hiện có " + (_cards != null ? _cards.freeCards : 0) + " cái)", 14, TextAnchor.MiddleCenter, Color.white, 25, 137, 220, 44);
            var normal = LegacyUiFactory.PixelButton(modal, "Vào Tiệc", 60, 205, 150, 38, () => Join(_selectedRank, 1));
            normal.interactable = _cards != null && _cards.freeCards > 0;

            LegacyUiFactory.PixelLabel(modal, "Lối đi VIP", 19, TextAnchor.MiddleCenter, Color.white, 285, 105, 160, 30);
            LegacyUiFactory.PixelLabel(modal, "(Tốn 1 thiệp mời VIP, hiện có " + (_cards != null ? _cards.goldCards : 0) + " cái)", 14, TextAnchor.MiddleCenter, Color.white, 255, 137, 220, 44);
            LegacyUiFactory.PixelButton(modal, "Lối đi VIP", 290, 205, 150, 38, () =>
            {
                if (_cards != null && _cards.goldCards > 0) Join(_selectedRank, 2);
                else StartBuy(1, _selectedRank);
            });
        }

        void StartBuy(int type, int joinAfterBuyRank)
        {
            if (_cards == null) return;
            _pendingBuyType = type;
            _buyThenJoinRank = joinAfterBuyRank;
            Draw();
        }

        void DrawBuyConfirm()
        {
            var price = _pendingBuyType == 1 ? _cards.goldCard1 : _cards.goldCard10;
            var count = _pendingBuyType == 1 ? 1 : 10;
            var modal = LegacyUiFactory.PixelPanel(_window, "BanquetBuyConfirm", 420, 260, 440, 210, new Color(.06f,.04f,.02f,1));
            LegacyUiFactory.PixelLabel(modal, "Bạn chắc chắn dùng " + price + " vàng mua thiệp mời VIP?", 17, TextAnchor.MiddleCenter, Color.white, 30, 35, 380, 70);
            LegacyUiFactory.PixelLabel(modal, "×" + count, 18, TextAnchor.MiddleCenter, new Color(1f,.82f,.35f), 175, 105, 90, 28);
            LegacyUiFactory.PixelButton(modal, "Mua", 90, 150, 120, 36, () => BuyCards(_pendingBuyType));
            LegacyUiFactory.PixelButton(modal, "×", 230, 150, 120, 36, () => { _pendingBuyType = 0; _buyThenJoinRank = 0; Draw(); });
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
                if (joinRank > 0 && _cards != null && _cards.goldCards > 0) Join(joinRank, 2);
            }
            catch (Exception ex) { _status(ex.Message); }
        }

        async void Join(int rank, int cardType)
        {
            if (_refreshing) return;
            try
            {
                _room = await _api.JoinKfzbFeastRoomAsync(rank, cardType);
                _selectedRank = 0;
                _pendingBuyType = 0;
                _buyThenJoinRank = 0;
                _expiryRefreshQueued = false;
                await Refresh();
            }
            catch (Exception ex) { _status(ex.Message); }
        }

        void DrawRoom()
        {
            LegacyUiFactory.PixelButton(_window, "×", 1218, 18, 42, 32, () => Destroy(gameObject));
            LegacyUiFactory.PixelLabel(_window, "Thịnh Yến", 30, TextAnchor.MiddleCenter, new Color(1f,.82f,.35f), 500, 18, 280, 42);
            LegacyUiFactory.PixelLabel(_window, "#" + _room.rank, 18, TextAnchor.MiddleCenter, new Color(.78f,.67f,.46f), 270, 196, 100, 25);
            if (_room.drink) LegacyUiFactory.PixelLabel(_window, "Nữ Nhi Hồng", 15, TextAnchor.MiddleCenter, new Color(1f,.78f,.32f), 255, 78, 130, 28);

            var participants = _room.participants ?? Array.Empty<KfzbFeastParticipantView>();
            var ordered = participants.Select((p, i) => new { p, i })
                .OrderByDescending(x => _player != null && x.p.playerId == _player.id)
                .ThenBy(x => x.i).Select(x => x.p).ToArray();
            var wei = participants.Count(x => x.forceId == 1);
            var shu = participants.Count(x => x.forceId == 2);
            var wu = participants.Count(x => x.forceId == 3);
            LegacyUiFactory.PixelLabel(_window, "Dự Tiệc", 14, TextAnchor.MiddleLeft, new Color(.88f,.76f,.22f), 11, 6, 95, 25);
            LegacyUiFactory.PixelLabel(_window, "Ngụy " + wei + " người", 14, TextAnchor.MiddleLeft, Color.white, 115, 6, 130, 25);
            LegacyUiFactory.PixelLabel(_window, "Thục " + shu + " người", 14, TextAnchor.MiddleLeft, Color.white, 250, 6, 130, 25);
            LegacyUiFactory.PixelLabel(_window, "Ngô " + wu + " người", 14, TextAnchor.MiddleLeft, Color.white, 385, 6, 130, 25);
            LegacyUiFactory.PixelLabel(_window, "Tổng:" + participants.Length + " người", 14, TextAnchor.MiddleLeft, Color.white, 520, 6, 140, 25);

            for (var i = 0; i < Math.Min(10, ordered.Length); i++)
            {
                var p = ordered[i];
                var point = ParticipantPoints[i];
                var slot = LegacyUiFactory.PixelPanel(_window, "BanquetParticipant", point.x - 62, point.y - 56, 124, 78, new Color(.04f,.028f,.018f,.86f));
                LegacyUiFactory.PixelLabel(slot, ForceName(p.forceId), 12, TextAnchor.MiddleCenter, ForceColor(p.forceId), 7, 5, 110, 22);
                LegacyUiFactory.PixelLabel(slot, p.name, 14, TextAnchor.MiddleCenter, Color.white, 7, 29, 110, 24);
                if (_player != null && p.playerId == _player.id)
                    LegacyUiFactory.PixelLabel(slot, "•", 18, TextAnchor.MiddleCenter, new Color(1f,.82f,.35f), 48, 52, 28, 20);
            }

            if (_room.state == 1)
            {
                _countdown = LegacyUiFactory.PixelLabel(_window, "", 17, TextAnchor.MiddleCenter, new Color(.9f,.88f,.83f), 528, 300, 170, 40);
                UpdateCountdown();
            }
            else if (_room.state == 2)
            {
                DrawResult(participants);
            }
        }

        void DrawResult(KfzbFeastParticipantView[] participants)
        {
            var self = _player == null ? null : participants.FirstOrDefault(x => x.playerId == _player.id);
            var result = LegacyUiFactory.PixelPanel(_window, "BanquetResultAssetPlaceholder", 500, 300, 280, 150, new Color(.045f,.03f,.018f,.92f));
            if (self != null)
            {
                // Legacy uses the embedded SWF symbol banquet.banquetHold.result{titleId}; that sprite is not yet imported into Unity Resources.
                LegacyUiFactory.PixelLabel(result, "#" + self.titleId, 26, TextAnchor.MiddleCenter, new Color(1f,.82f,.35f), 70, 18, 140, 38);
                LegacyUiFactory.PixelLabel(result, "Nhận", 18, TextAnchor.MiddleRight, new Color(.27f,.88f,.22f), 35, 72, 85, 28);
                LegacyUiFactory.PixelLabel(result, self.tickets.ToString(), 20, TextAnchor.MiddleLeft, new Color(.27f,.88f,.22f), 128, 72, 110, 28);
            }
        }

        void UpdateCountdown()
        {
            if (_countdown == null || _room == null || _room.state != 1) return;
            DateTimeOffset expires;
            if (!DateTimeOffset.TryParse(_room.expiresAt, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out expires))
            {
                _countdown.text = "";
                return;
            }
            var remaining = expires - DateTimeOffset.UtcNow;
            if (remaining < TimeSpan.Zero) remaining = TimeSpan.Zero;
            _countdown.text = "Nếu không đủ 10 người thì chúng ta sau " + ((int)remaining.TotalMinutes).ToString("00") + ":" + remaining.Seconds.ToString("00") + " giải tán thôi";
            if (remaining == TimeSpan.Zero && !_expiryRefreshQueued)
            {
                _expiryRefreshQueued = true;
                _ = Refresh(false);
            }
        }

        void DrawRules()
        {
            var modal = LegacyUiFactory.PixelPanel(_window, "BanquetRules", 250, 110, 780, 550, new Color(.055f,.035f,.018f,.99f));
            LegacyUiFactory.PixelLabel(modal, "Quy tắc tiệc", 24, TextAnchor.MiddleCenter, new Color(1f,.82f,.35f), 240, 18, 300, 36);
            LegacyUiFactory.PixelButton(modal, "×", 722, 14, 42, 32, () => { _showRules = false; Draw(); });
            LegacyUiFactory.PixelLabel(modal, RulesText, 16, TextAnchor.UpperLeft, Color.white, 38, 72, 704, 440);
        }

        static string ForceName(int forceId)
        {
            switch (forceId)
            {
                case 1: return "Ngụy";
                case 2: return "Thục";
                case 3: return "Ngô";
                default: return "";
            }
        }

        static Color ForceColor(int forceId)
        {
            switch (forceId)
            {
                case 1: return new Color(.86f,.86f,.86f);
                case 2: return new Color(.92f,.38f,.28f);
                case 3: return new Color(.34f,.68f,.92f);
                default: return Color.white;
            }
        }
    }
}
