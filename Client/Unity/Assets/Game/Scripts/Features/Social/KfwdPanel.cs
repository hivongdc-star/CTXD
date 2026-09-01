using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CTXD.Client.Features.Battle;
using CTXD.Client.Networking;
using UnityEngine;
using UnityEngine.UI;

namespace CTXD.Client.Features.Social
{
    public sealed class KfwdPanel : MonoBehaviour
    {
        enum WindowMode { None, Match, Formation, Ranking, Reward }

        ApiClient _api;
        Action<string> _status;
        RectTransform _host;
        RectTransform _root;
        CrossServerLegacyVisuals.MapSurface _map;
        KfwdView _view;
        GeneralRosterResponse _roster;
        KfwdRanking _ranking;
        KfwdRewardView _reward;
        readonly HashSet<int> _selected = new HashSet<int>();
        int _selectedCityId;
        WindowMode _windowMode;
        bool _busy;

        public static KfwdPanel Open(RectTransform host, ApiClient api, Action<string> status)
        {
            var go = new GameObject("KfwdPanel");
            go.transform.SetParent(host, false);
            var panel = go.AddComponent<KfwdPanel>();
            panel._api = api;
            panel._status = status;
            panel._host = host;
            panel.Build();
            _ = panel.Refresh();
            return panel;
        }

        void Build()
        {
            _root = CrossServerLegacyVisuals.Root(transform, "KfWorldScene", Color.black);
            CrossServerLegacyVisuals.Label(_root, "Đang đồng bộ KFWD...", 440, 360, 400, 30, 16, TextAnchor.MiddleCenter, Color.white, true);
        }

        async Task Refresh()
        {
            if (_busy) return;
            _busy = true;
            try
            {
                _view = await _api.GetKfwdAsync();
                _roster = await _api.GetGeneralsAsync();
                try { _ranking = await _api.GetKfwdRankingAsync(); } catch { _ranking = null; }
                try { _reward = await _api.GetKfwdRewardsAsync(); } catch { _reward = null; }
                Draw();
            }
            catch (Exception ex)
            {
                _status(ex.Message);
                Destroy(gameObject);
            }
            finally { _busy = false; }
        }

        void Draw()
        {
            if (_root == null || _view == null) return;
            CrossServerLegacyVisuals.DestroyChildren(_root);
            DrawWorldSurface();
            DrawLegacyChrome();
            DrawFinishOverlay();
            if (_windowMode != WindowMode.None) DrawWindow();
        }

        void DrawWorldSurface()
        {
            // KFWD public state has no authoritative KfWorld worldId/city state contract.
            // Legacy world 1 is rendered as the recovered KfWorld scene; city clicks are local presentation only.
            _map = CrossServerLegacyVisuals.BuildWorldMap(_root, 1, 140, 84, 1000, 600);
            if (_map == null) return;

            var cities = _map.definition.cities;
            for (var i = 0; i < cities.Count; i++)
            {
                var city = cities[i];
                CrossServerLegacyVisuals.AddCityModel(_map, city);
                if (city.id == _selectedCityId) CrossServerLegacyVisuals.AddSelectionLight(_map, city);
                var captured = city.id;
                CrossServerLegacyVisuals.HitArea(_map.content, city.x - 42, city.y - 48, 84, 92, () => SelectCity(captured));
                CrossServerLegacyVisuals.Label(_map.content, city.name, city.x - 70, city.y + 40, 140, 20, 12,
                    TextAnchor.MiddleCenter, new Color(1f, .88f, .57f), true).raycastTarget = false;
            }

            if (_selectedCityId != 0)
            {
                var city = _map.definition.FindCity(_selectedCityId);
                if (city != null) CrossServerLegacyVisuals.Focus(_map, city.x, city.y);
            }
            else
            {
                var focus = _map.definition.FindCity(13) ?? _map.definition.cities.FirstOrDefault();
                if (focus != null) CrossServerLegacyVisuals.Focus(_map, focus.x, focus.y);
            }
        }

