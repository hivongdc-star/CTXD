using System;
using System.Threading.Tasks;
using CTXD.Client.Features.FirstPlayable;
using CTXD.Client.Networking;
using UnityEngine;
using UnityEngine.UI;

namespace CTXD.Client.Features.Tavern
{
    public sealed class GeneralRosterPanel : MonoBehaviour
    {
        ApiClient _api;
        RectTransform _window;
        Button _closeButton;
        Action<string> _status;
        GeneralRosterResponse _data;
        int _type = 2;
        int _selectedIndex;
        bool _busy;

        const string ComponentRoot = "LegacyVisual/Component/";
        const string TavernRoot = "LegacyVisual/Tavern/";
        const string GeneralPicRoot = "LegacyVisual/GeneralPic/";
        const string GeneralPicMaxRoot = "LegacyVisual/GeneralPicMax/";

        public static GeneralRosterPanel Open(RectTransform host, ApiClient api, Action<string> status, int initialType = 2)
        {
            var go = new GameObject("GeneralRosterPanel");
            go.transform.SetParent(host, false);
            var panel = go.AddComponent<GeneralRosterPanel>();
            panel._api = api;
            panel._status = status;
            panel._type = initialType;
            panel.BuildFrame();
            _ = panel.LoadAsync();
            return panel;
        }

        void BuildFrame()
        {
            var blocker = LegacyUiFactory.Panel(transform, "GeneralBlocker", Vector2.zero, Vector2.one, new Color(0, 0, 0, .48f));
            _window = LegacyUiFactory.PixelPanel(blocker, "GeneralWindow", 309, 192, 662, 385, Color.clear);
            LegacyUiFactory.PixelImage(_window, ComponentRoot + "Window3/background", 0, 0, 662, 385);
            _closeButton = LegacyUiFactory.PixelButton(_window, "", 639, 9, 21, 23, () => Destroy(gameObject),
                ComponentRoot + "CloseButton3/up",
                ComponentRoot + "CloseButton3/over",
                ComponentRoot + "CloseButton3/down");
        }

        async Task LoadAsync()
        {
            if (_busy) return;
            _busy = true;
            _status?.Invoke("Đang mở Tướng Lĩnh...");
            try
            {
                _data = await _api.GetGeneralsAsync();
                _selectedIndex = 0;
                Render();
                _status?.Invoke("");
            }
            catch (Exception ex) { _status?.Invoke(ex.Message); }
            finally { _busy = false; }
        }

        GeneralView[] CurrentList => _type == 2
            ? (_data?.military ?? Array.Empty<GeneralView>())
            : (_data?.civil ?? Array.Empty<GeneralView>());

        int CurrentMax => _type == 2 ? (_data?.militaryMax ?? 0) : (_data?.civilMax ?? 0);

        void Select(int index)
        {
            var list = CurrentList;
            if (index < 0 || index >= list.Length || index == _selectedIndex) return;
            _selectedIndex = index;
            Render();
        }

        void Render()
        {
            var old = _window.Find("Content");
            if (old != null) Destroy(old.gameObject);
            var content = LegacyUiFactory.PixelPanel(_window, "Content", 0, 0, 662, 385, Color.clear);
            _closeButton.transform.SetAsLastSibling();

            DrawGeneralBackground(content);
            DrawRoster(content);

            var list = CurrentList;
            if (list.Length == 0) return;
            _selectedIndex = Mathf.Clamp(_selectedIndex, 0, list.Length - 1);
            DrawSelectedGeneral(content, list[_selectedIndex]);
        }

