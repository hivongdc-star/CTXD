using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CTXD.Client.Features.Battle;
using CTXD.Client.Features.FirstPlayable;
using CTXD.Client.Networking;
using UnityEngine;
using UnityEngine.UI;

namespace CTXD.Client.Features.World
{
    public sealed class KfgzPanel : MonoBehaviour
    {
        const int LegacyClaimLimit = 4;
        static KfgzPanel _open;

        enum ViewMode { Battle, Ranking, Rewards }

        ApiClient _api;
        KfgzExtendedApi _ext;
        Action<string> _status;
        RectTransform _window;
        PlayerView _player;
        KfgzView _view;
        KfgzWarView _war;
        GeneralRosterResponse _roster;
        KfgzBattleResourceView _battleResources;
        KfgzRanking _ranking;
        KfgzRewardView _roundReward;
        KfgzEndRewardView _endReward;
        KfgzTitlesResponse _titles;
        string _rankingError;
        string _roundRewardError;
        string _endRewardError;
        string _titleError;
        readonly HashSet<int> _selectedGenerals = new HashSet<int>();
        int _cityId;
        bool _busy;
        float _nextRefresh;
        ViewMode _mode;

        public static KfgzPanel Open(RectTransform host, ApiClient api, Action<string> status)
        {
            if (_open != null) Destroy(_open.gameObject);
            var go = new GameObject("KfgzPanel");
            go.transform.SetParent(host, false);
            var panel = go.AddComponent<KfgzPanel>();
            panel._api = api;
            panel._ext = new KfgzExtendedApi(api);
            panel._status = status;
            _open = panel;
            panel.Build();
            _ = panel.RefreshAsync();
            return panel;
        }

        public static void RefreshOpenFromPush() { if (_open != null && !_open._busy) _ = _open.RefreshAsync(); }
        void OnDestroy() { if (_open == this) _open = null; }

        void Update()
        {
            if (_busy || _api == null || string.IsNullOrEmpty(_api.Token) || Time.unscaledTime < _nextRefresh) return;
            _nextRefresh = Time.unscaledTime + 10f;
            _ = RefreshAsync();
        }

        void Build()
        {
            var blocker = LegacyUiFactory.Panel(transform, "KfgzBlocker", Vector2.zero, Vector2.one, new Color(0, 0, 0, .84f));
            _window = LegacyUiFactory.PixelPanel(blocker, "KfgzWindow", 55, 43, 1170, 634, new Color(.045f, .032f, .018f, 1));
            LegacyUiFactory.PixelLabel(_window, "KFGZ", 24, TextAnchor.MiddleCenter, new Color(1f, .82f, .35f), 450, 9, 270, 34);
            LegacyUiFactory.PixelButton(_window, "Đóng", 1082, 9, 72, 28, () => Destroy(gameObject));
            LegacyUiFactory.PixelLabel(_window, "Đang đồng bộ KFGZ...", 16, TextAnchor.MiddleCenter, Color.white, 330, 285, 510, 36);
        }

        async Task RefreshAsync()
        {
            try
            {
                _player = await _api.GetPlayerAsync();
                _view = await _api.GetKfgzAsync();
                _roster = await _api.GetGeneralsAsync();
                _war = null;
                _battleResources = null;
                _roundReward = null;
                _ranking = null;
                _endReward = null;
                _titles = null;
                _rankingError = null;
                _roundRewardError = null;
                _endRewardError = null;
                _titleError = null;

                try { _ranking = await _api.GetKfgzRankingAsync(); }
                catch (Exception ex) { _rankingError = ex.Message; }

                try { _endReward = await _ext.GetEndRewardAsync(); }
                catch (Exception ex) { _endRewardError = ex.Message; }

                try { _titles = await _ext.GetTitlesAsync(); }
                catch (Exception ex) { _titleError = ex.Message; }

                if (_view != null && _view.signed)
                {
                    try { _battleResources = await _ext.GetResourcesAsync(); } catch { }
                    try { _war = await _api.GetKfgzWorldAsync(); } catch { }
                    if (_war != null)
                    {
                        if (_cityId == 0)
                        {
                            var own = (_war.deployments ?? Array.Empty<KfgzDeploymentView>()).FirstOrDefault(x => x.playerId == _player.id);
                            _cityId = own != null ? own.cityId : (_war.cities ?? Array.Empty<KfgzCityView>()).FirstOrDefault()?.id ?? 0;
                        }
                        try { _roundReward = await _ext.GetRoundRewardAsync(_war.roundId); }
                        catch (Exception ex) { _roundRewardError = ex.Message; }
                    }
                    else
                    {
                        _roundRewardError = "Public KFGZ state hiện không cung cấp roundId để đọc round reward.";
                    }
                }
                else
                {
                    _roundRewardError = "Round reward cần roundId authoritative của người chơi đã tham gia KFGZ.";
                }

                _nextRefresh = Time.unscaledTime + 10f;
                Draw();
            }
            catch (Exception ex)
            {
                _status(ex.Message);
                Destroy(gameObject);
            }
        }

