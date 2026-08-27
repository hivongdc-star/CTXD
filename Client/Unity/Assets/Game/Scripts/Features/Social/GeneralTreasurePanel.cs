using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CTXD.Client.Features.FirstPlayable;
using CTXD.Client.Networking;
using UnityEngine;
using UnityEngine.UI;

namespace CTXD.Client.Features.Social
{
    public sealed class GeneralTreasurePanel : MonoBehaviour
    {
        ApiClient _api;
        Action<string> _status;
        RectTransform _window;
        GeneralTreasureListResponse _treasures;
        GeneralRosterResponse _roster;
        GeneralView[] _generals = Array.Empty<GeneralView>();
        int _generalIndex;
        int _page;
        bool _busy;

        public static GeneralTreasurePanel Open(Transform host, ApiClient api, Action<string> status)
        {
            var go = new GameObject("GeneralTreasurePanel");
            go.transform.SetParent(host, false);
            var panel = go.AddComponent<GeneralTreasurePanel>();
            panel._api = api;
            panel._status = status;
            panel.Build();
            _ = panel.RefreshAsync();
            return panel;
        }

        void Build()
        {
            var blocker = LegacyUiFactory.Panel(transform, "GeneralTreasureBlocker", Vector2.zero, Vector2.one, new Color(0, 0, 0, .9f));
            _window = LegacyUiFactory.PixelPanel(blocker, "GeneralTreasureWindow", 255, 80, 770, 600, new Color(.05f, .025f, .012f, 1));
        }

        async Task RefreshAsync()
        {
            if (_busy) return;
            _busy = true;
            try
            {
                _treasures = await _api.GetGeneralTreasuresAsync();
                _roster = await _api.GetGeneralsAsync();
                _generals = MergeGenerals(_roster);
                if (_generalIndex >= _generals.Length) _generalIndex = Math.Max(0, _generals.Length - 1);
                Draw();
            }
            catch (Exception e)
            {
                _status(e.Message);
            }
            finally { _busy = false; }
        }

        static GeneralView[] MergeGenerals(GeneralRosterResponse roster)
        {
            if (roster == null) return Array.Empty<GeneralView>();
            var result = new List<GeneralView>();
            var seen = new HashSet<int>();
            foreach (var general in (roster.military ?? Array.Empty<GeneralView>()).Concat(roster.civil ?? Array.Empty<GeneralView>()))
                if (general != null && seen.Add(general.id)) result.Add(general);
            return result.ToArray();
        }

        void Draw()
        {
            LegacyUiFactory.DestroyChildren(_window);
            LegacyUiFactory.PixelLabel(_window, "武将宝物", 22, TextAnchor.MiddleCenter, new Color(1, .75f, .25f), 245, 12, 280, 34);
            LegacyUiFactory.PixelButton(_window, "Close", 680, 15, 65, 27, () => Destroy(gameObject));
            LegacyUiFactory.PixelLabel(_window, "武将等级需求：35（最终校验以服务器为准）", 14, TextAnchor.MiddleLeft, Color.gray, 28, 55, 420, 26);

            DrawGeneralSelector();

            var items = _treasures?.items ?? Array.Empty<GeneralTreasureView>();
            if (items.Length == 0)
            {
                LegacyUiFactory.PixelLabel(_window, "暂无武将宝物", 17, TextAnchor.MiddleCenter, Color.gray, 160, 145, 450, 35);
                return;
            }

            const int pageSize = 7;
            var pageCount = Math.Max(1, (items.Length + pageSize - 1) / pageSize);
            if (_page >= pageCount) _page = pageCount - 1;
            var y = 112f;
            foreach (var item in items.Skip(_page * pageSize).Take(pageSize))
            {
                DrawTreasureRow(item, y);
                y += 66f;
            }
            if (pageCount > 1)
            {
                var previous = LegacyUiFactory.PixelButton(_window, "<", 570, 565, 40, 24, () => { _page = Math.Max(0, _page - 1); Draw(); });
                previous.interactable = _page > 0;
                LegacyUiFactory.PixelLabel(_window, (_page + 1) + "/" + pageCount, 13, TextAnchor.MiddleCenter, Color.gray, 615, 565, 60, 24);
                var next = LegacyUiFactory.PixelButton(_window, ">", 680, 565, 40, 24, () => { _page = Math.Min(pageCount - 1, _page + 1); Draw(); });
                next.interactable = _page + 1 < pageCount;
            }
        }

