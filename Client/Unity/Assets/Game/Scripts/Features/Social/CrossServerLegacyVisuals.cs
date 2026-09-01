using System;
using System.Collections.Generic;
using System.Globalization;
using System.Xml;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace CTXD.Client.Features.Social
{
    /// <summary>
    /// Local W8 visual helper. It only reconstructs presentation from recovered legacy assets.
    /// No gameplay/API semantics live here.
    /// </summary>
    public static class CrossServerLegacyVisuals
    {
        public const float LegacyFrameRate = 25f;
        static Font _font;
        static readonly Dictionary<string, AtlasData> Atlases = new Dictionary<string, AtlasData>(StringComparer.Ordinal);
        static readonly Dictionary<string, Sprite> WholeSprites = new Dictionary<string, Sprite>(StringComparer.Ordinal);
        static readonly Dictionary<string, LegacyWorldDefinition> Worlds = new Dictionary<string, LegacyWorldDefinition>(StringComparer.Ordinal);

        public sealed class LegacyCity
        {
            public int id;
            public float x;
            public float y;
            public string name;
            public string model;
        }

        public sealed class LegacyRoad
        {
            public int a;
            public int b;
        }

        public sealed class LegacyWorldDefinition
        {
            public int worldId;
            public int width;
            public int height;
            public readonly List<LegacyCity> cities = new List<LegacyCity>();
            public readonly List<LegacyRoad> roads = new List<LegacyRoad>();

            public LegacyCity FindCity(int id)
            {
                for (var i = 0; i < cities.Count; i++) if (cities[i].id == id) return cities[i];
                return null;
            }
        }

        public sealed class MapSurface
        {
            public RectTransform viewport;
            public RectTransform content;
            public LegacyWorldDefinition definition;
            public int worldId;
        }

        sealed class AtlasData
        {
            public Texture2D texture;
            public readonly Dictionary<string, Rect> rects = new Dictionary<string, Rect>(StringComparer.Ordinal);
            public readonly Dictionary<string, Sprite> sprites = new Dictionary<string, Sprite>(StringComparer.Ordinal);
        }

        static Font Font
        {
            get
            {
                if (_font == null) _font = Resources.GetBuiltinResource<Font>("Arial.ttf");
                return _font;
            }
        }

        public static RectTransform Root(Transform parent, string name, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            go.GetComponent<Image>().color = color;
            return rt;
        }

        public static RectTransform Panel(Transform parent, string name, float x, float y, float width, float height, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            SetTopLeft(rt, x, y, width, height);
            var image = go.GetComponent<Image>();
            image.color = color;
            return rt;
        }

        public static Text Label(Transform parent, string value, float x, float y, float width, float height, int size,
            TextAnchor align, Color color, bool bold = false)
        {
            var go = new GameObject("LegacyText", typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            SetTopLeft(rt, x, y, width, height);
            var text = go.GetComponent<Text>();
            text.font = Font;
            text.fontSize = size;
            text.alignment = align;
            text.color = color;
            text.text = value ?? string.Empty;
            text.supportRichText = true;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            if (bold)
            {
                var outline = go.AddComponent<Outline>();
                outline.effectColor = new Color(0.12f, 0.08f, 0.04f, 1f);
                outline.effectDistance = new Vector2(1f, -1f);
            }
            return text;
        }

        public static Image ResourceImage(Transform parent, string path, float x, float y, float width, float height, bool preserveAspect = false)
        {
            var go = new GameObject(System.IO.Path.GetFileName(path), typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            SetTopLeft(rt, x, y, width, height);
            var image = go.GetComponent<Image>();
            image.sprite = LoadWholeSprite(path);
            image.preserveAspect = preserveAspect;
            image.raycastTarget = false;
            return image;
        }

        public static Image ResourceSpriteNative(Transform parent, string path, float centerX, float centerY)
        {
            var sprite = Resources.Load<Sprite>(path);
            if (sprite == null) return null;
            var go = new GameObject(System.IO.Path.GetFileName(path), typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(.5f, .5f);
            rt.anchoredPosition = new Vector2(centerX, -centerY);
            rt.sizeDelta = sprite.rect.size;
            var image = go.GetComponent<Image>();
            image.sprite = sprite;
            image.raycastTarget = false;
            return image;
        }

        public static Image AtlasImage(Transform parent, string group, string key, float x, float y, float width = -1, float height = -1)
        {
            var sprite = AtlasSprite(group, key);
            if (sprite == null) return null;
            if (width <= 0) width = sprite.rect.width;
            if (height <= 0) height = sprite.rect.height;
            var go = new GameObject(key, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            SetTopLeft(rt, x, y, width, height);
            var image = go.GetComponent<Image>();
            image.sprite = sprite;
            image.raycastTarget = false;
            return image;
        }

        public static Button AtlasButton(Transform parent, string group, string normalKey, string overKey, string downKey,
            float x, float y, float width, float height, UnityAction onClick, string label = null)
        {
            var normal = AtlasSprite(group, normalKey);
            var over = string.IsNullOrEmpty(overKey) ? normal : AtlasSprite(group, overKey);
            var down = string.IsNullOrEmpty(downKey) ? over : AtlasSprite(group, downKey);
            var go = new GameObject("LegacyAtlasButton", typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            SetTopLeft(rt, x, y, width, height);
            var image = go.GetComponent<Image>();
            image.sprite = normal;
            image.preserveAspect = true;
            var button = go.GetComponent<Button>();
            button.targetGraphic = image;
            var states = button.spriteState;
            states.highlightedSprite = over;
            states.selectedSprite = over;
            states.pressedSprite = down;
            button.spriteState = states;
            if (onClick != null) button.onClick.AddListener(onClick);
            if (!string.IsNullOrEmpty(label)) Label(go.transform, label, 0, 0, width, height, 15, TextAnchor.MiddleCenter, new Color(1f, .91f, .65f), true);
            return button;
        }

        public static Button SkinButton(Transform parent, string skin, string label, float x, float y, float width, float height, UnityAction onClick)
        {
            var basePath = "LegacyVisual/Component/" + skin + "/";
            var up = Resources.Load<Sprite>(basePath + "up");
            var over = Resources.Load<Sprite>(basePath + "over");
            var down = Resources.Load<Sprite>(basePath + "down");
            var go = new GameObject("LegacySkinButton_" + skin, typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            SetTopLeft(rt, x, y, width, height);
            var image = go.GetComponent<Image>();
            image.sprite = up;
            image.type = Image.Type.Sliced;
            var button = go.GetComponent<Button>();
            button.targetGraphic = image;
            var state = button.spriteState;
            state.highlightedSprite = over != null ? over : up;
            state.selectedSprite = state.highlightedSprite;
            state.pressedSprite = down != null ? down : state.highlightedSprite;
            button.spriteState = state;
            if (onClick != null) button.onClick.AddListener(onClick);
            if (!string.IsNullOrEmpty(label)) Label(go.transform, label, 0, 0, width, height, 15, TextAnchor.MiddleCenter, new Color(1f, .9f, .62f), true);
            return button;
        }

        public static Button CloseButton(Transform parent, float x, float y, UnityAction onClick)
        {
            return SkinButton(parent, "CloseButton3", string.Empty, x, y, 34, 35, onClick);
        }

        public static Button HitArea(Transform parent, float x, float y, float width, float height, UnityAction onClick)
        {
            var go = new GameObject("LegacyHitArea", typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            SetTopLeft(rt, x, y, width, height);
            var image = go.GetComponent<Image>();
            image.color = new Color(1f, 1f, 1f, .001f);
            var button = go.GetComponent<Button>();
            button.targetGraphic = image;
            button.transition = Selectable.Transition.None;
            if (onClick != null) button.onClick.AddListener(onClick);
            return button;
        }

        public static MapSurface BuildWorldMap(Transform parent, int worldId, float x, float y, float width = 1000, float height = 600)
        {
            var def = LoadWorld(worldId);
            if (def == null) return null;
            var viewportGo = new GameObject("KfWorldViewport", typeof(RectTransform), typeof(Image), typeof(RectMask2D));
            viewportGo.transform.SetParent(parent, false);
            var viewport = (RectTransform)viewportGo.transform;
            SetTopLeft(viewport, x, y, width, height);
            viewportGo.GetComponent<Image>().color = Color.black;

            var contentGo = new GameObject("KfWorldNativeMap", typeof(RectTransform));
            contentGo.transform.SetParent(viewport, false);
            var content = (RectTransform)contentGo.transform;
            content.anchorMin = content.anchorMax = new Vector2(0f, 1f);
            content.pivot = new Vector2(0f, 1f);
            content.anchoredPosition = Vector2.zero;
            content.sizeDelta = new Vector2(def.width, def.height);

            LoadTiles(worldId, content);
            return new MapSurface { viewport = viewport, content = content, definition = def, worldId = worldId };
        }

        public static void Focus(MapSurface map, float x, float y)
        {
            if (map == null || map.viewport == null || map.content == null) return;
            var vw = map.viewport.rect.width;
            var vh = map.viewport.rect.height;
            var minX = Mathf.Min(0, vw - map.definition.width);
            var maxY = Mathf.Max(0, map.definition.height - vh);
            var px = Mathf.Clamp(vw * .5f - x, minX, 0f);
            var py = Mathf.Clamp(y - vh * .5f, 0f, maxY);
            map.content.anchoredPosition = new Vector2(px, py);
        }

        public static Image AddCityModel(MapSurface map, LegacyCity city)
        {
            if (map == null || city == null || string.IsNullOrEmpty(city.model)) return null;
            return ResourceSpriteNative(map.content, "LegacyVisual/World/City/" + city.model, city.x, city.y);
        }

        public static Image AddSelectionLight(MapSurface map, LegacyCity city)
        {
            if (map == null || city == null) return null;
            var img = AtlasImage(map.content, "KFWD", "city.light", city.x - 66, city.y + 31, 132, 30);
            if (img != null) img.transform.SetAsFirstSibling();
            return img;
        }

        public static LegacyWorldDefinition LoadWorld(int worldId)
        {
            var key = worldId.ToString(CultureInfo.InvariantCulture);
            LegacyWorldDefinition cached;
            if (Worlds.TryGetValue(key, out cached)) return cached;
            var asset = Resources.Load<TextAsset>("LegacyVisual/KFWD/world" + worldId + "_city");
            if (asset == null) return null;
            var doc = new XmlDocument();
            doc.LoadXml(asset.text);
            var result = new LegacyWorldDefinition { worldId = worldId };
            var map = doc.SelectSingleNode("/config/map");
            if (map != null)
            {
                var tileW = AttrInt(map, "width");
                var tileH = AttrInt(map, "height");
                result.width = tileW * AttrInt(map, "column");
                result.height = tileH * AttrInt(map, "row");
            }
            var cities = doc.SelectNodes("/config/cities/city");
            if (cities != null)
            {
                foreach (XmlNode node in cities)
                    result.cities.Add(new LegacyCity
                    {
                        id = AttrInt(node, "id"), x = AttrFloat(node, "x"), y = AttrFloat(node, "y"),
                        name = Attr(node, "name"), model = Attr(node, "model")
                    });
            }
            var roads = doc.SelectNodes("/config/roads/road");
            if (roads != null)
            {
                foreach (XmlNode node in roads)
                {
                    var pair = Attr(node, "city").Split('-');
                    int a, b;
                    if (pair.Length == 2 && int.TryParse(pair[0], out a) && int.TryParse(pair[1], out b))
                        result.roads.Add(new LegacyRoad { a = a, b = b });
                }
            }
            Worlds[key] = result;
            return result;
        }

        public static Image AddTimeline(Transform parent, string group, string keyPrefix, int frames, float x, float y, float width, float height, int holdFrames = 1)
        {
            var image = AtlasImage(parent, group, keyPrefix + ".0", x, y, width, height);
            if (image == null) return null;
            var player = image.gameObject.AddComponent<AtlasTimelinePlayer>();
            player.Configure(group, keyPrefix, frames, Mathf.Max(1, holdFrames), image);
            return image;
        }

        public static Sprite AtlasSprite(string group, string key)
        {
            var atlas = Atlas(group);
            if (atlas == null) return null;
            Sprite cached;
            if (atlas.sprites.TryGetValue(key, out cached)) return cached;
            Rect rect;
            if (!atlas.rects.TryGetValue(key, out rect)) return null;
            var sprite = Sprite.Create(atlas.texture, rect, new Vector2(.5f, .5f), 100f, 0, SpriteMeshType.FullRect);
            sprite.name = group + ":" + key;
            atlas.sprites[key] = sprite;
            return sprite;
        }

        static AtlasData Atlas(string group)
        {
            AtlasData cached;
            if (Atlases.TryGetValue(group, out cached)) return cached;
            var texture = Resources.Load<Texture2D>("LegacyVisual/" + group + "/swf_atlas");
            var xml = Resources.Load<TextAsset>("LegacyVisual/" + group + "/swf_atlas");
            if (texture == null || xml == null) return null;
            var data = new AtlasData { texture = texture };
            var doc = new XmlDocument();
            doc.LoadXml(xml.text);
            var nodes = doc.SelectNodes("/atlas/sprite");
            if (nodes != null)
            {
                foreach (XmlNode node in nodes)
                {
                    var key = Attr(node, "key");
                    data.rects[key] = new Rect(AttrFloat(node, "x"), AttrFloat(node, "y"), AttrFloat(node, "width"), AttrFloat(node, "height"));
                }
            }
            Atlases[group] = data;
            return data;
        }

        static void LoadTiles(int worldId, RectTransform content)
        {
            var xml = Resources.Load<TextAsset>("LegacyVisual/KFWD/kfworld_tiles");
            if (xml == null) return;
            var doc = new XmlDocument();
            doc.LoadXml(xml.text);
            var nodes = doc.SelectNodes("/kfworldTiles/world[@id='" + worldId + "']/tile");
            if (nodes == null) return;
            foreach (XmlNode node in nodes)
            {
                ResourceImage(content, Attr(node, "resource"), AttrFloat(node, "x"), AttrFloat(node, "y"), AttrFloat(node, "width"), AttrFloat(node, "height"));
            }
        }

        static Sprite LoadWholeSprite(string path)
        {
            Sprite cached;
            if (WholeSprites.TryGetValue(path, out cached)) return cached;
            var texture = Resources.Load<Texture2D>(path);
            if (texture == null) return null;
            var sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(.5f, .5f), 100f, 0, SpriteMeshType.FullRect);
            sprite.name = path;
            WholeSprites[path] = sprite;
            return sprite;
        }

        public static void DestroyChildren(Transform parent)
        {
            for (var i = parent.childCount - 1; i >= 0; i--) UnityEngine.Object.Destroy(parent.GetChild(i).gameObject);
        }

        public static void SetTopLeft(RectTransform rt, float x, float y, float width, float height)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(x, -y);
            rt.sizeDelta = new Vector2(width, height);
        }

        static string Attr(XmlNode node, string name)
        {
            return node != null && node.Attributes != null && node.Attributes[name] != null ? node.Attributes[name].Value : string.Empty;
        }

        static int AttrInt(XmlNode node, string name)
        {
            int value;
            return int.TryParse(Attr(node, name), NumberStyles.Integer, CultureInfo.InvariantCulture, out value) ? value : 0;
        }

        static float AttrFloat(XmlNode node, string name)
        {
            float value;
            return float.TryParse(Attr(node, name), NumberStyles.Float, CultureInfo.InvariantCulture, out value) ? value : 0f;
        }
    }

    public sealed class AtlasTimelinePlayer : MonoBehaviour
    {
        string _group;
        string _prefix;
        int _frames;
        int _hold;
        Image _image;
        float _started;
        int _last = -1;

        public void Configure(string group, string prefix, int frames, int holdFrames, Image image)
        {
            _group = group;
            _prefix = prefix;
            _frames = Mathf.Max(1, frames);
            _hold = Mathf.Max(1, holdFrames);
            _image = image;
            _started = Time.unscaledTime;
        }

        void Update()
        {
            if (_image == null || _frames <= 1) return;
            var frame = ((int)((Time.unscaledTime - _started) * CrossServerLegacyVisuals.LegacyFrameRate) / _hold) % _frames;
            if (frame == _last) return;
            var sprite = CrossServerLegacyVisuals.AtlasSprite(_group, _prefix + "." + frame);
            if (sprite != null) _image.sprite = sprite;
            _last = frame;
        }
    }
}