        void Draw()
        {
            if (_window == null || _view == null) return;
            LegacyUiFactory.DestroyChildren(_window);
            LegacyUiFactory.PixelLabel(_window, "KFGZ - SEASON " + _view.seasonNo, 23, TextAnchor.MiddleCenter, new Color(1f, .82f, .35f), 400, 8, 370, 34);
            LegacyUiFactory.PixelButton(_window, "Đóng", 1082, 9, 72, 28, () => Destroy(gameObject));
            LegacyUiFactory.PixelButton(_window, "Làm mới", 995, 9, 78, 28, () => _ = RefreshAsync());
            DrawViewTabs();
            LegacyUiFactory.PixelLabel(_window, "State " + _view.state + "   Force " + _view.forceId + "   " + (_view.signed ? "Đã đăng ký" : "Chưa đăng ký"), 15, TextAnchor.MiddleLeft, Color.white, 415, 47, 560, 26);

            if (_mode == ViewMode.Ranking) { DrawRanking(); return; }
            if (_mode == ViewMode.Rewards) { DrawRewards(); return; }

            if (!_view.signed)
            {
                LegacyUiFactory.PixelLabel(_window, "KFGZ chưa đồng bộ đội hình cho season hiện tại.", 17, TextAnchor.MiddleCenter, Color.white, 310, 245, 550, 36);
                LegacyUiFactory.PixelButton(_window, "Đăng ký KFGZ", 455, 300, 260, 42, Signup);
                return;
            }

            var resource = _view.resources;
            var extra = _battleResources;
            LegacyUiFactory.PixelLabel(_window,
                "Gold " + (resource?.gold ?? 0) + "   Food " + (resource?.food ?? 0) + "   Iron " + (resource?.iron ?? 0) +
                "   RecruitToken " + (extra?.recruitToken ?? 0) + "   Phantom " + (extra?.phantomCount ?? 0) + "   Mubing/h " + (extra?.mubing ?? 0),
                14, TextAnchor.MiddleCenter, new Color(.9f, .84f, .66f), 20, 76, 1130, 28);

            DrawGenerals();
            DrawCities();
            DrawActions();
        }

        void DrawViewTabs()
        {
            LegacyUiFactory.PixelButton(_window, _mode == ViewMode.Battle ? "[Chiến trường]" : "Chiến trường", 18, 45, 120, 28, () => SetMode(ViewMode.Battle));
            LegacyUiFactory.PixelButton(_window, _mode == ViewMode.Ranking ? "[Xếp hạng]" : "Xếp hạng", 145, 45, 120, 28, () => SetMode(ViewMode.Ranking));
            LegacyUiFactory.PixelButton(_window, _mode == ViewMode.Rewards ? "[Thưởng]" : "Thưởng", 272, 45, 120, 28, () => SetMode(ViewMode.Rewards));
        }

        void SetMode(ViewMode mode)
        {
            _mode = mode;
            Draw();
        }

