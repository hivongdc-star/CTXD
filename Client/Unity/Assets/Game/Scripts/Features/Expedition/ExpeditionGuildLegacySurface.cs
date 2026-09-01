using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace CTXD.Client.Features.Expedition
{
    /// <summary>
    /// Visual-only reconstruction of ExpeditionGuild.xml (662x385). It emits selection/page intents only.
    /// </summary>
    public sealed class ExpeditionGuildLegacySurface : MonoBehaviour
    {
        const float DesignWidth = 662f;
        const float DesignHeight = 385f;
        RectTransform _content;
        RectTransform _body;
        ExpeditionGuildLegacyViewState _state;

        public event Action<int> GuideRequested;
        public event Action<int> TargetRequested;
        public event Action GuidePreviousRequested;
        public event Action GuideNextRequested;
        public event Action TargetPagePreviousRequested;
        public event Action TargetPageNextRequested;

        public static ExpeditionGuildLegacySurface Mount(RectTransform host)
        {
            if (host == null) throw new ArgumentNullException(nameof(host));
            var go = new GameObject("ExpeditionGuildLegacySurface", typeof(RectTransform), typeof(ExpeditionGuildLegacySurface));
            go.transform.SetParent(host, false);
            var root = (RectTransform)go.transform;
            root.anchorMin = Vector2.zero;
            root.anchorMax = Vector2.one;
            root.offsetMin = Vector2.zero;
            root.offsetMax = Vector2.zero;
            var surface = go.GetComponent<ExpeditionGuildLegacySurface>();
            surface.BuildRoot();
            return surface;
        }

        void BuildRoot()
        {
            _content = ExpeditionLegacyAssets.Rect(transform, "LegacyGuild662x385", 0f, 0f, DesignWidth, DesignHeight);
            _content.anchorMin = _content.anchorMax = new Vector2(.5f, .5f);
            _content.pivot = new Vector2(.5f, .5f);
            _content.anchoredPosition = Vector2.zero;
            _body = ExpeditionLegacyAssets.Rect(_content, "Body", 0f, 0f, DesignWidth, DesignHeight);
        }

        void LateUpdate()
        {
            if (_content == null || !(transform is RectTransform host)) return;
            var rect = host.rect;
            if (rect.width <= 0f || rect.height <= 0f) return;
            var scale = Mathf.Min(rect.width / DesignWidth, rect.height / DesignHeight);
            _content.localScale = new Vector3(scale, scale, 1f);
        }

        public void SetState(ExpeditionGuildLegacyViewState state)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            _state = state;
            ExpeditionLegacyAssets.DestroyChildren(_body);
            DrawBackgroundAndLabels();
            DrawGuideViewport();
            DrawTargets();
            DrawPaging();
        }

        void DrawBackgroundAndLabels()
        {
            ExpeditionLegacyAssets.AtlasImage(_body, "GuildBg", ExpeditionLegacyAssets.Ui("guildBg"), 17f, 50f, 0, false);
            var brown = new Color(.757f, .514f, .369f, 1f);
            var beige = new Color(.8f, .725f, .525f, 1f);
            ExpeditionLegacyAssets.Label(_body, "KeywordHeader", "Từ khóa", 210f, 188f, 80f, 20f, 15, brown, TextAnchor.MiddleCenter);
            ExpeditionLegacyAssets.Label(_body, "DescriptionHeader", "Miêu tả", 526f, 188f, 80f, 20f, 15, brown, TextAnchor.MiddleCenter);
            ExpeditionLegacyAssets.Label(_body, "TargetHeader", "Mục tiêu", 494f, 214f, 40f, 20f, 14, beige, TextAnchor.MiddleLeft);
            ExpeditionLegacyAssets.Label(_body, "ContentHeader", "Nội dung", 494f, 255f, 40f, 20f, 14, beige, TextAnchor.MiddleLeft);
            ExpeditionLegacyAssets.Label(_body, "TargetName", _state.selectedTargetName, 494f, 232f, 148f, 20f, 14,
                new Color(.953f, .839f, .714f), TextAnchor.MiddleCenter);
            ExpeditionLegacyAssets.Label(_body, "TargetDescription", _state.selectedTargetDescription, 494f, 275f, 145f, 110f, 14,
                new Color(.953f, .839f, .714f), TextAnchor.UpperLeft);
        }

        void DrawGuideViewport()
        {
            var viewport = ExpeditionLegacyAssets.Rect(_body, "GuideViewport", 58f, 59f, 542f, 114f);
            viewport.gameObject.AddComponent<RectMask2D>();
            var guides = new Dictionary<int, ExpeditionGuildGuideVisualState>();
            foreach (var guide in _state.guides ?? Array.Empty<ExpeditionGuildGuideVisualState>())
                if (guide != null) guides[guide.index] = guide;

            var first = Mathf.Max(1, _state.firstGuideIndex);
            for (var visible = 0; visible < 2; ++visible)
            {
                var index = first + visible;
                if (index > 12) break;
                guides.TryGetValue(index, out var guide);
                DrawGuideCard(viewport, visible * 273f, 0f, index, guide);
            }

            ExpeditionLegacyAssets.AtlasButton(_body, "GuidePrev", ExpeditionLegacyAssets.Ui("guildPrev"), 22f, 90f,
                () => GuidePreviousRequested?.Invoke(), first > 1);
            ExpeditionLegacyAssets.AtlasButton(_body, "GuideNext", ExpeditionLegacyAssets.Ui("guildNext"), 614f, 90f,
                () => GuideNextRequested?.Invoke(), first + 1 < 12);
        }

        void DrawGuideCard(RectTransform parent, float x, float y, int index, ExpeditionGuildGuideVisualState state)
        {
            var card = ExpeditionLegacyAssets.Rect(parent, "Guide" + index, x, y, 271f, 114f);
            var button = ExpeditionLegacyAssets.AtlasButton(card, "ListBg", ExpeditionLegacyAssets.Ui("guildListBg"), 0f, 0f,
                () => GuideRequested?.Invoke(index), state == null || state.open);
            var visual = button != null ? button.GetComponent<LegacyExpeditionAtlasButtonVisual>() : null;
            if (visual != null) visual.SetSelected(index == _state.selectedGuideIndex);

            var guideTexture = Resources.Load<Texture2D>(ExpeditionLegacyAssets.Root + "Guild/Guide/" + index);
            if (guideTexture != null)
            {
                var go = new GameObject("GuideImage", typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage));
                go.transform.SetParent(card, false);
                var raw = go.GetComponent<RawImage>();
                raw.texture = guideTexture;
                raw.raycastTarget = false;
                ExpeditionLegacyAssets.Place((RectTransform)go.transform, 3f, 3f, 265f, 108f);
            }

            var name = ExpeditionLegacyAssets.Ui("guildName");
            if (name != null)
                ExpeditionLegacyAssets.AtlasImage(card, "Name", name, 17f, 10f, Mathf.Clamp(index - 1, 0, name.frames - 1), false);
            ExpeditionLegacyAssets.AtlasImage(card, "LvBg", ExpeditionLegacyAssets.Ui("guildLvBg"), 17f, 44f, 0, false);
            ExpeditionLegacyAssets.Label(card, "Level", state == null || state.level <= 0 ? string.Empty : state.level.ToString(),
                17f, 45f, 102f, 20f, 15, new Color(.91f, .82f, .70f), TextAnchor.MiddleCenter, true);

            if (state != null && !state.open)
            {
                ExpeditionLegacyAssets.AtlasImage(card, "NoOpen", ExpeditionLegacyAssets.Ui("guildNoOpen"), 0f, 0f, 0, false);
                ExpeditionLegacyAssets.Label(card, "NoOpenText", "Chưa mở", 96f, 48f, 80f, 20f, 14,
                    new Color(.4f, .4f, .4f), TextAnchor.MiddleCenter, true);
            }
        }

        void DrawTargets()
        {
            if (_state.waiting)
            {
                ExpeditionLegacyAssets.AtlasImage(_body, "Wait", ExpeditionLegacyAssets.Ui("guildWait"), 33f, 211f, 0, false);
                return;
            }

            var slots = new Dictionary<int, ExpeditionGuildTargetVisualState>();
            foreach (var target in _state.targets ?? Array.Empty<ExpeditionGuildTargetVisualState>())
                if (target != null) slots[target.slot] = target;

            for (var slot = 0; slot < 4; ++slot)
            {
                if (!slots.TryGetValue(slot, out var target)) continue;
                var col = slot % 2;
                var row = slot / 2;
                DrawTarget(_body, 27f + col * 226f, 214f + row * 73f, target);
            }
        }

        void DrawTarget(RectTransform parent, float x, float y, ExpeditionGuildTargetVisualState state)
        {
            var item = ExpeditionLegacyAssets.Rect(parent, "Target" + state.slot, x, y, 225f, 72f);
            var button = ExpeditionLegacyAssets.AtlasButton(item, "SelectBg", ExpeditionLegacyAssets.Ui("guildSelectBg"), 0f, 0f,
                () => TargetRequested?.Invoke(state.slot));
            var visual = button != null ? button.GetComponent<LegacyExpeditionAtlasButtonVisual>() : null;
            if (visual != null) visual.SetSelected(state.slot == _state.selectedTargetSlot);

            var maskAsset = ExpeditionLegacyAssets.Ui("guildMask");
            var maskGraphic = ExpeditionLegacyAssets.AtlasImage(item, "PortraitMask", maskAsset, 20f, 6f, 0, false);
            if (maskGraphic != null)
            {
                var mask = maskGraphic.gameObject.AddComponent<Mask>();
                mask.showMaskGraphic = false;
                if (!string.IsNullOrEmpty(state.portraitResourcePath))
                {
                    var texture = Resources.Load<Texture2D>(state.portraitResourcePath);
                    if (texture != null)
                    {
                        var go = new GameObject("Portrait", typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage));
                        go.transform.SetParent(maskGraphic.transform, false);
                        var raw = go.GetComponent<RawImage>();
                        raw.texture = texture;
                        raw.raycastTarget = false;
                        // XML portrait 15,-4 (72x72) relative to mask 20,6 (60x60).
                        ExpeditionLegacyAssets.Place((RectTransform)go.transform, -5f, -10f, 72f, 72f);
                    }
                }
            }

            ExpeditionLegacyAssets.AtlasImage(item, "HeadFrame", ExpeditionLegacyAssets.Ui("guildHeadFrame"), 3f, 0f, 0, false);
            if (state.mainTarget)
                ExpeditionLegacyAssets.AtlasImage(item, "MainTarget", ExpeditionLegacyAssets.Ui("guildMainTarget"), 25f, 16f, 0, false);

            ExpeditionLegacyAssets.Label(item, "Name", state.name, 90f, 16f, 120f, 20f, 15,
                new Color(.953f, .686f, .361f), TextAnchor.MiddleCenter);
            ExpeditionLegacyAssets.Label(item, "Description", state.description, 83f, 38f, 146f, 20f, 13,
                new Color(1f, 1f, .8f), TextAnchor.MiddleCenter);

            if (state.completed)
                ExpeditionLegacyAssets.AtlasImage(item, "Completed", ExpeditionLegacyAssets.Ui("guildCompleted"), 154f, 14f, 0, false);
            ExpeditionLegacyAssets.AtlasImage(item, "LevelFont", ExpeditionLegacyAssets.Ui("guildLevelFont"), 48f, 48f, 0, false);
            ExpeditionLegacyAssets.Label(item, "Level", state.level > 0 ? state.level.ToString() : string.Empty,
                46f, 49f, 42f, 18f, 13, new Color(1f, .93f, .70f), TextAnchor.MiddleCenter, true);
        }

        void DrawPaging()
        {
            var prev = ExpeditionLegacyAssets.AtlasButton(_body, "TargetPagePrev", ExpeditionLegacyAssets.Ui("page5Prev"), 199f, 361f,
                () => TargetPagePreviousRequested?.Invoke(), _state.targetPage > 1);
            var next = ExpeditionLegacyAssets.AtlasButton(_body, "TargetPageNext", ExpeditionLegacyAssets.Ui("page5Next"), 281f, 361f,
                () => TargetPageNextRequested?.Invoke(), _state.targetPage < Mathf.Max(1, _state.targetPageCount));
            if (prev != null) prev.gameObject.name = "TargetPagePrev";
            if (next != null) next.gameObject.name = "TargetPageNext";
            ExpeditionLegacyAssets.Label(_body, "TargetPageText", _state.targetPage + "/" + Mathf.Max(1, _state.targetPageCount),
                232f, 358f, 58f, 24f, 13, Color.white, TextAnchor.MiddleCenter);
        }
    }
}
