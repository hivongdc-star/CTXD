using System;
using System.Linq;
using System.Threading.Tasks;
using CTXD.Client.Features.FirstPlayable;
using CTXD.Client.Networking;
using UnityEngine;
using UnityEngine.UI;

namespace CTXD.Client.Features.Social
{
    public sealed class KfzbPanel : MonoBehaviour
    {
        ApiClient _api;
        Action<string> _status;
        RectTransform _host;
        RectTransform _window;
        KfzbView _view;
        KfzbRewardView _reward;
        GeneralTreasureListResponse _treasures;
        GeneralRosterResponse _roster;
        int[] _selected = Array.Empty<int>();
        bool _claiming;
        bool _feastOpen;

        public static KfzbPanel Open(RectTransform host, ApiClient api, Action<string> status)
        {
            var go = new GameObject("KfzbPanel");
            go.transform.SetParent(host, false);
            var panel = go.AddComponent<KfzbPanel>();
            panel._api = api;
            panel._status = status;
            panel._host = host;
            panel.Build();
            _ = panel.Refresh();
            return panel;
        }

        void Build()
        {
            var blocker = LegacyUiFactory.Panel(transform, "KfzbBlocker", Vector2.zero, Vector2.one, new Color(0, 0, 0, .84f));
            _window = LegacyUiFactory.PixelPanel(blocker, "KfzbWindow", 220, 55, 840, 640, new Color(.05f, .025f, .012f, 1));
        }

        async Task Refresh()
        {
            try
            {
                _view = await _api.GetKfzbAsync();
                _reward = await _api.GetKfzbRewardAsync();
                _treasures = await _api.GetGeneralTreasuresAsync();
                _roster = await _api.GetGeneralsAsync();
                _feastOpen = false;
                try
                {
                    await _api.GetKfzbFeastCardsAsync();
                    _feastOpen = true;
                }
                catch (ApiException ex) when (ex.Code == "KFZB_FEAST_CLOSED" || ex.Code == "KFZB_INACTIVE") { }
                catch (Exception ex) { _status(ex.Message); }
                Draw();
            }
            catch (Exception e)
            {
                _status(e.Message);
                Destroy(gameObject);
            }
        }

        void Draw()
        {
            LegacyUiFactory.DestroyChildren(_window);
            LegacyUiFactory.PixelLabel(_window, "KFZB - SEASON " + _view.seasonNo, 22, TextAnchor.MiddleCenter, new Color(1, .75f, .25f), 240, 12, 360, 35);
            LegacyUiFactory.PixelButton(_window, "Close", 750, 16, 65, 27, () => Destroy(gameObject));
            LegacyUiFactory.PixelLabel(_window,
                "State " + _view.globalState + "   W " + _view.wins + " / L " + _view.losses + (_view.eliminated ? "   ELIMINATED" : ""),
                16, TextAnchor.MiddleCenter, _view.eliminated ? new Color(1, .35f, .25f) : Color.white, 120, 55, 600, 30);

            if (_feastOpen)
                LegacyUiFactory.PixelButton(_window, "Thịnh Yến", 450, 105, 270, 36, () => KfzbFeastPanel.Open(_host, _api, _status));
            else if (!_view.signed && _view.globalState == 20)
                LegacyUiFactory.PixelButton(_window, "Sign up with all generals", 450, 105, 270, 36, () => Signup());

            var y = 105;
            foreach (var general in (_roster?.military ?? Array.Empty<GeneralView>()).Take(9))
            {
                var id = general.id;
                var selected = _selected.Contains(id);
                LegacyUiFactory.PixelButton(_window, (selected ? "* " : "") + general.name + " Lv" + general.level, 45, y, 320, 30, () =>
                {
                    _selected = selected ? _selected.Where(x => x != id).ToArray() : _selected.Append(id).ToArray();
                    Draw();
                });
                y += 37;
            }
            if (_view.signed && !_view.eliminated)
                LegacyUiFactory.PixelButton(_window, "Sync selected formation", 450, 150, 270, 34, () => Sync());
            if (_view.match != null)
            {
                var match = _view.match;
                LegacyUiFactory.PixelLabel(_window, "Phase " + match.phase + "  Layer " + match.layer + "  Round " + match.round, 17, TextAnchor.MiddleCenter, Color.white, 430, 220, 330, 30);
                LegacyUiFactory.PixelLabel(_window, "Opponent " + match.opponentPlayerId, 15, TextAnchor.MiddleCenter, Color.gray, 430, 255, 330, 28);
                if (match.battleId > 0 && match.state == 1)
                {
                    var battle = match.battleId;
                    LegacyUiFactory.PixelButton(_window, "Resume battle", 480, 300, 230, 38, async () =>
                    {
                        try
                        {
                            await _api.GetBattleAsync(battle);
                            _status("KFZB battle " + battle + " ready");
                            await Refresh();
                        }
                        catch (Exception e) { _status(e.Message); }
                    });
                }
                else if (match.state == 2)
                    LegacyUiFactory.PixelLabel(_window, "Winner " + match.winnerPlayerId, 17, TextAnchor.MiddleCenter, new Color(1, .8f, .3f), 470, 300, 250, 35);
            }

            DrawReward();
        }

