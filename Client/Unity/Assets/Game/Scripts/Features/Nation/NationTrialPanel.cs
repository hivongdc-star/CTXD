using System;
using System.Threading.Tasks;
using CTXD.Client.Networking;
using UnityEngine;
using UnityEngine.UI;

namespace CTXD.Client.Features.Nation
{
    public sealed class NationTrialPanel : MonoBehaviour
    {
        const string Root = "LegacyVisual/Nation/";
        ApiClient _api; Action<string> _status; RectTransform _window; NationTrialView _view; bool _busy;
        public static NationTrialPanel Open(RectTransform host, ApiClient api, Action<string> status)
        {
            var go = new GameObject("NationTrialPanel"); go.transform.SetParent(host, false);
            var p = go.AddComponent<NationTrialPanel>(); p._api = api; p._status = status; p.Build(); _ = p.Refresh(); return p;
        }
        void Build()
        {
            var overlay = W7LegacyUi.Overlay(transform, "NationTrialLegacyOverlay");
            _window = W7LegacyUi.Window(overlay, W7LegacyUi.Common + "Window3", 309, 191, 662, 385);
            W7LegacyUi.Close(_window, 635, 6, () => Destroy(gameObject));
        }
        async Task Refresh()
        {
            try { _view = await _api.GetNationTrialAsync(); Draw(); }
            catch (Exception ex) { _status(ex.Message); Destroy(gameObject); }
        }
        void Draw()
        {
            if (_view == null) return;
            for (var i = _window.childCount - 1; i >= 0; i--) { var c = _window.GetChild(i); if (c.name != "W7Close") Destroy(c.gameObject); }
            W7LegacyUi.Image(_window, Root + "trial_bg", 18, 50, 626, 343);
            W7LegacyUi.Text(_window, string.IsNullOrEmpty(_view.name) ? "" : _view.name, 353, 132, 200, 20, 13, TextAnchor.MiddleLeft);
            W7LegacyUi.Text(_window, W7LegacyUi.Remaining(_view.endsAt), 353, 162, 200, 20, 13, TextAnchor.MiddleLeft, W7LegacyUi.Danger);
            W7LegacyUi.Text(_window, _view.cityId > 0 ? "#" + _view.cityId : string.Empty, 354, 181, 300, 20, 13, TextAnchor.MiddleLeft, Color.white);
            W7LegacyUi.Text(_window, _view.rank.ToString(), 535, 217, 88, 20);
            W7LegacyUi.Text(_window, _view.playerKills.ToString(), 348, 325, 68, 20, 12, TextAnchor.MiddleCenter);
            W7LegacyUi.Text(_window, _view.stage.ToString(), 409, 325, 68, 20, 12, TextAnchor.MiddleCenter);
            if (_view.rewardAvailable) W7LegacyUi.Button30(_window, "Nhận", 510, 353, 103, 38, async () => await Reward());
        }
        async Task Reward()
        {
            if (_busy) return; _busy = true;
            try { await _api.ClaimNationTrialRewardAsync(); await Refresh(); }
            catch (Exception ex) { _status(ex.Message); }
            finally { _busy = false; }
        }
    }
}
