using System;
using System.Collections;
using System.Collections.Generic;
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
        GeneralRosterResponse _roster;
        Text _refreshText;
        Text _freeRefreshText;
        Button _refreshButton;
        Button _closeButton;
        List<RectTransform> _cards = new List<RectTransform>(5);
        int _type;
        bool _busy;
        float _nextClockUpdate;
        Coroutine _refreshAnimation;

        const int CivilFunction = 44;
        const int MilitaryFunction = 45;
        const int RefreshFunction = 55;
        const string ComponentRoot = "LegacyVisual/Component/";

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
            _ = panel.LoadAsync(true);
            return panel;
        }

        static bool HasFunction(PlayerView player, int id) => player?.functionIds != null && Array.IndexOf(player.functionIds, id) >= 0;

        void BuildFrame()
        {
            var blocker = LegacyUiFactory.Panel(transform, "TavernBlocker", Vector2.zero, Vector2.one, new Color(0, 0, 0, .48f));
            _window = LegacyUiFactory.PixelPanel(blocker, "TavernWindow", 309, 192, 662, 385, Color.clear);
            LegacyUiFactory.PixelImage(_window, ComponentRoot + "Window3/background", 0, 0, 662, 385);
            LegacyUiFactory.PixelImage(_window, "LegacyVisual/Tavern/01210", 0, 0, 628, 310);
            _closeButton = LegacyButton(_window, "", 639, 9, 21, 23, Close, "CloseButton3");
        }

        void Update()
        {
            if (_refreshText == null || Time.unscaledTime < _nextClockUpdate) return;
            _nextClockUpdate = Time.unscaledTime + 1f;
            UpdateRefreshClock();
        }

        async void SwitchType(int type)
        {
            if (_busy || _refreshAnimation != null || type == _type) return;
            _type = type;
            await LoadAsync(false);
        }

        async Task LoadAsync(bool loadRoster)
        {
            if (_busy) return;
            _busy = true;
            SetStatus("Đang mở Tửu Quán...");
            try
            {
                _data = await _api.GetTavernAsync(_type);
                if (loadRoster || _roster == null)
                {
                    try { _roster = await _api.GetGeneralsAsync(); }
                    catch { _roster = null; }
                }
                RenderContent(false);
                SetStatus("");
            }
            catch (Exception ex) { SetStatus(ex.Message); }
            finally { _busy = false; }
        }

        void RenderContent(bool animateRefresh, IReadOnlyList<RectTransform> previousCards = null)
        {
            var old = _window.Find("Content");
            if (old != null) Destroy(old.gameObject);
            var content = LegacyUiFactory.PixelPanel(_window, "Content", 0, 0, 662, 385, Color.clear);
            _closeButton.transform.SetAsLastSibling();

            DrawTabs(content);
            DrawOwnedGenerals(content);

            var cards = new List<RectTransform>(5);
            var offers = _data.offers ?? Array.Empty<TavernOfferView>();
            for (var slot = 1; slot <= 5; slot++)
            {
                var offer = offers.FirstOrDefault(x => x.position == slot);
                cards.Add(DrawOffer(content, slot, offer));
            }
            _cards = cards;

            if (HasFunction(_player, RefreshFunction))
            {
                _refreshText = LegacyUiFactory.PixelLabel(content, "", 12,
                    TextAnchor.MiddleRight, Color.white, 444, 358, 80, 20);
                AddOutline(_refreshText, new Color(.19f, .13f, .06f));
                _freeRefreshText = LegacyUiFactory.PixelLabel(content, "", 12,
                    TextAnchor.MiddleRight, Color.white, 465, 358, 80, 20);
                AddOutline(_freeRefreshText, new Color(.19f, .13f, .06f));
                _refreshButton = LegacyButton(content, "Cập nhật", 544, 349, 78, 35, async () => await RefreshAsync(), "Button22");
                var refreshState = _refreshButton.spriteState;
                refreshState.disabledSprite = Resources.Load<Sprite>(ComponentRoot + "Button22/disabled");
                _refreshButton.spriteState = refreshState;
                UpdateRefreshClock();
            }

            if (animateRefresh)
                _refreshAnimation = StartCoroutine(AnimateRefreshCards(cards, previousCards));
        }

        void DrawTabs(RectTransform parent)
        {
            var x = 18f;
            if (HasFunction(_player, MilitaryFunction))
            {
                DrawTab(parent, "Võ tướng", x, 2);
                x += 66f;
            }
            if (HasFunction(_player, CivilFunction)) DrawTab(parent, "Quan Văn", x, 1);
        }

        void DrawTab(RectTransform parent, string label, float x, int type)
        {
            var state = _type == type ? "selected_" : "";
            var button = LegacyUiFactory.PixelButton(parent, label, x, 50, 91, 32, () => SwitchType(type),
                ComponentRoot + "Button21/" + state + "up",
                ComponentRoot + "Button21/" + state + "over",
                ComponentRoot + "Button21/" + state + "down");
            var labelRect = button.GetComponentInChildren<Text>().rectTransform;
            labelRect.offsetMax = new Vector2(-18, -4);
            labelRect.offsetMin = new Vector2(0, 4);
        }

        void DrawOwnedGenerals(RectTransform parent)
        {
            LegacyUiFactory.PixelImage(parent, _type == 2 ? "LegacyVisual/Tavern/01244" : "LegacyVisual/Tavern/01246",
                70, 118, 102, 17);
            DrawBitmapNumber(parent, $"{_data.nowGeneralNum}/{_data.maxGeneralNum}", 178, 118);

            var list = _type == 2 ? (_roster?.military ?? Array.Empty<GeneralView>()) : (_roster?.civil ?? Array.Empty<GeneralView>());
            var max = Mathf.Clamp(_type == 2 ? (_roster?.militaryMax ?? _data.maxGeneralNum) : (_roster?.civilMax ?? _data.maxGeneralNum), 0, 5);
            for (var i = 0; i < 5; i++)
            {
                var x = 268 + i * 74;
                var general = i < list.Length ? list[i] : null;
                var quality = general == null ? 1 : Mathf.Clamp(general.quality, 1, 6);
                LegacyUiFactory.PixelImage(parent, $"LegacyVisual/Tavern/{(1185 + quality * 2):00000}", x, 99, 54, 54);

                if (general != null)
                {
                    LegacyUiFactory.PixelImage(parent, "LegacyVisual/GeneralPic/" + general.pic, x + 2, 101, 50, 50, true);
                    LegacyUiFactory.PixelImage(parent, "LegacyVisual/Tavern/01240", x + 2, 136, 50, 15);
                    var level = LegacyUiFactory.PixelLabel(parent, "Lv." + general.level, 11, TextAnchor.MiddleCenter,
                        new Color(1f, .8f, .35f), x + 2, 135, 50, 18);
                    AddOutline(level, Color.black);
                    TransparentButton(parent, x, 99, 54, 54, () => GeneralRosterPanel.Open(_host, _api, _status, _type));
                }
                else if (i >= max)
                {
                    LegacyUiFactory.PixelImage(parent, "LegacyVisual/Tavern/00640", x, 99, 54, 54);
                    var closed = LegacyUiFactory.PixelLabel(parent, "Chưa mở", 11, TextAnchor.MiddleCenter,
                        new Color(.4f, .4f, .4f), x + 7, 117, 40, 20);
                    AddOutline(closed, Color.black);
                }
            }
        }

        RectTransform DrawOffer(RectTransform parent, int slot, TavernOfferView offer)
        {
            var card = LegacyUiFactory.PixelPanel(parent, "Offer_" + slot, 50 + (slot - 1) * 116, 168, 105, 177, Color.clear);
            LegacyUiFactory.PixelImage(card, "LegacyVisual/Tavern/01216", 0, 0, 105, 177);
            if (offer == null) return card;

            var q = Mathf.Clamp(offer.quality, 1, 6);
            LegacyUiFactory.PixelImage(card, $"LegacyVisual/Tavern/{(697 + q * 2):00000}", 14, 40, 76, 76);
            LegacyUiFactory.PixelImage(card, "LegacyVisual/GeneralPic/" + offer.pic, 16, 42, 72, 72, true);
            var name = LegacyUiFactory.PixelLabel(card, offer.name, 14, TextAnchor.MiddleCenter, QualityColor(q), 14, 12, 70, 20);
            AddOutline(name, new Color(.19f, .13f, .06f));

            var currency = offer.isGold ? "LegacyVisual/Tavern/00891" : "LegacyVisual/Tavern/01225";
            LegacyUiFactory.PixelImage(card, currency, 14, 120, 24, 16);
            var price = LegacyUiFactory.PixelLabel(card, offer.price.ToString(), 12, TextAnchor.MiddleLeft, Color.white, 36, 119, 60, 20);
            AddOutline(price, new Color(.19f, .13f, .06f));

            if (offer.bought)
            {
                LegacyUiFactory.PixelImage(card, "LegacyVisual/Tavern/01235", 0, 65, 95, 54);
                return card;
            }

            var lockPath = offer.locked ? "LegacyVisual/Tavern/01219" : "LegacyVisual/Tavern/01222";
            LegacyUiFactory.PixelButton(card, "", 75, 45, 10, 12, async () => await ToggleLockAsync(offer), lockPath);
            LegacyButton(card, "Chiêu mộ", 15, 138, 78, 35, async () => await RecruitAsync(offer), "Button23");
            return card;
        }

        List<RectTransform> CloneCurrentCards()
        {
            var clones = new List<RectTransform>(_cards.Count);
            foreach (var card in _cards)
            {
                if (card == null) continue;
                var clone = Instantiate(card, _window, false);
                clone.name = card.name + "_Previous";
                var group = clone.gameObject.AddComponent<CanvasGroup>();
                group.interactable = false;
                group.blocksRaycasts = false;
                clones.Add(clone);
            }
            return clones;
        }

        IEnumerator AnimateRefreshCards(IReadOnlyList<RectTransform> cards, IReadOnlyList<RectTransform> previousCards)
        {
            foreach (var card in cards) card.localScale = new Vector3(0, 1, 1);
            for (var index = 0; index < cards.Count; index++)
            {
                var card = cards[index];
                var previous = previousCards != null && index < previousCards.Count ? previousCards[index] : null;
                var previousStart = previous == null ? Vector2.zero : previous.anchoredPosition;
                var elapsed = 0f;
                while (elapsed < 2f)
                {
                    elapsed += Time.unscaledDeltaTime;
                    var progress = Mathf.Clamp01(elapsed / 2f);
                    card.localScale = new Vector3(progress, 1, 1);
                    if (previous != null)
                    {
                        previous.localScale = new Vector3(1f - progress, 1, 1);
                        previous.anchoredPosition = previousStart + new Vector2(193f * progress, 0);
                    }
                    yield return null;
                }
                card.localScale = Vector3.one;
                if (previous != null) Destroy(previous.gameObject);
            }
            if (previousCards != null)
                foreach (var previous in previousCards)
                    if (previous != null) Destroy(previous.gameObject);
            _refreshAnimation = null;
        }

        void DrawBitmapNumber(RectTransform parent, string value, float x, float y)
        {
            for (var i = 0; i < value.Length; i++)
            {
                var c = value[i];
                var id = c == '/' ? 1249 : c == '0' ? 568 : 548 + (c - '0') * 2;
                LegacyUiFactory.PixelImage(parent, $"LegacyVisual/Tavern/{id:00000}", x + i * 13, y, 14, 16);
            }
        }

        Button LegacyButton(RectTransform parent, string text, float x, float y, float width, float height,
            UnityEngine.Events.UnityAction onClick, string skin)
        {
            return LegacyUiFactory.PixelButton(parent, text, x, y, width, height, onClick,
                ComponentRoot + skin + "/up", ComponentRoot + skin + "/over", ComponentRoot + skin + "/down");
        }

        static void TransparentButton(RectTransform parent, float x, float y, float width, float height, UnityEngine.Events.UnityAction onClick)
        {
            var button = LegacyUiFactory.PixelButton(parent, "", x, y, width, height, onClick);
            button.image.color = Color.clear;
        }

        static void AddOutline(Text text, Color color)
        {
            var outline = text.gameObject.AddComponent<Outline>();
            outline.effectColor = color;
            outline.effectDistance = new Vector2(1, -1);
        }

        static Color QualityColor(int q) => q switch
        {
            1 => Color.white,
            2 => new Color(.45f, 1f, .45f),
            3 => new Color(.45f, .75f, 1f),
            4 => new Color(.75f, .45f, 1f),
            5 => new Color(1f, .62f, .25f),
            _ => new Color(1f, .32f, .25f)
        };

        void UpdateRefreshClock()
        {
            var sec = RefreshRemainingSeconds(_data?.nextRefreshAt);
            _refreshText.text = sec <= 0 ? "" : "CD:" + FormatRemainingTime(sec);
            if (_freeRefreshText != null) _freeRefreshText.text = sec <= 0 ? "Miễn phí" : "";
            if (_refreshButton != null) _refreshButton.interactable = sec < 3600;
        }

        static string FormatRemainingTime(int sec) => sec >= 3600
            ? $"{sec / 3600:00}:{sec / 60 % 60:00}:{sec % 60:00}"
            : $"{sec / 60:00}:{sec % 60:00}";

        static int RefreshRemainingSeconds(string iso)
        {
            if (!DateTimeOffset.TryParse(iso, out var next)) return 0;
            return Math.Max(0, (int)Math.Ceiling((next - DateTimeOffset.UtcNow).TotalSeconds));
        }

        async Task RefreshAsync()
        {
            if (_busy || _refreshAnimation != null) return;
            _busy = true;
            try
            {
                _data = await _api.RefreshTavernAsync(_type);
                var previousCards = CloneCurrentCards();
                RenderContent(true, previousCards);
                if (_onChanged != null) await _onChanged();
            }
            catch (Exception ex) { SetStatus(ex.Message); }
            finally { _busy = false; }
        }

        async Task ToggleLockAsync(TavernOfferView offer)
        {
            if (_busy || _refreshAnimation != null) return;
            _busy = true;
            try
            {
                _data = await _api.LockGeneralAsync(offer.generalId, !offer.locked);
                RenderContent(false);
            }
            catch (Exception ex) { SetStatus(ex.Message); }
            finally { _busy = false; }
        }

        async Task RecruitAsync(TavernOfferView offer)
        {
            if (_busy || _refreshAnimation != null) return;
            _busy = true;
            try
            {
                await _api.RecruitGeneralAsync(offer.generalId);
                _data = await _api.GetTavernAsync(_type);
                try { _roster = await _api.GetGeneralsAsync(); } catch { }
                RenderContent(false);
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