        static void DrawGeneralBackground(RectTransform parent)
        {
            // General.bg, reconstructed from the authoritative Tavern SWF symbol timeline.
            LegacyUiFactory.PixelImage(parent, TavernRoot + "00780", 0, 0, 567, 353);
            LegacyUiFactory.PixelImage(parent, TavernRoot + "00782", 112, 335, 360, 2);
            LegacyUiFactory.PixelImage(parent, TavernRoot + "00785", 97, 60.45f, 28, 58);

            // General.downBg.
            LegacyUiFactory.PixelImage(parent, TavernRoot + "00914", 150.85f, 339.35f, 323, 44);
            LegacyUiFactory.PixelImage(parent, TavernRoot + "00916", 156.35f, 342, 16, 16);
            LegacyUiFactory.PixelImage(parent, TavernRoot + "00919", 263.55f, 342, 16, 16);
            LegacyUiFactory.PixelImage(parent, TavernRoot + "00922", 371.25f, 342, 16, 16);
        }

        void DrawRoster(RectTransform parent)
        {
            var list = CurrentList;
            var max = Mathf.Clamp(CurrentMax, 0, 5);
            for (var i = 0; i < 5; i++)
            {
                var y = 52 + i * 62;
                if (i >= max)
                {
                    LegacyUiFactory.PixelImage(parent, TavernRoot + "00789", 13, y, 82, 70);
                    continue;
                }

                var general = i < list.Length ? list[i] : null;
                LegacyUiFactory.PixelImage(parent, TavernRoot + (general != null && i == _selectedIndex ? "00794" : "00792"), 13, y, 82, 70);
                if (general == null) continue;

                LegacyUiFactory.PixelImage(parent, GeneralPicRoot + general.pic, 29, y + 10, 50, 50, true);
                LegacyUiFactory.PixelImage(parent, TavernRoot + "00675", 29, y + 45, 50, 15);
                var level = LegacyUiFactory.PixelLabel(parent, "Lv." + general.level, 11, TextAnchor.MiddleCenter,
                    new Color(1f, .88f, .55f), 29, y + 43, 50, 18);
                AddOutline(level, new Color(.19f, .13f, .06f));
                TransparentButton(parent, 13, y, 82, 59, () => Select(i));
            }
        }

        void DrawSelectedGeneral(RectTransform parent, GeneralView general)
        {
            var verticalName = LegacyUiFactory.PixelLabel(parent, VerticalName(general.name), 14, TextAnchor.UpperCenter,
                HtmlColor(0xCCB986), 97, 64, 30, 72);
            verticalName.fontStyle = FontStyle.Bold;
            verticalName.horizontalOverflow = HorizontalWrapMode.Wrap;
            AddOutline(verticalName, HtmlColor(0x302010));

            DrawMaxPortrait(parent, general.pic);

            var name = LegacyUiFactory.PixelLabel(parent, general.name, 15, TextAnchor.MiddleLeft,
                HtmlColor(0xFFFFCC), 166, 58, 180, 22);
            name.fontStyle = FontStyle.Bold;
            AddOutline(name, HtmlColor(0x302010));

            var intel = LegacyUiFactory.PixelLabel(parent, general.intel.ToString(), 13, TextAnchor.MiddleRight,
                Color.white, 336, 58, 60, 20);
            intel.fontStyle = FontStyle.Bold;
            AddOutline(intel, HtmlColor(0x302010));
            var politics = LegacyUiFactory.PixelLabel(parent, general.politics.ToString(), 13, TextAnchor.MiddleRight,
                Color.white, 411, 58, 60, 20);
            politics.fontStyle = FontStyle.Bold;
            AddOutline(politics, HtmlColor(0x302010));

            var expLabel = LegacyUiFactory.PixelLabel(parent, "EXP", 13, TextAnchor.MiddleLeft,
                HtmlColor(0xCCB986), 127, 91, 60, 17);
            AddOutline(expLabel, HtmlColor(0x2F271A));
            LegacyUiFactory.PixelImage(parent, TavernRoot + "00885", 169, 98, 92, 14);
            var exp = LegacyUiFactory.PixelLabel(parent, general.exp.ToString(), 12, TextAnchor.MiddleCenter,
                HtmlColor(0xFFFFCC), 169, 93, 130, 20);
            AddOutline(exp, HtmlColor(0x302010));

            var tacticLabel = LegacyUiFactory.PixelLabel(parent, "Chiến pháp", 13, TextAnchor.MiddleLeft,
                HtmlColor(0xCCB986), 320, 91, 50, 20);
            AddOutline(tacticLabel, HtmlColor(0x2F271A));
            var tactic = LegacyUiFactory.PixelLabel(parent, general.tacticId > 0 ? "#" + general.tacticId : "-", 13,
                TextAnchor.MiddleLeft, HtmlColor(0xFFFFCC), 356, 91, 80, 20);
            AddOutline(tactic, HtmlColor(0x302010));

            var strengthLabel = LegacyUiFactory.PixelLabel(parent, "Võ lực", 13, TextAnchor.MiddleLeft,
                HtmlColor(0xCCB986), 106, 131, 60, 17);
            AddOutline(strengthLabel, HtmlColor(0x2F271A));
            var strength = LegacyUiFactory.PixelLabel(parent, general.strength.ToString(), 13, TextAnchor.MiddleLeft,
                HtmlColor(0xFFFFCC), 191, 131, 60, 20);
            AddOutline(strength, HtmlColor(0x302010));

            DrawEquipmentSlots(parent);
            DrawRecruitPanel(parent, general);
        }