        void DrawRanking()
        {
            var personalListView = LegacyUiFactory.PixelPanel(_window, "personalListView", 0, 0, 1170, 634, Color.clear);
            personalListView.GetComponent<Image>().raycastTarget = false;
            DisableRankingTab(personalListView, "Bảng Quốc Gia", 20);
            DisableRankingTab(personalListView, "Bảng Dũng Sĩ", 210);
            DisableRankingTab(personalListView, "Bảng Chiếm Thành Tổ", 400);
            DisableRankingTab(personalListView, "Khiêu Chiến Nhóm", 590);
            DisableRankingTab(personalListView, "Bảng Sát Địch Tổ", 780);

            LegacyUiFactory.PixelLabel(personalListView,
                "Xếp hạng KFGZ public (backend chưa expose loại bảng legacy; 5 tab legacy giữ blocked).",
                14, TextAnchor.MiddleLeft, Color.gray, 20, 126, 1120, 28);

            if (!string.IsNullOrEmpty(_rankingError))
            {
                LegacyUiFactory.PixelLabel(personalListView, "Ranking API: " + _rankingError, 16, TextAnchor.MiddleCenter, Color.white, 100, 245, 970, 42);
                return;
            }

            var items = _ranking?.items ?? Array.Empty<KfgzRankEntry>();
            LegacyUiFactory.PixelLabel(personalListView, "Hạng", 14, TextAnchor.MiddleCenter, new Color(1f, .82f, .35f), 25, 168, 65, 28);
            LegacyUiFactory.PixelLabel(personalListView, "Người chơi", 14, TextAnchor.MiddleLeft, new Color(1f, .82f, .35f), 100, 168, 260, 28);
            LegacyUiFactory.PixelLabel(personalListView, "Force", 14, TextAnchor.MiddleCenter, new Color(1f, .82f, .35f), 365, 168, 75, 28);
            LegacyUiFactory.PixelLabel(personalListView, "Sát địch", 14, TextAnchor.MiddleCenter, new Color(1f, .82f, .35f), 445, 168, 130, 28);
            LegacyUiFactory.PixelLabel(personalListView, "Chiếm thành", 14, TextAnchor.MiddleCenter, new Color(1f, .82f, .35f), 580, 168, 130, 28);
            LegacyUiFactory.PixelLabel(personalListView, "Solo thắng", 14, TextAnchor.MiddleCenter, new Color(1f, .82f, .35f), 715, 168, 120, 28);
            LegacyUiFactory.PixelLabel(personalListView, "Thắng", 14, TextAnchor.MiddleCenter, new Color(1f, .82f, .35f), 840, 168, 100, 28);
            LegacyUiFactory.PixelLabel(personalListView, "Thua", 14, TextAnchor.MiddleCenter, new Color(1f, .82f, .35f), 945, 168, 100, 28);

            for (var i = 0; i < Math.Min(items.Length, 12); i++)
            {
                var row = items[i];
                var y = 198 + i * 31;
                LegacyUiFactory.PixelLabel(personalListView, row.rank.ToString(), 14, TextAnchor.MiddleCenter, Color.white, 25, y, 65, 27);
                LegacyUiFactory.PixelLabel(personalListView, string.IsNullOrEmpty(row.name) ? "#" + row.playerId : row.name, 14, TextAnchor.MiddleLeft, Color.white, 100, y, 260, 27);
                LegacyUiFactory.PixelLabel(personalListView, row.forceId.ToString(), 14, TextAnchor.MiddleCenter, Color.white, 365, y, 75, 27);
                LegacyUiFactory.PixelLabel(personalListView, row.killArmy.ToString(), 14, TextAnchor.MiddleCenter, Color.white, 445, y, 130, 27);
                LegacyUiFactory.PixelLabel(personalListView, row.occupyCity.ToString(), 14, TextAnchor.MiddleCenter, Color.white, 580, y, 130, 27);
                LegacyUiFactory.PixelLabel(personalListView, row.soloWins.ToString(), 14, TextAnchor.MiddleCenter, Color.white, 715, y, 120, 27);
                LegacyUiFactory.PixelLabel(personalListView, row.wins.ToString(), 14, TextAnchor.MiddleCenter, Color.white, 840, y, 100, 27);
                LegacyUiFactory.PixelLabel(personalListView, row.losses.ToString(), 14, TextAnchor.MiddleCenter, Color.white, 945, y, 100, 27);
            }

            if (items.Length == 0)
                LegacyUiFactory.PixelLabel(personalListView, "Server chưa trả dữ liệu ranking KFGZ.", 16, TextAnchor.MiddleCenter, Color.gray, 260, 260, 650, 36);
        }

        void DisableRankingTab(RectTransform parent, string label, float x)
        {
            var button = LegacyUiFactory.PixelButton(parent, label, x, 88, 180, 30, () => { });
            button.interactable = false;
        }