        void DrawFinishOverlay()
        {
            var match = _view.match;
            if (match == null || match.state != 2 || match.winnerPlayerId <= 0) return;
            var won = match.winnerPlayerId == _view.competitorId;
            if (won)
            {
                CrossServerLegacyVisuals.AddTimeline(_root, "KFWD", "world.win.fx", 10, 540, 284, 200, 200);
                CrossServerLegacyVisuals.AtlasImage(_root, "KFWD", "world.win.title", 457, 347, 366, 75);
            }
            else
            {
                CrossServerLegacyVisuals.AtlasImage(_root, "KFWD", "world.lose.title", 457, 347, 366, 75);
            }
            CrossServerLegacyVisuals.Label(_root, "Winner #" + match.winnerPlayerId, 550, 439, 180, 24, 14,
                TextAnchor.MiddleCenter, new Color(.81f, .74f, .51f), true);
        }

        void DrawLegacyChrome()
        {
            CrossServerLegacyVisuals.AtlasImage(_root, "KFWD", "world.vsbg", 379, 34, 522, 67);
            var match = _view.match;
            var left = _view.competitorId > 0 ? "#" + _view.competitorId : "--";
            var right = match != null && match.opponentPlayerId > 0 ? "#" + match.opponentPlayerId : "--";
            CrossServerLegacyVisuals.Label(_root, left, 283, 46, 205, 20, 14, TextAnchor.MiddleRight, new Color(.95f, .69f, .22f), true);
            CrossServerLegacyVisuals.Label(_root, right, 792, 46, 205, 20, 14, TextAnchor.MiddleLeft, new Color(.95f, .69f, .22f), true);
            CrossServerLegacyVisuals.Label(_root, _view.wins.ToString(), 604, 39, 25, 22, 16, TextAnchor.MiddleCenter, Color.white, true);
            CrossServerLegacyVisuals.Label(_root, _view.losses.ToString(), 666, 39, 25, 22, 16, TextAnchor.MiddleCenter, Color.white, true);
            CrossServerLegacyVisuals.Label(_root, string.IsNullOrEmpty(_view.nextStateAt) ? string.Empty : _view.nextStateAt,
                579, 78, 125, 18, 11, TextAnchor.MiddleCenter, new Color(.81f, .74f, .51f));

            CrossServerLegacyVisuals.ResourceImage(_root, "LegacyVisual/KFWD/map1_smallMap", 1120, 35, 150, 90);
            CrossServerLegacyVisuals.CloseButton(_root, 1234, 10, () => Destroy(gameObject));

            // Recovered KfWorld bottom command group. These actions use existing public contracts only.
            CrossServerLegacyVisuals.SkinButton(_root, "Button23", "Đối chiến", 475, 704, 105, 32, () => OpenWindow(WindowMode.Match));
            CrossServerLegacyVisuals.SkinButton(_root, "Button23", "Xếp hạng", 588, 704, 105, 32, () => OpenWindow(WindowMode.Ranking));
            CrossServerLegacyVisuals.SkinButton(_root, "Button23", "Thưởng", 701, 704, 105, 32, () => OpenWindow(WindowMode.Reward));

            if (!_view.signed && _view.globalState == 20)
                CrossServerLegacyVisuals.SkinButton(_root, "Button23", "Đăng ký", 814, 704, 105, 32, () => OpenWindow(WindowMode.Formation));
            else if (_view.signed && !_view.synced && _view.globalState == 50)
                CrossServerLegacyVisuals.SkinButton(_root, "Button23", "Đồng bộ", 814, 704, 105, 32, () => OpenWindow(WindowMode.Formation));

            CrossServerLegacyVisuals.Label(_root,
                "KFWD city interaction: visual-only (backend chưa expose KfWorld city/world contract)",
                150, 742, 820, 18, 11, TextAnchor.MiddleLeft, new Color(.66f, .63f, .57f));

            if (_selectedCityId != 0)
            {
                var city = _map != null ? _map.definition.FindCity(_selectedCityId) : null;
                if (city != null)
                {
                    CrossServerLegacyVisuals.AtlasImage(_root, "KFWD", "world.downbg", 150, 106, 631, 30);
                    CrossServerLegacyVisuals.Label(_root, city.name, 162, 110, 180, 20, 14, TextAnchor.MiddleLeft, new Color(1f, .86f, .52f), true);
                    CrossServerLegacyVisuals.Label(_root, "City action blocked: thiếu authoritative contract", 350, 110, 405, 20, 11, TextAnchor.MiddleLeft, Color.gray);
                }
            }
        }

