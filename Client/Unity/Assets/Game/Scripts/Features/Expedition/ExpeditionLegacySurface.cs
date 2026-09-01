using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace CTXD.Client.Features.Expedition
{
    /// <summary>
    /// Visual-only reconstruction of the legacy Expedition map scene. The class exposes intent callbacks;
    /// it deliberately does not know an ApiClient, transport DTO, Battle engine, or scenario endpoint.
    /// </summary>
    public sealed class ExpeditionLegacySurface : MonoBehaviour
    {
        const float DesignWidth = 1280f;
        const float DesignHeight = 768f;
        const float RightX = 280f;       // 1000-wide rSp aligned to the right of a 1280 stage.
        const float RightBottomX = 280f; // 1000-wide rdSp.
        const float RightBottomY = 168f; // 600-high rdSp aligned to the bottom of a 768 stage.

        RectTransform _content;
        RectTransform _scene;
        RectTransform _chrome;
        ExpeditionLegacyViewState _state;
        Coroutine _slide;
        readonly Dictionary<int, RectTransform> _enemyRoots = new Dictionary<int, RectTransform>();

        public event Action<int> EnemyRequested;
        public event Action PreviousPageRequested;
        public event Action NextPageRequested;
        public event Action BackRequested;
        public event Action DramaRequested;
        public event Action HelpRequested;
        public event Action ExtraRequested;

        public static ExpeditionLegacySurface Mount(RectTransform host)
        {
            if (host == null) throw new ArgumentNullException(nameof(host));
            var go = new GameObject("ExpeditionLegacySurface", typeof(RectTransform), typeof(ExpeditionLegacySurface));
            go.transform.SetParent(host, false);
            var root = (RectTransform)go.transform;
            root.anchorMin = Vector2.zero;
            root.anchorMax = Vector2.one;
            root.offsetMin = Vector2.zero;
            root.offsetMax = Vector2.zero;
            var surface = go.GetComponent<ExpeditionLegacySurface>();
            surface.BuildRoot();
            return surface;
        }

        void BuildRoot()
        {
            _content = ExpeditionLegacyAssets.Rect(transform, "LegacyStage1280x768", 0f, 0f, DesignWidth, DesignHeight);
            _content.anchorMin = _content.anchorMax = new Vector2(.5f, .5f);
            _content.pivot = new Vector2(.5f, .5f);
            _content.anchoredPosition = Vector2.zero;
            _chrome = ExpeditionLegacyAssets.Rect(_content, "LegacyChrome", 0f, 0f, DesignWidth, DesignHeight);
        }

        void LateUpdate()
        {
            if (_content == null || !(transform is RectTransform host)) return;
            var rect = host.rect;
            if (rect.width <= 0f || rect.height <= 0f) return;
            var scale = Mathf.Min(rect.width / DesignWidth, rect.height / DesignHeight);
            _content.localScale = new Vector3(scale, scale, 1f);
        }

        public void SetState(ExpeditionLegacyViewState state)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            var oldPage = _state?.currentPage ?? state.currentPage;
            var oldScene = _scene;
            _state = state;
            var newScene = BuildScene(state);
            _scene = newScene;

            if (oldScene != null && oldPage != state.currentPage)
            {
                if (_slide != null) StopCoroutine(_slide);
                var direction = state.currentPage > oldPage ? 1f : -1f;
                _slide = StartCoroutine(Slide(oldScene, newScene, direction));
            }
            else
            {
                if (oldScene != null) Destroy(oldScene.gameObject);
                ExpeditionLegacyAssets.Place(newScene, 0f, 0f, DesignWidth, DesignHeight);
            }

            BuildChrome(state);
        }

        RectTransform BuildScene(ExpeditionLegacyViewState state)
        {
            var map = ExpeditionLegacyAssets.Map(state.mapId);
            if (map == null) throw new InvalidOperationException("No packed legacy Expedition map for mapId=" + state.mapId);
            var scene = ExpeditionLegacyAssets.Rect(_content, "ExpeditionMap" + state.mapId, DesignWidth, 0f, DesignWidth, DesignHeight);
            scene.SetSiblingIndex(0);
            _enemyRoots.Clear();

            var enemyState = new Dictionary<int, ExpeditionEnemyVisualState>();
            foreach (var item in state.enemies ?? Array.Empty<ExpeditionEnemyVisualState>())
                if (item != null) enemyState[item.index] = item;

            var entries = new List<SceneEntry>();
            foreach (var layer in map.layers ?? Array.Empty<ExpeditionSceneLayerAsset>())
            {
                if (layer?.atlas == null) continue;
                entries.Add(new SceneEntry(layer.depth, () =>
                {
                    var raw = ExpeditionLegacyAssets.AtlasImage(scene, "Layer_" + layer.depth,
                        layer.atlas, map.offsetX + layer.x, map.offsetY + layer.y, 0, layer.atlas.frames > 1);
                    if (raw != null) raw.raycastTarget = false;
                }));
            }

            foreach (var placement in map.enemies ?? Array.Empty<ExpeditionEnemyPlacementAsset>())
            {
                if (placement == null) continue;
                enemyState.TryGetValue(placement.index, out var visual);
                if (visual != null && !visual.visible) continue;
                var copy = placement;
                entries.Add(new SceneEntry(copy.depth, () => DrawEnemy(scene, map, copy, visual)));
            }

            entries.Sort((a, b) => a.depth.CompareTo(b.depth));
            foreach (var entry in entries) entry.draw();
            return scene;
        }

        void DrawEnemy(RectTransform scene, ExpeditionMapAsset map, ExpeditionEnemyPlacementAsset placement, ExpeditionEnemyVisualState state)
        {
            var x = map.offsetX + placement.x;
            var y = map.offsetY + placement.y;
            var holder = ExpeditionLegacyAssets.Rect(scene, placement.name, x, y, 1f, 1f);
            _enemyRoots[placement.index] = holder;

            if (state != null && state.attacked)
            {
                var attacked = ExpeditionLegacyAssets.Ui("attacked");
                ExpeditionLegacyAssets.AtlasImage(holder, "Attacked", attacked, 0f, 0f, 0, attacked != null && attacked.frames > 1);
                return;
            }

            var atlas = ExpeditionLegacyAssets.EnemyAtlas(placement.atlasKey);
            var raw = ExpeditionLegacyAssets.AtlasImage(holder, "EnemyTimeline", atlas, 0f, 0f, 0, true);
            if (raw == null) return;
            raw.raycastTarget = true;

            var attackable = state == null || state.attackable;
            if (!attackable)
            {
                var material = ExpeditionLegacyAssets.GrayscaleMaterial(1f);
                if (material != null) raw.material = material;
            }

            var button = raw.gameObject.AddComponent<Button>();
            button.targetGraphic = raw;
            button.transition = Selectable.Transition.None;
            button.interactable = attackable;
            var index = placement.index;
            button.onClick.AddListener(() => EnemyRequested?.Invoke(index));

            // These are legacy state symbols; their positions remain anchored to the enemy instance origin.
            // No gameplay/unlock rule is inferred here.
            if (state != null && state.elite)
            {
                var elite = ExpeditionLegacyAssets.Ui("eliteTitle");
                if (elite != null)
                    ExpeditionLegacyAssets.AtlasImage(holder, "EliteTitle", elite,
                        atlas.offsetX + atlas.cellWidth * .5f, atlas.offsetY - 22f, 0, false);
            }
            if (state != null && state.showRequiredLevel)
                DrawRequiredLevel(holder, atlas, state.requiredLevel);
        }

        void DrawRequiredLevel(RectTransform enemyRoot, ExpeditionAtlasAsset enemyAtlas, int level)
        {
            if (enemyAtlas == null) return;
            var bg = ExpeditionLegacyAssets.Ui("levelBg");
            var open = ExpeditionLegacyAssets.Ui("levelOpen");
            var x = enemyAtlas.offsetX + enemyAtlas.cellWidth * .5f;
            var y = enemyAtlas.offsetY + enemyAtlas.cellHeight - 4f;
            if (bg != null) ExpeditionLegacyAssets.AtlasImage(enemyRoot, "LevelBg", bg, x, y, 0, false);
            if (open != null) ExpeditionLegacyAssets.AtlasImage(enemyRoot, "LevelOpen", open, x, y + 3f, 0, false);
            ExpeditionLegacyAssets.Label(enemyRoot, "Level", level.ToString(), x - 17f, y + 2f, 34f, 20f, 14,
                new Color(1f, .93f, .72f, 1f), TextAnchor.MiddleCenter, true);
        }

        void BuildChrome(ExpeditionLegacyViewState state)
        {
            ExpeditionLegacyAssets.DestroyChildren(_chrome);

            // rSp: 1000x600, right aligned, vertical alignment cancelled.
            if (state.showDramaButton)
            {
                var originX = RightX + 871f;
                var originY = 87f;
                ExpeditionLegacyAssets.AtlasButton(_chrome, "DramaBtn", ExpeditionLegacyAssets.Ui("dramaBtn"), originX, originY,
                    () => DramaRequested?.Invoke());
                ExpeditionLegacyAssets.Label(_chrome, "DramaLabel", "Chinh chiến cốt truyện", originX + 30f, originY + 4f,
                    74f, 20f, 13, new Color(.953f, .761f, .463f, 1f), TextAnchor.MiddleCenter);
            }

            if (state.showHelpButton)
            {
                // rdSp: 1000x600, right/bottom aligned.
                ExpeditionLegacyAssets.AtlasButton(_chrome, "HelpBtn", ExpeditionLegacyAssets.Ui("helpBtn"),
                    RightBottomX + 936f, RightBottomY + 428f, () => HelpRequested?.Invoke());
            }

            if (state.showExtraButton)
                DrawExtra(state);

            var prevX = RightX + 733f;
            var nextX = RightX + 878f;
            if (state.showBackButton)
            {
                ExpeditionLegacyAssets.AtlasButton(_chrome, "BackBtn", ExpeditionLegacyAssets.Ui("page6Prev"), prevX, 38f,
                    () => BackRequested?.Invoke());
                ExpeditionLegacyAssets.Label(_chrome, "BackText", state.previousPageText, RightX + 746f, 45f,
                    60f, 20f, 13, new Color(.867f, .804f, .675f), TextAnchor.MiddleCenter);
            }
            else if (state.showPreviousButton)
            {
                ExpeditionLegacyAssets.AtlasButton(_chrome, "PrevBtn", ExpeditionLegacyAssets.Ui("page6Prev"), prevX, 38f,
                    () => PreviousPageRequested?.Invoke());
                ExpeditionLegacyAssets.Label(_chrome, "PrevText", state.previousPageText, RightX + 746f, 45f,
                    60f, 20f, 13, new Color(.867f, .804f, .675f), TextAnchor.MiddleCenter);
            }

            if (state.showNextButton)
            {
                ExpeditionLegacyAssets.AtlasButton(_chrome, "NextBtn", ExpeditionLegacyAssets.Ui("page6Next"), nextX, 38f,
                    () => NextPageRequested?.Invoke());
                ExpeditionLegacyAssets.Label(_chrome, "NextText", state.nextPageText, RightX + 917f, 45f,
                    60f, 20f, 13, new Color(.867f, .804f, .675f), TextAnchor.MiddleCenter);
            }

            ExpeditionLegacyAssets.Label(_chrome, "PageText", string.IsNullOrEmpty(state.pageText)
                    ? state.currentPage + "/" + Mathf.Max(1, state.pageCount)
                    : state.pageText,
                RightX + 822f, 45f, 80f, 20f, 13, new Color(1f, .996f, .804f), TextAnchor.MiddleCenter);
        }

        void DrawExtra(ExpeditionLegacyViewState state)
        {
            var originX = RightX + 871f;
            var originY = 136f;
            ExpeditionLegacyAssets.AtlasButton(_chrome, "ExtraBtn", ExpeditionLegacyAssets.Ui("extraBtn"), originX, originY,
                () => ExtraRequested?.Invoke());
            ExpeditionLegacyAssets.Label(_chrome, "ExtraLabel", state.extraLabel, RightX + 905f, 140f, 74f, 20f, 13,
                new Color(.953f, .761f, .463f), TextAnchor.MiddleCenter);
            ExpeditionLegacyAssets.Label(_chrome, "ExtraText", state.extraText, RightX + 809f, 163f, 111f, 20f, 13,
                new Color(.922f, .506f, .459f), TextAnchor.MiddleCenter);

            if (!string.IsNullOrEmpty(state.extraPortraitResourcePath))
            {
                var texture = Resources.Load<Texture2D>(state.extraPortraitResourcePath);
                var maskAsset = ExpeditionLegacyAssets.Ui("extraMask");
                if (texture != null && maskAsset != null)
                {
                    var maskGraphic = ExpeditionLegacyAssets.AtlasImage(_chrome, "ExtraMask", maskAsset,
                        originX - 10f, originY - 2f, 0, false);
                    if (maskGraphic != null)
                    {
                        maskGraphic.raycastTarget = false;
                        var mask = maskGraphic.gameObject.AddComponent<Mask>();
                        mask.showMaskGraphic = false;
                        var portrait = new GameObject("ExtraPortrait", typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage));
                        portrait.transform.SetParent(maskGraphic.transform, false);
                        var raw = portrait.GetComponent<RawImage>();
                        raw.texture = texture;
                        raw.raycastTarget = false;
                        // smallPic is x=860,y=126,40x40 relative to rSp; mask is x=-10,y=-2 relative to extraBtn.
                        ExpeditionLegacyAssets.Place((RectTransform)portrait.transform, -1f, -8f, 40f, 40f);
                    }
                }
            }
        }

        IEnumerator Slide(RectTransform oldScene, RectTransform newScene, float direction)
        {
            var width = DesignWidth;
            ExpeditionLegacyAssets.Place(oldScene, 0f, 0f, DesignWidth, DesignHeight);
            ExpeditionLegacyAssets.Place(newScene, direction * width, 0f, DesignWidth, DesignHeight);
            var elapsed = 0f;
            while (elapsed < 1f)
            {
                elapsed += Time.unscaledDeltaTime;
                var t = Mathf.Clamp01(elapsed); // legacy TweenMax duration is one second; easing is left linear here.
                oldScene.anchoredPosition = new Vector2(-direction * width * t, 0f);
                newScene.anchoredPosition = new Vector2(direction * width * (1f - t), 0f);
                yield return null;
            }
            newScene.anchoredPosition = Vector2.zero;
            if (oldScene != null) Destroy(oldScene.gameObject);
            _slide = null;
        }

        public void PlayDefeatTransition(int enemyIndex)
        {
            if (!_enemyRoots.TryGetValue(enemyIndex, out var root) || root == null) return;
            ExpeditionLegacyAssets.DestroyChildren(root);
            var white = ExpeditionLegacyAssets.Ui("whiteflag");
            var attacked = ExpeditionLegacyAssets.Ui("attacked");
            var raw = ExpeditionLegacyAssets.AtlasImage(root, "WhiteFlag", white, 0f, 0f, 0, false);
            if (raw == null || white == null)
            {
                if (attacked != null) ExpeditionLegacyAssets.AtlasImage(root, "Attacked", attacked, 0f, 0f, 0, attacked.frames > 1);
                return;
            }
            raw.gameObject.AddComponent<LegacyExpeditionAtlasAnimator>().Initialize(raw, white, 0, white.frames - 1, false, () =>
            {
                if (root == null) return;
                ExpeditionLegacyAssets.DestroyChildren(root);
                if (attacked != null) ExpeditionLegacyAssets.AtlasImage(root, "Attacked", attacked, 0f, 0f, 0, attacked.frames > 1);
            });
        }

        public void PlayFirstWin(string npcName)
        {
            var text = ExpeditionLegacyAssets.Label(_chrome, "FirstWinText", "Đánh bại " + (npcName ?? string.Empty),
                540f, 150f, 200f, 35f, 18, new Color(1f, .8f, 0f), TextAnchor.MiddleCenter, true);
            StartCoroutine(FadeAndDestroy(text.gameObject, 3f, false));
        }

        public void PlayCompletion(string text)
        {
            var label = ExpeditionLegacyAssets.Label(_chrome, "ResultMC", text ?? string.Empty,
                380f, 150f, 520f, 48f, 30, new Color(1f, .88f, .35f), TextAnchor.MiddleCenter, true);
            StartCoroutine(FadeAndDestroy(label.gameObject, 3f, true));
        }

        IEnumerator FadeAndDestroy(GameObject target, float duration, bool circleEase)
        {
            if (target == null) yield break;
            var group = target.AddComponent<CanvasGroup>();
            var elapsed = 0f;
            while (elapsed < duration && target != null)
            {
                elapsed += Time.unscaledDeltaTime;
                var t = Mathf.Clamp01(elapsed / duration);
                var e = circleEase ? 1f - Mathf.Sqrt(Mathf.Max(0f, 1f - t * t)) : t;
                group.alpha = 1f - e;
                yield return null;
            }
            if (target != null) Destroy(target);
        }

        sealed class SceneEntry
        {
            internal readonly int depth;
            internal readonly Action draw;
            internal SceneEntry(int depth, Action draw) { this.depth = depth; this.draw = draw; }
        }
    }
}