        void DrawRewards()
        {
            var endView = LegacyUiFactory.PixelPanel(_window, "endView", 0, 0, 1170, 634, Color.clear);
            endView.GetComponent<Image>().raycastTarget = false;
            LegacyUiFactory.PixelImage(endView, "LegacyVisual/Kfgz/kfgz", 22, 84, 60, 60, true);
            LegacyUiFactory.PixelLabel(endView, "KFGZ REWARD", 18, TextAnchor.MiddleLeft, new Color(1f, .82f, .35f), 94, 91, 240, 30);
            LegacyUiFactory.PixelLabel(endView, "Mọi số liệu dưới đây lấy trực tiếp từ public server state.", 14, TextAnchor.MiddleLeft, Color.gray, 94, 119, 500, 24);

            var roundPanel = LegacyUiFactory.PixelPanel(endView, "roundRewardState", 18, 154, 542, 456, new Color(.07f, .05f, .026f, .96f));
            LegacyUiFactory.PixelLabel(roundPanel, "ROUND REWARD", 17, TextAnchor.MiddleCenter, new Color(1f, .82f, .35f), 12, 10, 518, 28);
            DrawRoundReward(roundPanel);

            var endPanel = LegacyUiFactory.PixelPanel(endView, "endRewardState", 575, 84, 577, 526, new Color(.07f, .05f, .026f, .96f));
            LegacyUiFactory.PixelLabel(endPanel, "END REWARD / TITLE", 17, TextAnchor.MiddleCenter, new Color(1f, .82f, .35f), 12, 10, 553, 28);
            DrawEndReward(endPanel);
        }

        void DrawRoundReward(RectTransform panel)
        {
            if (!string.IsNullOrEmpty(_roundRewardError))
            {
                LegacyUiFactory.PixelLabel(panel, _roundRewardError, 14, TextAnchor.MiddleCenter, Color.gray, 24, 58, 494, 70);
                return;
            }
            if (_roundReward == null || !_roundReward.mapped)
            {
                LegacyUiFactory.PixelLabel(panel, "Round reward: " + SafeBlocker(_roundReward?.blocker), 14, TextAnchor.MiddleCenter, Color.gray, 24, 58, 494, 70);
                return;
            }

            LegacyUiFactory.PixelLabel(panel, "Round #" + _roundReward.referenceId + "   Season " + _roundReward.seasonId, 14, TextAnchor.MiddleCenter, Color.white, 18, 44, 506, 26);
            LegacyUiFactory.PixelLabel(panel, "CityTickets: " + _roundReward.cityTickets, 15, TextAnchor.MiddleLeft, Color.white, 28, 79, 235, 26);
            LegacyUiFactory.PixelLabel(panel, "WinTickets: " + _roundReward.winTickets, 15, TextAnchor.MiddleLeft, Color.white, 278, 79, 235, 26);
            LegacyUiFactory.PixelLabel(panel, "KillRankTickets: " + _roundReward.killRankTickets, 15, TextAnchor.MiddleLeft, Color.white, 28, 110, 235, 26);
            LegacyUiFactory.PixelLabel(panel, "SoloTickets: " + _roundReward.soloTickets, 15, TextAnchor.MiddleLeft, Color.white, 278, 110, 235, 26);
            LegacyUiFactory.PixelLabel(panel, "OccupyTickets: " + _roundReward.occupyTickets, 15, TextAnchor.MiddleLeft, Color.white, 28, 141, 300, 26);

            var me = (_ranking?.items ?? Array.Empty<KfgzRankEntry>()).FirstOrDefault(x => _player != null && x.playerId == _player.id);
            if (me != null)
            {
                LegacyUiFactory.PixelLabel(panel, string.Format("Sát địch {0} người", me.killArmy), 14, TextAnchor.MiddleLeft, new Color(.86f, .82f, .7f), 28, 177, 235, 24);
                LegacyUiFactory.PixelLabel(panel, string.Format("Chiếm Thành {0} tòa", me.occupyCity), 14, TextAnchor.MiddleLeft, new Color(.86f, .82f, .7f), 278, 177, 235, 24);
                LegacyUiFactory.PixelLabel(panel, "SoloWins: " + me.soloWins, 14, TextAnchor.MiddleLeft, new Color(.86f, .82f, .7f), 28, 201, 235, 24);
            }

            LegacyUiFactory.PixelLabel(panel, "BaseTickets: " + _roundReward.baseTickets, 15, TextAnchor.MiddleLeft, Color.white, 28, 241, 235, 26);
            LegacyUiFactory.PixelLabel(panel, "NextTickets: " + _roundReward.nextTickets, 15, TextAnchor.MiddleLeft, Color.white, 278, 241, 235, 26);
            LegacyUiFactory.PixelLabel(panel, "GoldCost: " + _roundReward.goldCost, 15, TextAnchor.MiddleLeft, Color.white, 28, 272, 235, 26);
            LegacyUiFactory.PixelLabel(panel, "ClaimTimes: " + _roundReward.claimTimes, 15, TextAnchor.MiddleLeft, Color.white, 278, 272, 235, 26);

            var remaining = Math.Max(0, LegacyClaimLimit - _roundReward.claimTimes);
            LegacyUiFactory.PixelLabel(panel, string.Format("(Còn có thể nhận {0} lần)", remaining), 14, TextAnchor.MiddleCenter, new Color(1f, .82f, .35f), 65, 310, 420, 25);
            if (_roundReward.claimTimes < LegacyClaimLimit)
            {
                if (_roundReward.goldCost > 0)
                    LegacyUiFactory.PixelLabel(panel, string.Format("Tốn {0} Vàng, nhận gấp {1} thưởng?", _roundReward.goldCost, RepeatMultiplier(_roundReward.claimTimes)), 14, TextAnchor.MiddleCenter, Color.white, 45, 338, 450, 30);
                LegacyUiFactory.PixelImage(panel, "LegacyVisual/Kfgz/costGetTicket", 120, 378, 46, 46, true);
                LegacyUiFactory.PixelButton(panel, "Nhận thưởng", 180, 382, 190, 38, ClaimRoundReward);
            }
            else
            {
                LegacyUiFactory.PixelLabel(panel, "Đã nhận đủ 4 lần.", 15, TextAnchor.MiddleCenter, Color.gray, 100, 380, 340, 38);
            }
        }