        void DrawReward()
        {
            if (_reward == null) return;

            LegacyUiFactory.PixelPanel(_window, "RewardBox", 395, 350, 410, 255, new Color(.08f, .04f, .015f, .96f));
            LegacyUiFactory.PixelLabel(_window, "争霸赛奖励", 18, TextAnchor.MiddleCenter, new Color(1, .75f, .25f), 505, 360, 190, 28);

            if (!string.IsNullOrEmpty(_reward.title))
                LegacyUiFactory.PixelLabel(_window, _reward.title, 18, TextAnchor.MiddleCenter, new Color(1f, .82f, .36f), 430, 392, 340, 30);

            var rewardInfo = _reward.rewardInfo ?? Array.Empty<int>();
            var progress = string.Join("  ", rewardInfo.Select((value, index) => (index < _reward.doneNum ? "✓" : "·") + value));
            LegacyUiFactory.PixelLabel(_window, "点券  " + progress, 14, TextAnchor.MiddleLeft, Color.white, 415, 427, 370, 42);
            LegacyUiFactory.PixelLabel(_window,
                "进度 " + _reward.doneNum + "/" + rewardInfo.Length + "   总计 " + _reward.totalTickets + "   待领取 " + _reward.pendingTickets,
                14, TextAnchor.MiddleLeft, Color.gray, 415, 470, 370, 25);

            if (_reward.eliminated)
            {
                var layer = _reward.eliminatedLayer > 0 ? ("  Layer " + _reward.eliminatedLayer) : string.Empty;
                LegacyUiFactory.PixelLabel(_window, "本轮已被淘汰" + layer, 15, TextAnchor.MiddleLeft, new Color(1, .42f, .28f), 415, 500, 240, 26);
            }
            else if (_reward.eventEnded)
                LegacyUiFactory.PixelLabel(_window, "争霸赛已结束", 15, TextAnchor.MiddleLeft, Color.gray, 415, 500, 240, 26);

            var claim = LegacyUiFactory.PixelButton(_window, "领取奖励", 655, 500, 125, 34, () => ClaimReward());
            claim.interactable = _reward.pendingTickets > 0 && !_claiming;

            var treasureCount = _treasures?.items?.Length ?? 0;
            LegacyUiFactory.PixelButton(_window, "武将宝物 (" + treasureCount + ")", 415, 548, 175, 38,
                () => GeneralTreasurePanel.Open(_window.parent, _api, _status));
            LegacyUiFactory.PixelLabel(_window, "领取后由服务器刷新点券；客户端不本地累加", 12, TextAnchor.MiddleLeft, Color.gray, 600, 548, 185, 38);
        }

        async void ClaimReward()
        {
            if (_claiming || _reward == null || _reward.pendingTickets <= 0) return;
            _claiming = true;
            try
            {
                var result = await _api.ClaimKfzbRewardAsync();
                _status("争霸赛获得点券：" + result.ticketsGranted);
                await Refresh();
            }
            catch (Exception e) { _status(e.Message); }
            finally { _claiming = false; }
        }

        async void Signup()
        {
            try
            {
                _view = await _api.SignupKfzbAsync();
                _status("KFZB signup complete");
                await Refresh();
            }
            catch (Exception e) { _status(e.Message); }
        }

        async void Sync()
        {
            try
            {
                if (_selected.Length == 0) throw new Exception("Select at least one general");
                _view = await _api.SyncKfzbAsync(_selected);
                _status("KFZB formation synchronized");
                await Refresh();
            }
            catch (Exception e) { _status(e.Message); }
        }
    }
}
