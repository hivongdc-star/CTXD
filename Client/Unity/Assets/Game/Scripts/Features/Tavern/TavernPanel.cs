using System;
using System.Linq;
using System.Threading.Tasks;
using CTXD.Client.Features.FirstPlayable;
using CTXD.Client.Networking;
using UnityEngine;
using UnityEngine.UI;

namespace CTXD.Client.Features.Tavern
{
    public sealed class TavernPanel : MonoBehaviour
    {
        ApiClient _api;
        PlayerView _player;
        RectTransform _host;
        RectTransform _window;
        Action<string> _status;
        Func<Task> _onChanged;
        TavernResponse _data;
        int _type;
        bool _busy;

        const int CivilFunction = 44;
        const int MilitaryFunction = 45;
        const int RefreshFunction = 55;

        public static TavernPanel Open(RectTransform host, ApiClient api, PlayerView player, Action<string> status, Func<Task> onChanged)
        {
            var go = new GameObject("TavernPanel");
            go.transform.SetParent(host, false);
            var panel = go.AddComponent<TavernPanel>();
            panel._host = host;
            panel._api = api;
            panel._player = player;
            panel._status = status;
            panel._onChanged = onChanged;
            panel._type = HasFunction(player, MilitaryFunction) ? 2 : 1;
            panel.BuildFrame();
            _ = panel.LoadAsync();
            return panel;
        }

        static bool HasFunction(PlayerView player, int id) => player?.functionIds != null && Array.IndexOf(player.functionIds, id) >= 0;

        void BuildFrame()
        {
            var blocker = LegacyUiFactory.Panel(transform, "TavernBlocker", Vector2.zero, Vector2.one, new Color(0, 0, 0, .48f));
            _window = LegacyUiFactory.PixelPanel(blocker, "TavernWindow", 326, 229, 628, 310, Color.white);
            LegacyUiFactory.PixelImage(_window, "LegacyVisual/Tavern/01210", 0, 0, 628, 310);
            LegacyUiFactory.PixelLabel(_window, "TỬU QUÁN", 22, TextAnchor.MiddleCenter, new Color(1f,.82f,.35f), 215, 7, 198, 30);

            var tabX = 12f;
            if (HasFunction(_player, MilitaryFunction))
            {
                LegacyUiFactory.PixelButton(_window, "Võ tướng", tabX, 10, 88, 28, () => SwitchType(2));
                tabX += 94;
            }
            if (HasFunction(_player, CivilFunction))
                LegacyUiFactory.PixelButton(_window, "Văn quan", tabX, 10, 88, 28, () => SwitchType(1));

            LegacyUiFactory.PixelButton(_window, "Tướng", 486, 8, 66, 28, () => GeneralRosterPanel.Open(_host, _api, _status, _type));
            LegacyUiFactory.PixelButton(_window, "Đóng", 558, 8, 58, 28, Close);
        }

        async void SwitchType(int type)
        {
            if (_busy || type == _type) return;
            _type = type;
            await LoadAsync();
        }

        async Task LoadAsync()
        {
            if (_busy) return;
            _busy = true;
            SetStatus("Đang mở Tửu Quán...");
            try
            {
                _data = await _api.GetTavernAsync(_type);
                RenderContent();
                SetStatus("");
            }
            catch (Exception ex) { SetStatus(ex.Message); }
            finally { _busy = false; }
        }

        void RenderContent()
        {
            var old = _window.Find("Content");
            if (old != null) Destroy(old.gameObject);
            var content = LegacyUiFactory.PixelPanel(_window, "Content", 0, 39, 628, 271, Color.clear);

            var title = _type == 2 ? "Võ tướng" : "Văn quan";
            LegacyUiFactory.PixelLabel(content, $"{title}  {_data.nowGeneralNum}/{_data.maxGeneralNum}", 15, TextAnchor.MiddleLeft,
                new Color(1f,.88f,.55f), 12, 2, 165, 24);

            if (HasFunction(_player, RefreshFunction))
            {
                var remain = RefreshRemainingText(_data.nextRefreshAt);
                LegacyUiFactory.PixelLabel(content, remain, 13, TextAnchor.MiddleRight, new Color(.9f,.83f,.68f), 350, 2, 160, 24);
                LegacyUiFactory.PixelButton(content, "Thăm dò", 516, 1, 96, 26, async () => await RefreshAsync());
            }

            var offers = _data.offers ?? Array.Empty<TavernOfferView>();
            for (var slot = 1; slot <= 5; slot++)
            {
                var offer = offers.FirstOrDefault(x => x.position == slot);
                DrawOffer(content, slot, offer);
            }
        }

