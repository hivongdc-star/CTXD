using System;
using System.Threading.Tasks;
using CTXD.Client.Features.Nation;
using CTXD.Client.Networking;
using UnityEngine;
using UnityEngine.UI;

namespace CTXD.Client.Features.Market
{
    public sealed class MarketPanel : MonoBehaviour
    {
        const string Root = "LegacyVisual/Market/";
        ApiClient _api; Action<string> _status; RectTransform _window; MarketView _view; bool _busy;
        public static MarketPanel Open(RectTransform host, ApiClient api, Action<string> status)
        {
            var go = new GameObject("MarketPanel"); go.transform.SetParent(host, false);
            var p = go.AddComponent<MarketPanel>(); p._api = api; p._status = status; p.Build(); _ = p.Refresh(); return p;
        }
        void Build()
        {
            var overlay = W7LegacyUi.Overlay(transform, "MarketLegacyOverlay");
            _window = W7LegacyUi.Window(overlay, W7LegacyUi.Common + "Window2", 309, 191, 662, 385);
            W7LegacyUi.Close(_window, 635, 6, () => Destroy(gameObject));
        }
        async Task Refresh()
        {
            try { _view = await _api.GetMarketAsync(); Draw(); }
            catch (Exception ex) { _status(ex.Message); Destroy(gameObject); }
        }
        void Draw()
        {
            for (var i = _window.childCount - 1; i >= 0; i--) { var c = _window.GetChild(i); if (c.name != "W7Close") Destroy(c.gameObject); }
            if (_view == null) return;
            W7LegacyUi.Text(_window, _view.canBuy.ToString(), 38, 95, 220, 20, 13, TextAnchor.MiddleRight, W7LegacyUi.Muted);
            W7LegacyUi.Text(_window, W7LegacyUi.Remaining(_view.refreshAt), 340, 95, 220, 20, 13, TextAnchor.MiddleRight, W7LegacyUi.Muted);
            var offers = _view.offers ?? Array.Empty<MarketOfferView>();
            for (var i = 0; i < Math.Min(3, offers.Length); i++) DrawOffer(offers[i], 137 + i * 152, 138);
        }
        void DrawOffer(MarketOfferView offer, float x, float y)
        {
            var q = Mathf.Clamp(offer.quality, 1, 6); var product = ProductKey(offer.itemType);
            W7LegacyUi.Image(_window, Root + "Quality/q" + q, x + 10, y + 10, 76, 76);
            var sprite = Resources.Load<Sprite>(Root + "Products/" + product);
            if (sprite != null) W7LegacyUi.Image(_window, Root + "Products/" + product, x + 12, y + 12, 72, 72, true);
            W7LegacyUi.Text(_window, offer.itemType + (offer.itemNum > 1 ? " ×" + offer.itemNum : string.Empty), x + 10, y + 65, 75, 17, 11, TextAnchor.MiddleCenter, Color.white);
            var gold = !string.IsNullOrEmpty(offer.costType) && offer.costType.IndexOf("gold", StringComparison.OrdinalIgnoreCase) >= 0;
            if (gold) W7LegacyUi.Image(_window, Root + "gold_cost_icon", x + 9, y + 86, 24, 16, true);
            W7LegacyUi.Text(_window, offer.costNum.ToString(), x + 40, y + 86, 60, 17, 11, TextAnchor.MiddleLeft, Color.white);
            var b = W7LegacyUi.Button5(_window, "Mua", x + 10, y + 106, 72, 25, async () => await Buy(offer));
            b.interactable = !_busy && _view.canBuy > 0;
        }
        async Task Buy(MarketOfferView offer)
        {
            if (_busy) return; _busy = true;
            try { var r = await _api.BuyMarketAsync(offer.slot, Guid.NewGuid().ToString("N")); _status(r.itemType + " +" + r.added); await Refresh(); }
            catch (Exception ex) { _status(ex.Message); }
            finally { _busy = false; }
        }
        static string ProductKey(string value)
        {
            var key = W7LegacyUi.ResourceKey(value).ToLowerInvariant();
            if (key == "wood") return "lumber";
            if (key == "recruittoken" || key == "recruit-token") return "recruit_token";
            return key;
        }
    }
}
