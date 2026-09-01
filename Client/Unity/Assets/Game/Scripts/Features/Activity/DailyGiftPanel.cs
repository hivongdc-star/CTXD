using System;
using System.Threading.Tasks;
using CTXD.Client.Features.Nation;
using CTXD.Client.Networking;
using UnityEngine;
using UnityEngine.UI;

namespace CTXD.Client.Features.Activity
{
    public sealed class DailyGiftPanel : MonoBehaviour
    {
        const string Root = "LegacyVisual/Activity/DailyGift/";
        ApiClient _api; Action<string> _status; RectTransform _surface; DailyGiftView _view; bool _busy;
        public static DailyGiftPanel Open(RectTransform host, ApiClient api, Action<string> status)
        {
            var go = new GameObject("DailyGiftPanel"); go.transform.SetParent(host, false);
            var p = go.AddComponent<DailyGiftPanel>(); p._api = api; p._status = status; p.Build(); _ = p.Refresh(); return p;
        }
        void Build()
        {
            var overlay = W7LegacyUi.Overlay(transform, "DailyGiftLegacyOverlay");
            _surface = LegacyUiFactory.PixelPanel(overlay, "DailyGiftSurface", 415, 252, 449, 264, Color.clear);
            W7LegacyUi.Image(_surface, Root + "background", 0, 0, 449, 264, true);
            W7LegacyUi.Close(_surface, 319, -22, () => Destroy(gameObject));
        }
        async Task Refresh()
        {
            try { _view = await _api.GetDailyGiftAsync(); Draw(); }
            catch (Exception ex) { _status(ex.Message); Destroy(gameObject); }
        }
        void Draw()
        {
            for (var i = _surface.childCount - 1; i >= 0; i--) { var c = _surface.GetChild(i); if (c.name != "W7Close" && c.name != "background") Destroy(c.gameObject); }
            if (_view == null || !_view.available) return;
            W7LegacyUi.Button(_surface, string.Empty, 74, 158, 160, 71, async () => await Claim(), Root + "try_up", Root + "try_over", Root + "try_down");
        }
        async Task Claim()
        {
            if (_busy) return; _busy = true;
            try
            {
                var r = await _api.ClaimDailyGiftAsync(Guid.NewGuid().ToString("N"));
                W7LegacyUi.Text(_surface, "×" + (r.cards == null ? 0 : r.cards.Length), 75, 10, 280, 22, 14, TextAnchor.MiddleCenter, new Color(1f, .8f, 0f));
                await Refresh();
            }
            catch (Exception ex) { _status(ex.Message); }
            finally { _busy = false; }
        }
    }
}
