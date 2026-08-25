using System;
using System.Linq;
using System.Threading.Tasks;
using CTXD.Client.Features.FirstPlayable;
using CTXD.Client.Networking;
using UnityEngine;
using UnityEngine.UI;

namespace CTXD.Client.Features.Technology
{
    /// <summary>
    /// Technology list built from the original Tech.swf visual resources. It keeps the legacy
    /// 8-row paging/state model and only replaces Flash/AS3 networking/state with the new API.
    /// </summary>
    public sealed class TechnologyPanel : MonoBehaviour
    {
        ApiClient _api;
        RectTransform _host;
        RectTransform _window;
        Action<string> _status;
        Func<Task> _onChanged;
        TechnologyListResponse _data;
        int _page = 1;
        bool _busy;

        public static TechnologyPanel Open(RectTransform host, ApiClient api, Action<string> status, Func<Task> onChanged)
        {
            var go = new GameObject("TechnologyPanel");
            go.transform.SetParent(host, false);
            var panel = go.AddComponent<TechnologyPanel>();
            panel._host = host;
            panel._api = api;
            panel._status = status;
            panel._onChanged = onChanged;
            panel.BuildFrame();
            _ = panel.LoadAsync();
            return panel;
        }

        void BuildFrame()
        {
            var blocker = LegacyUiFactory.Panel(transform, "TechBlocker", Vector2.zero, Vector2.one, new Color(0, 0, 0, .52f));
            _window = LegacyUiFactory.PixelPanel(blocker, "TechWindow", 327, 167, 626, 430, new Color(.055f, .045f, .032f, .98f));
            DrawShell();
        }

        void DrawShell()
        {
            LegacyUiFactory.DestroyChildren(_window);
            LegacyUiFactory.PixelLabel(_window, "KHOA KỸ", 21, TextAnchor.MiddleCenter,
                new Color(1f, .83f, .36f), 195, 4, 230, 28);
            LegacyUiFactory.PixelButton(_window, "Đóng", 544, 4, 70, 27, Close);
            LegacyUiFactory.PixelLabel(_window, "", 1, TextAnchor.MiddleLeft, Color.clear, 0, 35, 1, 1);
        }

        async Task LoadAsync()
        {
            if (_busy) return;
            _busy = true;
            SetStatus("Đang mở Khoa Kỹ...");
            try
            {
                _data = await _api.GetTechnologyAsync(_page);
                if (_data.totalPage > 0 && _page > _data.totalPage)
                {
                    _page = _data.totalPage;
                    _data = await _api.GetTechnologyAsync(_page);
                }
                DrawShell();
                Render();
                SetStatus("");
            }
            catch (Exception ex) { SetStatus(ex.Message); }
            finally { _busy = false; }
        }

        void Render()
        {
            var techs = _data?.technologies ?? Array.Empty<TechnologyView>();
            for (var i = 0; i < 8; i++)
                DrawRow(i, i < techs.Length ? techs[i] : null);

            var total = Math.Max(1, _data?.totalPage ?? 1);
            LegacyUiFactory.PixelButton(_window, "", 250, 390, 34, 22, PrevPage,
                "LegacyVisual/Tech/00017", "LegacyVisual/Tech/00015", "LegacyVisual/Tech/00013");
            LegacyUiFactory.PixelLabel(_window, $"{_page}/{total}", 14, TextAnchor.MiddleCenter, Color.white, 287, 388, 52, 24);
            LegacyUiFactory.PixelButton(_window, "", 343, 390, 34, 22, NextPage,
                "LegacyVisual/Tech/00013", "LegacyVisual/Tech/00015", "LegacyVisual/Tech/00017");
        }