        void DrawEndReward(RectTransform panel)
        {
            if (!string.IsNullOrEmpty(_endRewardError))
            {
                LegacyUiFactory.PixelLabel(panel, "End reward API: " + _endRewardError, 14, TextAnchor.MiddleCenter, Color.gray, 24, 46, 529, 48);
            }
            else if (_endReward == null || !_endReward.mapped)
            {
                LegacyUiFactory.PixelLabel(panel, "End reward: " + SafeBlocker(_endReward?.blocker), 14, TextAnchor.MiddleCenter, Color.gray, 24, 46, 529, 48);
            }
            else
            {
                LegacyUiFactory.PixelLabel(panel, "Season " + _endReward.seasonId + "   NationScore " + _endReward.nationScore, 14, TextAnchor.MiddleCenter, Color.white, 20, 42, 537, 26);
                var slots = _endReward.slots ?? Array.Empty<KfgzEndRewardSlotView>();
                for (var i = 0; i < Math.Min(slots.Length, 4); i++)
                {
                    var slot = slots[i];
                    var y = 76 + i * 78;
                    LegacyUiFactory.PixelLabel(panel,
                        "Slot " + slot.slot + "  yêu cầu " + slot.requiredNationScore + "  Base " + slot.baseTickets + "  Next " + slot.nextTickets,
                        13, TextAnchor.MiddleLeft, slot.available ? Color.white : Color.gray, 22, y, 395, 24);
                    LegacyUiFactory.PixelLabel(panel,
                        "GoldCost " + slot.goldCost + "  ClaimTimes " + slot.claimTimes,
                        13, TextAnchor.MiddleLeft, slot.available ? new Color(.86f, .82f, .7f) : Color.gray, 22, y + 25, 395, 22);

                    if (slot.available && slot.claimTimes < LegacyClaimLimit)
                    {
                        var capturedSlot = slot.slot;
                        LegacyUiFactory.PixelButton(panel, "Nhận thưởng", 425, y + 7, 125, 38, () => ClaimEndReward(capturedSlot));
                    }
                    else
                    {
                        var state = !slot.available ? "Chưa đạt" : "Đã đủ 4 lần";
                        LegacyUiFactory.PixelLabel(panel, state, 13, TextAnchor.MiddleCenter, Color.gray, 425, y + 7, 125, 38);
                    }
                }
            }

            LegacyUiFactory.PixelLabel(panel, "TITLE", 15, TextAnchor.MiddleLeft, new Color(1f, .82f, .35f), 22, 393, 80, 24);
            if (!string.IsNullOrEmpty(_titleError))
            {
                LegacyUiFactory.PixelLabel(panel, "Title API: " + _titleError, 13, TextAnchor.MiddleLeft, Color.gray, 22, 420, 525, 42);
                return;
            }

            var titles = _titles?.items ?? Array.Empty<KfgzTitleView>();
            if (titles.Length == 0)
            {
                LegacyUiFactory.PixelLabel(panel, "Không có title KFGZ do server cấp.", 13, TextAnchor.MiddleLeft, Color.gray, 22, 420, 525, 28);
                return;
            }

            for (var i = 0; i < Math.Min(titles.Length, 3); i++)
            {
                var title = titles[i];
                LegacyUiFactory.PixelLabel(panel,
                    "Season " + title.seasonId + "  " + title.playerName + "  " + TitleDisplay(title.titleKey),
                    13, TextAnchor.MiddleLeft, Color.white, 22, 420 + i * 26, 525, 24);
            }
        }

