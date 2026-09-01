using System;
using System.Threading.Tasks;
using CTXD.Client.Networking;
using UnityEngine;
using UnityEngine.UI;

namespace CTXD.Client.Features.Nation
{
    public sealed class PoliticsPanel : MonoBehaviour
    {
        const string Root = "LegacyVisual/Officer/Politics/";
        ApiClient _api; Action<string> _status; RectTransform _surface; PoliticsView _view; bool _busy;
        public static PoliticsPanel Open(RectTransform host, ApiClient api, Action<string> status)
        {
            var go = new GameObject("PoliticsPanel"); go.transform.SetParent(host, false);
            var p = go.AddComponent<PoliticsPanel>(); p._api = api; p._status = status; p.Build(); _ = p.Refresh(); return p;
        }
        void Build()
        {
            var overlay = W7LegacyUi.Overlay(transform, "PoliticsLegacyOverlay");
            _surface = W7LegacyUi.Window(overlay, Root + "background", 262, 214, 756, 340);
            W7LegacyUi.Close(_surface, 724, 8, () => Destroy(gameObject));
        }
        async Task Refresh()
        {
            try { _view = await _api.GetPoliticsAsync(); Draw(); }
            catch (Exception ex) { _status(ex.Message); Destroy(gameObject); }
        }
        void Draw()
        {
            for (var i = _surface.childCount - 1; i >= 0; i--) { var c = _surface.GetChild(i); if (c.name != "W7Close") Destroy(c.gameObject); }
            var events = _view?.events ?? Array.Empty<PoliticsEventView>(); if (events.Length == 0) return;
            var e = events[0];
            var key = W7LegacyUi.ResourceKey(e.picture);
            if (!string.IsNullOrEmpty(key)) W7LegacyUi.Image(_surface, Root + "Pictures/" + key, 28, 72, 237, 186, true);
            W7LegacyUi.Text(_surface, e.name, 287, 45, 420, 26, 16, TextAnchor.MiddleCenter, W7LegacyUi.Gold);
            W7LegacyUi.Text(_surface, e.description, 287, 77, 420, 68, 13, TextAnchor.UpperLeft, W7LegacyUi.Muted);
            Answer(e, 1, e.option1, 287, 148);
            Answer(e, 2, e.option2, 287, 244);
        }
        void Answer(PoliticsEventView e, int option, string text, float x, float y)
        {
            var b = W7LegacyUi.Button(_surface, string.Empty, x, y, 333, 95, async () => await Choose(e, option), Root + "Answer/up", Root + "Answer/over", Root + "Answer/down");
            var label = W7LegacyUi.Text(b.transform, text, 18, 12, 297, 70, 13, TextAnchor.MiddleLeft, W7LegacyUi.Gold); label.raycastTarget = false;
        }
        async Task Choose(PoliticsEventView e, int option)
        {
            if (_busy) return; _busy = true;
            try { var r = await _api.ChoosePoliticsAsync(e.buildingId, option); _status(r.type + " +" + r.value); await Refresh(); }
            catch (Exception ex) { _status(ex.Message); }
            finally { _busy = false; }
        }
    }
}
