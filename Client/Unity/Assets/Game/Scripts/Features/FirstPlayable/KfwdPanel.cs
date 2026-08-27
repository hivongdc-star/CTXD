using System;
using System.Threading.Tasks;
using CTXD.Client.Networking;
using UnityEngine;
using UnityEngine.UI;

namespace CTXD.Client.Features.FirstPlayable
{
    public sealed class KfwdPanel : MonoBehaviour
    {
        const int RankingRows = 10;
        static KfwdPanel _open;

        ApiClient _api;
        Action<string> _status;
        RectTransform _window;
        KfwdView _core;
        KfwdRanking _ranking;
        KfwdRewardView _reward;
        bool _busy;

        public static KfwdPanel Open(RectTransform host, ApiClient api, Action<string> status)
        {
            if (_open != null) Destroy(_open.gameObject);
            var go = new GameObject("KfwdPanel");
            go.transform.SetParent(host, false);
            var panel = go.AddComponent<KfwdPanel>();
            panel._api = api;
            panel._status = status;
            _open = panel;
            panel.Build();
            _ = panel.RefreshAsync();
            return panel;
        }

        void OnDestroy()
        {
            if (_open == this) _open = null;
        }

        void Build()
        {
            var blocker = LegacyUiFactory.Panel(transform, "KfwdBlocker", Vector2.zero, Vector2.one, new Color(0, 0, 0, .82f));
            _window = LegacyUiFactory.PixelPanel(blocker, "KfwdWindow", 140, 48, 1000, 660, new Color(.045f, .032f, .018f, .98f));
            LegacyUiFactory.PixelLabel(_window, "Xếp hạng", 23, TextAnchor.MiddleCenter, new Color(1f, .82f, .35f), 300, 16, 400, 36);
            LegacyUiFactory.PixelLabel(_window, "Cập Nhật", 15, TextAnchor.MiddleCenter, Color.white, 380, 305, 240, 30);
        }

        async Task RefreshAsync()
        {
            if (_busy) return;
            _busy = true;
            try
            {
                // Re-open/refresh always reads fresh server state. No local ranking/reward cache is authoritative.
                _core = await _api.GetKfwdAsync();
                _ranking = await _api.GetKfwdRankingAsync();
                _reward = _core != null && _core.signed ? await _api.GetKfwdRewardsAsync() : null;
                Draw();
            }
            catch (Exception ex)
            {
                _status?.Invoke(ex.Message);
            }
            finally
            {
                _busy = false;
            }
        }

        void Draw()
        {
            if (_window == null) return;
            LegacyUiFactory.DestroyChildren(_window);

            LegacyUiFactory.PixelLabel(_window, "Xếp hạng", 23, TextAnchor.MiddleCenter, new Color(1f, .82f, .35f), 300, 12, 400, 36);
            LegacyUiFactory.PixelButton(_window, "Cập Nhật", 790, 16, 92, 28, () => _ = RefreshAsync());
            LegacyUiFactory.PixelButton(_window, "Trở về", 892, 16, 82, 28, () => Destroy(gameObject));

            var round = _core != null && _core.match != null ? _core.match.round : 0;
            var lifecycle = _core == null ? "" : (_core.globalState >= 70 ? "Đã kết thúc" : "S" + _core.globalState);
            LegacyUiFactory.PixelLabel(_window, "R" + round + "   " + lifecycle, 14, TextAnchor.MiddleLeft, new Color(.86f, .78f, .58f), 24, 18, 240, 26);

            DrawRanking();
            DrawDayRewards();
            DrawTreasure();
        }

        void DrawRanking()
        {
            LegacyUiFactory.PixelLabel(_window, "Hạng", 14, TextAnchor.MiddleCenter, new Color(.9f, .82f, .58f), 24, 68, 66, 24);
            LegacyUiFactory.PixelLabel(_window, "Nhân Vật", 14, TextAnchor.MiddleLeft, new Color(.9f, .82f, .58f), 105, 68, 210, 24);
            LegacyUiFactory.PixelLabel(_window, "Tổng tích điểm", 14, TextAnchor.MiddleCenter, new Color(.9f, .82f, .58f), 318, 68, 130, 24);

            var items = _ranking != null && _ranking.items != null ? _ranking.items : Array.Empty<KfwdRankEntry>();
            var count = Math.Min(items.Length, RankingRows);
            for (var i = 0; i < count; i++)
            {
                // Deliberately preserve the server ordering. Do not sort/re-rank on the client.
                var entry = items[i];
                var y = 98 + i * 30;
                LegacyUiFactory.PixelLabel(_window, entry.rank.ToString(), 14, TextAnchor.MiddleCenter, Color.white, 24, y, 66, 24);
                LegacyUiFactory.PixelLabel(_window, string.IsNullOrEmpty(entry.name) ? entry.playerId.ToString() : entry.name, 14, TextAnchor.MiddleLeft, Color.white, 105, y, 210, 24);
                LegacyUiFactory.PixelLabel(_window, entry.score.ToString(), 14, TextAnchor.MiddleCenter, Color.white, 318, y, 130, 24);
            }
        }