        void DrawOffer(RectTransform parent, int slot, TavernOfferView offer)
        {
            const float y = 34;
            var x = 18 + (slot - 1) * 119;
            LegacyUiFactory.PixelImage(parent, "LegacyVisual/Tavern/01216", x, y, 105, 177);
            if (offer == null)
            {
                LegacyUiFactory.PixelLabel(parent, "—", 20, TextAnchor.MiddleCenter, new Color(.65f,.6f,.5f), x+8, y+55, 89, 45);
                return;
            }

            var q = Mathf.Clamp(offer.quality, 1, 6);
            LegacyUiFactory.PixelImage(parent, $"LegacyVisual/Tavern/{(697 + q*2):00000}", x+14, y+8, 76, 76);
            LegacyUiFactory.PixelImage(parent, "LegacyVisual/GeneralPic/" + offer.pic, x+16, y+10, 72, 72, true);
            LegacyUiFactory.PixelLabel(parent, offer.name, 15, TextAnchor.MiddleCenter, QualityColor(q), x+4, y+84, 97, 21);
            LegacyUiFactory.PixelLabel(parent, Stats(offer), 12, TextAnchor.UpperCenter, Color.white, x+4, y+104, 97, 30);

            var currency = offer.isGold ? "LegacyVisual/Tavern/00891" : "LegacyVisual/Tavern/01225";
            LegacyUiFactory.PixelImage(parent, currency, x+14, y+137, 20, 14, true);
            LegacyUiFactory.PixelLabel(parent, offer.price.ToString(), 12, TextAnchor.MiddleLeft, new Color(1f,.9f,.58f), x+36, y+133, 61, 21);

            if (offer.bought)
            {
                LegacyUiFactory.PixelImage(parent, "LegacyVisual/Tavern/01235", x+5, y+118, 95, 54, true);
                return;
            }

            var lockPath = offer.locked ? "LegacyVisual/Tavern/01219" : "LegacyVisual/Tavern/01222";
            LegacyUiFactory.PixelButton(parent, "", x+5, y+154, 20, 20, async () => await ToggleLockAsync(offer), lockPath);
            LegacyUiFactory.PixelButton(parent, "Chiêu", x+31, y+153, 68, 22, async () => await RecruitAsync(offer));
        }

        static string Stats(TavernOfferView o)
        {
            if (o.type == 1) return $"Trí {o.intel}  Chính {o.politics}";
            return $"Thống {o.leader}  Võ {o.strength}";
        }

        static Color QualityColor(int q)
        {
            return q switch
            {
                1 => Color.white,
                2 => new Color(.45f,1f,.45f),
                3 => new Color(.45f,.75f,1f),
                4 => new Color(.75f,.45f,1f),
                5 => new Color(1f,.62f,.25f),
                _ => new Color(1f,.32f,.25f)
            };
        }

        static string RefreshRemainingText(string iso)
        {
            if (!DateTimeOffset.TryParse(iso, out var next)) return "";
            var sec = Math.Max(0, (int)Math.Ceiling((next - DateTimeOffset.UtcNow).TotalSeconds));
            return sec <= 0 ? "Có thể thăm dò" : $"Hồi: {sec / 60:00}:{sec % 60:00}";
        }

        async Task RefreshAsync()
        {
            if (_busy) return;
            _busy = true;
            try
            {
                _data = await _api.RefreshTavernAsync(_type);
                RenderContent();
                if (_onChanged != null) await _onChanged();
            }
            catch (Exception ex) { SetStatus(ex.Message); }
            finally { _busy = false; }
        }

        async Task ToggleLockAsync(TavernOfferView offer)
        {
            if (_busy) return;
            _busy = true;
            try
            {
                _data = await _api.LockGeneralAsync(offer.generalId, !offer.locked);
                RenderContent();
            }
            catch (Exception ex) { SetStatus(ex.Message); }
            finally { _busy = false; }
        }

        async Task RecruitAsync(TavernOfferView offer)
        {
            if (_busy) return;
            _busy = true;
            try
            {
                await _api.RecruitGeneralAsync(offer.generalId);
                _data = await _api.GetTavernAsync(_type);
                RenderContent();
                SetStatus($"Đã chiêu mộ {offer.name}.");
                if (_onChanged != null) await _onChanged();
            }
            catch (Exception ex) { SetStatus(ex.Message); }
            finally { _busy = false; }
        }

        void SetStatus(string value) => _status?.Invoke(value);
        void Close() => Destroy(gameObject);
    }
}
