using System;
using System.Threading.Tasks;
using CTXD.Client.Features.Nation;
using CTXD.Client.Networking;
using UnityEngine;
using UnityEngine.UI;

namespace CTXD.Client.Features.Activity
{
    public sealed class OnlineGiftPanel : MonoBehaviour
    {
        const string Root = "LegacyVisual/Activity/OnlineGift/";
        ApiClient _api; Action<string> _status; RectTransform _surface; OnlineGiftView _view; bool _busy;
        public static OnlineGiftPanel Open(RectTransform host, ApiClient api, Action<string> status)
        {
            var go = new GameObject("OnlineGiftPanel"); go.transform.SetParent(host, false);
            var p = go.AddComponent<OnlineGiftPanel>(); p._api = api; p._status = status; p.Build(); _ = p.Refresh(); return p;
        }
        void Build()
        {
            var overlay = W7LegacyUi.Overlay(transform, "OnlineGiftLegacyOverlay");
            _surface = LegacyUiFactory.PixelPanel(overlay, "OnlineGiftSurface", 415, 255, 449, 258, Color.clear);
            W7LegacyUi.Image(_surface, Root + "background", 0, 0, 449, 258, true);
            W7LegacyUi.Close(_surface, 310, -18, () => Destroy(gameObject));
        }
        async Task Refresh()
        {
            try { _view = await _api.GetOnlineGiftAsync(); Draw(); }
            catch (Exception ex) { _status(ex.Message); Destroy(gameObject); }
        }
        void Draw()
        {
            for (var i = _surface.childCount - 1; i >= 0; i--) { var c = _surface.GetChild(i); if (c.name != "W7Close" && c.name != "background") Destroy(c.gameObject); }
            if (_view == null) return;
            W7LegacyUi.Text(_surface, W7LegacyUi.Remaining(_view.nextAt), 48, 80, 200, 40, 25, TextAnchor.MiddleCenter, W7LegacyUi.Gold);
            if (_view.available > 0)
            {
                W7LegacyUi.Text(_surface, _view.available.ToString(), 48, 44, 200, 40, 14, TextAnchor.MiddleCenter, W7LegacyUi.Muted);
                W7LegacyUi.Button23(_surface, "Nhận", 105, 178, 78, 34, async () => await Claim());
            }
        }
        async Task Claim()
        {
            if (_busy) return; _busy = true;
            try { await _api.ClaimOnlineGiftAsync(Guid.NewGuid().ToString("N")); await Refresh(); }
            catch (Exception ex) { _status(ex.Message); }
            finally { _busy = false; }
        }
    }
}
