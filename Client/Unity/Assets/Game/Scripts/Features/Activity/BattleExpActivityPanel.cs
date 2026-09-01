using System;
using System.Threading.Tasks;
using CTXD.Client.Features.Nation;
using CTXD.Client.Networking;
using UnityEngine;
using UnityEngine.UI;

namespace CTXD.Client.Features.Activity
{
    public sealed class BattleExpActivityPanel : MonoBehaviour
    {
        const string Root = "LegacyVisual/Activity/BattleExp/";
        ApiClient _api; Action<string> _status; RectTransform _window; BattleExpActivityView _view; bool _busy;
        public static BattleExpActivityPanel Open(RectTransform host, ApiClient api, Action<string> status)
        {
            var go = new GameObject("BattleExpActivityPanel"); go.transform.SetParent(host, false);
            var p = go.AddComponent<BattleExpActivityPanel>(); p._api = api; p._status = status; p.Build(); _ = p.Refresh(); return p;
        }
        void Build()
        {
            var overlay = W7LegacyUi.Overlay(transform, "BattleExpLegacyOverlay");
            _window = W7LegacyUi.Window(overlay, W7LegacyUi.Common + "Window3", 309, 191, 662, 385);
            W7LegacyUi.Close(_window, 635, 6, () => Destroy(gameObject));
        }
        async Task Refresh()
        {
            try { _view = await _api.GetBattleExpActivityAsync(); Draw(); }
            catch (Exception ex) { _status(ex.Message); Destroy(gameObject); }
        }
        void Draw()
        {
            for (var i = _window.childCount - 1; i >= 0; i--) { var c = _window.GetChild(i); if (c.name != "W7Close") Destroy(c.gameObject); }
            W7LegacyUi.Image(_window, Root + "background", 0, 0, 630, 343);
            if (_view == null || !_view.active) return;
            W7LegacyUi.Text(_window, _view.condition, 430, 370, 100, 20, 13, TextAnchor.MiddleLeft, W7LegacyUi.Muted);
            W7LegacyUi.Text(_window, "+" + _view.addPercent + "%", 265, 210, 120, 30, 16, TextAnchor.MiddleCenter, W7LegacyUi.Gold);
            if (!_view.activated) W7LegacyUi.Button30(_window, "Kích hoạt", 370, 325, 103, 38, async () => await Activate());
        }
        async Task Activate()
        {
            if (_busy) return; _busy = true;
            try { await _api.ActivateBattleExpActivityAsync(); await Refresh(); }
            catch (Exception ex) { _status(ex.Message); }
            finally { _busy = false; }
        }
    }
}