        void DrawDayRewards()
        {
            const float x = 500;
            LegacyUiFactory.PixelLabel(_window, "Nhận thưởng", 18, TextAnchor.MiddleCenter, new Color(1f, .82f, .35f), x, 68, 450, 30);

            if (_reward == null || _reward.days == null)
            {
                LegacyUiFactory.PixelLabel(_window, "—", 18, TextAnchor.MiddleCenter, Color.gray, x, 120, 450, 30);
                return;
            }

            for (var i = 0; i < _reward.days.Length; i++)
            {
                // Server day array is authoritative; preserve its order and never calculate a local rank bucket.
                var day = _reward.days[i];
                var y = 112 + i * 48;
                var rank = day.rank > 0 ? day.rank.ToString() : "—";
                var issued = day.granted ? "Đã nhận" : "";
                LegacyUiFactory.PixelLabel(_window, "D" + day.day, 15, TextAnchor.MiddleCenter, new Color(.9f, .82f, .58f), x + 10, y, 46, 28);
                LegacyUiFactory.PixelLabel(_window, "Hạng " + rank, 15, TextAnchor.MiddleLeft, Color.white, x + 68, y, 110, 28);
                LegacyUiFactory.PixelLabel(_window, day.tickets > 0 ? "+" + day.tickets : "—", 15, TextAnchor.MiddleCenter, new Color(1f, .82f, .35f), x + 185, y, 90, 28);
                LegacyUiFactory.PixelLabel(_window, issued, 14, TextAnchor.MiddleCenter, new Color(.6f, .85f, .55f), x + 282, y, 120, 28);
            }

            if (_core != null)
                LegacyUiFactory.PixelLabel(_window, _core.tickets.ToString(), 17, TextAnchor.MiddleCenter, new Color(1f, .82f, .35f), x + 330, 274, 72, 30);
        }

        void DrawTreasure()
        {
            const float x = 500;
            const float y = 330;
            LegacyUiFactory.PixelLabel(_window, "Kết thúc đấu xếp hạng nhận bảo vật", 18, TextAnchor.MiddleCenter, new Color(1f, .82f, .35f), x, y, 450, 32);

            if (_reward == null) return;
            var finalRank = FinalRank();

            if (_reward.treasure != null)
            {
                DrawGrantedTreasure(_reward.treasure, x, y + 44);
                LegacyUiFactory.PixelLabel(_window, "Đã nhận", 14, TextAnchor.MiddleCenter, new Color(.6f, .85f, .55f), x + 300, y + 174, 120, 28);
                return;
            }

            LegacyUiFactory.PixelLabel(_window, "Hạng: " + (finalRank > 0 ? finalRank.ToString() : "—"), 15, TextAnchor.MiddleLeft, Color.white, x + 30, y + 55, 200, 28);

            // The server owns eligibility and the state-70 window. Never infer a rank bucket or send a fake claim after state 70.
            if (_reward.treasureClaimAvailable && !_reward.treasureClaimed && _reward.globalState < 70)
                LegacyUiFactory.PixelButton(_window, "Nhận thưởng", x + 270, y + 50, 130, 32, () => _ = ClaimTreasureAsync());
            else if (_reward.treasureClaimed)
                LegacyUiFactory.PixelLabel(_window, "Đã nhận", 14, TextAnchor.MiddleCenter, new Color(.6f, .85f, .55f), x + 270, y + 50, 130, 32);
        }

        void DrawGrantedTreasure(KfwdGeneralTreasureView treasure, float x, float y)
        {
            var iconPath = TreasureIconPath(treasure.treasureId);
            if (!string.IsNullOrEmpty(iconPath))
                DrawLegacyTexture(iconPath, x + 26, y + 10, 92, 92);

            LegacyUiFactory.PixelLabel(_window, string.IsNullOrEmpty(treasure.name) ? treasure.treasureId.ToString() : treasure.name,
                18, TextAnchor.MiddleLeft, new Color(1f, .82f, .35f), x + 140, y + 8, 260, 30);
            LegacyUiFactory.PixelLabel(_window, "Q" + treasure.quality + "   LEA " + treasure.leadership + "   STR " + treasure.strength,
                15, TextAnchor.MiddleLeft, Color.white, x + 140, y + 48, 280, 28);
            LegacyUiFactory.PixelLabel(_window, "#" + treasure.instanceId, 13, TextAnchor.MiddleLeft, new Color(.72f, .68f, .58f), x + 140, y + 78, 240, 24);
        }

        async Task ClaimTreasureAsync()
        {
            if (_busy || _reward == null || !_reward.treasureClaimAvailable || _reward.treasureClaimed || _reward.globalState >= 70)
                return;

            _busy = true;
            try
            {
                var granted = await _api.ClaimKfwdTreasureAsync();
                // Surface the exact server-returned treasure immediately via status, then refresh authoritative state.
                if (granted != null)
                    _status?.Invoke((granted.name ?? granted.treasureId.ToString()) + "  Q" + granted.quality + "  LEA " + granted.leadership + "  STR " + granted.strength);
                _core = await _api.GetKfwdAsync();
                _ranking = await _api.GetKfwdRankingAsync();
                _reward = await _api.GetKfwdRewardsAsync();
                Draw();
            }
            catch (Exception ex)
            {
                _status?.Invoke(ex.Message);
            }
            finally
            {
                _busy = false;
            }
        }

        int FinalRank()
        {
            if (_reward == null || _reward.days == null) return 0;
            for (var i = 0; i < _reward.days.Length; i++)
                if (_reward.days[i] != null && _reward.days[i].day == 3) return _reward.days[i].rank;
            return 0;
        }

        static string TreasureIconPath(int treasureId)
        {
            switch (treasureId)
            {
                case 4: return "LegacyVisual/KFWD/heshibi";
                case 5: return "LegacyVisual/KFWD/yemingzhu";
                case 6: return "LegacyVisual/KFWD/zishanhu";
                default: return null;
            }
        }

        void DrawLegacyTexture(string resourcePath, float x, float y, float width, float height)
        {
            var texture = Resources.Load<Texture2D>(resourcePath);
            if (texture == null) return;
            var sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(.5f, .5f), 100f);
            var image = LegacyUiFactory.PixelImage(_window, "", x, y, width, height, true);
            image.sprite = sprite;
        }
    }
}
