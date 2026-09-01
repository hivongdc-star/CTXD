using System;
using CTXD.Client.Features.FirstPlayable;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace CTXD.Client.Features.Nation
{
    internal static class W7LegacyUi
    {
        internal const string Common = "LegacyVisual/Social/Common/";
        internal static readonly Color Gold = new Color(1f, .93f, .76f, 1f);
        internal static readonly Color Muted = new Color(.80f, .72f, .55f, 1f);
        internal static readonly Color Danger = new Color(.99f, .38f, .38f, 1f);

        internal static RectTransform Overlay(Transform host, string name)
        {
            var root = LegacyUiFactory.Panel(host, name, Vector2.zero, Vector2.one, Color.clear);
            root.SetAsLastSibling();
            return root;
        }

        internal static RectTransform Window(RectTransform root, string path, float x, float y, float w, float h)
        {
            var image = LegacyUiFactory.PixelImage(root, path, x, y, w, h);
            image.raycastTarget = true;
            return (RectTransform)image.transform;
        }

        internal static Image Image(Transform parent, string path, float x, float y, float w, float h, bool preserve = false)
        {
            var image = LegacyUiFactory.PixelImage(parent, path, x, y, w, h, preserve);
            image.raycastTarget = false;
            return image;
        }

        internal static Text Text(Transform parent, string value, float x, float y, float w, float h, int size = 13,
            TextAnchor align = TextAnchor.MiddleLeft, Color? color = null)
        {
            var text = LegacyUiFactory.PixelLabel(parent, value ?? string.Empty, size, align, color ?? Gold, x, y, w, h);
            text.raycastTarget = false;
            var outline = text.gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(.18f, .14f, .10f, 1f);
            outline.effectDistance = new Vector2(1f, -1f);
            return text;
        }

        internal static Button Button(Transform parent, string label, float x, float y, float w, float h, UnityAction click,
            string up, string over, string down)
        {
            var b = LegacyUiFactory.PixelButton(parent, label, x, y, w, h, click, up, over, down);
            b.image.color = Color.white;
            return b;
        }

        internal static Button Button5(Transform parent, string label, float x, float y, float w, float h, UnityAction click) =>
            Button(parent, label, x, y, w, h, click, Common + "Button5/up", Common + "Button5/over", Common + "Button5/down");
        internal static Button Button22(Transform parent, string label, float x, float y, float w, float h, UnityAction click) =>
            Button(parent, label, x, y, w, h, click, Common + "Button22/up", Common + "Button22/over", Common + "Button22/down");
        internal static Button Button23(Transform parent, string label, float x, float y, float w, float h, UnityAction click) =>
            Button(parent, label, x, y, w, h, click, Common + "Button23/up", Common + "Button23/over", Common + "Button23/down");
        internal static Button Button30(Transform parent, string label, float x, float y, float w, float h, UnityAction click) =>
            Button(parent, label, x, y, w, h, click, Common + "Button30/up", Common + "Button30/over", Common + "Button30/down");
        internal static Button Button31(Transform parent, string label, float x, float y, float w, float h, UnityAction click) =>
            Button(parent, label, x, y, w, h, click, Common + "Button31/up", Common + "Button31/over", Common + "Button31/down");

        internal static Button Close(Transform parent, float x, float y, UnityAction click)
        {
            var b = Button(parent, string.Empty, x, y, 22, 22, click,
                Common + "Close3/up", Common + "Close3/over", Common + "Close3/down");
            b.name = "W7Close";
            return b;
        }

        internal static Button Hit(Transform parent, float x, float y, float w, float h, UnityAction click)
        {
            var go = new GameObject("Hit", typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0, 1); rt.pivot = new Vector2(0, 1);
            rt.anchoredPosition = new Vector2(x, -y); rt.sizeDelta = new Vector2(w, h);
            var image = go.GetComponent<Image>(); image.color = new Color(1, 1, 1, .001f);
            var button = go.GetComponent<Button>(); button.targetGraphic = image; button.onClick.AddListener(click);
            return button;
        }

        internal static InputField Input(Transform parent, float x, float y, float w, float h, int size = 13)
        {
            var go = new GameObject("LegacyInput", typeof(RectTransform), typeof(Image), typeof(InputField));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0, 1); rt.pivot = new Vector2(0, 1);
            rt.anchoredPosition = new Vector2(x, -y); rt.sizeDelta = new Vector2(w, h);
            go.GetComponent<Image>().color = Color.clear;
            var text = Text(go.transform, string.Empty, 2, 0, w - 4, h, size, TextAnchor.MiddleLeft, Gold);
            text.supportRichText = false;
            var field = go.GetComponent<InputField>(); field.textComponent = text; field.lineType = InputField.LineType.SingleLine;
            field.contentType = InputField.ContentType.Standard; field.caretColor = Gold;
            return field;
        }

        internal static W7LegacyToggle Toggle(Transform parent, bool value, float x, float y, bool checkbox3, Action<bool> changed)
        {
            var off = Common + (checkbox3 ? "CheckBox3/off" : "CheckBox2/off");
            var on = Common + (checkbox3 ? "CheckBox3/on" : "CheckBox2/on");
            var size = checkbox3 ? 19f : 17f;
            var image = LegacyUiFactory.PixelImage(parent, value ? on : off, x, y, size, size);
            image.raycastTarget = true;
            var button = image.gameObject.AddComponent<Button>(); button.targetGraphic = image;
            var toggle = new W7LegacyToggle(image, off, on, value, changed);
            button.onClick.AddListener(toggle.Flip);
            return toggle;
        }

        internal static void Pager(Transform parent, bool page5, float x, float y, int page, int pages, UnityAction prev, UnityAction next)
        {
            pages = Math.Max(1, pages); page = Mathf.Clamp(page, 0, pages - 1);
            var dir = Common + (page5 ? "Page5/" : "Page2/");
            var px = page5 ? -42f : -40f; var nx = page5 ? 40f : 48f;
            var sz = page5 ? 26f : 16f;
            var p = Button(parent, string.Empty, x + px, y - (page5 ? 3 : 0), sz, sz, prev,
                dir + "prev", dir + "prev", dir + "prev");
            var n = Button(parent, string.Empty, x + nx, y - (page5 ? 3 : 0), sz, sz, next,
                dir + "next", dir + "next", dir + "next");
            p.interactable = page > 0; n.interactable = page + 1 < pages;
            Text(parent, (page + 1) + "/" + pages, x - 18, y - 1, 58, 20, 12, TextAnchor.MiddleCenter, Color.white);
        }

        internal static string ForceName(int id) => id == 1 ? "Ngụy" : id == 2 ? "Thục" : id == 3 ? "Ngô" : id.ToString();
        internal static string ResourceName(string kind) => string.IsNullOrEmpty(kind) ? string.Empty : kind;
        internal static string ResourceKey(string value)
        {
            if (string.IsNullOrEmpty(value)) return "0";
            value = value.Replace('\\', '/'); var slash = value.LastIndexOf('/'); if (slash >= 0) value = value.Substring(slash + 1);
            var dot = value.LastIndexOf('.'); if (dot > 0) value = value.Substring(0, dot);
            return value;
        }
        internal static string Remaining(string iso)
        {
            if (!DateTimeOffset.TryParse(iso, out var end)) return string.Empty;
            var left = end - DateTimeOffset.UtcNow; if (left < TimeSpan.Zero) left = TimeSpan.Zero;
            return left.TotalHours >= 1 ? ((int)left.TotalHours) + ":" + left.Minutes.ToString("00") + ":" + left.Seconds.ToString("00")
                : left.Minutes.ToString("00") + ":" + left.Seconds.ToString("00");
        }
    }

    internal sealed class W7LegacyToggle
    {
        readonly Image _image; readonly string _off, _on; readonly Action<bool> _changed;
        internal bool Value { get; private set; }
        internal W7LegacyToggle(Image image, string off, string on, bool value, Action<bool> changed)
        { _image = image; _off = off; _on = on; Value = value; _changed = changed; Refresh(); }
        internal void Set(bool value, bool notify = false) { Value = value; Refresh(); if (notify) _changed?.Invoke(Value); }
        internal void Flip() { Value = !Value; Refresh(); _changed?.Invoke(Value); }
        void Refresh() { _image.sprite = Resources.Load<Sprite>(Value ? _on : _off); }
    }
}
