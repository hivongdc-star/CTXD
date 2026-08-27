using System;
using System.Linq;
using System.Threading.Tasks;
using CTXD.Client.Features.FirstPlayable;
using CTXD.Client.Networking;
using UnityEngine;
using UnityEngine.UI;

namespace CTXD.Client.Features.Equipment
{
    /// <summary>
    /// Legacy-shaped equipment shop/inventory surface. The panel deliberately uses extracted Equip.swf
    /// bitmaps and the original 628x310 footprint instead of introducing a new mobile/desktop design.
    /// </summary>
    public sealed class EquipmentPanel : MonoBehaviour
    {
        const int MilitaryStoreFunction = 18;
        const int CivilStoreFunction = 17;

        ApiClient _api;
        PlayerView _player;
        RectTransform _host;
        RectTransform _window;
        Action<string> _status;
        Func<Task> _onChanged;
        StoreResponse _store;
        InventoryResponse _inventory;
        GeneralRosterResponse _generals;
        int _type = 1;
        bool _inventoryMode;
        bool _busy;

        public static EquipmentPanel Open(RectTransform host, ApiClient api, PlayerView player, Action<string> status, Func<Task> onChanged)
        {
            var go = new GameObject("EquipmentPanel");
            go.transform.SetParent(host, false);
            var panel = go.AddComponent<EquipmentPanel>();
            panel._host = host;
            panel._api = api;
            panel._player = player;
            panel._status = status;
            panel._onChanged = onChanged;
            panel._type = HasFunction(player, MilitaryStoreFunction) ? 1 : 2;
            panel.BuildFrame();
            _ = panel.LoadStoreAsync();
            return panel;
        }

        static bool HasFunction(PlayerView player, int id) => player?.functionIds != null && Array.IndexOf(player.functionIds, id) >= 0;

        void BuildFrame()
        {
            var blocker = LegacyUiFactory.Panel(transform, "EquipBlocker", Vector2.zero, Vector2.one, new Color(0, 0, 0, .5f));
            _window = LegacyUiFactory.PixelPanel(blocker, "EquipWindow", 326, 229, 628, 310, Color.clear);
            DrawShell();
        }

        void DrawShell()
        {
            LegacyUiFactory.DestroyChildren(_window);
            LegacyUiFactory.PixelImage(_window, _inventoryMode ? "LegacyVisual/Equip/00136" : "LegacyVisual/Equip/00509", 0, 0,
                _inventoryMode ? 626 : 628, _inventoryMode ? 298 : 310);
            LegacyUiFactory.PixelLabel(_window, _inventoryMode ? "KHO TRANG BỊ" : "CỬA HÀNG TRANG BỊ", 20,
                TextAnchor.MiddleCenter, new Color(1f,.82f,.35f), 200, 4, 230, 28);

            if (!_inventoryMode)
            {
                if (HasFunction(_player, MilitaryStoreFunction))
                    LegacyUiFactory.PixelButton(_window, "Võ", 10, 8, 55, 24, () => SwitchStore(1));
                if (HasFunction(_player, CivilStoreFunction))
                    LegacyUiFactory.PixelButton(_window, "Văn", 70, 8, 55, 24, () => SwitchStore(2));
            }

            LegacyUiFactory.PixelButton(_window, _inventoryMode ? "Cửa hàng" : "Kho", 480, 7, 72, 25, ToggleMode);
            LegacyUiFactory.PixelButton(_window, "Bảo vật", 400, 7, 74, 25, () => TreasurePanel.Open(_host,_api,_status));
            LegacyUiFactory.PixelButton(_window, "", 558, 8, 58, 24, Close, "LegacyVisual/Equip/00429");
        }

        async void SwitchStore(int type)
        {
            if (_busy || _type == type) return;
            _type = type;
            await LoadStoreAsync();
        }

        async void ToggleMode()
        {
            if (_busy) return;
            _inventoryMode = !_inventoryMode;
            DrawShell();
            if (_inventoryMode) await LoadInventoryAsync(); else await LoadStoreAsync();
        }

        async Task LoadStoreAsync()
        {
            if (_busy) return;
            _busy = true;
            SetStatus("Đang mở cửa hàng trang bị...");
            try
            {
                _store = await _api.GetEquipmentStoreAsync(_type);
                DrawShell();
                RenderStore();
                SetStatus("");
            }
            catch (Exception ex) { SetStatus(ex.Message); }
            finally { _busy = false; }
        }

