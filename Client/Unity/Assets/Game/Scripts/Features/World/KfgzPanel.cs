using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CTXD.Client.Features.Battle;
using CTXD.Client.Features.Social;
using CTXD.Client.Networking;
using UnityEngine;
using UnityEngine.UI;

namespace CTXD.Client.Features.World
{
    public sealed class KfgzPanel : MonoBehaviour
    {
        const int LegacyClaimLimit = 4;
        static KfgzPanel _open;
        enum WindowMode { None, Ranking, Rewards }

        ApiClient _api;
        KfgzExtendedApi _ext;
        Action<string> _status;
        RectTransform _host;
        RectTransform _root;
        CrossServerLegacyVisuals.MapSurface _map;
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
        WindowMode _windowMode;

        public static KfgzPanel Open(RectTransform host, ApiClient api, Action<string> status)
        {
            if (_open != null) Destroy(_open.gameObject);
            var go = new GameObject("KfgzPanel");
            go.transform.SetParent(host, false);
            var panel = go.AddComponent<KfgzPanel>();
            panel._api = api;
            panel._ext = new KfgzExtendedApi(api);
            panel._status = status;
            panel._host = host;
            _open = panel;
            panel.Build();
            _ = panel.RefreshAsync();
            return panel;
        }

        public static void RefreshOpenFromPush()
        {
            if (_open != null && !_open._busy) _ = _open.RefreshAsync();
        }

        void OnDestroy()
        {
            if (_open == this) _open = null;
        }

        void Update()
        {
            if (_busy || _api == null || string.IsNullOrEmpty(_api.Token) || Time.unscaledTime < _nextRefresh) return;
            _nextRefresh = Time.unscaledTime + 10f;
            _ = RefreshAsync();
        }

