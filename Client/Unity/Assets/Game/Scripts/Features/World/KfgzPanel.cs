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
        static KfgzPanel _open;
        ApiClient _api;
        KfgzExtendedApi _ext;
        Action<string> _status;
        RectTransform _window;
        PlayerView _player;
        KfgzView _view;
        KfgzWarView _war;
        GeneralRosterResponse _roster;
        KfgzBattleResourceView _battleResources;
        readonly HashSet<int> _selectedGenerals = new HashSet<int>();
        int _cityId;
        bool _busy;
        float _nextRefresh;

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
                if (_view != null && _view.signed)
                {
                    try { _battleResources = await _ext.GetResourcesAsync(); } catch { }
                    try { _war = await _api.GetKfgzWorldAsync(); } catch { }
                    if (_war != null && _cityId == 0)
                    {
                        var own = (_war.deployments ?? Array.Empty<KfgzDeploymentView>()).FirstOrDefault(x => x.playerId == _player.id);
                        _cityId = own != null ? own.cityId : (_war.cities ?? Array.Empty<KfgzCityView>()).FirstOrDefault()?.id ?? 0;
                    }
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
            LegacyUiFactory.PixelLabel(_window, "State " + _view.state + "   Force " + _view.forceId + "   " + (_view.signed ? "Đã đăng ký" : "Chưa đăng ký"), 15, TextAnchor.MiddleLeft, Color.white, 18, 47, 500, 26);

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
            if (_busy) return; _busy = true;
            try
            {
                var general = RequireSingle();
                var city = RequireCity();
                var targetBattle = (_war?.battles ?? Array.Empty<KfgzBattleView>()).FirstOrDefault(x => x.cityId == city && x.state == 1);
                if (targetBattle != null)
                {
                    var result = await _ext.ReinforceAsync(targetBattle.battleId, new[] { general });
                    _status("Đã gia nhập battle #" + result.battleId + " ở side " + result.side + ".");
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
            if (_busy) return; _busy = true;
            try
            {
                var info = await _ext.GetCallGeneralsAsync(RequireCity());
                _selectedGenerals.Clear();
                foreach (var id in info.generalIds ?? Array.Empty<int>()) _selectedGenerals.Add(id);
                _status("Đã chọn các tướng có thể gọi: " + _selectedGenerals.Count);
                Draw();
            }
            catch (Exception ex) { _status(ex.Message); }
            finally { _busy = false; }
        }

        async void CallSelected()
        {
            if (_busy) return; _busy = true;
            try
            {
                var result = await _ext.CallGeneralsAsync(RequireCity(), RequireSelected());
                var failed = result.failed?.Length ?? 0;
                _status("Call-general: " + (result.movedGeneralIds?.Length ?? 0) + " thành công, " + failed + " thất bại.");
                await RefreshAsync();
            }
            catch (Exception ex) { _status(ex.Message); }
            finally { _busy = false; }
        }

        async void RetreatSelected()
        {
            if (_busy) return; _busy = true;
            try { _war = await _api.RetreatKfgzGeneralsAsync(RequireSelected(), RequireCity()); _status("Đã rút quân KFGZ."); await RefreshAsync(); }
            catch (Exception ex) { _status(ex.Message); }
            finally { _busy = false; }
        }

        async void StartMubing()
        {
            if (_busy) return; _busy = true;
            try { var result = await _ext.StartMubingAsync(RequireSingle()); _status("Đã bắt đầu tuyển quân: " + result.mubing + "/h"); await RefreshAsync(); }
            catch (Exception ex) { _status(ex.Message); }
            finally { _busy = false; }
        }

        async void FastRecruit()
        {
            if (_busy) return; _busy = true;
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
            if (_busy) return; _busy = true;
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
            if (_busy) return; _busy = true;
            try
            {
                var battle = CurrentBattleId();
                if (battle <= 0) throw new Exception("Không có battle KFGZ đang hoạt động cho tướng đã chọn.");
                var result = await _ext.RushAsync(battle, RequireSelected(), RequireCity());
                _status(result.targetBattleId > 0 ? "Rush đã chuyển sang battle #" + result.targetBattleId : result.captured ? "Rush đã chiếm thành." : "Rush hoàn tất.");
                await RefreshAsync();
            }
            catch (Exception ex) { _status(ex.Message); }
            finally { _busy = false; }
        }

        void OpenBattle()
        {
            var battle = CurrentBattleId();
            if (battle <= 0) { _status("Không có battle KFGZ đang hoạt động cho tướng đã chọn."); return; }
            BattlePanel.Open((RectTransform)_window.parent, _api, _status, battle);
        }
    }
}
