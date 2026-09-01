using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using CTXD.Client.Features.Battle;
using CTXD.Client.Features.FirstPlayable;
using CTXD.Client.Networking;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace CTXD.Client.Features.World
{
    public sealed class WorldPanel : MonoBehaviour
    {
        const float StageWidth = 1000f;
        const float StageHeight = 600f;
        const float MapWidth = 6000f;
        const float MapHeight = 3600f;
        const float CanvasScale = 1.28f;
        const float MiniMapScale = 40f;
        const string VisualRoot = "LegacyVisual/World/";

        static readonly HashSet<int> LegacyInfoCities = new HashSet<int> { 19, 123, 207, 250, 251, 252 };
        static WorldPanel _open;

        ApiClient _api;
        Action<string> _status;
        RectTransform _host;
        RectTransform _root;
        RectTransform _viewport;
        RectTransform _map;
        RectTransform _cityLayer;
        RectTransform _generalLayer;
        RectTransform _moveLayer;
        RectTransform _fogLayer;
        RectTransform _menuLayer;
        RectTransform _cityMenu;
        RectTransform _cityInfo;
        WorldResponse _world;
        GeneralRosterResponse _generals;
        int _generalId;
        int _cityId;
        bool _busy;
        bool _mapPositionInitialized;
        Vector2 _mapOffset;
        Vector2 _pointerStart;
        Vector2 _dragStartOffset;
        bool _suppressClick;

        public static WorldPanel Open(RectTransform host, ApiClient api, Action<string> status)
        {
            var go = new GameObject("WorldPanel");
            go.transform.SetParent(host, false);
            var panel = go.AddComponent<WorldPanel>();
            panel._host = host;
            panel._api = api;
            panel._status = status;
            _open = panel;
            panel.Build();
            _ = panel.LoadAsync();
            return panel;
        }

        public static void RefreshOpenFromPush()
        {
            if (_open != null && !_open._busy) _ = _open.RefreshAsync();
        }

        void OnDestroy()
        {
            if (_open == this) _open = null;
        }

        void Update()
        {
            if (!Input.GetKeyDown(KeyCode.Escape)) return;
            if (_cityInfo != null)
            {
                Destroy(_cityInfo.gameObject);
                _cityInfo = null;
                _cityId = 0;
                RefreshCityLights();
                return;
            }
            if (_cityMenu != null)
            {
                Destroy(_cityMenu.gameObject);
                _cityMenu = null;
                _cityId = 0;
                RefreshCityLights();
                return;
            }
            Destroy(gameObject);
        }

        void Build()
        {
            var blocker = LegacyUiFactory.Panel(transform, "WorldLegacyScene", Vector2.zero, Vector2.one, Color.black);
            _root = LegacyUiFactory.PixelPanel(blocker, "LegacyStage1000x600", 0, 0, StageWidth, StageHeight, Color.black);
            _root.localScale = new Vector3(CanvasScale, CanvasScale, 1f);
        }

        async Task LoadAsync()
        {
            try
            {
                _world = await _api.GetWorldAsync();
                _generals = await _api.GetGeneralsAsync();
                var military = _generals?.military ?? Array.Empty<GeneralView>();
                if (military.Length > 0)
                    _generalId = _world.focusGeneralId != 0 ? _world.focusGeneralId : military[0].id;
                Draw();
            }
            catch (Exception ex) { _status(ex.Message); }
        }

        async Task RefreshAsync()
        {
            try
            {
                _world = await _api.GetWorldAsync();
                _generals = await _api.GetGeneralsAsync();
                Draw();
            }
            catch (Exception ex) { _status(ex.Message); }
        }

        void Draw()
        {
            if (_root == null || _world == null) return;
            LegacyUiFactory.DestroyChildren(_root);
            _cityMenu = null;
            _cityInfo = null;

            _viewport = LegacyUiFactory.PixelPanel(_root, "WorldViewport", 0, 0, StageWidth, StageHeight, Color.black);
            _viewport.gameObject.AddComponent<RectMask2D>();
            _map = LegacyUiFactory.PixelPanel(_viewport, "WorldMap6000x3600", 0, 0, MapWidth, MapHeight, new Color(1, 1, 1, .001f));
            _map.gameObject.AddComponent<LegacyWorldDragSurface>().Initialize(BeginDrag, DragMap, EndDrag);

            if (!_mapPositionInitialized)
            {
                _mapOffset = InitialMapOffset();
                _mapPositionInitialized = true;
            }
            ApplyMapOffset();

            DrawTiles();
            _cityLayer = Layer("Cities");
            _generalLayer = Layer("Generals");
            _moveLayer = Layer("Movement");
            _fogLayer = Layer("Fog");
            _menuLayer = Layer("CityMenu");

            DrawCities();
            DrawGeneralsAndMoves();
            DrawLegacyFog();
            DrawSmallMap();
        }

        RectTransform Layer(string name)
        {
            return LegacyUiFactory.PixelPanel(_map, name, 0, 0, MapWidth, MapHeight, Color.clear);
        }

        Vector2 InitialMapOffset()
        {
            WorldCityView city = null;
            var focus = (_generals?.military ?? Array.Empty<GeneralView>()).FirstOrDefault(x => x.id == _generalId);
            if (focus != null && focus.locationId > 0) city = Cities.FirstOrDefault(x => x.id == focus.locationId);
            if (city == null) city = Cities.FirstOrDefault(x => x.id == _world.capitalCityId);
            if (city == null) city = Cities.FirstOrDefault(x => !x.fogged) ?? Cities.FirstOrDefault();
            if (city == null) return Vector2.zero;
            var center = CityCenter(city);
            return ClampMapOffset(new Vector2(StageWidth * .5f - center.x, StageHeight * .5f - center.y));
        }

        WorldCityView[] Cities => _world?.cities ?? Array.Empty<WorldCityView>();
        WorldRoadView[] Roads => _world?.roads ?? Array.Empty<WorldRoadView>();
        WorldMoveView[] Moves => _world?.moves ?? Array.Empty<WorldMoveView>();

        void DrawTiles()
        {
            for (var column = 1; column <= 6; column++)
            for (var row = 1; row <= 6; row++)
            {
                var image = LegacyUiFactory.PixelImage(_map, $"{VisualRoot}Tiles/block{column}_{row}",
                    (column - 1) * 1000f, (row - 1) * 600f, 1000f, 600f);
                image.raycastTarget = false;
            }
        }

        void DrawCities()
        {
            foreach (var city in Cities.Where(x => !x.fogged)) DrawCity(city);
        }

        void DrawCity(WorldCityView city)
        {
            var model = ModelNumber(city.model);
            var sprite = Resources.Load<Sprite>($"{VisualRoot}City/model{model}") ?? Resources.Load<Sprite>($"{VisualRoot}City/model301");
            if (sprite == null) return;

            var body = LegacyUiFactory.PixelImage(_cityLayer, $"{VisualRoot}City/model{model}", city.x, city.y, sprite.rect.width, sprite.rect.height);
            if (body.sprite == null) body.sprite = sprite;
            body.raycastTarget = false;

            var geometry = CityGeometry.For(model);
            var statusOrigin = new Vector2(city.x + geometry.Status.x, city.y + geometry.Status.y);
            var light = AtlasImage(_cityLayer, "UI/city_light", statusOrigin.x, statusOrigin.y, 132, 30);
            light.enabled = city.id == _cityId;
            light.raycastTarget = false;

            var bannerPath = ForceBanner(city.ownerForceId);
            if (!string.IsNullOrEmpty(bannerPath))
            {
                var banner = AtlasImage(_cityLayer, bannerPath, statusOrigin.x, statusOrigin.y, 19, 26);
                banner.raycastTarget = false;
            }

            var statusBg = AtlasImage(_cityLayer, "UI/city_status", statusOrigin.x + 18.95f, statusOrigin.y + 3f, 98, 21);
            statusBg.raycastTarget = false;
            var name = LegacyUiFactory.PixelLabel(_cityLayer, city.name ?? string.Empty, 12, TextAnchor.MiddleCenter,
                new Color(1f, .96f, .78f), statusOrigin.x + 20.6f, statusOrigin.y + 8f, 94, 16);
            AddOutline(name, new Color(.18f, .11f, .04f));
            name.raycastTarget = false;

            if (city.attackable) DrawAttackable(city, geometry);

            var hitWidth = Mathf.Max(70f, sprite.rect.width * .72f);
            var hitHeight = Mathf.Max(55f, sprite.rect.height * .58f);
            var hit = LegacyUiFactory.PixelPanel(_cityLayer, "CityHit_" + city.id,
                city.x + geometry.Mouse.x - hitWidth * .5f, city.y + geometry.Mouse.y - hitHeight * .5f,
                hitWidth, hitHeight, new Color(1, 1, 1, .001f));
            hit.gameObject.AddComponent<LegacyWorldCityHit>().Initialize(
                city.id, BeginDrag, DragMap, EndDrag,
                () =>
                {
                    if (_suppressClick) { _suppressClick = false; return; }
                    SelectCity(city.id);
                },
                inside => light.enabled = inside || city.id == _cityId);
        }

        void DrawAttackable(WorldCityView city, CityGeometry geometry)
        {
            var origin = new Vector2(city.x + geometry.Mouse.x, city.y + geometry.Mouse.y);
            var image = RuntimeAtlasImage(_cityLayer, "AttackBtn1_" + city.id, origin.x - 75f, origin.y - 75f, 150, 150);
            image.raycastTarget = false;
            image.gameObject.AddComponent<LegacyWorldAtlasAnimator>()
                .Initialize(image, "UI/attack_btn1", 11, 1, 150, 150, 0, 24f, true);
        }

        void SelectCity(int cityId)
        {
            _cityId = cityId;
            var city = Cities.FirstOrDefault(x => x.id == cityId && !x.fogged);
            if (city == null) return;
            if (_cityMenu != null) Destroy(_cityMenu.gameObject);
            if (_cityInfo != null) Destroy(_cityInfo.gameObject);
            _cityMenu = null;
            _cityInfo = null;
            RefreshCityLights();
            if (LegacyInfoCities.Contains(cityId) && city.state == 0) ShowCityInfo(city);
            else ShowCityMenu(city);
        }

        void RefreshCityLights()
        {
            if (_cityLayer == null) return;
            foreach (var hit in _cityLayer.GetComponentsInChildren<LegacyWorldCityHit>(true))
                hit.SetSelected(hit.CityId == _cityId);
        }

        void ShowCityMenu(WorldCityView city)
        {
            var center = CityCenter(city);
            _cityMenu = LegacyUiFactory.PixelPanel(_menuLayer, "LegacyCityMenu", center.x, center.y, 1, 1, Color.clear);
            var bg = LegacyUiFactory.PixelImage(_cityMenu, VisualRoot + "UI/city_menu_bg", -76.5f, -76.5f, 153, 153);
            bg.raycastTarget = false;

            var military = (_generals?.military ?? Array.Empty<GeneralView>())
                .Where(x => x.id > 0 && x.locationId > 0 && !Moves.Any(m => m.generalId == x.id))
                .Take(8).ToArray();
            for (var i = 0; i < military.Length; i++)
            {
                var angle = (202.5f + i * 45f) * Mathf.Deg2Rad;
                DrawMenuGeneral(_cityMenu, military[i], city.id, Mathf.Cos(angle) * 85f, Mathf.Sin(angle) * 85f);
            }

            var battle = (_world.battles ?? Array.Empty<WorldBattleHandoffView>()).FirstOrDefault(x => x.cityId == city.id && x.status == 0);
            var war = LegacyUiFactory.PixelPanel(_cityMenu, "WarInfoPanel", -20, -30, 1, 1, Color.clear);
            if (battle != null)
            {
                var attacker = ForceIcon(battle.attackerForceId);
                if (!string.IsNullOrEmpty(attacker))
                {
                    var icon = AtlasImage(war, attacker, -20, -15, 27, 27);
                    icon.raycastTarget = false;
                }
                var defender = ForceIcon(battle.defenderForceId);
                if (!string.IsNullOrEmpty(defender))
                {
                    var icon = AtlasImage(war, defender, 35, -15, 27, 27);
                    icon.raycastTarget = false;
                }
                var vs = AtlasImage(war, "UI/vs", 0, 10, 41, 43);
                vs.raycastTarget = false;
            }

            var view = AtlasButton(war, "", -105, 75, 108, 37, async () => await DetailAsync(city.id),
                "UI/view_battle_up", "UI/view_battle_over", "UI/view_battle_down");
            view.spriteState = DisabledSprite(view.spriteState, "UI/view_battle_disabled");
            view.interactable = battle != null;

            var assemble = AtlasButton(war, "", 40, 75, 108, 37, () => { },
                "UI/assemble_up", "UI/assemble_over", "UI/assemble_down");
            assemble.spriteState = DisabledSprite(assemble.spriteState, "UI/assemble_disabled");
            assemble.interactable = false;
        }

        void DrawMenuGeneral(RectTransform parent, GeneralView general, int cityId, float x, float y)
        {
            var slot = LegacyUiFactory.PixelPanel(parent, "General_" + general.id, x, y, 1, 1, Color.clear);
            var headBg = AtlasImage(slot, "UI/general_head_bg", -25, -25, 50, 50);
            headBg.raycastTarget = false;
            var maskRoot = LegacyUiFactory.PixelPanel(slot, "GeneralPicMask", -20, -20, 41, 41, Color.white);
            var maskImage = maskRoot.GetComponent<Image>();
            maskImage.sprite = LegacyWorldAtlas.Sprite("UI/general_head_mask");
            maskImage.raycastTarget = false;
            maskRoot.gameObject.AddComponent<Mask>().showMaskGraphic = false;
            var portrait = LegacyUiFactory.PixelImage(maskRoot, "LegacyVisual/GeneralPic/" + general.pic, -6, -3, 48, 48, true);
            portrait.raycastTarget = false;
            var hit = LegacyUiFactory.PixelButton(slot, "", -26, -25, 52, 52, async () => await MoveGeneralFromMenu(general.id, cityId));
            hit.image.color = new Color(1, 1, 1, .001f);
        }

        async Task MoveGeneralFromMenu(int generalId, int cityId)
        {
            if (_busy) return;
            _generalId = generalId;
            await MoveAsync(cityId, true);
        }

        void ShowCityInfo(WorldCityView city)
        {
            _cityInfo = LegacyUiFactory.PixelPanel(_root, "CityInfoPanel", 254, 181, 492, 238, Color.clear);
            var bg = AtlasImage(_cityInfo, "UI/city_info_bg", 0, 0, 492, 238);
            bg.raycastTarget = false;
            var nation = ForceIcon(city.ownerForceId);
            if (!string.IsNullOrEmpty(nation))
            {
                var flag = AtlasImage(_cityInfo, nation, 58, 4, 27, 27);
                flag.raycastTarget = false;
            }
            var name = LegacyUiFactory.PixelLabel(_cityInfo, city.name ?? string.Empty, 14, TextAnchor.MiddleLeft,
                new Color(1f, .84f, 0f), 98, 8, 100, 20);
            AddOutline(name, new Color(.19f, .125f, .063f));
            if (city.terrainEffectType >= 1 && city.terrainEffectType <= 5)
            {
                var terrain = AtlasImage(_cityInfo, "UI/terrain_" + city.terrainEffectType, 347, 8, 16, 16);
                terrain.raycastTarget = false;
            }
            var close = LegacyUiFactory.PixelButton(_cityInfo, "", 430, -20, 21, 23, CloseCityInfo,
                "LegacyVisual/Component/CloseButton3/up", "LegacyVisual/Component/CloseButton3/over", "LegacyVisual/Component/CloseButton3/down");
            close.transform.SetAsLastSibling();
        }

        void CloseCityInfo()
        {
            if (_cityInfo != null) Destroy(_cityInfo.gameObject);
            _cityInfo = null;
            _cityId = 0;
            RefreshCityLights();
        }

        void DrawGeneralsAndMoves()
        {
            foreach (var move in Moves) DrawMovePath(move);
            foreach (var general in _generals?.military ?? Array.Empty<GeneralView>())
            {
                var move = Moves.FirstOrDefault(x => x.generalId == general.id);
                if (move != null) { DrawMovingGeneral(general, move); continue; }
                if (general.locationId <= 0) continue;
                var city = Cities.FirstOrDefault(x => x.id == general.locationId && !x.fogged);
                if (city != null) DrawGeneralMarker(_generalLayer, general, CityCenter(city), 0, false);
            }
        }

        void DrawMovePath(WorldMoveView move)
        {
            var points = ResolveMovePoints(move);
            if (points.Count < 2) return;
            var go = new GameObject("LegacyRoad_" + move.generalId, typeof(RectTransform), typeof(LegacyWorldPathGraphic));
            go.transform.SetParent(_moveLayer, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0, 1);
            rt.pivot = new Vector2(0, 1);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = new Vector2(MapWidth, MapHeight);
            var graphic = go.GetComponent<LegacyWorldPathGraphic>();
            graphic.raycastTarget = false;
            graphic.SetPoints(points, 3f, Color.red);
        }

        List<Vector2> ResolveMovePoints(WorldMoveView move)
        {
            var ids = move.pathCityIds ?? Array.Empty<int>();
            var points = new List<Vector2>();
            if (ids.Length >= 2)
            {
                var startIndex = Mathf.Clamp(move.pathIndex, 0, ids.Length - 2);
                for (var i = startIndex; i < ids.Length - 1; i++) AppendRoad(points, ids[i], ids[i + 1]);
            }
            else AppendRoad(points, move.fromCityId, move.toCityId);
            return points;
        }

        void AppendRoad(List<Vector2> output, int fromCityId, int toCityId)
        {
            var from = Cities.FirstOrDefault(x => x.id == fromCityId);
            var to = Cities.FirstOrDefault(x => x.id == toCityId);
            if (from == null || to == null) return;

            var fromCenter = CityCenter(from);
            var toCenter = CityCenter(to);
            var road = Roads.FirstOrDefault(x =>
                (x.start == fromCityId && x.end == toCityId) || (x.start == toCityId && x.end == fromCityId));
            var anchors = road == null ? new List<Vector2>() : ParseRoadAnchors(road.trace);
            if (road != null && road.start != fromCityId) anchors.Reverse();

            if (output.Count == 0) output.Add(fromCenter);
            else if (Vector2.Distance(output[output.Count - 1], fromCenter) >= 2f) output.Add(fromCenter);
            output.AddRange(anchors);
            output.Add(toCenter);
        }

        static List<Vector2> ParseRoadAnchors(string trace)
        {
            var result = new List<Vector2>();
            if (string.IsNullOrWhiteSpace(trace)) return result;
            var items = trace.Split(';');
            // Legacy BlocksContainer.initUI starts at index 2: XML points[0..1] are city endpoints,
            // while only points[2..] become roadList anchors. Endpoints come from each city center.
            for (var i = 2; i < items.Length; i++)
            {
                var parts = items[i].Split('|');
                if (parts.Length != 2) continue;
                if (!float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var x) ||
                    !float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var y)) continue;
                result.Add(new Vector2(x, y));
            }
            return result;
        }

        void DrawMovingGeneral(GeneralView general, WorldMoveView move)
        {
            var points = ResolveMovePoints(move);
            Vector2 position;
            Vector2 target;
            if (points.Count >= 2) PositionAlongPolyline(points, MoveProgress(move), out position, out target);
            else
            {
                var from = Cities.FirstOrDefault(x => x.id == move.fromCityId);
                var to = Cities.FirstOrDefault(x => x.id == move.toCityId);
                position = from == null ? Vector2.zero : CityCenter(from);
                target = to == null ? position : CityCenter(to);
            }
            DrawGeneralMarker(_moveLayer, general, position, LegacyDirection(position, target), true);
        }

        static float MoveProgress(WorldMoveView move)
        {
            if (!DateTimeOffset.TryParse(move.startedAt, out var start) || !DateTimeOffset.TryParse(move.arrivesAt, out var end)) return 0f;
            var total = (end - start).TotalSeconds;
            if (total <= 0) return 1f;
            return Mathf.Clamp01((float)((DateTimeOffset.UtcNow - start).TotalSeconds / total));
        }

        static void PositionAlongPolyline(IReadOnlyList<Vector2> points, float progress, out Vector2 position, out Vector2 target)
        {
            var total = 0f;
            for (var i = 1; i < points.Count; i++) total += Vector2.Distance(points[i - 1], points[i]);
            if (total <= .001f) { position = target = points[0]; return; }
            var wanted = total * Mathf.Clamp01(progress);
            var passed = 0f;
            for (var i = 1; i < points.Count; i++)
            {
                var length = Vector2.Distance(points[i - 1], points[i]);
                if (passed + length >= wanted)
                {
                    var t = length <= .001f ? 0f : (wanted - passed) / length;
                    position = Vector2.Lerp(points[i - 1], points[i], t);
                    target = points[i];
                    return;
                }
                passed += length;
            }
            position = points[points.Count - 1];
            target = position;
        }

        void DrawGeneralMarker(RectTransform layer, GeneralView general, Vector2 position, int direction, bool moving)
        {
            var marker = LegacyUiFactory.PixelPanel(layer, "WorldGeneral_" + general.id, position.x, position.y, 1, 1, Color.clear);
            var troop = SupportedTroop(general.troopId) ? general.troopId : 501;
            var actor = RuntimeAtlasImage(marker, "Troop_" + troop, -48, -60, 96, 72);
            actor.raycastTarget = false;
            actor.gameObject.AddComponent<LegacyWorldAtlasAnimator>()
                .Initialize(actor, "General/move_" + troop, 6, 5, 96, 72, direction, 12f, moving);
            if (general.id == _generalId) DrawFocusGeneral(marker);
            var bg = AtlasImage(marker, "General/general_bg", -40.65f, -49.7f, 76, 23);
            bg.raycastTarget = false;
            var label = LegacyUiFactory.PixelLabel(marker, general.name ?? string.Empty, 11, TextAnchor.MiddleCenter,
                new Color(1f, 1f, .8f), -38, -46, 70, 17);
            AddOutline(label, Color.black);
            label.raycastTarget = false;
        }

        void DrawFocusGeneral(RectTransform marker)
        {
            var leftA = AtlasImage(marker, "General/focus_star", -44f, -45f, 12, 12);
            var leftB = AtlasImage(marker, "General/focus_star", -35.35f, -45f, 12, 12);
            var rightA = AtlasImage(marker, "General/focus_star", 26.3f, -45f, 12, 12);
            var rightB = AtlasImage(marker, "General/focus_star", 17.65f, -45f, 12, 12);
            leftA.raycastTarget = leftB.raycastTarget = rightA.raycastTarget = rightB.raycastTarget = false;
            marker.gameObject.AddComponent<LegacyWorldFocusAnimator>().Initialize(leftA.rectTransform, leftB.rectTransform, rightA.rectTransform, rightB.rectTransform);
        }

        static Image AtlasImage(Transform parent, string key, float x, float y, float width, float height)
        {
            var image = RuntimeAtlasImage(parent, key, x, y, width, height);
            image.sprite = LegacyWorldAtlas.Sprite(key);
            return image;
        }

        static Button AtlasButton(Transform parent, string text, float x, float y, float width, float height,
            UnityEngine.Events.UnityAction onClick, string normalKey, string highlightedKey, string pressedKey)
        {
            var button = LegacyUiFactory.PixelButton(parent, text, x, y, width, height, onClick);
            var normal = LegacyWorldAtlas.Sprite(normalKey);
            button.image.sprite = normal;
            button.image.type = Image.Type.Simple;
            button.image.color = Color.white;
            var state = button.spriteState;
            state.highlightedSprite = LegacyWorldAtlas.Sprite(highlightedKey) ?? normal;
            state.pressedSprite = LegacyWorldAtlas.Sprite(pressedKey) ?? state.highlightedSprite;
            state.selectedSprite = state.highlightedSprite;
            button.spriteState = state;
            return button;
        }

        static Image RuntimeAtlasImage(Transform parent, string name, float x, float y, float width, float height)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0, 1);
            rt.pivot = new Vector2(0, 1);
            rt.anchoredPosition = new Vector2(x, -y);
            rt.sizeDelta = new Vector2(width, height);
            return go.GetComponent<Image>();
        }

        static bool SupportedTroop(int troopId) => troopId == 501 || troopId == 601 || troopId == 701 || troopId == 801;

        static int LegacyDirection(Vector2 start, Vector2 target)
        {
            var dy = start.y - target.y;
            var dx = start.x - target.x;
            if (dy > 0) return dx > 0 ? 4 : 3;
            return dx > 0 ? 1 : 2;
        }

        void DrawLegacyFog()
        {
            var sprite = Resources.Load<Sprite>(VisualRoot + "fog");
            if (sprite == null) return;
            foreach (var city in Cities.Where(x => x.fogged))
            {
                var center = CityCenter(city);
                var fog = LegacyUiFactory.PixelImage(_fogLayer, VisualRoot + "fog", center.x - sprite.rect.width * .5f,
                    center.y - sprite.rect.height * .5f, sprite.rect.width, sprite.rect.height);
                fog.raycastTarget = false;
            }
        }

        void DrawSmallMap()
        {
            var holder = LegacyUiFactory.PixelPanel(_root, "SmallMap", 834, 54, 164, 125, Color.clear);
            var frame = AtlasImage(holder, "UI/minimap_frame", 0, 0, 164, 125);
            frame.raycastTarget = false;
            const float mapX = 6f;
            const float mapY = 6f;
            foreach (var city in Cities.Where(x => !x.fogged && x.ownerForceId >= 1 && x.ownerForceId <= 3))
            {
                var center = CityCenter(city);
                var dot = AtlasImage(holder, MiniDot(city.ownerForceId), mapX + center.x / MiniMapScale - 2f,
                    mapY + center.y / MiniMapScale - 2f, 4, 4);
                dot.raycastTarget = false;
            }
            var hit = LegacyUiFactory.PixelPanel(holder, "SmallMapHit", mapX, mapY, 150, 90, new Color(1, 1, 1, .001f));
            hit.gameObject.AddComponent<LegacyWorldMiniMapHit>().Initialize(OnMiniMapClick);
        }

        void OnMiniMapClick(Vector2 local)
        {
            var world = new Vector2(local.x * MiniMapScale, local.y * MiniMapScale);
            _mapOffset = ClampMapOffset(new Vector2(StageWidth * .5f - world.x, StageHeight * .5f - world.y));
            ApplyMapOffset();
        }

        void BeginDrag(Vector2 pointer)
        {
            _pointerStart = pointer;
            _dragStartOffset = _mapOffset;
            _suppressClick = false;
        }

        void DragMap(Vector2 pointer)
        {
            var delta = (pointer - _pointerStart) / CanvasScale;
            if (delta.sqrMagnitude > 9f) _suppressClick = true;
            _mapOffset = ClampMapOffset(_dragStartOffset + delta);
            ApplyMapOffset();
        }

        void EndDrag() { }

        void ApplyMapOffset()
        {
            if (_map != null) _map.anchoredPosition = _mapOffset;
        }

        static Vector2 ClampMapOffset(Vector2 value)
        {
            return new Vector2(Mathf.Clamp(value.x, StageWidth - MapWidth, 0f), Mathf.Clamp(value.y, StageHeight - MapHeight, 0f));
        }

        async Task DetailAsync(int cityId)
        {
            if (_busy) return;
            _busy = true;
            try
            {
                var detail = await _api.GetWorldCityAsync(cityId);
                if (detail.inBattle && detail.battle != null) BattlePanel.Open(_host, _api, _status, detail.battle.id);
            }
            catch (Exception ex) { _status(ex.Message); }
            finally { _busy = false; }
        }

        async Task MoveAsync(int cityId, bool auto)
        {
            if (_busy || _generalId == 0) return;
            _busy = true;
            try
            {
                _world = auto ? await _api.AutoMoveWorldGeneralAsync(_generalId, cityId) : await _api.MoveWorldGeneralAsync(_generalId, cityId);
                _generals = await _api.GetGeneralsAsync();
                _cityId = 0;
                Draw();
            }
            catch (Exception ex) { _status(ex.Message); }
            finally { _busy = false; }
        }

        static string ModelNumber(string model)
        {
            if (string.IsNullOrEmpty(model)) return "301";
            var index = model.LastIndexOf("model", StringComparison.OrdinalIgnoreCase);
            var value = index >= 0 ? model.Substring(index + 5) : model;
            return string.IsNullOrEmpty(value) ? "301" : value;
        }

        static Vector2 CityCenter(WorldCityView city)
        {
            var center = CityGeometry.For(ModelNumber(city.model)).Center;
            return new Vector2(city.x + center.x, city.y + center.y);
        }

        static string ForceBanner(int force)
        {
            if (force == 1) return "Flag/banner_wei";
            if (force == 2) return "Flag/banner_shu";
            if (force == 3) return "Flag/banner_wu";
            return null;
        }

        static string ForceIcon(int force)
        {
            if (force == 1) return "Flag/icon_wei";
            if (force == 2) return "Flag/icon_shu";
            if (force == 3) return "Flag/icon_wu";
            return null;
        }

        static string MiniDot(int force)
        {
            if (force == 2) return "UI/minimap_shu";
            if (force == 3) return "UI/minimap_wu";
            return "UI/minimap_wei";
        }

        static SpriteState DisabledSprite(SpriteState state, string path)
        {
            state.disabledSprite = LegacyWorldAtlas.Sprite(path);
            return state;
        }

        static void AddOutline(Text text, Color color)
        {
            var outline = text.gameObject.AddComponent<Outline>();
            outline.effectColor = color;
            outline.effectDistance = new Vector2(1, -1);
        }

        struct CityGeometry
        {
            public readonly Vector2 Status;
            public readonly Vector2 Center;
            public readonly Vector2 Mouse;
            CityGeometry(float sx, float sy, float cx, float cy, float mx, float my)
            {
                Status = new Vector2(sx, sy); Center = new Vector2(cx, cy); Mouse = new Vector2(mx, my);
            }
            public static CityGeometry For(string model)
            {
                switch (model)
                {
                    case "101": return new CityGeometry(27.4f, 93.5f, 90, 64, 89.95f, 57);
                    case "201": return new CityGeometry(22.4f, 91.5f, 90, 64, 90.3f, 52);
                    case "301": return new CityGeometry(25.4f, 93f, 90, 64, 92.3f, 53);
                    case "401": return new CityGeometry(23.4f, 93f, 90, 64, 90.3f, 54);
                    case "501": return new CityGeometry(22.4f, 97.5f, 90, 64, 90.3f, 54);
                    case "502": return new CityGeometry(33.4f, 93.5f, 90, 64, 89.25f, 49);
                    case "503": return new CityGeometry(19.4f, 99f, 90, 64, 92.3f, 50);
                    case "601": return new CityGeometry(45.4f, 127.5f, 113, 73, 110, 69);
                    case "602": return new CityGeometry(21.85f, 110.5f, 90, 64, 90.3f, 60);
                    case "603": return new CityGeometry(41.4f, 127.5f, 113, 75, 111.3f, 76);
                    case "604": return new CityGeometry(23.9f, 109f, 90, 64, 90.3f, 63);
                    case "605": return new CityGeometry(23.9f, 109.5f, 90, 64, 88.3f, 58);
                    default: return new CityGeometry(25.4f, 93f, 90, 64, 92.3f, 53);
                }
            }
        }
    }

    sealed class LegacyWorldDragSurface : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
    {
        Action<Vector2> _down; Action<Vector2> _drag; Action _up;
        public void Initialize(Action<Vector2> down, Action<Vector2> drag, Action up) { _down = down; _drag = drag; _up = up; }
        public void OnPointerDown(PointerEventData eventData) => _down?.Invoke(eventData.position);
        public void OnDrag(PointerEventData eventData) => _drag?.Invoke(eventData.position);
        public void OnPointerUp(PointerEventData eventData) => _up?.Invoke();
    }

    sealed class LegacyWorldCityHit : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler, IPointerClickHandler, IPointerEnterHandler, IPointerExitHandler
    {
        Action<Vector2> _down; Action<Vector2> _drag; Action _up; Action _click; Action<bool> _hover; bool _selected;
        public int CityId { get; private set; }
        public void Initialize(int cityId, Action<Vector2> down, Action<Vector2> drag, Action up, Action click, Action<bool> hover)
        { CityId = cityId; _down = down; _drag = drag; _up = up; _click = click; _hover = hover; }
        public void SetSelected(bool selected) { _selected = selected; _hover?.Invoke(selected); }
        public void OnPointerDown(PointerEventData e) => _down?.Invoke(e.position);
        public void OnDrag(PointerEventData e) => _drag?.Invoke(e.position);
        public void OnPointerUp(PointerEventData e) => _up?.Invoke();
        public void OnPointerClick(PointerEventData e) => _click?.Invoke();
        public void OnPointerEnter(PointerEventData e) => _hover?.Invoke(true);
        public void OnPointerExit(PointerEventData e) => _hover?.Invoke(_selected);
    }

    sealed class LegacyWorldMiniMapHit : MonoBehaviour, IPointerClickHandler
    {
        Action<Vector2> _click; RectTransform _rect;
        public void Initialize(Action<Vector2> click) { _click = click; _rect = (RectTransform)transform; }
        public void OnPointerClick(PointerEventData e)
        {
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(_rect, e.position, e.pressEventCamera, out var local))
                _click?.Invoke(new Vector2(local.x, -local.y));
        }
    }

    sealed class LegacyWorldAtlasAnimator : MonoBehaviour
    {
        Image _image; Sprite[] _frames; int _columns; int _row; int _cellWidth; int _cellHeight; float _fps; bool _animate; int _lastFrame = -1;
        public void Initialize(Image image, string texturePath, int columns, int rows, int cellWidth, int cellHeight, int row, float fps, bool animate)
        {
            _image = image; _columns = Mathf.Max(1, columns); _row = Mathf.Clamp(row, 0, Mathf.Max(0, rows - 1));
            _cellWidth = cellWidth; _cellHeight = cellHeight; _fps = fps; _animate = animate;
            _frames = new Sprite[_columns];
            for (var column = 0; column < _columns; column++)
                _frames[column] = LegacyWorldAtlas.CellSprite(texturePath, column, _row, _cellWidth, _cellHeight);
            if (_frames[0] == null) { image.enabled = false; return; }
            ShowFrame(0);
        }
        void Update()
        {
            if (_frames == null || _frames.Length == 0) return;
            ShowFrame(_animate ? Mathf.FloorToInt(Time.unscaledTime * _fps) % _columns : 0);
        }
        void ShowFrame(int frame)
        {
            if (_lastFrame == frame || _image == null) return;
            _lastFrame = frame; _image.sprite = _frames[frame]; _image.enabled = true;
        }
        void OnDestroy()
        {
            if (_frames == null) return;
            foreach (var sprite in _frames) if (sprite != null) Destroy(sprite);
        }
    }

    sealed class LegacyWorldFocusAnimator : MonoBehaviour
    {
        static readonly float[] First = { 0,0,0,.3f,.65f,.95f,1.25f,1.5f,1.8f,1.55f,1.25f,1f,.75f,.5f,.25f,0,0,0 };
        static readonly float[] Second = { 8.65f,8.7f,8.7f,8.95f,9.2f,9.45f,9.65f,9.9f,10.1f,9.9f,9.7f,9.45f,9.25f,9.1f,8.9f,8.7f,8.7f,8.7f };
        static readonly float[] SecondY = { 0,0,0,0,0,0,0,0,0,0,0,0,.05f,.05f,.05f,.05f,.05f,0 };
        RectTransform _leftA, _leftB, _rightA, _rightB; int _last = -1;
        public void Initialize(RectTransform leftA, RectTransform leftB, RectTransform rightA, RectTransform rightB)
        { _leftA = leftA; _leftB = leftB; _rightA = rightA; _rightB = rightB; Apply(0); }
        void Update() => Apply(Mathf.FloorToInt(Time.unscaledTime * 24f) % 18);
        void Apply(int frame)
        {
            if (_last == frame || _leftA == null) return;
            _last = frame; var a = First[frame]; var b = Second[frame]; var by = SecondY[frame];
            _leftA.anchoredPosition = new Vector2(-44f + a, 45f); _leftB.anchoredPosition = new Vector2(-44f + b, 45f - by);
            _rightA.anchoredPosition = new Vector2(38.3f - a - 12f, 45f); _rightB.anchoredPosition = new Vector2(38.3f - b - 12f, 45f - by);
        }
    }

    sealed class LegacyWorldPathGraphic : MaskableGraphic
    {
        IReadOnlyList<Vector2> _points; float _width;
        public void SetPoints(IReadOnlyList<Vector2> points, float width, Color lineColor) { _points = points; _width = width; color = lineColor; SetVerticesDirty(); }
        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear(); if (_points == null || _points.Count < 2) return;
            for (var i = 1; i < _points.Count; i++) AddSegment(vh, _points[i - 1], _points[i]);
        }
        void AddSegment(VertexHelper vh, Vector2 a, Vector2 b)
        {
            a.y = -a.y; b.y = -b.y; var direction = b - a; if (direction.sqrMagnitude < .001f) return;
            var normal = new Vector2(-direction.y, direction.x).normalized * (_width * .5f); var index = vh.currentVertCount; var c = color;
            vh.AddVert(a - normal, c, Vector2.zero); vh.AddVert(a + normal, c, Vector2.zero);
            vh.AddVert(b + normal, c, Vector2.zero); vh.AddVert(b - normal, c, Vector2.zero);
            vh.AddTriangle(index, index + 1, index + 2); vh.AddTriangle(index, index + 2, index + 3);
        }
    }
    static class LegacyWorldAtlas
    {
        const string ResourcePath = "LegacyVisual/World/w2_atlas";
        static Texture2D _texture;
        static readonly Dictionary<string, RectInt> TopLeftRects = new Dictionary<string, RectInt>
        {
            { "Flag/banner_shu", new RectInt(1101, 876, 19, 26) },
            { "Flag/banner_wei", new RectInt(1122, 876, 19, 26) },
            { "Flag/banner_wu", new RectInt(1143, 876, 19, 26) },
            { "Flag/icon_shu", new RectInt(1014, 876, 27, 27) },
            { "Flag/icon_wei", new RectInt(1043, 876, 27, 27) },
            { "Flag/icon_wu", new RectInt(1072, 876, 27, 27) },
            { "General/focus_star", new RectInt(1432, 876, 12, 12) },
            { "General/general_bg", new RectInt(1164, 876, 76, 23) },
            { "General/move_501", new RectInt(0, 0, 576, 360) },
            { "General/move_601", new RectInt(578, 0, 576, 360) },
            { "General/move_701", new RectInt(1156, 0, 576, 360) },
            { "General/move_801", new RectInt(0, 362, 576, 360) },
            { "UI/assemble_disabled", new RectInt(0, 876, 108, 37) },
            { "UI/assemble_down", new RectInt(110, 876, 108, 37) },
            { "UI/assemble_over", new RectInt(220, 876, 108, 37) },
            { "UI/assemble_up", new RectInt(330, 876, 108, 37) },
            { "UI/attack_btn1", new RectInt(0, 724, 1650, 150) },
            { "UI/city_info_bg", new RectInt(578, 362, 492, 238) },
            { "UI/city_light", new RectInt(880, 876, 132, 30) },
            { "UI/city_menu_bg", new RectInt(1072, 362, 153, 153) },
            { "UI/city_status", new RectInt(1242, 876, 98, 21) },
            { "UI/general_head_bg", new RectInt(1818, 724, 50, 50) },
            { "UI/general_head_mask", new RectInt(1913, 724, 41, 41) },
            { "UI/minimap_frame", new RectInt(1652, 724, 164, 125) },
            { "UI/minimap_shu", new RectInt(1446, 876, 4, 4) },
            { "UI/minimap_wei", new RectInt(1452, 876, 4, 4) },
            { "UI/minimap_wu", new RectInt(1458, 876, 4, 4) },
            { "UI/terrain_1", new RectInt(1342, 876, 16, 16) },
            { "UI/terrain_2", new RectInt(1360, 876, 16, 16) },
            { "UI/terrain_3", new RectInt(1378, 876, 16, 16) },
            { "UI/terrain_4", new RectInt(1396, 876, 16, 16) },
            { "UI/terrain_5", new RectInt(1414, 876, 16, 16) },
            { "UI/view_battle_disabled", new RectInt(440, 876, 108, 37) },
            { "UI/view_battle_down", new RectInt(550, 876, 108, 37) },
            { "UI/view_battle_over", new RectInt(660, 876, 108, 37) },
            { "UI/view_battle_up", new RectInt(770, 876, 108, 37) },
            { "UI/vs", new RectInt(1870, 724, 41, 43) },
        };
        static readonly Dictionary<string, Sprite> Sprites = new Dictionary<string, Sprite>();

        static Texture2D Texture => _texture != null ? _texture : (_texture = Resources.Load<Texture2D>(ResourcePath));

        public static Sprite Sprite(string key)
        {
            if (string.IsNullOrEmpty(key)) return null;
            if (Sprites.TryGetValue(key, out var cached) && cached != null) return cached;
            var texture = Texture;
            if (texture == null || !TopLeftRects.TryGetValue(key, out var top)) return null;
            var rect = new Rect(top.x, texture.height - top.y - top.height, top.width, top.height);
            var sprite = UnityEngine.Sprite.Create(texture, rect, new Vector2(.5f, .5f), 100f);
            sprite.name = key;
            Sprites[key] = sprite;
            return sprite;
        }

        public static Sprite CellSprite(string key, int column, int row, int cellWidth, int cellHeight)
        {
            var texture = Texture;
            if (texture == null || !TopLeftRects.TryGetValue(key, out var top)) return null;
            var x = top.x + column * cellWidth;
            var yTop = top.y + row * cellHeight;
            return UnityEngine.Sprite.Create(texture,
                new Rect(x, texture.height - yTop - cellHeight, cellWidth, cellHeight), new Vector2(.5f, .5f), 100f);
        }
    }

}
