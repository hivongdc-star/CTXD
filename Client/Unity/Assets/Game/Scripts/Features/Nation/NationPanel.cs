using System;
using System.Linq;
using System.Threading.Tasks;
using CTXD.Client.Networking;
using UnityEngine;
using UnityEngine.UI;

namespace CTXD.Client.Features.Nation
{
    public sealed class NationPanel : MonoBehaviour
    {
        const string Root = "LegacyVisual/Nation/";
        ApiClient _api; Action<string> _status; RectTransform _window; NationView _view; NationTaskView _task; bool _busy;
        public static NationPanel Open(RectTransform host, ApiClient api, Action<string> status)
        {
            var go = new GameObject("NationPanel"); go.transform.SetParent(host, false);
            var p = go.AddComponent<NationPanel>(); p._api = api; p._status = status; p.Build(); _ = p.Refresh(); return p;
        }
        void Build()
        {
            var overlay = W7LegacyUi.Overlay(transform, "NationLegacyOverlay");
            _window = W7LegacyUi.Window(overlay, W7LegacyUi.Common + "Window3", 309, 191, 662, 385);
            W7LegacyUi.Close(_window, 635, 6, () => Destroy(gameObject));
        }
        async Task Refresh()
        {
            try { _view = await _api.GetNationAsync(); _task = await _api.GetNationTaskAsync(); Draw(); }
            catch (Exception ex) { _status(ex.Message); Destroy(gameObject); }
        }
        void Draw()
        {
            if (_view == null) return;
            for (var i = _window.childCount - 1; i >= 0; i--) { var c = _window.GetChild(i); if (c.name != "W7Close") Destroy(c.gameObject); }
            W7LegacyUi.Image(_window, Root + "nation_view_bg", 18, 50, 626, 344);
            var forces = (_view.nations ?? Array.Empty<NationForceView>()).OrderBy(n => n.forceId).ToArray();
            var player = forces.FirstOrDefault(n => n.forceId == _view.playerForceId);
            W7LegacyUi.Text(_window, "Lv." + _view.forceLevel, 290, 150, 60, 20, 13, TextAnchor.MiddleLeft);
            W7LegacyUi.Text(_window, _view.forceExp + "/" + _view.maxExp, 408, 150, 130, 20, 12, TextAnchor.MiddleCenter);
            W7LegacyUi.Text(_window, player == null ? string.Empty : player.exp + "/" + player.maxExp, 139, 329, 120, 20, 12, TextAnchor.MiddleCenter);
            var other = forces.Where(n => n.forceId != _view.playerForceId).Take(2).ToArray();
            if (other.Length > 0)
            {
                W7LegacyUi.Text(_window, W7LegacyUi.ForceName(other[0].forceId), 290, 260, 150, 20);
                W7LegacyUi.Text(_window, "Lv." + other[0].level, 290, 276, 60, 20);
                W7LegacyUi.Text(_window, other[0].exp + "/" + other[0].maxExp, 408, 276, 130, 20, 12, TextAnchor.MiddleCenter);
            }
            if (other.Length > 1)
            {
                W7LegacyUi.Text(_window, W7LegacyUi.ForceName(other[1].forceId), 290, 309, 150, 20);
                W7LegacyUi.Text(_window, "Lv." + other[1].level, 290, 325, 60, 20);
                W7LegacyUi.Text(_window, other[1].exp + "/" + other[1].maxExp, 408, 326, 130, 20, 12, TextAnchor.MiddleCenter);
            }
            if (_task != null && !string.IsNullOrEmpty(_task.endsAt))
                W7LegacyUi.Text(_window, W7LegacyUi.Remaining(_task.endsAt), 484, 220, 120, 20, 12, TextAnchor.MiddleCenter, W7LegacyUi.Danger);
            W7LegacyUi.Button22(_window, "Quốc vụ", 365, 183, 78, 34, () => _status("Quốc vụ hiện tại: " + (_task == null ? "-" : _task.progress + "/" + _task.target)));
            var upgrade = W7LegacyUi.Button22(_window, "Thăng cấp", 503, 183, 78, 34, async () => await StartUpgrade());
            upgrade.interactable = !_busy && (_task == null || string.IsNullOrEmpty(_task.endsAt) || _task.type == 0);
        }
        async Task StartUpgrade()
        {
            if (_busy) return; _busy = true;
            try { _task = await _api.StartNationUpgradeAsync(); await Refresh(); }
            catch (Exception ex) { _status(ex.Message); }
            finally { _busy = false; }
        }
    }
}