        void RenderStore()
        {
            if (_store == null) return;
            LegacyUiFactory.PixelImage(_window, "LegacyVisual/Equip/00437", 12, 42, 87, 86, true);
            LegacyUiFactory.PixelLabel(_window, $"Kho {_store.nowItemNum}/{_store.maxItemNum}", 13, TextAnchor.MiddleLeft,
                new Color(1f,.88f,.55f), 12, 130, 100, 22);
            LegacyUiFactory.PixelLabel(_window, $"Thân mật {_store.intimacy}", 12, TextAnchor.MiddleLeft,
                new Color(.92f,.82f,.63f), 12, 151, 100, 22);

            var remain = RefreshRemainingText(_store.nextRefreshAt);
            LegacyUiFactory.PixelLabel(_window, remain, 12, TextAnchor.MiddleLeft, Color.white, 12, 176, 104, 35);
            LegacyUiFactory.PixelButton(_window, "Làm mới", 17, 216, 82, 27, async () => await RefreshAsync(),
                "LegacyVisual/Equip/00499", "LegacyVisual/Equip/00501", "LegacyVisual/Equip/00503");

            var offers = _store.offers ?? Array.Empty<StoreOfferView>();
            for (var position = 1; position <= 6; position++)
                DrawStoreOffer(position, offers.FirstOrDefault(x => x.position == position));
        }

        void DrawStoreOffer(int position, StoreOfferView offer)
        {
            var x = 116 + (position - 1) * 84;
            const float y = 42;
            // Legacy renderer is 92px wide; slight overlap is also how the original six-card strip fits its 628px frame.
            LegacyUiFactory.PixelImage(_window, "LegacyVisual/Equip/00506", x, y, 92, 166);
            if (offer == null)
            {
                LegacyUiFactory.PixelLabel(_window, "—", 18, TextAnchor.MiddleCenter, new Color(.65f,.6f,.5f), x+8, y+50, 76, 54);
                return;
            }

            LegacyUiFactory.PixelImage(_window, QualityShopPath(offer.quality), x+8, y+9, 76, 76);
            LegacyUiFactory.PixelImage(_window, TypeIconPath(offer.goodsType), x+20, y+20, 52, 52, true);
            LegacyUiFactory.PixelLabel(_window, offer.name, 12, TextAnchor.MiddleCenter, QualityColor(offer.quality), x+2, y+87, 88, 36);
            LegacyUiFactory.PixelLabel(_window, $"Lv.{offer.level}  +{offer.attribute}", 11, TextAnchor.MiddleCenter, Color.white, x+4, y+121, 84, 18);

            var currency = offer.isGold ? "LegacyVisual/Equip/00522" : "LegacyVisual/Equip/00519";
            LegacyUiFactory.PixelImage(_window, currency, x+8, y+141, 24, 16, true);
            LegacyUiFactory.PixelLabel(_window, offer.price.ToString(), 11, TextAnchor.MiddleLeft,
                offer.isCheap ? new Color(.45f,1f,.45f) : new Color(1f,.9f,.6f), x+33, y+138, 55, 19);

            if (offer.bought)
            {
                LegacyUiFactory.PixelLabel(_window, "Đã mua", 12, TextAnchor.MiddleCenter, new Color(.7f,.7f,.7f), x+4, y+160, 84, 20);
                return;
            }

            LegacyUiFactory.PixelButton(_window, "", x+5, y+160, 20, 20, async () => await ToggleLockAsync(offer),
                offer.locked ? "LegacyVisual/Equip/00513" : "LegacyVisual/Equip/00516");
            LegacyUiFactory.PixelButton(_window, "Mua", x+29, y+159, 58, 21, async () => await BuyAsync(offer));
        }

        async Task RefreshAsync()
        {
            if (_busy) return;
            _busy = true;
            try
            {
                _store = await _api.RefreshEquipmentStoreAsync(_type);
                DrawShell(); RenderStore();
                if (_onChanged != null) await _onChanged();
            }
            catch (Exception ex) { SetStatus(ex.Message); }
            finally { _busy = false; }
        }

        async Task ToggleLockAsync(StoreOfferView offer)
        {
            if (_busy) return;
            _busy = true;
            try
            {
                _store = await _api.LockEquipmentOfferAsync(offer.equipmentId, !offer.locked);
                DrawShell(); RenderStore();
            }
            catch (Exception ex) { SetStatus(ex.Message); }
            finally { _busy = false; }
        }