        void DrawRow(int index, TechnologyView tech)
        {
            var y = 38 + index * 43;
            var bg = tech == null || tech.status == 0 ? "LegacyVisual/Tech/00010" : "LegacyVisual/Tech/00006";
            LegacyUiFactory.PixelImage(_window, bg, 10, y, 606, 35);
            if (tech == null) return;

            var iconPath = tech.status == 0 || string.IsNullOrWhiteSpace(tech.pic)
                ? "LegacyVisual/Tech/00053"
                : "LegacyVisual/Tech/Icons/" + tech.pic;
            LegacyUiFactory.PixelImage(_window, iconPath, 17, y + 3, 28, 28, true);

            if (tech.isNew)
                LegacyUiFactory.PixelImage(_window, "LegacyVisual/Tech/00048", 42, y - 1, 23, 21, true);

            var nameColor = tech.status == 0 ? new Color(.55f, .52f, .48f) : new Color(1f, .86f, .48f);
            LegacyUiFactory.PixelLabel(_window, tech.name, 14, TextAnchor.MiddleLeft, nameColor, 55, y + 1, 125, 32);
            LegacyUiFactory.PixelLabel(_window, StateText(tech), 12, TextAnchor.MiddleLeft, Color.white, 180, y + 1, 190, 32);

            if (tech.status is 2 or 3)
            {
                LegacyUiFactory.PixelLabel(_window, CostText(tech), 11, TextAnchor.MiddleRight,
                    new Color(.92f, .82f, .62f), 355, y + 1, 164, 32);
                LegacyUiFactory.PixelButton(_window, "Chú tư", 525, y + 5, 82, 25, async () => await InjectAsync(tech));
            }
            else if (tech.status == 1)
            {
                LegacyUiFactory.PixelImage(_window, "LegacyVisual/Tech/00037", 181, y, 320, 25, true);
                LegacyUiFactory.PixelButton(_window, "Nghiên cứu", 510, y + 5, 97, 25, async () => await ResearchAsync(tech));
            }
            else if (tech.status == 4)
            {
                var ratio = ResearchRatio(tech);
                LegacyUiFactory.PixelImage(_window, "LegacyVisual/Tech/00040", 371, y + 14, 135, 9);
                var green = LegacyUiFactory.PixelImage(_window, "LegacyVisual/Tech/00044", 371, y + 14, 135 * ratio, 9);
                green.type = Image.Type.Simple;
                LegacyUiFactory.PixelLabel(_window, RemainingText(tech), 11, TextAnchor.MiddleRight,
                    new Color(.72f, 1f, .66f), 508, y + 1, 96, 32);
            }
            else if (tech.status == 5)
            {
                LegacyUiFactory.PixelImage(_window, "LegacyVisual/Tech/00034", 526, y + 2, 64, 30, true);
            }
        }

        async Task InjectAsync(TechnologyView tech)
        {
            if (_busy) return;
            _busy = true;
            try
            {
                var result = await _api.InjectTechnologyAsync(tech.id);
                SetStatus($"Đã chú tư {result.technology.name} ({result.technology.injectedCount}/{result.technology.requiredInjections}).");
                if (_onChanged != null) await _onChanged();
            }
            catch (Exception ex) { SetStatus(ex.Message); }
            finally { _busy = false; }
            await LoadAsync();
        }

        async Task ResearchAsync(TechnologyView tech)
        {
            if (_busy) return;
            _busy = true;
            try
            {
                var result = await _api.ResearchTechnologyAsync(tech.id);
                SetStatus($"Bắt đầu nghiên cứu {result.technology.name}.");
                if (_onChanged != null) await _onChanged();
            }
            catch (Exception ex) { SetStatus(ex.Message); }
            finally { _busy = false; }
            await LoadAsync();
        }

        async void PrevPage()
        {
            if (_busy || _page <= 1) return;
            _page--;
            await LoadAsync();
        }

        async void NextPage()
        {
            if (_busy || _data == null || _page >= Math.Max(1, _data.totalPage)) return;
            _page++;
            await LoadAsync();
        }

        static string StateText(TechnologyView t)
        {
            return t.status switch
            {
                0 => "Chưa đạt điều kiện mở",
                1 => $"Đã chú tư {t.injectedCount}/{t.requiredInjections}",
                2 => $"Chú tư {t.injectedCount}/{t.requiredInjections}",
                3 => $"Chú tư {t.injectedCount}/{t.requiredInjections}",
                4 => "Đang nghiên cứu",
                5 => "Nghiên cứu hoàn thành",
                _ => ""
            };
        }

        static string CostText(TechnologyView t)
        {
            var costs = t.resources ?? Array.Empty<TechnologyResourceCost>();
            return string.Join("  ", costs.Select(x => ResourceName(x.type) + " " + x.value));
        }

        static string ResourceName(string type) => type switch
        {
            "copper" => "Bạc",
            "wood" => "Gỗ",
            "food" => "Lương",
            "iron" => "Sắt",
            _ => type
        };

        static float ResearchRatio(TechnologyView t)
        {
            if (t.status != 4 || t.researchDurationMs <= 0 || !TryTime(t.researchCompleteAt, out var end)) return 0f;
            var remain = Math.Max(0d, (end - DateTimeOffset.UtcNow).TotalMilliseconds);
            return Mathf.Clamp01(1f - (float)(remain / t.researchDurationMs));
        }

        static string RemainingText(TechnologyView t)
        {
            if (!TryTime(t.researchCompleteAt, out var end)) return "";
            var s = Math.Max(0, (int)Math.Ceiling((end - DateTimeOffset.UtcNow).TotalSeconds));
            if (s >= 3600) return $"{s / 3600}:{(s / 60) % 60:00}:{s % 60:00}";
            return $"{s / 60}:{s % 60:00}";
        }

        static bool TryTime(string value, out DateTimeOffset result) =>
            DateTimeOffset.TryParse(value, out result);

        void Close() => Destroy(gameObject);
        void SetStatus(string value) { _status?.Invoke(value); }
    }
}