        static string SafeBlocker(string blocker) => string.IsNullOrEmpty(blocker) ? "authoritative state chưa khả dụng" : blocker;

        static int RepeatMultiplier(int claimTimes)
        {
            switch (claimTimes)
            {
                case 0: return 1;
                case 1: return 1;
                case 2: return 2;
                case 3: return 4;
                default: return 0;
            }
        }

        static string TitleDisplay(string titleKey)
        {
            return string.Equals(titleKey, "TITLE_KFGZ_1", StringComparison.Ordinal) ? "TITLE_KFGZ_1  第一勇士" : titleKey;
        }

        async void ClaimRoundReward()
        {
            if (_busy || _war == null || _roundReward == null || !_roundReward.mapped || _roundReward.claimTimes >= LegacyClaimLimit) return;
            _busy = true;
            var roundId = _war.roundId;
            try
            {
                await _ext.ClaimRoundRewardAsync(roundId);
                _roundReward = await _ext.GetRoundRewardAsync(roundId);
                _status("Đã nhận thưởng KFGZ round #" + roundId + ".");
                Draw();
                await RefreshAsync();
            }
            catch (Exception ex) { _status(ex.Message); }
            finally { _busy = false; }
        }

        async void ClaimEndReward(int slot)
        {
            if (_busy || slot < 1 || slot > 4) return;
            _busy = true;
            try
            {
                await _ext.ClaimEndRewardAsync(slot);
                _endReward = await _ext.GetEndRewardAsync();
                _status("Đã nhận end reward KFGZ slot " + slot + ".");
                Draw();
                await RefreshAsync();
            }
            catch (Exception ex) { _status(ex.Message); }
            finally { _busy = false; }
        }

        void DrawGenerals()
        {
            LegacyUiFactory.PixelLabel(_window, "VÕ TƯỚNG KFGZ", 17, TextAnchor.MiddleCenter, new Color(1f, .82f, .35f), 16, 112, 330, 27);
            var generals = _view.generals ?? Array.Empty<KfgzGeneralStateView>();
            for (var i = 0; i < Math.Min(generals.Length, 10); i++)
            {
                var g = generals[i];
                var roster = (_roster?.military ?? Array.Empty<GeneralView>()).FirstOrDefault(x => x.id == g.generalId);
                var name = roster != null ? roster.name : "General " + g.generalId;
                var selected = _selectedGenerals.Contains(g.generalId);
                var state = g.state == 3 ? " [chiến]" : g.state == 1 ? "" : " [state " + g.state + "]";
                LegacyUiFactory.PixelButton(_window, (selected ? "✓ " : "") + name + " Lv" + g.level + "  HP " + g.forces + state,
                    18, 145 + i * 38, 326, 31, () => ToggleGeneral(g.generalId));
            }
        }

        void DrawCities()
        {
            LegacyUiFactory.PixelLabel(_window, "THÀNH KFGZ", 17, TextAnchor.MiddleCenter, new Color(1f, .82f, .35f), 365, 112, 390, 27);
            if (_war == null)
            {
                LegacyUiFactory.PixelLabel(_window, "Chưa có round chiến đấu đang khả dụng.", 15, TextAnchor.MiddleCenter, Color.gray, 365, 155, 390, 40);
                return;
            }
            var cities = _war.cities ?? Array.Empty<KfgzCityView>();
            for (var i = 0; i < Math.Min(cities.Length, 11); i++)
            {
                var city = cities[i];
                var mark = city.id == _cityId ? "▶ " : "";
                var owner = city.ownerSide == 1 ? "P1" : city.ownerSide == 2 ? "P2" : "--";
                var fighting = (_war.battles ?? Array.Empty<KfgzBattleView>()).Any(x => x.cityId == city.id && x.state == 1) ? " ⚔" : "";
                LegacyUiFactory.PixelButton(_window, mark + city.name + " #" + city.id + "  " + owner + fighting,
                    370, 145 + i * 36, 380, 29, () => { _cityId = city.id; Draw(); });
            }
        }