        void SelectCity(int cityId)
        {
            _selectedCityId = cityId;
            Draw();
        }

        void OpenWindow(WindowMode mode)
        {
            _windowMode = mode;
            Draw();
        }

        void CloseWindow()
        {
            _windowMode = WindowMode.None;
            Draw();
        }

        void DrawWindow()
        {
            var modal = CrossServerLegacyVisuals.Root(_root, "KfWorldWarWindowModal", new Color(0f, 0f, 0f, .58f));
            CrossServerLegacyVisuals.AtlasImage(modal, "KFWD", "war.window.bg", 193, 180, 894, 409);
            CrossServerLegacyVisuals.CloseButton(modal, 1044, 193, CloseWindow);

            switch (_windowMode)
            {
                case WindowMode.Match: DrawMatchWindow(modal); break;
                case WindowMode.Formation: DrawFormationWindow(modal); break;
                case WindowMode.Ranking: DrawRankingWindow(modal); break;
                case WindowMode.Reward: DrawRewardWindow(modal); break;
            }
        }

        void DrawMatchWindow(RectTransform modal)
        {
            CrossServerLegacyVisuals.AtlasImage(modal, "KFWD", "war.window.playing.title", 543, 156, 180, 54);
            CrossServerLegacyVisuals.AtlasImage(modal, "KFWD", "war.window.playingbg", 275, 246, 730, 246);
            var match = _view.match;
            if (match == null)
            {
                CrossServerLegacyVisuals.Label(modal, "Chưa có đối trận authoritative.", 390, 335, 500, 30, 16, TextAnchor.MiddleCenter, new Color(.82f, .75f, .63f), true);
                return;
            }

            CrossServerLegacyVisuals.AtlasImage(modal, "KFWD", "war.window.vs", 623, 332, 35, 37);
            CrossServerLegacyVisuals.Label(modal, "#" + _view.competitorId, 325, 302, 240, 28, 16, TextAnchor.MiddleCenter, new Color(.95f, .81f, .48f), true);
            CrossServerLegacyVisuals.Label(modal, "#" + match.opponentPlayerId, 715, 302, 240, 28, 16, TextAnchor.MiddleCenter, new Color(.95f, .81f, .48f), true);
            CrossServerLegacyVisuals.Label(modal, "Round " + match.round, 585, 242, 110, 20, 12, TextAnchor.MiddleCenter, new Color(1f, .81f, 0f), true);
            CrossServerLegacyVisuals.Label(modal, string.IsNullOrEmpty(match.startsAt) ? match.deadlineAt : match.startsAt,
                433, 530, 413, 35, 16, TextAnchor.MiddleCenter, new Color(1f, 1f, .81f), true);

            if (match.state == 1 && match.battleId > 0)
            {
                var battleId = match.battleId;
                CrossServerLegacyVisuals.SkinButton(modal, "Button23", "Vào chiến đấu", 560, 478, 160, 36,
                    () => BattlePanel.Open(_host, _api, _status, battleId));
            }
            else if (match.state == 2)
            {
                var won = match.winnerPlayerId != 0 && match.winnerPlayerId == _view.competitorId;
                CrossServerLegacyVisuals.AtlasImage(modal, "KFWD", won ? "war.window.win" : "war.window.lose", 590, 394, 100, 58);
                CrossServerLegacyVisuals.Label(modal, "Winner #" + match.winnerPlayerId, 490, 461, 300, 25, 14, TextAnchor.MiddleCenter, new Color(.95f, .79f, .38f), true);
            }
        }

