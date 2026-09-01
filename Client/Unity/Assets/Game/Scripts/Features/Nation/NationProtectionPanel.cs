using System;
using System.Threading.Tasks;
using CTXD.Client.Networking;
using UnityEngine;
using UnityEngine.UI;

namespace CTXD.Client.Features.Nation
{
    public sealed class NationProtectionPanel : MonoBehaviour
    {
        const string Root = "LegacyVisual/Nation/";
        ApiClient _api; Action<string> _status; RectTransform _window; NationProtectionView _view; bool _busy;
        public static NationProtectionPanel Open(RectTransform host, ApiClient api, Action<string> status)
        {
            var go = new GameObject("NationProtectionPanel"); go.transform.SetParent(host, false);
            var p = go.AddComponent<NationProtectionPanel>(); p._api = api; p._status = status; p.Build(); _ = p.Refresh(); return p;
        }
        void Build()
        {
            var overlay = W7LegacyUi.Overlay(transform, "NationProtectionLegacyOverlay");
            _window = W7LegacyUi.Window(overlay, W7LegacyUi.Common + "Window3", 309, 191, 662, 385);
            W7LegacyUi.Close(_window, 635, 6, () => Destroy(gameObject));
        }
        async Task Refresh()
        {
            try { _view = await _api.GetNationProtectionAsync(); Draw(); }
            catch (Exception ex) { _status(ex.Message); Destroy(gameObject); }
        }
        void Draw()
        {
            if (_view == null) return;
            for (var i = _window.childCount - 1; i >= 0; i--) { var c = _window.GetChild(i); if (c.name != "W7Close") Destroy(c.gameObject); }
            W7LegacyUi.Image(_window, Root + "protection_bg", 18, 50, 626, 343);
            W7LegacyUi.Text(_window, W7LegacyUi.Remaining(_view.endsAt), 340, 140, 120, 20, 13, TextAnchor.MiddleLeft, W7LegacyUi.Danger);
            W7LegacyUi.Text(_window, W7LegacyUi.ForceName(_view.attackingForceId), 340, 160, 284, 20);
            W7LegacyUi.Text(_window, _view.playerKills.ToString(), 486, 127, 120, 20, 13, TextAnchor.MiddleRight);
            W7LegacyUi.Text(_window, _view.rank.ToString(), 486, 192, 118, 20, 13, TextAnchor.MiddleRight);
            if (_view.rewardAvailable) W7LegacyUi.Button30(_window, "Nhận", 522, 350, 103, 38, async () => await Reward());
        }
        async Task Reward()
        {
            if (_busy) return; _busy = true;
            try { await _api.ClaimNationProtectionRewardAsync(); await Refresh(); }
            catch (Exception ex) { _status(ex.Message); }
            finally { _busy = false; }
        }
    }
}
