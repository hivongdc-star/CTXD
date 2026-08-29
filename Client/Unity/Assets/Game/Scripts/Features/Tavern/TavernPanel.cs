using System;
using System.Collections;
using System.Linq;
using System.Threading.Tasks;
using CTXD.Client.Features.FirstPlayable;
using CTXD.Client.Networking;
using UnityEngine;
using UnityEngine.EventSystems;
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
        Coroutine _refreshReveal;

        const int CivilFunction = 44;
        const int MilitaryFunction = 45;
        const int RefreshFunction = 55;
        const float LegacyRefreshPhaseSeconds = 2f;

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

        void RenderContent(bool animateRefresh = false)
        {
            if (_refreshReveal != null)
            {
                StopCoroutine(_refreshReveal);
                _refreshReveal = null;
            }

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

            if (animateRefresh)
                _refreshReveal = StartCoroutine(AnimateRefreshReveal(content));
        }

        void DrawOffer(RectTransform parent, int slot, TavernOfferView offer)
        {
            const float y = 34;
            var x = 18 + (slot - 1) * 119;
            var root = LegacyUiFactory.PixelPanel(parent, $"OfferSlot_{slot}", x, y, 105, 177, Color.clear);
            root.gameObject.GetComponent<Image>().raycastTarget = true;
            LegacyUiFactory.PixelImage(root, "LegacyVisual/Tavern/01216", 0, 0, 105, 177);
            if (offer == null)
            {
                LegacyUiFactory.PixelLabel(root, "—", 20, TextAnchor.MiddleCenter, new Color(.65f,.6f,.5f), 8, 55, 89, 45);
                return;
            }

            var q = Mathf.Clamp(offer.quality, 1, 6);
            LegacyUiFactory.PixelImage(root, $"LegacyVisual/Tavern/{(697 + q*2):00000}", 14, 8, 76, 76);
            LegacyUiFactory.PixelImage(root, "LegacyVisual/GeneralPic/" + offer.pic, 16, 10, 72, 72, true);
            LegacyUiFactory.PixelLabel(root, offer.name, 15, TextAnchor.MiddleCenter, QualityColor(q), 4, 84, 97, 21);
            LegacyUiFactory.PixelLabel(root, Stats(offer), 12, TextAnchor.UpperCenter, Color.white, 4, 104, 97, 30);

            var currency = offer.isGold ? "LegacyVisual/Tavern/00891" : "LegacyVisual/Tavern/01225";
            LegacyUiFactory.PixelImage(root, currency, 14, 137, 20, 14, true);
            LegacyUiFactory.PixelLabel(root, offer.price.ToString(), 12, TextAnchor.MiddleLeft, new Color(1f,.9f,.58f), 36, 133, 61, 21);

            AddHover(root, () => ShowOfferDetail(parent, offer, slot), () => HideDetail(parent));

            if (offer.bought)
            {
                LegacyUiFactory.PixelImage(root, "LegacyVisual/Tavern/01235", 5, 118, 95, 54, true);
                return;
            }

            var lockPath = offer.locked ? "LegacyVisual/Tavern/01219" : "LegacyVisual/Tavern/01222";
            LegacyUiFactory.PixelButton(root, "", 5, 154, 20, 20, async () => await ToggleLockAsync(offer), lockPath);
            LegacyUiFactory.PixelButton(root, "Chiêu", 31, 153, 68, 22, async () => await RecruitAsync(offer));
        }

        static void AddHover(RectTransform target, Action enter, Action exit)
        {
            var trigger = target.gameObject.AddComponent<EventTrigger>();
            trigger.triggers = new System.Collections.Generic.List<EventTrigger.Entry>();

            var over = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
            over.callback.AddListener(_ => enter());
            trigger.triggers.Add(over);

            var outEntry = new EventTrigger.Entry { eventID = EventTriggerType.PointerExit };
            outEntry.callback.AddListener(_ => exit());
            trigger.triggers.Add(outEntry);
        }

        void ShowOfferDetail(RectTransform parent, TavernOfferView offer, int slot)
        {
            HideDetail(parent);
            var x = Mathf.Clamp(18 + (slot - 1) * 119 - 42, 4, 434);
            var detail = LegacyUiFactory.PixelPanel(parent, "HoverDetail", x, 29, 190, 138, new Color(.055f, .035f, .018f, .97f));
            var outline = detail.gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(.72f, .53f, .22f, 1f);
            outline.effectDistance = new Vector2(1, -1);

            var q = Mathf.Clamp(offer.quality, 1, 6);
            LegacyUiFactory.PixelImage(detail, $"LegacyVisual/Tavern/{(697 + q*2):00000}", 8, 8, 76, 76);
            LegacyUiFactory.PixelImage(detail, "LegacyVisual/GeneralPic/" + offer.pic, 10, 10, 72, 72, true);
            LegacyUiFactory.PixelLabel(detail, offer.name, 16, TextAnchor.MiddleLeft, QualityColor(q), 90, 8, 94, 25);
            LegacyUiFactory.PixelLabel(detail, Stats(offer), 13, TextAnchor.UpperLeft, Color.white, 90, 35, 94, 45);
            LegacyUiFactory.PixelLabel(detail, offer.locked ? "Đã khóa" : "Có thể chiêu mộ", 12, TextAnchor.MiddleLeft,
                new Color(1f,.84f,.45f), 90, 80, 94, 22);
            LegacyUiFactory.PixelLabel(detail, offer.isGold ? $"Vàng: {offer.price}" : $"Bạc: {offer.price}", 12,
                TextAnchor.MiddleLeft, new Color(.95f,.88f,.7f), 10, 105, 170, 22);
            detail.SetAsLastSibling();
        }

        static void HideDetail(RectTransform parent)
        {
            var detail = parent.Find("HoverDetail");
            if (detail != null) Destroy(detail.gameObject);
        }

        IEnumerator AnimateRefreshReveal(RectTransform content)
        {
            for (var slot = 1; slot <= 5; slot++)
            {
                var card = content.Find($"OfferSlot_{slot}") as RectTransform;
                if (card == null) continue;
                StartCoroutine(AnimateLegacyFlip(card));
                yield return new WaitForSecondsRealtime(.12f);
            }
            _refreshReveal = null;
        }

        static IEnumerator AnimateLegacyFlip(RectTransform card)
        {
            var elapsed = 0f;
            var original = card.localScale;
            while (elapsed < LegacyRefreshPhaseSeconds)
            {
                elapsed += Time.unscaledDeltaTime;
                var t = Mathf.Clamp01(elapsed / LegacyRefreshPhaseSeconds);
                var x = Mathf.Lerp(.02f, 1f, Mathf.SmoothStep(0f, 1f, t));
                card.localScale = new Vector3(original.x * x, original.y, original.z);
                yield return null;
            }
            card.localScale = original;
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
                RenderContent(true);
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