        void DrawFormationWindow(RectTransform modal)
        {
            CrossServerLegacyVisuals.AtlasImage(modal, "KFWD", "war.window.selection", 298, 190, 684, 39);
            CrossServerLegacyVisuals.Label(modal, _view.signed ? "ĐỒNG BỘ VÕ TƯỚNG" : "ĐĂNG KÝ VÕ TƯỚNG",
                430, 205, 420, 28, 18, TextAnchor.MiddleCenter, new Color(1f, .82f, .35f), true);

            var generals = _roster != null ? (_roster.military ?? Array.Empty<GeneralView>()) : Array.Empty<GeneralView>();
            for (var i = 0; i < Math.Min(10, generals.Length); i++)
            {
                var general = generals[i];
                var col = i % 2;
                var row = i / 2;
                var x = 330 + col * 315;
                var y = 270 + row * 48;
                CrossServerLegacyVisuals.AtlasImage(modal, "KFWD", "world.general.bg", x, y, 76, 23);
                var selected = _selected.Contains(general.id);
                CrossServerLegacyVisuals.Label(modal, general.name + " Lv" + general.level,
                    x + 82, y - 2, 205, 27, 14, TextAnchor.MiddleLeft, selected ? new Color(1f, .87f, .38f) : Color.white, true);
                var id = general.id;
                CrossServerLegacyVisuals.HitArea(modal, x, y - 3, 287, 32, () => ToggleGeneral(id));
            }

            var submit = CrossServerLegacyVisuals.SkinButton(modal, "Button23", _view.signed ? "Đồng bộ" : "Đăng ký",
                555, 526, 170, 36, () => Submit(_view.signed));
            submit.interactable = _selected.Count > 0 && !_busy;
        }

        void ToggleGeneral(int generalId)
        {
            if (!_selected.Add(generalId)) _selected.Remove(generalId);
            Draw();
        }

        void DrawRankingWindow(RectTransform modal)
        {
            CrossServerLegacyVisuals.AtlasImage(modal, "KFWD", "war.window.rank.title", 598, 192, 84, 54);
            CrossServerLegacyVisuals.Label(modal, "Hạng", 295, 270, 70, 24, 13, TextAnchor.MiddleCenter, new Color(.94f, .79f, .34f), true);
            CrossServerLegacyVisuals.Label(modal, "Người chơi", 390, 270, 220, 24, 13, TextAnchor.MiddleLeft, new Color(.94f, .79f, .34f), true);
            CrossServerLegacyVisuals.Label(modal, "Điểm", 650, 270, 100, 24, 13, TextAnchor.MiddleCenter, new Color(.94f, .79f, .34f), true);
            CrossServerLegacyVisuals.Label(modal, "Thắng", 775, 270, 85, 24, 13, TextAnchor.MiddleCenter, new Color(.94f, .79f, .34f), true);
            CrossServerLegacyVisuals.Label(modal, "Vé", 885, 270, 70, 24, 13, TextAnchor.MiddleCenter, new Color(.94f, .79f, .34f), true);

            var items = _ranking != null ? (_ranking.items ?? Array.Empty<KfwdRankEntry>()) : Array.Empty<KfwdRankEntry>();
            for (var i = 0; i < Math.Min(8, items.Length); i++)
            {
                var row = items[i];
                var y = 303 + i * 31;
                CrossServerLegacyVisuals.Label(modal, row.rank.ToString(), 295, y, 70, 22, 13, TextAnchor.MiddleCenter, Color.white);
                CrossServerLegacyVisuals.Label(modal, string.IsNullOrEmpty(row.name) ? "#" + row.playerId : row.name, 390, y, 220, 22, 13, TextAnchor.MiddleLeft, Color.white);
                CrossServerLegacyVisuals.Label(modal, row.score.ToString(), 650, y, 100, 22, 13, TextAnchor.MiddleCenter, Color.white);
                CrossServerLegacyVisuals.Label(modal, row.wins.ToString(), 775, y, 85, 22, 13, TextAnchor.MiddleCenter, Color.white);
                CrossServerLegacyVisuals.Label(modal, row.tickets.ToString(), 885, y, 70, 22, 13, TextAnchor.MiddleCenter, Color.white);
            }
            if (items.Length == 0)
                CrossServerLegacyVisuals.Label(modal, "Ranking chưa có dữ liệu.", 390, 365, 500, 30, 15, TextAnchor.MiddleCenter, Color.gray);
        }

