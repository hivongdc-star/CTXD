using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Events;
using UnityEngine.UI;

namespace CTXD.Client.Features.FirstPlayable
{
    public static class LegacyUiFactory
    {
        static Font _font;
        static Font Font => _font != null ? _font : (_font = Resources.GetBuiltinResource<Font>("Arial.ttf"));

        public static Canvas CreateCanvas()
        {
            EnsureEventSystem();
            var go = new GameObject("CTXD_LegacyCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280, 768);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
            return canvas;
        }

        static void EnsureEventSystem()
        {
            var eventSystem = EventSystem.current != null
                ? EventSystem.current
                : Object.FindFirstObjectByType<EventSystem>();
            if (eventSystem == null)
            {
                var go = new GameObject("CTXD_EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
                Object.DontDestroyOnLoad(go);
                return;
            }

            if (eventSystem.GetComponent<BaseInputModule>() == null)
                eventSystem.gameObject.AddComponent<StandaloneInputModule>();
        }

        public static RectTransform Panel(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = anchorMin; rt.anchorMax = anchorMax; rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
            go.GetComponent<Image>().color = color;
            return rt;
        }

        public static Image ResourceImage(Transform parent, string path, Vector2 anchorMin, Vector2 anchorMax, bool preserveAspect = false)
        {
            var go = new GameObject(System.IO.Path.GetFileName(path), typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = anchorMin; rt.anchorMax = anchorMax; rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
            var img = go.GetComponent<Image>();
            img.sprite = Resources.Load<Sprite>(path);
            img.preserveAspect = preserveAspect;
            return img;
        }

        public static Text Label(Transform parent, string text, int size, TextAnchor alignment, Color color,
            Vector2 anchorMin, Vector2 anchorMax)
        {
            var go = new GameObject("Text", typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = anchorMin; rt.anchorMax = anchorMax; rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
            var t = go.GetComponent<Text>();
            t.font = Font; t.fontSize = size; t.alignment = alignment; t.color = color; t.text = text;
            t.horizontalOverflow = HorizontalWrapMode.Wrap; t.verticalOverflow = VerticalWrapMode.Truncate;
            return t;
        }

        public static InputField Input(Transform parent, string placeholder, Vector2 anchorMin, Vector2 anchorMax, bool password = false)
        {
            var root = Panel(parent, "Input", anchorMin, anchorMax, new Color(0.08f, 0.06f, 0.04f, 0.92f));
            var outline = root.gameObject.AddComponent<Outline>(); outline.effectColor = new Color(0.65f, 0.5f, 0.2f, 1f);
            var text = Label(root, "", 20, TextAnchor.MiddleLeft, Color.white, new Vector2(0.03f,0), new Vector2(0.97f,1));
            var ph = Label(root, placeholder, 18, TextAnchor.MiddleLeft, new Color(0.75f,0.7f,0.6f,0.75f), new Vector2(0.03f,0), new Vector2(0.97f,1));
            var f = root.gameObject.AddComponent<InputField>(); f.textComponent = text; f.placeholder = ph;
            if (password) f.contentType = InputField.ContentType.Password;
            return f;
        }

        public static InputField PixelInput(Transform parent, string name, float x, float y, float width, float height,
            Color background, Color textColor, int fontSize, bool password = false, bool outline = true)
        {
            var root = PixelPanel(parent, name, x, y, width, height, background);
            if (outline)
            {
                var border = root.gameObject.AddComponent<Outline>();
                border.effectColor = Color.black;
                border.effectDistance = new Vector2(1, -1);
            }
            var text = PixelLabel(root, "", fontSize, TextAnchor.MiddleLeft, textColor, 3, 0, width - 6, height);
            text.supportRichText = false;
            var field = root.gameObject.AddComponent<InputField>();
            field.textComponent = text;
            field.contentType = password ? InputField.ContentType.Password : InputField.ContentType.Standard;
            field.lineType = InputField.LineType.SingleLine;
            field.caretColor = textColor;
            field.selectionColor = new Color(.35f, .55f, .8f, .75f);
            return field;
        }

        public static Button Button(Transform parent, string text, Vector2 anchorMin, Vector2 anchorMax, UnityAction onClick,
            Sprite sprite = null)
        {
            var go = new GameObject("Button_" + text, typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = anchorMin; rt.anchorMax = anchorMax; rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
            var image = go.GetComponent<Image>();
            image.sprite = sprite;
            image.type = sprite != null ? Image.Type.Simple : Image.Type.Sliced;
            image.color = sprite != null ? Color.white : new Color(0.22f, 0.12f, 0.045f, 0.96f);
            var button = go.GetComponent<Button>();
            button.targetGraphic = image; button.onClick.AddListener(onClick);
            var label = Label(go.transform, text, 19, TextAnchor.MiddleCenter, new Color(1f,0.88f,0.53f), Vector2.zero, Vector2.one);
            var outline = label.gameObject.AddComponent<Outline>(); outline.effectColor = Color.black; outline.effectDistance = new Vector2(1, -1);
            return button;
        }


        public static Button SpriteButton(Transform parent, string text, Vector2 anchorMin, Vector2 anchorMax, UnityAction onClick,
            string normalPath, string highlightedPath = null, string pressedPath = null)
        {
            var normal = Resources.Load<Sprite>(normalPath);
            var button = Button(parent, text, anchorMin, anchorMax, onClick, normal);
            var state = button.spriteState;
            state.highlightedSprite = string.IsNullOrEmpty(highlightedPath) ? normal : Resources.Load<Sprite>(highlightedPath);
            state.pressedSprite = string.IsNullOrEmpty(pressedPath) ? state.highlightedSprite : Resources.Load<Sprite>(pressedPath);
            state.selectedSprite = state.highlightedSprite;
            button.spriteState = state;
            return button;
        }

        public static RectTransform PixelPanel(Transform parent, string name, float x, float y, float width, float height, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            SetPixelRect(rt, x, y, width, height);
            go.GetComponent<Image>().color = color;
            return rt;
        }

        public static Image PixelImage(Transform parent, string path, float x, float y, float width, float height, bool preserveAspect = false)
        {
            var go = new GameObject(System.IO.Path.GetFileName(path), typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            SetPixelRect(rt, x, y, width, height);
            var img = go.GetComponent<Image>();
            img.sprite = Resources.Load<Sprite>(path);
            img.preserveAspect = preserveAspect;
            return img;
        }

        public static Text PixelLabel(Transform parent, string text, int size, TextAnchor alignment, Color color, float x, float y, float width, float height)
        {
            var go = new GameObject("Text", typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            SetPixelRect(rt, x, y, width, height);
            var t = go.GetComponent<Text>();
            t.font = Font; t.fontSize = size; t.alignment = alignment; t.color = color; t.text = text;
            t.horizontalOverflow = HorizontalWrapMode.Wrap; t.verticalOverflow = VerticalWrapMode.Truncate;
            return t;
        }

        public static Button PixelButton(Transform parent, string text, float x, float y, float width, float height, UnityAction onClick,
            string normalPath = null, string highlightedPath = null, string pressedPath = null)
        {
            var normal = string.IsNullOrEmpty(normalPath) ? null : Resources.Load<Sprite>(normalPath);
            var go = new GameObject("Button_" + text, typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            SetPixelRect(rt, x, y, width, height);
            var image = go.GetComponent<Image>();
            image.sprite = normal;
            image.type = normal != null ? Image.Type.Simple : Image.Type.Sliced;
            image.color = normal != null ? Color.white : new Color(0.22f, 0.12f, 0.045f, 0.96f);
            var button = go.GetComponent<Button>();
            button.targetGraphic = image; button.onClick.AddListener(onClick);
            if (normal != null)
            {
                var state = button.spriteState;
                state.highlightedSprite = string.IsNullOrEmpty(highlightedPath) ? normal : Resources.Load<Sprite>(highlightedPath);
                state.pressedSprite = string.IsNullOrEmpty(pressedPath) ? state.highlightedSprite : Resources.Load<Sprite>(pressedPath);
                state.selectedSprite = state.highlightedSprite;
                button.spriteState = state;
            }
            if (!string.IsNullOrEmpty(text))
            {
                var label = Label(go.transform, text, 16, TextAnchor.MiddleCenter, new Color(1f,0.88f,0.53f), Vector2.zero, Vector2.one);
                var outline = label.gameObject.AddComponent<Outline>(); outline.effectColor = Color.black; outline.effectDistance = new Vector2(1, -1);
            }
            return button;
        }

        static void SetPixelRect(RectTransform rt, float x, float y, float width, float height)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(0, 1);
            rt.pivot = new Vector2(0, 1);
            rt.anchoredPosition = new Vector2(x, -y);
            rt.sizeDelta = new Vector2(width, height);
        }

        public static void DestroyChildren(Transform t)
        {
            for (var i = t.childCount - 1; i >= 0; i--) Object.Destroy(t.GetChild(i).gameObject);
        }
    }
}
