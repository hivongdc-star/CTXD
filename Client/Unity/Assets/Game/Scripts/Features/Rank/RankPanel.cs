using System;
using System.Threading.Tasks;
using CTXD.Client.Features.FirstPlayable;
using CTXD.Client.Networking;
using UnityEngine;
using UnityEngine.UI;

namespace CTXD.Client.Features.Rank
{
    public sealed class RankPanel : MonoBehaviour
    {
        const int PageSize = 15;
        static RankPanel _open;
        ApiClient _api;
        RankApi _rank;
        Action<string> _status;
        RectTransform _window;
        LevelRankView _view;
        int _page;
        bool _busy;

        public static RankPanel Open(RectTransform host, ApiClient api, Action<string> status)
        {
            if (_open != null) Destroy(_open.gameObject);
            var go = new GameObject("RankPanel");
            go.transform.SetParent(host, false);
            var panel = go.AddComponent<RankPanel>();
            panel._api = api;
            panel._rank = new RankApi(api);
            panel._status = status;
            _open = panel;
            panel.Build();
            _ = panel.RefreshAsync();
            return panel;
        }

        void OnDestroy() { if (_open == this) _open = null; }

        void Build()
        {
            var blocker = LegacyUiFactory.Panel(transform, "RankBlocker", Vector2.zero, Vector2.one, new Color(0, 0, 0, .82f));
            _window = LegacyUiFactory.PixelPanel(blocker, "RankWindow", 250, 65, 780, 620, new Color(.045f, .032f, .018f, .98f));
            LegacyUiFactory.PixelLabel(_window, "BẢNG XẾP HẠNG CẤP ĐỘ", 23, TextAnchor.MiddleCenter, new Color(1f, .82f, .35f), 160, 12, 460, 36);
            LegacyUiFactory.PixelButton(_window, "Đóng", 690, 14, 72, 28, () => Destroy(gameObject));
            LegacyUiFactory.PixelLabel(_window, "Đang tải bảng xếp hạng...", 16, TextAnchor.MiddleCenter, Color.white, 180, 280, 420, 34);
        }

        async Task RefreshAsync()
        {
            if (_busy) return;
            _busy = true;
            try
            {
                _view = await _rank.GetLevelRankAsync();
                var count = _view?.rankList?.Length ?? 0;
                var maxPage = Math.Max(0, (count - 1) / PageSize);
                _page = Math.Min(_page, maxPage);
                Draw();
            }
            catch (Exception ex) { _status(ex.Message); }
            finally { _busy = false; }
        }

        void Draw()
        {
            if (_window == null) return;
            LegacyUiFactory.DestroyChildren(_window);
            LegacyUiFactory.PixelLabel(_window, "BẢNG XẾP HẠNG CẤP ĐỘ", 23, TextAnchor.MiddleCenter, new Color(1f, .82f, .35f), 160, 12, 460, 36);
            LegacyUiFactory.PixelButton(_window, "Làm mới", 596, 14, 84, 28, () => _ = RefreshAsync());
            LegacyUiFactory.PixelButton(_window, "Đóng", 690, 14, 72, 28, () => Destroy(gameObject));

            LegacyUiFactory.PixelLabel(_window, "HẠNG", 15, TextAnchor.MiddleCenter, new Color(.9f, .82f, .58f), 28, 62, 90, 28);
            LegacyUiFactory.PixelLabel(_window, "NGƯỜI CHƠI", 15, TextAnchor.MiddleLeft, new Color(.9f, .82f, .58f), 142, 62, 430, 28);
            LegacyUiFactory.PixelLabel(_window, "CẤP", 15, TextAnchor.MiddleCenter, new Color(.9f, .82f, .58f), 620, 62, 100, 28);

            var items = _view?.rankList ?? Array.Empty<LevelRankEntry>();
            if (items.Length == 0)
            {
                LegacyUiFactory.PixelLabel(_window, "Chưa có dữ liệu xếp hạng.", 16, TextAnchor.MiddleCenter, Color.gray, 160, 270, 460, 36);
            }
            else
            {
                var start = _page * PageSize;
                var end = Math.Min(items.Length, start + PageSize);
                for (var i = start; i < end; i++)
                {
                    var row = i - start;
                    var y = 98 + row * 29;
                    var entry = items[i];
                    var name = string.IsNullOrWhiteSpace(entry.playerName) ? "Player " + entry.playerId : entry.playerName;
                    LegacyUiFactory.PixelLabel(_window, (i + 1).ToString(), 15, TextAnchor.MiddleCenter, Color.white, 28, y, 90, 25);
                    LegacyUiFactory.PixelLabel(_window, name, 15, TextAnchor.MiddleLeft, Color.white, 142, y, 430, 25);
                    LegacyUiFactory.PixelLabel(_window, entry.playerLv.ToString(), 15, TextAnchor.MiddleCenter, Color.white, 620, y, 100, 25);
                }
            }

            var pages = Math.Max(1, (items.Length + PageSize - 1) / PageSize);
            LegacyUiFactory.PixelLabel(_window, "Trang " + (_page + 1) + "/" + pages, 14, TextAnchor.MiddleCenter, new Color(.8f, .76f, .66f), 315, 552, 150, 30);
            if (_page > 0) LegacyUiFactory.PixelButton(_window, "Trước", 205, 550, 100, 30, () => { _page--; Draw(); });
            if ((_page + 1) * PageSize < items.Length) LegacyUiFactory.PixelButton(_window, "Sau", 475, 550, 100, 30, () => { _page++; Draw(); });
        }
    }
}