        async Task BuyAsync(StoreOfferView offer)
        {
            if (_busy) return;
            _busy = true;
            try
            {
                await _api.BuyEquipmentAsync(offer.equipmentId);
                _store = await _api.GetEquipmentStoreAsync(_type);
                DrawShell(); RenderStore();
                SetStatus($"Đã mua {offer.name}.");
                if (_onChanged != null) await _onChanged();
            }
            catch (Exception ex) { SetStatus(ex.Message); }
            finally { _busy = false; }
        }

        async Task LoadInventoryAsync()
        {
            if (_busy) return;
            _busy = true;
            SetStatus("Đang mở kho trang bị...");
            try
            {
                _inventory = await _api.GetEquipmentInventoryAsync();
                _generals ??= await _api.GetGeneralsAsync();
                DrawShell(); RenderInventory();
                SetStatus("");
            }
            catch (Exception ex) { SetStatus(ex.Message); }
            finally { _busy = false; }
        }

        void RenderInventory()
        {
            if (_inventory == null) return;
            LegacyUiFactory.PixelLabel(_window, $"{_inventory.nowItemNum}/{_inventory.maxItemNum}", 13,
                TextAnchor.MiddleRight, new Color(1f,.88f,.55f), 480, 39, 110, 22);

            var items = _inventory.items ?? Array.Empty<PlayerEquipmentView>();
            var shown = items.Take(18).ToArray();
            for (var i = 0; i < shown.Length; i++)
            {
                var col = i % 6; var row = i / 6;
                DrawInventoryItem(shown[i], 25 + col * 96, 63 + row * 72);
            }
            if (items.Length > shown.Length)
                LegacyUiFactory.PixelLabel(_window, $"+{items.Length-shown.Length} vật phẩm", 12, TextAnchor.MiddleRight,
                    Color.white, 450, 276, 150, 18);
        }

        void DrawInventoryItem(PlayerEquipmentView item, float x, float y)
        {
            LegacyUiFactory.PixelImage(_window, "LegacyVisual/Equip/00298", x, y, 88, 64);
            LegacyUiFactory.PixelImage(_window, QualityInventoryPath(item.quality), x+4, y+5, 54, 54);
            LegacyUiFactory.PixelImage(_window, TypeIconPath(item.goodsType), x+10, y+11, 42, 42, true);
            LegacyUiFactory.PixelLabel(_window, item.name, 10, TextAnchor.UpperLeft, QualityColor(item.quality), x+58, y+4, 30, 32);
            LegacyUiFactory.PixelLabel(_window, item.ownerGeneralId > 0 ? "Đã mặc" : "Kho", 9, TextAnchor.MiddleLeft,
                item.ownerGeneralId > 0 ? new Color(.55f,1f,.55f) : Color.white, x+58, y+35, 30, 14);

            var button = LegacyUiFactory.PixelButton(_window, "", x, y, 88, 64, () => OpenItemActions(item));
            button.GetComponent<Image>().color = new Color(1,1,1,.02f);
        }

        void OpenItemActions(PlayerEquipmentView item)
        {
            var panel = LegacyUiFactory.PixelPanel(_window, "ItemActions", 178, 73, 272, 164, new Color(.035f,.025f,.018f,.97f));
            LegacyUiFactory.PixelLabel(panel, item.name, 16, TextAnchor.MiddleCenter, QualityColor(item.quality), 10, 8, 252, 27);
            LegacyUiFactory.PixelLabel(panel, $"Lv.{item.level}  Thuộc tính +{item.attribute}\nBán: {item.copperSold} bạc", 13,
                TextAnchor.MiddleLeft, Color.white, 18, 40, 236, 45);

            if (item.ownerGeneralId > 0)
                LegacyUiFactory.PixelButton(panel, "Tháo", 20, 103, 70, 30, async () => await UnequipAsync(item, panel));
            else
            {
                LegacyUiFactory.PixelButton(panel, "Mặc", 20, 103, 70, 30, () => OpenGeneralPicker(item, panel));
                LegacyUiFactory.PixelButton(panel, "Bán", 101, 103, 70, 30, async () => await SellAsync(item, panel));
            }
            LegacyUiFactory.PixelButton(panel, "Đóng", 182, 103, 70, 30, () => Destroy(panel.gameObject));
        }