        static void DrawEquipmentSlots(RectTransform parent)
        {
            // General.equip is a 2x3 grid with 57px spacing in the legacy SWF.
            for (var row = 0; row < 3; row++)
                for (var col = 0; col < 2; col++)
                    LegacyUiFactory.PixelImage(parent, TavernRoot + "00832", 366 + col * 57, 59 + row * 57, 54, 54);
        }

        static void DrawRecruitPanel(RectTransform parent, GeneralView general)
        {
            LegacyUiFactory.PixelImage(parent, TavernRoot + "00903", 478, 53, 163, 336);

            var troop = LegacyUiFactory.PixelLabel(parent, general.troopId > 0 ? "Binh chủng #" + general.troopId : "-", 12,
                TextAnchor.MiddleLeft, HtmlColor(0xCCB9B6), 526, 116, 110, 20);
            AddOutline(troop, HtmlColor(0x302010));
            var forces = LegacyUiFactory.PixelLabel(parent, "Quân: " + general.forces, 12,
                TextAnchor.MiddleLeft, HtmlColor(0xFFFFCF), 526, 213, 110, 20);
            AddOutline(forces, HtmlColor(0x302010));
        }

        static void DrawMaxPortrait(RectTransform parent, string pic)
        {
            var image = LegacyUiFactory.PixelImage(parent, GeneralPicMaxRoot + pic, 125, 77, 240, 255);
            if (image.sprite != null) return;

            // The editor importer copies the authoritative 240x255 PNG pack from D:\\Sever.
            // Keep the existing legacy thumbnail as a non-blocking fallback only when that source is unavailable.
            image.sprite = Resources.Load<Sprite>(GeneralPicRoot + pic);
            image.preserveAspect = true;
        }

        static string VerticalName(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return "";
            return string.Join("\n", value.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries));
        }

        static void TransparentButton(RectTransform parent, float x, float y, float width, float height, UnityEngine.Events.UnityAction onClick)
        {
            var button = LegacyUiFactory.PixelButton(parent, "", x, y, width, height, onClick);
            button.image.color = Color.clear;
        }

        static Color HtmlColor(int rgb) => new Color(
            ((rgb >> 16) & 0xFF) / 255f,
            ((rgb >> 8) & 0xFF) / 255f,
            (rgb & 0xFF) / 255f,
            1f);

        static void AddOutline(Text text, Color color)
        {
            var outline = text.gameObject.AddComponent<Outline>();
            outline.effectColor = color;
            outline.effectDistance = new Vector2(1, -1);
        }
    }
}
