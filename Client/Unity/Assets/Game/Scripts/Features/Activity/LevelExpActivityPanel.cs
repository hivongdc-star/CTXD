using System;
using System.Threading.Tasks;
using CTXD.Client.Features.Nation;
using CTXD.Client.Networking;
using UnityEngine;
using UnityEngine.UI;

namespace CTXD.Client.Features.Activity
{
    public sealed class LevelExpActivityPanel : MonoBehaviour
    {
        const string Root = "LegacyVisual/Activity/LevelExp/";
        ApiClient _api; Action<string> _status; RectTransform _window; LevelExpActivityView _view; bool _busy;
        public static LevelExpActivityPanel Open(RectTransform host, ApiClient api, Action<string> status)
        {
            var go = new GameObject("LevelExpActivityPanel"); go.transform.SetParent(host, false);
            var p = go.AddComponent<LevelExpActivityPanel>(); p._api = api; p._status = status; p.Build(); _ = p.Refresh(); return p;
        }
        void Build()
        {
            var overlay = W7LegacyUi.Overlay(transform, "LevelExpLegacyOverlay");
            _window = W7LegacyUi.Window(overlay, W7LegacyUi.Common + "Window3", 309, 191, 662, 385);
            W7LegacyUi.Close(_window, 635, 6, () => Destroy(gameObject));
        }
        async Task Refresh()
        {
            try { _view = await _api.GetLevelExpActivityAsync(); Draw(); }
            catch (Exception ex) { _status(ex.Message); Destroy(gameObject); }
        }
        void Draw()
        {
            for (var i = _window.childCount - 1; i >= 0; i--) { var c = _window.GetChild(i); if (c.name != "W7Close") Destroy(c.gameObject); }
            W7LegacyUi.Image(_window, Root + "background", 0, 0, 628, 343);
            if (_view == null || !_view.active) return;
            W7LegacyUi.Text(_window, W7LegacyUi.Remaining(_view.endsAt), 350, 125, 70, 20, 12, TextAnchor.MiddleCenter, W7LegacyUi.Danger);
            W7LegacyUi.Text(_window, _view.rewardExp.ToString(), 430, 125, 90, 20, 13, TextAnchor.MiddleLeft, W7LegacyUi.Gold);
            W7LegacyUi.Text(_window, _view.targetLevel.ToString("0.###"), 280, 125, 70, 20, 12, TextAnchor.MiddleCenter, W7LegacyUi.Muted);
            W7LegacyUi.Text(_window, _view.startLevel.ToString("0.###"), 278, 196, 88, 20, 12, TextAnchor.MiddleCenter);
            W7LegacyUi.Text(_window, _view.currentLevel.ToString("0.###"), 368, 196, 88, 20, 12, TextAnchor.MiddleCenter);
            W7LegacyUi.Text(_window, _view.targetLevel.ToString("0.###"), 458, 196, 88, 20, 12, TextAnchor.MiddleCenter);
            if (_view.rewardAvailable) W7LegacyUi.Button30(_window, "Nhận", 378, 215, 103, 38, async () => await Claim());
        }
        async Task Claim()
        {
            if (_busy) return; _busy = true;
            try { await _api.ClaimLevelExpActivityAsync(); await Refresh(); }
            catch (Exception ex) { _status(ex.Message); }
            finally { _busy = false; }
        }
    }
}