        void OpenGeneralPicker(PlayerEquipmentView item, RectTransform itemPanel)
        {
            var compatibleType = item.goodsType <= 6 ? 2 : 1;
            var source = compatibleType == 2 ? _generals?.military : _generals?.civil;
            var list = source ?? Array.Empty<GeneralView>();
            LegacyUiFactory.DestroyChildren(itemPanel);
            LegacyUiFactory.PixelLabel(itemPanel, "Chọn tướng", 16, TextAnchor.MiddleCenter, new Color(1f,.82f,.35f), 10, 7, 252, 26);
            if (list.Length == 0)
            {
                LegacyUiFactory.PixelLabel(itemPanel, "Chưa có tướng phù hợp.", 13, TextAnchor.MiddleCenter, Color.white, 12, 48, 248, 42);
            }
            for (var i = 0; i < Math.Min(4, list.Length); i++)
            {
                var g = list[i];
                LegacyUiFactory.PixelButton(itemPanel, $"{g.name} Lv.{g.level}", 16, 40+i*28, 240, 24,
                    async () => await EquipAsync(item, g, itemPanel));
            }
            LegacyUiFactory.PixelButton(itemPanel, "Hủy", 96, 134, 80, 24, () => Destroy(itemPanel.gameObject));
        }

        async Task EquipAsync(PlayerEquipmentView item, GeneralView general, RectTransform overlay)
        {
            if (_busy) return;
            _busy = true;
            try
            {
                await _api.EquipEquipmentAsync(item.instanceId, general.id);
                Destroy(overlay.gameObject);
                _inventory = await _api.GetEquipmentInventoryAsync();
                DrawShell(); RenderInventory();
                SetStatus($"Đã trang bị {item.name} cho {general.name}.");
                if (_onChanged != null) await _onChanged();
            }
            catch (Exception ex) { SetStatus(ex.Message); }
            finally { _busy = false; }
        }

        async Task UnequipAsync(PlayerEquipmentView item, RectTransform overlay)
        {
            if (_busy) return;
            _busy = true;
            try
            {
                await _api.UnequipEquipmentAsync(item.instanceId);
                Destroy(overlay.gameObject);
                _inventory = await _api.GetEquipmentInventoryAsync();
                DrawShell(); RenderInventory();
                SetStatus($"Đã tháo {item.name}.");
                if (_onChanged != null) await _onChanged();
            }
            catch (Exception ex) { SetStatus(ex.Message); }
            finally { _busy = false; }
        }

        async Task SellAsync(PlayerEquipmentView item, RectTransform overlay)
        {
            if (_busy) return;
            _busy = true;
            try
            {
                var sold = await _api.SellEquipmentAsync(item.instanceId);
                Destroy(overlay.gameObject);
                _inventory = await _api.GetEquipmentInventoryAsync();
                DrawShell(); RenderInventory();
                SetStatus($"Đã bán {item.name}, nhận {sold.copperGained} bạc.");
                if (_onChanged != null) await _onChanged();
            }
            catch (Exception ex) { SetStatus(ex.Message); }
            finally { _busy = false; }
        }

        static string TypeIconPath(int goodsType)
        {
            var slot = goodsType > 6 ? goodsType - 6 : goodsType;
            return slot switch
            {
                1 => "LegacyVisual/Equip/00411", // weapon
                2 => "LegacyVisual/Equip/00414", // horse
                3 => "LegacyVisual/Equip/00405", // armour
                4 => "LegacyVisual/Equip/00408", // cloak
                5 => "LegacyVisual/Equip/00417", // banner
                _ => "LegacyVisual/Equip/00402"   // token
            };
        }

        static string QualityShopPath(int quality)
        {
            var q = Mathf.Clamp(quality, 1, 6);
            return $"LegacyVisual/Equip/{(476 + q*2):00000}";
        }

        static string QualityInventoryPath(int quality)
        {
            var q = Mathf.Clamp(quality, 1, 6);
            return $"LegacyVisual/Equip/{(302 + q*2):00000}";
        }

        static Color QualityColor(int q) => q switch
        {
            1 => Color.white,
            2 => new Color(.45f,1f,.45f),
            3 => new Color(.45f,.75f,1f),
            4 => new Color(.75f,.45f,1f),
            5 => new Color(1f,.62f,.25f),
            _ => new Color(1f,.32f,.25f)
        };

        static string RefreshRemainingText(string iso)
        {
            if (!DateTimeOffset.TryParse(iso, out var next)) return "";
            var sec = Math.Max(0, (int)Math.Ceiling((next - DateTimeOffset.UtcNow).TotalSeconds));
            return sec <= 0 ? "Có thể làm mới" : $"Chờ {sec / 60:00}:{sec % 60:00}";
        }

        void SetStatus(string value) => _status?.Invoke(value);
        void Close() => Destroy(gameObject);
    }
}