        void DrawGeneralSelector()
        {
            if (_generals.Length == 0)
            {
                LegacyUiFactory.PixelLabel(_window, "无可选武将", 15, TextAnchor.MiddleCenter, Color.gray, 465, 52, 250, 32);
                return;
            }
            var general = _generals[_generalIndex];
            LegacyUiFactory.PixelButton(_window, "<", 455, 52, 35, 30, () => { _generalIndex = (_generalIndex - 1 + _generals.Length) % _generals.Length; Draw(); });
            LegacyUiFactory.PixelLabel(_window, general.name + "  Lv" + general.level, 15, TextAnchor.MiddleCenter, Color.white, 493, 52, 190, 30);
            LegacyUiFactory.PixelButton(_window, ">", 685, 52, 35, 30, () => { _generalIndex = (_generalIndex + 1) % _generals.Length; Draw(); });
        }

        void DrawTreasureRow(GeneralTreasureView item, float y)
        {
            LegacyUiFactory.PixelPanel(_window, "Treasure_" + item.id, 24, y, 720, 58, new Color(.09f, .05f, .02f, .96f));
            var iconPath = IconPath(item.treasureId);
            var icon = LegacyUiFactory.PixelImage(_window, iconPath, 31, y + 4, 50, 50, true);
            if (icon.sprite == null && !string.IsNullOrEmpty(iconPath))
            {
                var texture = Resources.Load<Texture2D>(iconPath);
                if (texture != null) icon.sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(.5f, .5f));
            }
            LegacyUiFactory.PixelLabel(_window, item.name + "  Q" + item.quality, 16, TextAnchor.MiddleLeft, QualityColor(item.quality), 91, y + 3, 235, 25);
            LegacyUiFactory.PixelLabel(_window, "LEA +" + item.lea + "   STR +" + item.str, 14, TextAnchor.MiddleLeft, Color.white, 91, y + 29, 235, 24);

            if (item.equipped)
            {
                var owner = FindGeneral(item.ownerGeneralId);
                var ownerText = owner == null ? ("武将 #" + item.ownerGeneralId) : (owner.name + " Lv" + owner.level);
                LegacyUiFactory.PixelLabel(_window, "已装备：" + ownerText, 14, TextAnchor.MiddleLeft, new Color(.75f, .9f, .55f), 335, y + 9, 245, 34);
                LegacyUiFactory.PixelButton(_window, "卸下", 615, y + 12, 105, 34, () => Unequip(item.id));
            }
            else
            {
                var selected = _generals.Length == 0 ? null : _generals[_generalIndex];
                var selectedText = selected == null ? "请选择武将" : (selected.name + " Lv" + selected.level);
                LegacyUiFactory.PixelLabel(_window, selectedText, 14, TextAnchor.MiddleLeft, Color.gray, 335, y + 9, 245, 34);
                var button = LegacyUiFactory.PixelButton(_window, "装备", 615, y + 12, 105, 34,
                    () => { if (selected != null) Equip(item.id, selected.id); else _status("请选择武将"); });
                button.interactable = selected != null;
            }
        }

        GeneralView FindGeneral(int id) => _generals.FirstOrDefault(x => x.id == id);

        async void Equip(long instanceId, int generalId)
        {
            if (_busy) return;
            _busy = true;
            try
            {
                await _api.EquipGeneralTreasureAsync(instanceId, generalId);
                _status("装备成功");
            }
            catch (Exception e) { _status(e.Message); }
            finally { _busy = false; }
            await RefreshAsync();
        }

        async void Unequip(long instanceId)
        {
            if (_busy) return;
            _busy = true;
            try
            {
                await _api.UnequipGeneralTreasureAsync(instanceId);
                _status("卸下成功");
            }
            catch (Exception e) { _status(e.Message); }
            finally { _busy = false; }
            await RefreshAsync();
        }

        static string IconPath(int treasureId)
        {
            switch (treasureId)
            {
                case 4: return "LegacyVisual/GeneralTreasurePic/heshibi";
                case 5: return "LegacyVisual/GeneralTreasurePic/yemingzhu";
                case 6: return "LegacyVisual/GeneralTreasurePic/zishanhu";
                default: return string.Empty;
            }
        }

        static Color QualityColor(int quality)
        {
            switch (quality)
            {
                case 6: return new Color(1f, .55f, .22f);
                case 5: return new Color(.9f, .45f, 1f);
                case 4: return new Color(.45f, .65f, 1f);
                default: return Color.white;
            }
        }
    }
}
