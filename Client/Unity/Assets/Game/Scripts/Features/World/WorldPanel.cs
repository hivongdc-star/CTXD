using System;
using System.Linq;
using System.Threading.Tasks;
using CTXD.Client.Features.FirstPlayable;
using CTXD.Client.Networking;
using UnityEngine;
using UnityEngine.UI;
using System.Xml;
using CTXD.Client.Features.Battle;

namespace CTXD.Client.Features.World
{
    public sealed class WorldPanel : MonoBehaviour
    {
        const float MapScale = .145f;
        static WorldPanel _open;
        ApiClient _api; Action<string> _status; RectTransform _window;
        WorldResponse _world; GeneralRosterResponse _generals;
        int _generalId, _cityId; bool _busy;

        public static WorldPanel Open(RectTransform host, ApiClient api, Action<string> status)
        {
            var go = new GameObject("WorldPanel"); go.transform.SetParent(host, false);
            var panel = go.AddComponent<WorldPanel>(); panel._api = api; panel._status = status;
            _open = panel; panel.Build(); _ = panel.LoadAsync(); return panel;
        }

        public static void RefreshOpenFromPush() { if (_open != null && !_open._busy) _ = _open.RefreshAsync(); }
        void OnDestroy() { if (_open == this) _open = null; }

        async Task RefreshAsync()
        {
            try { _world = await _api.GetWorldAsync(); _generals = await _api.GetGeneralsAsync(); if (_window != null) Draw(); }
            catch (Exception ex) { _status(ex.Message); }
        }

        void Build()
        {
            var blocker = LegacyUiFactory.Panel(transform, "WorldBlocker", Vector2.zero, Vector2.one, new Color(0, 0, 0, .82f));
            _window = LegacyUiFactory.PixelPanel(blocker, "WorldWindow", 55, 43, 1170, 634, new Color(.045f, .036f, .025f, 1));
            LegacyUiFactory.PixelLabel(_window, "THẾ GIỚI", 23, TextAnchor.MiddleCenter, new Color(1f, .82f, .35f), 450, 8, 270, 34);
            LegacyUiFactory.PixelButton(_window, "Đóng", 1082, 9, 72, 28, () => Destroy(gameObject));
            LegacyUiFactory.PixelLabel(_window, "Đang tải World từ server...", 16, TextAnchor.MiddleCenter, Color.white, 300, 275, 570, 40);
        }

        async Task LoadAsync()
        {
            try
            {
                _world = await _api.GetWorldAsync(); _generals = await _api.GetGeneralsAsync();
                var military = _generals?.military ?? Array.Empty<GeneralView>();
                if (military.Length > 0) _generalId = _world.focusGeneralId != 0 ? _world.focusGeneralId : military[0].id;
                _cityId = _world.capitalCityId; Draw();
            }
            catch (Exception ex) { _status(ex.Message); }
        }

        void Draw()
        {
            if (_window == null || _world == null) return;
            LegacyUiFactory.DestroyChildren(_window);
            LegacyUiFactory.PixelLabel(_window, "THẾ GIỚI", 23, TextAnchor.MiddleCenter, new Color(1f, .82f, .35f), 450, 8, 270, 34);
            LegacyUiFactory.PixelButton(_window, "Đóng", 1082, 9, 72, 28, () => Destroy(gameObject));
            DrawSidebar(); DrawMap();
        }

        void DrawMap()
        {
            var map = LegacyUiFactory.PixelPanel(_window, "LegacyWorldMap", 280, 48, 870, 522, Color.black);
            for (var column = 1; column <= 6; column++)
                for (var row = 1; row <= 6; row++)
                    LegacyUiFactory.PixelImage(map, $"LegacyVisual/World/Tiles/block{column}_{row}",
                        (column - 1) * 1000 * MapScale, (row - 1) * 600 * MapScale, 1000 * MapScale, 600 * MapScale);
            foreach (var city in (_world.cities ?? Array.Empty<WorldCityView>()).Where(x => !x.fogged))
            {
                var model = string.IsNullOrEmpty(city.model) ? "301" : city.model.Substring(city.model.LastIndexOf("model", StringComparison.Ordinal) + 5);
                var sprite = Resources.Load<Sprite>("LegacyVisual/World/City/model" + model) ?? Resources.Load<Sprite>("LegacyVisual/World/City/model301");
                var width = sprite == null ? 18f : sprite.rect.width * MapScale; var height = sprite == null ? 16f : sprite.rect.height * MapScale;
                var button = LegacyUiFactory.PixelButton(map, "", city.x * MapScale - width / 2, city.y * MapScale - height / 2,
                    width, height, () => SelectCity(city.id));
                var image = button.GetComponent<Image>();
                image.sprite = sprite; image.color = ForceColor(city.ownerForceId, city.attackable);
            }
            DrawLegacyFog(map);
            LegacyUiFactory.PixelLabel(_window, "Bản đồ và tọa độ CityInfo legacy · server quyết định fog/ownership/hành quân", 13,
                TextAnchor.MiddleLeft, new Color(.82f, .78f, .67f), 280, 578, 870, 26);
        }