        void Build()
        {
            _root = CrossServerLegacyVisuals.Root(transform, "KfgzKfWorldScene", Color.black);
            CrossServerLegacyVisuals.Label(_root, "Đang đồng bộ KFGZ...", 440, 360, 400, 30, 16, TextAnchor.MiddleCenter, Color.white, true);
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
                            var own = (_war.deployments ?? Array.Empty<KfgzDeploymentView>()).FirstOrDefault(x => _player != null && x.playerId == _player.id);
                            _cityId = own != null ? own.cityId : ((_war.cities ?? Array.Empty<KfgzCityView>()).FirstOrDefault()?.id ?? 0);
                        }
                        try { _roundReward = await _ext.GetRoundRewardAsync(_war.roundId); }
                        catch (Exception ex) { _roundRewardError = ex.Message; }
                    }
                    else _roundRewardError = "KFGZ world state chưa trả roundId authoritative.";
                }
                else _roundRewardError = "Round reward cần người chơi đã tham gia KFGZ.";

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
            if (_root == null || _view == null) return;
            CrossServerLegacyVisuals.DestroyChildren(_root);
            DrawWorldSurface();
            DrawChrome();
            DrawFinishOverlay();
            if (_windowMode != WindowMode.None) DrawWindow();
        }

        void DrawWorldSurface()
        {
            var worldId = _war != null && (_war.worldId == 1 || _war.worldId == 2) ? _war.worldId : 1;
            _map = CrossServerLegacyVisuals.BuildWorldMap(_root, worldId, 140, 84, 1000, 600);
            if (_map == null) return;

            var stateCities = _war != null ? (_war.cities ?? Array.Empty<KfgzCityView>()) : Array.Empty<KfgzCityView>();
            if (stateCities.Length == 0)
            {
                // Pre-round/signed state: show recovered map without inventing ownership/state.
                for (var i = 0; i < _map.definition.cities.Count; i++) CrossServerLegacyVisuals.AddCityModel(_map, _map.definition.cities[i]);
                return;
            }

            for (var i = 0; i < stateCities.Length; i++)
            {
                var state = stateCities[i];
                var city = _map.definition.FindCity(state.id);
                if (city == null) continue;
                CrossServerLegacyVisuals.AddCityModel(_map, city);
                if (city.id == _cityId) CrossServerLegacyVisuals.AddSelectionLight(_map, city);
                var id = city.id;
                CrossServerLegacyVisuals.HitArea(_map.content, city.x - 42, city.y - 48, 84, 92, () => SelectCity(id));
                var owner = state.ownerSide == 1 ? "P1" : state.ownerSide == 2 ? "P2" : "--";
                var fighting = (_war.battles ?? Array.Empty<KfgzBattleView>()).Any(x => x.cityId == state.id && x.state == 1);
                CrossServerLegacyVisuals.Label(_map.content, city.name + "  " + owner,
                    city.x - 90, city.y + 40, 180, 20, 12, TextAnchor.MiddleCenter, new Color(1f, .88f, .57f), true).raycastTarget = false;
                if (fighting)
                    CrossServerLegacyVisuals.AtlasImage(_map.content, "KFWD", "war.window.vs", city.x + 40, city.y - 18, 35, 37);
            }

            var selected = _map.definition.FindCity(_cityId);
            if (selected != null) CrossServerLegacyVisuals.Focus(_map, selected.x, selected.y);
        }

        void DrawFinishOverlay()
        {
            if (_war == null || _war.winnerSide <= 0 || _war.side <= 0) return;
            var won = _war.winnerSide == _war.side;
            if (won)
            {
                CrossServerLegacyVisuals.AddTimeline(_root, "KFWD", "world.win.fx", 10, 540, 284, 200, 200);
                CrossServerLegacyVisuals.AtlasImage(_root, "KFWD", "world.win.title", 457, 347, 366, 75);
            }
            else
            {
                CrossServerLegacyVisuals.AtlasImage(_root, "KFWD", "world.lose.title", 457, 347, 366, 75);
            }
            CrossServerLegacyVisuals.Label(_root, "Winner Side " + _war.winnerSide, 550, 439, 180, 24, 14,
                TextAnchor.MiddleCenter, new Color(.81f, .74f, .51f), true);
        }

        void DrawChrome()
        {
            CrossServerLegacyVisuals.AtlasImage(_root, "KFWD", "world.vsbg", 379, 34, 522, 67);
            var left = _war != null ? _war.force1.ToString() : _view.forceId.ToString();
            var right = _war != null ? _war.force2.ToString() : "--";
            CrossServerLegacyVisuals.Label(_root, left, 283, 46, 205, 20, 14, TextAnchor.MiddleRight, new Color(.95f, .69f, .22f), true);
            CrossServerLegacyVisuals.Label(_root, right, 792, 46, 205, 20, 14, TextAnchor.MiddleLeft, new Color(.95f, .69f, .22f), true);
            CrossServerLegacyVisuals.Label(_root, (_war != null ? _war.side1Cities : 0).ToString(), 604, 39, 25, 22, 16, TextAnchor.MiddleCenter, Color.white, true);
            CrossServerLegacyVisuals.Label(_root, (_war != null ? _war.side2Cities : 0).ToString(), 666, 39, 25, 22, 16, TextAnchor.MiddleCenter, Color.white, true);
            CrossServerLegacyVisuals.Label(_root, _war != null ? "Round " + _war.round : "Season " + _view.seasonNo,
                579, 78, 125, 18, 11, TextAnchor.MiddleCenter, new Color(.81f, .74f, .51f));

            var worldId = _war != null && _war.worldId == 2 ? 2 : 1;
            CrossServerLegacyVisuals.ResourceImage(_root, "LegacyVisual/KFWD/map" + worldId + "_smallMap", 1120, 35, 150, 90);
            CrossServerLegacyVisuals.CloseButton(_root, 1234, 10, () => Destroy(gameObject));

            var resource = _view.resources;
            var extra = _battleResources;
            CrossServerLegacyVisuals.AtlasImage(_root, "KFWD", "world.downbg", 150, 106, 631, 30);
            CrossServerLegacyVisuals.Label(_root,
                "Gold " + (resource != null ? resource.gold : 0) + "   Food " + (resource != null ? resource.food : 0) +
                "   Iron " + (resource != null ? resource.iron : 0) + "   Token " + (extra != null ? extra.recruitToken : 0) +
                "   Phantom " + (extra != null ? extra.phantomCount : 0),
                160, 111, 580, 22, 12, TextAnchor.MiddleLeft, new Color(.9f, .84f, .66f), true);

            if (!_view.signed)
            {
                CrossServerLegacyVisuals.SkinButton(_root, "Button23", "Đăng ký KFGZ", 560, 640, 160, 38, Signup);
                return;
            }

            DrawGeneralCards();
            DrawActionStrip();
            CrossServerLegacyVisuals.SkinButton(_root, "Button23", "Xếp hạng", 1010, 704, 105, 32, () => { _windowMode = WindowMode.Ranking; Draw(); });
            CrossServerLegacyVisuals.SkinButton(_root, "Button23", "Thưởng", 1120, 704, 105, 32, () => { _windowMode = WindowMode.Rewards; Draw(); });
        }

        void DrawGeneralCards()
        {
            var generals = _view.generals ?? Array.Empty<KfgzGeneralStateView>();
            for (var i = 0; i < Math.Min(6, generals.Length); i++)
            {
                var g = generals[i];
                var roster = (_roster != null ? _roster.military : null) ?? Array.Empty<GeneralView>();
                var info = roster.FirstOrDefault(x => x.id == g.generalId);
                var name = info != null ? info.name : "#" + g.generalId;
                var x = 150 + i * 145;
                var y = 645;
                CrossServerLegacyVisuals.AtlasImage(_root, "KFWD", "world.general.bg", x, y, 76, 23);
                CrossServerLegacyVisuals.Label(_root, name,
                    x, y + 23, 135, 18, 11, TextAnchor.MiddleCenter, _selectedGenerals.Contains(g.generalId) ? new Color(1f, .84f, .31f) : Color.white, true);
                CrossServerLegacyVisuals.Label(_root, "HP " + g.forces, x, y + 40, 135, 16, 10, TextAnchor.MiddleCenter, new Color(.82f, .77f, .66f));
                var id = g.generalId;
                CrossServerLegacyVisuals.HitArea(_root, x - 4, y - 4, 140, 62, () => ToggleGeneral(id));
            }
        }

        void DrawActionStrip()
        {
            CrossServerLegacyVisuals.SkinButton(_root, "Button23", "Đi thành", 150, 704, 90, 32, MoveSelected);
            CrossServerLegacyVisuals.SkinButton(_root, "Button23", "Gọi tướng", 245, 704, 90, 32, CallSelected);
            CrossServerLegacyVisuals.SkinButton(_root, "Button23", "Có thể gọi", 340, 704, 90, 32, LoadCallable);
            CrossServerLegacyVisuals.SkinButton(_root, "Button23", "Rút lui", 435, 704, 90, 32, RetreatSelected);
            CrossServerLegacyVisuals.SkinButton(_root, "Button23", "Tuyển", 530, 704, 90, 32, StartMubing);
            CrossServerLegacyVisuals.SkinButton(_root, "Button23", "Tuyển nhanh", 625, 704, 90, 32, FastRecruit);
            CrossServerLegacyVisuals.SkinButton(_root, "Button23", "Battle", 720, 704, 90, 32, OpenBattle);
            CrossServerLegacyVisuals.SkinButton(_root, "Button23", "Phantom", 815, 704, 90, 32, CreatePhantom);
            CrossServerLegacyVisuals.SkinButton(_root, "Button23", "Rush", 910, 704, 90, 32, RushSelected);
        }

        void SelectCity(int cityId)
        {
            _cityId = cityId;
            Draw();
        }

        void ToggleGeneral(int generalId)
        {
            if (!_selectedGenerals.Add(generalId)) _selectedGenerals.Remove(generalId);
            Draw();
        }

        void DrawWindow()
        {
            var modal = CrossServerLegacyVisuals.Root(_root, "KfgzWarWindowModal", new Color(0f, 0f, 0f, .58f));
            CrossServerLegacyVisuals.AtlasImage(modal, "KFWD", "war.window.bg", 193, 180, 894, 409);
            CrossServerLegacyVisuals.CloseButton(modal, 1044, 193, () => { _windowMode = WindowMode.None; Draw(); });
            if (_windowMode == WindowMode.Ranking) DrawRanking(modal); else DrawRewards(modal);
        }

        void DrawRanking(RectTransform modal)
        {
            CrossServerLegacyVisuals.AtlasImage(modal, "KFWD", "war.window.rank.title", 598, 192, 84, 54);
            if (!string.IsNullOrEmpty(_rankingError))
            {
                CrossServerLegacyVisuals.Label(modal, _rankingError, 350, 350, 580, 40, 14, TextAnchor.MiddleCenter, Color.gray);
                return;
            }
            var items = _ranking != null ? (_ranking.items ?? Array.Empty<KfgzRankEntry>()) : Array.Empty<KfgzRankEntry>();
            CrossServerLegacyVisuals.Label(modal, "Hạng", 285, 270, 60, 22, 12, TextAnchor.MiddleCenter, new Color(.94f, .79f, .34f), true);
            CrossServerLegacyVisuals.Label(modal, "Người chơi", 355, 270, 210, 22, 12, TextAnchor.MiddleLeft, new Color(.94f, .79f, .34f), true);
            CrossServerLegacyVisuals.Label(modal, "Sát địch", 580, 270, 100, 22, 12, TextAnchor.MiddleCenter, new Color(.94f, .79f, .34f), true);
            CrossServerLegacyVisuals.Label(modal, "Chiếm", 690, 270, 80, 22, 12, TextAnchor.MiddleCenter, new Color(.94f, .79f, .34f), true);
            CrossServerLegacyVisuals.Label(modal, "Solo", 780, 270, 75, 22, 12, TextAnchor.MiddleCenter, new Color(.94f, .79f, .34f), true);
            CrossServerLegacyVisuals.Label(modal, "W/L", 865, 270, 100, 22, 12, TextAnchor.MiddleCenter, new Color(.94f, .79f, .34f), true);
            for (var i = 0; i < Math.Min(items.Length, 8); i++)
            {
                var row = items[i];
                var y = 302 + i * 31;
                CrossServerLegacyVisuals.Label(modal, row.rank.ToString(), 285, y, 60, 22, 12, TextAnchor.MiddleCenter, Color.white);
                CrossServerLegacyVisuals.Label(modal, string.IsNullOrEmpty(row.name) ? "#" + row.playerId : row.name, 355, y, 210, 22, 12, TextAnchor.MiddleLeft, Color.white);
                CrossServerLegacyVisuals.Label(modal, row.killArmy.ToString(), 580, y, 100, 22, 12, TextAnchor.MiddleCenter, Color.white);
                CrossServerLegacyVisuals.Label(modal, row.occupyCity.ToString(), 690, y, 80, 22, 12, TextAnchor.MiddleCenter, Color.white);
                CrossServerLegacyVisuals.Label(modal, row.soloWins.ToString(), 780, y, 75, 22, 12, TextAnchor.MiddleCenter, Color.white);
                CrossServerLegacyVisuals.Label(modal, row.wins + "/" + row.losses, 865, y, 100, 22, 12, TextAnchor.MiddleCenter, Color.white);
            }
            if (items.Length == 0)
                CrossServerLegacyVisuals.Label(modal, "Server chưa trả ranking KFGZ.", 390, 365, 500, 30, 14, TextAnchor.MiddleCenter, Color.gray);
        }

        void DrawRewards(RectTransform modal)
        {
            CrossServerLegacyVisuals.AtlasImage(modal, "KFWD", "war.window.endbg", 272, 244, 736, 36);
            CrossServerLegacyVisuals.Label(modal, "KFGZ REWARD", 480, 203, 320, 30, 18, TextAnchor.MiddleCenter, new Color(1f, .82f, .35f), true);

            if (!string.IsNullOrEmpty(_roundRewardError))
                CrossServerLegacyVisuals.Label(modal, _roundRewardError, 285, 290, 370, 45, 12, TextAnchor.MiddleCenter, Color.gray);
            else if (_roundReward != null && _roundReward.mapped)
            {
                CrossServerLegacyVisuals.Label(modal,
                    "Round #" + _roundReward.referenceId + "  Base " + _roundReward.baseTickets + "  Next " + _roundReward.nextTickets +
                    "  Gold " + _roundReward.goldCost + "  Nhận " + _roundReward.claimTimes + "/4",
                    285, 292, 420, 38, 13, TextAnchor.MiddleLeft, Color.white, true);
                CrossServerLegacyVisuals.Label(modal,
                    "City " + _roundReward.cityTickets + "   Win " + _roundReward.winTickets + "   Kill " + _roundReward.killRankTickets +
                    "   Solo " + _roundReward.soloTickets + "   Occupy " + _roundReward.occupyTickets,
                    285, 334, 420, 30, 12, TextAnchor.MiddleLeft, new Color(.86f, .82f, .7f));
                if (_roundReward.claimTimes < LegacyClaimLimit)
                    CrossServerLegacyVisuals.SkinButton(modal, "Button23", "Nhận round", 435, 382, 140, 34, ClaimRoundReward);
            }
            else CrossServerLegacyVisuals.Label(modal, "Round reward: " + SafeBlocker(_roundReward != null ? _roundReward.blocker : null), 285, 292, 420, 42, 12, TextAnchor.MiddleCenter, Color.gray);

            if (!string.IsNullOrEmpty(_endRewardError))
                CrossServerLegacyVisuals.Label(modal, _endRewardError, 690, 290, 300, 40, 12, TextAnchor.MiddleCenter, Color.gray);
            else if (_endReward != null && _endReward.mapped)
            {
                CrossServerLegacyVisuals.Label(modal, "NationScore " + _endReward.nationScore, 700, 292, 270, 24, 13, TextAnchor.MiddleCenter, Color.white, true);
                var slots = _endReward.slots ?? Array.Empty<KfgzEndRewardSlotView>();
                for (var i = 0; i < Math.Min(4, slots.Length); i++)
                {
                    var slot = slots[i];
                    var y = 326 + i * 46;
                    CrossServerLegacyVisuals.Label(modal, "#" + slot.slot + "  req " + slot.requiredNationScore + "  next " + slot.nextTickets,
                        690, y, 210, 22, 11, TextAnchor.MiddleLeft, slot.available ? Color.white : Color.gray);
                    if (slot.available && slot.claimTimes < LegacyClaimLimit)
                    {
                        var captured = slot.slot;
                        CrossServerLegacyVisuals.SkinButton(modal, "Button23", "Nhận", 905, y - 3, 75, 27, () => ClaimEndReward(captured));
                    }
                }
            }
            else CrossServerLegacyVisuals.Label(modal, "End reward: " + SafeBlocker(_endReward != null ? _endReward.blocker : null), 690, 292, 300, 42, 12, TextAnchor.MiddleCenter, Color.gray);

            if (!string.IsNullOrEmpty(_titleError))
                CrossServerLegacyVisuals.Label(modal, _titleError, 690, 525, 300, 26, 11, TextAnchor.MiddleCenter, Color.gray);
            else
            {
                var titles = _titles != null ? (_titles.items ?? Array.Empty<KfgzTitleView>()) : Array.Empty<KfgzTitleView>();
                if (titles.Length > 0)
                    CrossServerLegacyVisuals.Label(modal, titles[0].playerName + "  " + TitleDisplay(titles[0].titleKey), 690, 525, 300, 26, 12, TextAnchor.MiddleCenter, new Color(.95f, .79f, .38f), true);
            }
        }

        static string SafeBlocker(string blocker)
        {
            return string.IsNullOrEmpty(blocker) ? "authoritative state chưa khả dụng" : blocker;
        }

        static string TitleDisplay(string titleKey)
        {
            return string.Equals(titleKey, "TITLE_KFGZ_1", StringComparison.Ordinal) ? "第一勇士" : titleKey;
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
            if (_war == null || _player == null) return 0;
            var deployments = _war.deployments ?? Array.Empty<KfgzDeploymentView>();
            var d = deployments.FirstOrDefault(x => x.playerId == _player.id && x.state == 3 && x.battleId > 0 &&
                (_selectedGenerals.Count == 0 || _selectedGenerals.Contains(x.generalId)));
            return d != null ? d.battleId : 0;
        }

        async void Signup()
        {
            if (_busy) return;
            _busy = true;
            try { _view = await _api.SignupKfgzAsync(); _status("Đã đăng ký KFGZ."); await RefreshAsync(); }
            catch (Exception ex) { _status(ex.Message); }
            finally { _busy = false; }
        }

        async void MoveSelected()
        {
            if (_busy) return;
            _busy = true;
            try
            {
                var general = RequireSingle();
                var city = RequireCity();
                var targetBattle = (_war != null ? _war.battles : null) ?? Array.Empty<KfgzBattleView>();
                var active = targetBattle.FirstOrDefault(x => x.cityId == city && x.state == 1);
                if (active != null)
                {
                    var result = await _ext.ReinforceAsync(active.battleId, new[] { general });
                    _status("Đã gia nhập battle #" + result.battleId + ".");
                }
                else
                {
                    _war = await _api.MoveKfgzGeneralAsync(general, city);
                    _status("Đã điều tướng trong KFGZ.");
                }
                await RefreshAsync();
            }
            catch (Exception ex) { _status(ex.Message); }
            finally { _busy = false; }
        }

        async void LoadCallable()
        {
            if (_busy) return;
            _busy = true;
            try
            {
                var info = await _ext.GetCallGeneralsAsync(RequireCity());
                _selectedGenerals.Clear();
                foreach (var id in info.generalIds ?? Array.Empty<int>()) _selectedGenerals.Add(id);
                _status("Đã chọn " + _selectedGenerals.Count + " tướng có thể gọi.");
                Draw();
            }
            catch (Exception ex) { _status(ex.Message); }
            finally { _busy = false; }
        }

        async void CallSelected()
        {
            if (_busy) return;
            _busy = true;
            try
            {
                var result = await _ext.CallGeneralsAsync(RequireCity(), RequireSelected());
                _status("Call-general: " + (result.movedGeneralIds != null ? result.movedGeneralIds.Length : 0) + " thành công, " + (result.failed != null ? result.failed.Length : 0) + " thất bại.");
                await RefreshAsync();
            }
            catch (Exception ex) { _status(ex.Message); }
            finally { _busy = false; }
        }

        async void RetreatSelected()
        {
            if (_busy) return;
            _busy = true;
            try { _war = await _api.RetreatKfgzGeneralsAsync(RequireSelected(), RequireCity()); _status("Đã rút quân KFGZ."); await RefreshAsync(); }
            catch (Exception ex) { _status(ex.Message); }
            finally { _busy = false; }
        }

        async void StartMubing()
        {
            if (_busy) return;
            _busy = true;
            try { var result = await _ext.StartMubingAsync(RequireSingle()); _status("Đã bắt đầu tuyển quân: " + result.mubing + "/h"); await RefreshAsync(); }
            catch (Exception ex) { _status(ex.Message); }
            finally { _busy = false; }
        }

        async void FastRecruit()
        {
            if (_busy) return;
            _busy = true;
            try
            {
                var result = await _ext.FastRecruitAsync(RequireSingle());
                _status("Tuyển nhanh +" + result.healed + " quân; token " + result.recruitTokenSpent + ", gold " + result.goldSpent + ", food " + result.foodSpent + ".");
                await RefreshAsync();
            }
            catch (Exception ex) { _status(ex.Message); }
            finally { _busy = false; }
        }

        async void CreatePhantom()
        {
            if (_busy) return;
            _busy = true;
            try
            {
                var battle = CurrentBattleId();
                if (battle <= 0) throw new Exception("Không có battle KFGZ đang hoạt động cho tướng đã chọn.");
                var result = await _ext.CreatePhantomAsync(battle);
                _status("Đã tạo Phantom cho tướng " + result.generalId + (result.usedFree ? " bằng lượt miễn phí." : " với " + result.goldCost + " gold."));
                await RefreshAsync();
            }
            catch (Exception ex) { _status(ex.Message); }
            finally { _busy = false; }
        }

        async void RushSelected()
        {
            if (_busy) return;
            _busy = true;
            try
            {
                var battle = CurrentBattleId();
                if (battle <= 0) throw new Exception("Không có battle KFGZ đang hoạt động cho tướng đã chọn.");
                var result = await _ext.RushAsync(battle, RequireSelected(), RequireCity());
                _status(result.targetBattleId > 0 ? "Rush sang battle #" + result.targetBattleId : result.captured ? "Rush đã chiếm thành." : "Rush hoàn tất.");
                await RefreshAsync();
            }
            catch (Exception ex) { _status(ex.Message); }
            finally { _busy = false; }
        }

        void OpenBattle()
        {
            var battle = CurrentBattleId();
            if (battle <= 0) { _status("Không có battle KFGZ đang hoạt động cho tướng đã chọn."); return; }
            BattlePanel.Open(_host, _api, _status, battle);
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
    }
}
