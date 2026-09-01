using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace CTXD.Client.Features.Expedition
{
    [Serializable]
    internal sealed class ExpeditionLegacyAssetManifest
    {
        public int version;
        public float frameRate;
        public ExpeditionMapAsset[] maps = Array.Empty<ExpeditionMapAsset>();
        public ExpeditionAtlasAsset[] enemyAtlases = Array.Empty<ExpeditionAtlasAsset>();
        public ExpeditionAtlasAsset[] uiAssets = Array.Empty<ExpeditionAtlasAsset>();
    }

    [Serializable]
    internal sealed class ExpeditionMapAsset
    {
        public int mapId;
        public int sceneWidth;
        public int sceneHeight;
        public float offsetX;
        public float offsetY;
        public ExpeditionSceneLayerAsset[] layers = Array.Empty<ExpeditionSceneLayerAsset>();
        public ExpeditionEnemyPlacementAsset[] enemies = Array.Empty<ExpeditionEnemyPlacementAsset>();
    }

    [Serializable]
    internal sealed class ExpeditionSceneLayerAsset
    {
        public int depth;
        public float x;
        public float y;
        public ExpeditionAtlasAsset atlas;
    }

    [Serializable]
    internal sealed class ExpeditionEnemyPlacementAsset
    {
        public int index;
        public string name = string.Empty;
        public int depth;
        public float x;
        public float y;
        public string atlasKey = string.Empty;
    }

    [Serializable]
    internal sealed class ExpeditionAtlasAsset
    {
        public string key = string.Empty;
        public string path = string.Empty;
        public string symbol = string.Empty;
        public int frames = 1;
        public int cellWidth = 1;
        public int cellHeight = 1;
        public int columns = 1;
        public int rows = 1;
        public float offsetX;
        public float offsetY;
        public float fps = 24f;
        public ExpeditionFrameLabel[] labels = Array.Empty<ExpeditionFrameLabel>();
    }

    [Serializable]
    internal sealed class ExpeditionFrameLabel
    {
        public string name = string.Empty;
        public int start;
        public int end;
    }

    internal static class ExpeditionLegacyAssets
    {
        internal const string Root = "LegacyVisual/Expedition/Packed/";
        const string ManifestPath = Root + "expedition_manifest";

        static ExpeditionLegacyAssetManifest _manifest;
        static readonly Dictionary<string, ExpeditionAtlasAsset> EnemyByKey = new Dictionary<string, ExpeditionAtlasAsset>();
        static readonly Dictionary<string, ExpeditionAtlasAsset> UiByKey = new Dictionary<string, ExpeditionAtlasAsset>();
        static readonly Dictionary<string, Texture2D> TextureByPath = new Dictionary<string, Texture2D>();
        static Font _font;

        internal static ExpeditionLegacyAssetManifest Manifest
        {
            get
            {
                if (_manifest != null) return _manifest;
                var text = Resources.Load<TextAsset>(ManifestPath);
                if (text == null) throw new InvalidOperationException("Missing Expedition legacy manifest at Resources/" + ManifestPath + ".json");
                _manifest = JsonUtility.FromJson<ExpeditionLegacyAssetManifest>(text.text);
                if (_manifest == null) throw new InvalidOperationException("Invalid Expedition legacy manifest JSON.");
                EnemyByKey.Clear();
                UiByKey.Clear();
                foreach (var a in _manifest.enemyAtlases ?? Array.Empty<ExpeditionAtlasAsset>())
                    if (a != null && !string.IsNullOrEmpty(a.key)) EnemyByKey[a.key] = a;
                foreach (var a in _manifest.uiAssets ?? Array.Empty<ExpeditionAtlasAsset>())
                    if (a != null && !string.IsNullOrEmpty(a.key)) UiByKey[a.key] = a;
                return _manifest;
            }
        }

        internal static ExpeditionMapAsset Map(int mapId)
        {
            foreach (var map in Manifest.maps ?? Array.Empty<ExpeditionMapAsset>())
                if (map != null && map.mapId == mapId) return map;
            return null;
        }

        internal static ExpeditionAtlasAsset EnemyAtlas(string key)
        {
            _ = Manifest;
            return !string.IsNullOrEmpty(key) && EnemyByKey.TryGetValue(key, out var value) ? value : null;
        }

        internal static ExpeditionAtlasAsset Ui(string key)
        {
            _ = Manifest;
            return !string.IsNullOrEmpty(key) && UiByKey.TryGetValue(key, out var value) ? value : null;
        }

        internal static Texture2D Texture(ExpeditionAtlasAsset asset)
        {
            if (asset == null || string.IsNullOrEmpty(asset.path)) return null;
            if (TextureByPath.TryGetValue(asset.path, out var cached) && cached != null) return cached;
            var texture = Resources.Load<Texture2D>(asset.path);
            if (texture != null) TextureByPath[asset.path] = texture;
            return texture;
        }

        internal static Font Font
        {
            get
            {
                if (_font != null) return _font;
                try { _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); } catch { }
                if (_font == null)
                {
                    try { _font = Resources.GetBuiltinResource<Font>("Arial.ttf"); } catch { }
                }
                if (_font == null)
                {
                    try
                    {
                        var names = Font.GetOSInstalledFontNames();
                        if (names != null && names.Length > 0) _font = Font.CreateDynamicFontFromOSFont(names[0], 16);
                    }
                    catch { }
                }
                return _font;
            }
        }

        internal static RectTransform Rect(Transform parent, string name, float x, float y, float width, float height)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            Place(rt, x, y, width, height);
            return rt;
        }

        internal static void Place(RectTransform rt, float x, float y, float width, float height)
        {
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(x, -y);
            rt.sizeDelta = new Vector2(Mathf.Max(0f, width), Mathf.Max(0f, height));
            rt.localScale = Vector3.one;
            rt.localRotation = Quaternion.identity;
        }

        internal static RawImage AtlasImage(Transform parent, string name, ExpeditionAtlasAsset asset, float x, float y, int frame = 0, bool animate = false)
        {
            if (asset == null) return null;
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage));
            go.transform.SetParent(parent, false);
            var raw = go.GetComponent<RawImage>();
            raw.texture = Texture(asset);
            raw.raycastTarget = false;
            Place((RectTransform)go.transform, x + asset.offsetX, y + asset.offsetY, asset.cellWidth, asset.cellHeight);
            SetFrame(raw, asset, frame);
            if (animate && asset.frames > 1)
                go.AddComponent<LegacyExpeditionAtlasAnimator>().Initialize(raw, asset, 0, asset.frames - 1, true);
            return raw;
        }

        internal static Button AtlasButton(Transform parent, string name, ExpeditionAtlasAsset asset, float x, float y, Action click, bool interactable = true)
        {
            var raw = AtlasImage(parent, name, asset, x, y, 0, false);
            if (raw == null) return null;
            raw.raycastTarget = true;
            var button = raw.gameObject.AddComponent<Button>();
            button.targetGraphic = raw;
            button.transition = Selectable.Transition.None;
            button.interactable = interactable;
            if (click != null) button.onClick.AddListener(() => click());
            raw.gameObject.AddComponent<LegacyExpeditionAtlasButtonVisual>().Initialize(raw, button, asset);
            return button;
        }

        internal static Text Label(Transform parent, string name, string value, float x, float y, float width, float height, int size, Color color, TextAnchor align, bool bold = false)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            go.transform.SetParent(parent, false);
            var text = go.GetComponent<Text>();
            text.font = Font;
            text.text = value ?? string.Empty;
            text.fontSize = size;
            text.color = color;
            text.alignment = align;
            text.raycastTarget = false;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.fontStyle = bold ? FontStyle.Bold : FontStyle.Normal;
            Place((RectTransform)go.transform, x, y, width, height);
            return text;
        }

        internal static void SetFrame(RawImage image, ExpeditionAtlasAsset asset, int frame)
        {
            if (image == null || asset == null) return;
            var count = Mathf.Max(1, asset.frames);
            var cols = Mathf.Max(1, asset.columns);
            var rows = Mathf.Max(1, asset.rows);
            var f = ((frame % count) + count) % count;
            var col = f % cols;
            var row = f / cols;
            image.uvRect = new Rect((float)col / cols, 1f - (float)(row + 1) / rows, 1f / cols, 1f / rows);
        }

        internal static int LabelStart(ExpeditionAtlasAsset asset, string label, int fallback)
        {
            if (asset?.labels == null) return fallback;
            foreach (var item in asset.labels)
                if (item != null && string.Equals(item.name, label, StringComparison.Ordinal)) return item.start;
            return fallback;
        }

        internal static ExpeditionFrameLabel FrameLabel(ExpeditionAtlasAsset asset, string label)
        {
            if (asset?.labels == null) return null;
            foreach (var item in asset.labels)
                if (item != null && string.Equals(item.name, label, StringComparison.Ordinal)) return item;
            return null;
        }

        internal static Material GrayscaleMaterial(float amount = 1f)
        {
            var shader = Shader.Find("CTXD/LegacyExpeditionGrayscale");
            if (shader == null) return null;
            var material = new Material(shader) { hideFlags = HideFlags.DontSave };
            material.SetFloat("_GrayAmount", Mathf.Clamp01(amount));
            return material;
        }

        internal static void DestroyChildren(Transform parent)
        {
            if (parent == null) return;
            for (var i = parent.childCount - 1; i >= 0; --i)
                UnityEngine.Object.Destroy(parent.GetChild(i).gameObject);
        }
    }

    internal sealed class LegacyExpeditionAtlasAnimator : MonoBehaviour
    {
        RawImage _image;
        ExpeditionAtlasAsset _asset;
        int _start;
        int _end;
        bool _loop;
        float _started;
        Action _completed;
        bool _done;

        internal void Initialize(RawImage image, ExpeditionAtlasAsset asset, int start, int end, bool loop, Action completed = null)
        {
            _image = image;
            _asset = asset;
            _start = Mathf.Clamp(start, 0, Mathf.Max(0, asset.frames - 1));
            _end = Mathf.Clamp(end, _start, Mathf.Max(_start, asset.frames - 1));
            _loop = loop;
            _completed = completed;
            _started = Time.unscaledTime;
            _done = false;
            ExpeditionLegacyAssets.SetFrame(_image, _asset, _start);
        }

        void Update()
        {
            if (_image == null || _asset == null || _done) return;
            var fps = _asset.fps > 0f ? _asset.fps : 24f;
            var length = Mathf.Max(1, _end - _start + 1);
            var elapsedFrames = Mathf.FloorToInt((Time.unscaledTime - _started) * fps);
            if (_loop)
            {
                ExpeditionLegacyAssets.SetFrame(_image, _asset, _start + elapsedFrames % length);
                return;
            }
            if (elapsedFrames >= length)
            {
                ExpeditionLegacyAssets.SetFrame(_image, _asset, _end);
                _done = true;
                _completed?.Invoke();
                return;
            }
            ExpeditionLegacyAssets.SetFrame(_image, _asset, _start + elapsedFrames);
        }
    }

    internal sealed class LegacyExpeditionAtlasButtonVisual : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler
    {
        RawImage _image;
        Button _button;
        ExpeditionAtlasAsset _asset;
        bool _hover;
        bool _down;
        bool _selected;
        string _state = string.Empty;
        float _stateStarted;

        internal void Initialize(RawImage image, Button button, ExpeditionAtlasAsset asset)
        {
            _image = image;
            _button = button;
            _asset = asset;
            SetState("up");
        }

        internal void SetSelected(bool selected) { _selected = selected; RefreshState(); }

        public void OnPointerEnter(PointerEventData eventData) { _hover = true; RefreshState(); }
        public void OnPointerExit(PointerEventData eventData) { _hover = false; _down = false; RefreshState(); }
        public void OnPointerDown(PointerEventData eventData) { if (eventData.button == PointerEventData.InputButton.Left) { _down = true; RefreshState(); } }
        public void OnPointerUp(PointerEventData eventData) { if (eventData.button == PointerEventData.InputButton.Left) { _down = false; RefreshState(); } }

        void Update()
        {
            RefreshState();
            var range = ExpeditionLegacyAssets.FrameLabel(_asset, _state);
            if (range == null)
            {
                ExpeditionLegacyAssets.SetFrame(_image, _asset, 0);
                return;
            }
            var fps = _asset.fps > 0f ? _asset.fps : 24f;
            var length = Mathf.Max(1, range.end - range.start + 1);
            var frame = range.start + Mathf.FloorToInt((Time.unscaledTime - _stateStarted) * fps) % length;
            ExpeditionLegacyAssets.SetFrame(_image, _asset, frame);
        }

        void RefreshState()
        {
            var baseState = _button != null && !_button.interactable ? "disabled" : (_down ? "down" : (_hover ? "over" : "up"));
            var next = baseState;
            if (_selected)
            {
                if (baseState == "up") next = "selectedUp";
                else if (baseState == "over") next = "selectedOver";
                else if (baseState == "down") next = "selectedDown";
                else if (baseState == "disabled") next = "selectedDisabled";
            }
            if (!string.Equals(next, _state, StringComparison.Ordinal)) SetState(next);
        }

        void SetState(string value)
        {
            _state = value;
            _stateStarted = Time.unscaledTime;
        }
    }
}