        void DrawLegacyFog(RectTransform map)
        {
            var xmlAsset = Resources.Load<TextAsset>("LegacyData/World/WorldFog");
            var sprite = Resources.Load<Sprite>("LegacyVisual/World/fog");
            if (xmlAsset == null || sprite == null) return;
            var document = new XmlDocument(); document.LoadXml(xmlAsset.text);
            foreach (XmlNode node in document.SelectNodes("/config/bindCities/city"))
            {
                if (!int.TryParse(node.Attributes?["id"]?.Value, out var id)) continue;
                var city = (_world.cities ?? Array.Empty<WorldCityView>()).FirstOrDefault(x => x.id == id && x.fogged);
                if (city == null) continue;
                LegacyUiFactory.PixelImage(map, "LegacyVisual/World/fog", city.x * MapScale - sprite.rect.width * MapScale / 2,
                    city.y * MapScale - sprite.rect.height * MapScale / 2, sprite.rect.width * MapScale, sprite.rect.height * MapScale);
            }
        }

        void DrawSidebar()
        {
            var generals = _generals?.military ?? Array.Empty<GeneralView>();
            LegacyUiFactory.PixelLabel(_window, "Võ tướng", 17, TextAnchor.MiddleLeft, new Color(1f, .82f, .35f), 14, 48, 240, 25);
            for (var i = 0; i < Math.Min(4, generals.Length); i++)
            {
                var general = generals[i];
                var moving = (_world.moves ?? Array.Empty<WorldMoveView>()).Any(x => x.generalId == general.id);
                var battle = (_world.battles ?? Array.Empty<WorldBattleHandoffView>()).Any(x => x.attackerGeneralId == general.id && x.status == 0);
                var suffix = battle ? " [chiến đấu]" : moving ? " [đang đi]" : "";
                LegacyUiFactory.PixelButton(_window, (general.id == _generalId ? "▶ " : "") + general.name + suffix,
                    12, 76 + i * 43, 252, 35, () => { _generalId = general.id; Draw(); });
            }
            var city = (_world.cities ?? Array.Empty<WorldCityView>()).FirstOrDefault(x => x.id == _cityId);
            if (city == null) return;
            var owner = city.ownerForceId == 0 ? "Trung lập" : city.ownerForceId == 1 ? "Ngụy" : city.ownerForceId == 2 ? "Thục" : "Ngô";
            LegacyUiFactory.PixelLabel(_window, city.name + "  #" + city.id, 19, TextAnchor.MiddleLeft, Color.white, 14, 266, 245, 28);
            LegacyUiFactory.PixelLabel(_window, "Chủ quyền: " + owner + "\nĐịa hình: " + city.terrain + "\nSản lượng: " + city.output +
                (city.attackable ? "\nCó thể tấn công" : ""), 15, TextAnchor.UpperLeft, new Color(.88f, .84f, .74f), 14, 298, 245, 90);
            LegacyUiFactory.PixelButton(_window, "Chi tiết thành", 14, 397, 118, 34, async () => await DetailAsync(city.id));
            LegacyUiFactory.PixelButton(_window, "Đi trực tiếp", 140, 397, 118, 34, async () => await MoveAsync(city.id, false));
            LegacyUiFactory.PixelButton(_window, "Tự tìm đường", 14, 440, 244, 38, async () => await MoveAsync(city.id, true));
            LegacyUiFactory.PixelButton(_window, "KFGZ", 14, 488, 244, 38, () => KfgzPanel.Open((RectTransform)_window.parent, _api, _status));
            LegacyUiFactory.PixelButton(_window, "Tự động quốc chiến", 14, 536, 244, 34,
                () => AutoBattlePanel.Open((RectTransform)_window.parent, _api, _status, city.id));
            LegacyUiFactory.PixelButton(_window, "Truân Điền", 14, 578, 244, 34,
                () => FarmPanel.Open((RectTransform)_window.parent, _api, _status, _generalId));
            LegacyUiFactory.PixelButton(_window, "Khoáng", 1018, 578, 132, 34,
                () => MinePanel.Open((RectTransform)_window.parent, _api, _status, _generalId));
        }

        void SelectCity(int cityId) { _cityId = cityId; Draw(); }

        async Task DetailAsync(int cityId)
        {
            if (_busy) return; _busy = true;
            try { var detail = await _api.GetWorldCityAsync(cityId); if(detail.inBattle&&detail.battle!=null)BattlePanel.Open((RectTransform)_window.parent,_api,_status,detail.battle.id);else _status(detail.city.name+" không có chiến đấu."); }
            catch (Exception ex) { _status(ex.Message); }
            finally { _busy = false; }
        }

        async Task MoveAsync(int cityId, bool auto)
        {
            if (_busy || _generalId == 0) return; _busy = true;
            try
            {
                _world = auto ? await _api.AutoMoveWorldGeneralAsync(_generalId, cityId) : await _api.MoveWorldGeneralAsync(_generalId, cityId);
                _generals = await _api.GetGeneralsAsync(); Draw();
                var battle = (_world.battles ?? Array.Empty<WorldBattleHandoffView>()).Any(x => x.attackerGeneralId == _generalId && x.status == 0);
                _status(battle ? "Đã chuyển sang luồng chiến đấu." : "Đã bắt đầu hành quân.");
            }
            catch (Exception ex) { _status(ex.Message); }
            finally { _busy = false; }
        }

        static Color ForceColor(int force, bool attackable)
        {
            var color = force == 1 ? new Color(.35f, .55f, 1f) : force == 2 ? new Color(.25f, .85f, .38f) :
                force == 3 ? new Color(1f, .32f, .25f) : new Color(.72f, .68f, .58f);
            return attackable ? Color.Lerp(color, new Color(1f, .82f, .18f), .55f) : color;
        }
    }
}