        void DrawRewardWindow(RectTransform modal)
        {
            CrossServerLegacyVisuals.AtlasImage(modal, "KFWD", "war.window.endbg", 272, 244, 736, 36);
            CrossServerLegacyVisuals.Label(modal, "KFWD REWARD", 480, 203, 320, 30, 18, TextAnchor.MiddleCenter, new Color(1f, .82f, .35f), true);
            if (_reward == null)
            {
                CrossServerLegacyVisuals.Label(modal, "Reward state chưa khả dụng.", 390, 345, 500, 30, 15, TextAnchor.MiddleCenter, Color.gray);
                return;
            }

            var days = _reward.days ?? Array.Empty<KfwdDayRewardView>();
            for (var i = 0; i < Math.Min(days.Length, 6); i++)
            {
                var day = days[i];
                var y = 296 + i * 35;
                CrossServerLegacyVisuals.Label(modal,
                    "Ngày " + day.day + "    Hạng " + day.rank + "    Vé " + day.tickets + (day.granted ? "    Đã nhận" : string.Empty),
                    395, y, 490, 24, 14, TextAnchor.MiddleLeft, day.granted ? new Color(.73f, .91f, .59f) : Color.white);
            }

            var treasure = _reward.treasure;
            var text = treasure != null
                ? "Bảo vật: " + treasure.name + "  Q" + treasure.quality + "  L" + treasure.leadership + "  S" + treasure.strength
                : _reward.treasureClaimAvailable ? "Bảo vật đang chờ nhận" : "Bảo vật chưa khả dụng";
            CrossServerLegacyVisuals.Label(modal, text, 395, 515, 490, 28, 14, TextAnchor.MiddleLeft, new Color(.95f, .79f, .38f), true);
            if (_reward.treasureClaimAvailable && !_reward.treasureClaimed)
                CrossServerLegacyVisuals.SkinButton(modal, "Button23", "Nhận bảo vật", 860, 510, 140, 34, ClaimTreasure);
        }

        async void ClaimTreasure()
        {
            if (_busy) return;
            _busy = true;
            try
            {
                var treasure = await _api.ClaimKfwdTreasureAsync();
                _status(treasure != null ? "KFWD treasure: " + treasure.name : "Đã nhận bảo vật KFWD.");
                await RefreshAfterAction();
            }
            catch (Exception ex) { _status(ex.Message); }
            finally { _busy = false; }
        }

        async void Submit(bool sync)
        {
            if (_busy) return;
            var ids = _selected.OrderBy(x => x).ToArray();
            if (ids.Length == 0) { _status("Chọn ít nhất một võ tướng."); return; }
            _busy = true;
            try
            {
                _view = sync ? await _api.SyncKfwdAsync(ids) : await _api.SignupKfwdAsync(ids);
                _status(sync ? "KFWD formation synced." : "KFWD signup complete.");
                _windowMode = WindowMode.Match;
                await RefreshAfterAction();
            }
            catch (Exception ex) { _status(ex.Message); }
            finally { _busy = false; }
        }

        async Task RefreshAfterAction()
        {
            _busy = false;
            await Refresh();
            _busy = true;
        }
    }
}