        void DrawActions()
        {
            LegacyUiFactory.PixelLabel(_window, "ĐIỀU KHIỂN", 17, TextAnchor.MiddleCenter, new Color(1f, .82f, .35f), 775, 112, 375, 27);
            var selected = _selectedGenerals.OrderBy(x => x).ToArray();
            var selectedText = selected.Length == 0 ? "Chưa chọn tướng" : "Đã chọn: " + string.Join(",", selected);
            LegacyUiFactory.PixelLabel(_window, selectedText + "\nThành đích: " + (_cityId == 0 ? "--" : _cityId.ToString()), 14, TextAnchor.UpperLeft, Color.white, 785, 148, 350, 55);

            LegacyUiFactory.PixelButton(_window, "Đi tới thành", 785, 215, 165, 34, MoveSelected);
            LegacyUiFactory.PixelButton(_window, "Gọi tướng", 965, 215, 165, 34, CallSelected);
            LegacyUiFactory.PixelButton(_window, "Có thể gọi", 785, 258, 165, 34, LoadCallable);
            LegacyUiFactory.PixelButton(_window, "Rút lui", 965, 258, 165, 34, RetreatSelected);
            LegacyUiFactory.PixelButton(_window, "Bắt đầu tuyển", 785, 315, 165, 34, StartMubing);
            LegacyUiFactory.PixelButton(_window, "Tuyển nhanh", 965, 315, 165, 34, FastRecruit);

            var battleId = CurrentBattleId();
            LegacyUiFactory.PixelLabel(_window, battleId > 0 ? "Battle #" + battleId : "Không có battle đang chọn", 15, TextAnchor.MiddleCenter, Color.white, 785, 370, 345, 28);
            LegacyUiFactory.PixelButton(_window, "Mở battle", 785, 409, 165, 34, OpenBattle);
            LegacyUiFactory.PixelButton(_window, "Phantom", 965, 409, 165, 34, CreatePhantom);
            LegacyUiFactory.PixelButton(_window, "Rush", 785, 452, 345, 36, RushSelected);

            if (_war != null)
                LegacyUiFactory.PixelLabel(_window, "Round " + _war.round + "   Side " + _war.side + "   World " + _war.worldId, 14, TextAnchor.MiddleCenter, new Color(.8f, .76f, .66f), 785, 510, 345, 28);
        }

        void ToggleGeneral(int generalId)
        {
            if (!_selectedGenerals.Add(generalId)) _selectedGenerals.Remove(generalId);
            Draw();
        }

        int[] RequireSelected()
        {
            var ids = _selectedGenerals.OrderBy(x => x).ToArray();
            if (ids.Length == 0) throw new Exception("Chọn ít nhất một võ tướng.");
            return ids;
        }

        int RequireSingle()
        {
            var ids = RequireSelected();
            if (ids.Length != 1) throw new Exception("Thao tác này cần chọn đúng một võ tướng.");
            return ids[0];
        }

        int RequireCity()
        {
            if (_cityId == 0) throw new Exception("Chọn thành đích.");
            return _cityId;
        }

        long CurrentBattleId()
        {
            if (_war == null) return 0;
            var selected = _selectedGenerals;
            var deployments = _war.deployments ?? Array.Empty<KfgzDeploymentView>();
            var d = deployments.FirstOrDefault(x => x.playerId == _player.id && x.state == 3 && x.battleId > 0 && (selected.Count == 0 || selected.Contains(x.generalId)));
            return d != null ? d.battleId : 0;
        }

        async void Signup()
        {
            if (_busy) return; _busy = true;
            try { _view = await _api.SignupKfgzAsync(); _status("Đã đăng ký KFGZ."); await RefreshAsync(); }
            catch (Exception ex) { _status(ex.Message); }
            finally { _busy = false; }
        }

        async void MoveSelected()
        {
            if (_busy) return;
            int cityId;
            int[] ids;
            try { cityId = RequireCity(); ids = RequireSelected(); } catch (Exception ex) { _status(ex.Message); return; }
            _busy = true;
            try
            {
                foreach (var id in ids) await _api.MoveKfgzAsync(id, cityId);
                _status("Đã điều quân KFGZ."); await RefreshAsync();
            }
            catch (Exception ex) { _status(ex.Message); }
            finally { _busy = false; }
        }

        async void CallSelected()
        {
            if (_busy) return;
            int cityId;
            int[] ids;
            try { cityId = RequireCity(); ids = RequireSelected(); } catch (Exception ex) { _status(ex.Message); return; }
            _busy = true;
            try { var r = await _ext.CallGeneralsAsync(cityId, ids); _status("Gọi tướng: " + (r.movedGeneralIds?.Length ?? 0) + " thành công, " + (r.failed?.Length ?? 0) + " thất bại."); await RefreshAsync(); }
            catch (Exception ex) { _status(ex.Message); }
            finally { _busy = false; }
        }

        async void LoadCallable()
        {
            if (_busy) return;
            int cityId;
            try { cityId = RequireCity(); } catch (Exception ex) { _status(ex.Message); return; }
            _busy = true;
            try { var r = await _ext.GetCallGeneralsAsync(cityId); _status("Có thể gọi về: " + string.Join(",", r.generalIds ?? Array.Empty<int>())); }
            catch (Exception ex) { _status(ex.Message); }
            finally { _busy = false; }
        }

        async void RetreatSelected()
        {
            if (_busy) return;
            int cityId;
            int[] ids;
            try { cityId = RequireCity(); ids = RequireSelected(); } catch (Exception ex) { _status(ex.Message); return; }
            _busy = true;
            try { foreach (var id in ids) await _api.RetreatKfgzAsync(new[] { id }, cityId); _status("Đã gửi lệnh rút lui."); await RefreshAsync(); }
            catch (Exception ex) { _status(ex.Message); }
            finally { _busy = false; }
        }

        async void StartMubing()
        {
            if (_busy) return;
            int id;
            try { id = RequireSingle(); } catch (Exception ex) { _status(ex.Message); return; }
            _busy = true;
            try { var r = await _ext.StartMubingAsync(id); _status("Mubing tướng " + r.generalId + ": " + (r.active ? "đang chạy" : "đã dừng") + ", lực " + r.forces + "/" + r.maxForces); await RefreshAsync(); }
            catch (Exception ex) { _status(ex.Message); }
            finally { _busy = false; }
        }

        async void FastRecruit()
        {
            if (_busy) return;
            int id;
            try { id = RequireSingle(); } catch (Exception ex) { _status(ex.Message); return; }
            _busy = true;
            try { var r = await _ext.FastRecruitAsync(id); _status("Tuyển nhanh +" + r.healed + " lực, tốn token " + r.recruitTokenSpent + ", vàng " + r.goldSpent + "."); await RefreshAsync(); }
            catch (Exception ex) { _status(ex.Message); }
            finally { _busy = false; }
        }

        async void OpenBattle()
        {
            var battleId = CurrentBattleId();
            if (battleId <= 0) { _status("Không có battle KFGZ đang hoạt động cho tướng đã chọn."); return; }
            var root = GetComponentInParent<RectTransform>();
            var entry = GetComponentInParent<FirstPlayableEntry>();
            if (entry != null) BattlePanel.Open(root, _api, battleId, entry.SetStatus);
        }

        async void CreatePhantom()
        {
            if (_busy) return;
            var battleId = CurrentBattleId();
            if (battleId <= 0) { _status("Không có battle để tạo Phantom."); return; }
            _busy = true;
            try { var r = await _ext.CreatePhantomAsync(battleId); _status("Phantom #" + r.phantomUnitId + (r.usedFree ? " dùng lượt miễn phí." : " tốn " + r.goldCost + " vàng.")); await RefreshAsync(); }
            catch (Exception ex) { _status(ex.Message); }
            finally { _busy = false; }
        }

        async void RushSelected()
        {
            if (_busy) return;
            var battleId = CurrentBattleId();
            if (battleId <= 0) { _status("Không có battle nguồn để Rush."); return; }
            int cityId;
            int[] ids;
            try { cityId = RequireCity(); ids = RequireSelected(); } catch (Exception ex) { _status(ex.Message); return; }
            _busy = true;
            try { var r = await _ext.RushAsync(battleId, ids, cityId); _status("Rush sang thành " + r.targetCityId + (r.captured ? " và đã chiếm thành." : ".")); await RefreshAsync(); }
            catch (Exception ex) { _status(ex.Message); }
            finally { _busy = false; }
        }
    }
}
