using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CTXD.Client.Features.Battle;
using CTXD.Client.Networking;
using UnityEngine;
using UnityEngine.UI;

namespace CTXD.Client.Features.Social
{
    public sealed class KfzbPanel : MonoBehaviour
    {
        readonly struct BracketPoint
        {
            public readonly float x;
            public readonly float y;
            public readonly int legacyMatchId;
            public readonly int side;
            public BracketPoint(float x, float y, int legacyMatchId, int side)
            {
                this.x = x; this.y = y; this.legacyMatchId = legacyMatchId; this.side = side;
            }
        }

        static readonly BracketPoint[] LegacyBracket =
        {
            new(14,82,8,1),new(14,137,8,2),new(14,209,9,1),new(14,264,9,2),
            new(14,336,10,1),new(14,391,10,2),new(14,460,11,1),new(14,515,11,2),
            new(184,106,4,1),new(184,234,4,2),new(184,357,5,1),new(184,480,5,2),
            new(307,171,2,1),new(307,425,2,2),new(380,286,1,1),
            new(933,82,12,1),new(933,137,12,2),new(933,209,13,1),new(933,264,13,2),
            new(933,336,14,1),new(933,391,14,2),new(933,460,15,1),new(933,515,15,2),
            new(758,106,6,1),new(758,234,6,2),new(758,357,7,1),new(758,480,7,2),
            new(635,171,3,1),new(635,425,3,2),new(552,286,1,2)
        };

        const float BaseX = 140f; // KfZbWarVS 1000-wide centered on 1280.
        const float BaseY = 84f;  // KfZbWarVS 600-high centered on 768.

        ApiClient _api;
        Action<string> _status;
        RectTransform _host;
        RectTransform _root;
        KfzbView _view;
        KfzbTable _table;
        KfzbRewardView _reward;
        GeneralTreasureListResponse _treasures;
        GeneralRosterResponse _roster;
        readonly HashSet<int> _selectedGenerals = new();
        long _selectedMatchId;
        bool _feastOpen;
        bool _busy;
        bool _showFormation;
        bool _showReward;

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
            _root = CrossServerLegacyVisuals.Root(transform, "KfZbWarVS", new Color(0, 0, 0, 1f));
        }

        async Task Refresh()
        {
            if (_busy) return;
            _busy = true;
            try
            {
                _view = await _api.GetKfzbAsync();
                _table = await _api.GetKfzbTableAsync();
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

                if (_selectedMatchId == 0 && _view?.match != null)
                    _selectedMatchId = _view.match.id;
                Draw();
            }
            catch (Exception ex)
            {
                _status(ex.Message);
                Destroy(gameObject);
            }
            finally { _busy = false; }
        }

        void Draw()
        {
            if (_root == null || _view == null) return;
            CrossServerLegacyVisuals.DestroyChildren(_root);

            CrossServerLegacyVisuals.ResourceImage(_root, "LegacyVisual/KFZB/bg1", 0, 0, 1280, 768, false);
            CrossServerLegacyVisuals.AtlasImage(_root, "KFZB", "vs.title", 515, 20, 249, 41);
            CrossServerLegacyVisuals.AtlasButton(_root, "KFZB", "vs.close.up", "vs.close.over", "vs.close.down", 1226, 14, 34, 35,
                () => Destroy(gameObject));

            DrawPhase();
            DrawBracket();
            DrawChampion();
            DrawTopCommands();

            if (!_view.signed && _view.globalState == 20) DrawSignup();
            else if (_showFormation) DrawFormation();
            else if (_showReward) DrawReward();
            else DrawSelectedMatch();
        }

        void DrawPhase()
        {
            var layer = _view.match != null ? _view.match.layer : 1;
            var index = Mathf.Clamp(layer - 1, 0, 3);
            CrossServerLegacyVisuals.AtlasImage(_root, "KFZB", "vs.phase." + index, 766, 35, 100, 28);
            CrossServerLegacyVisuals.Label(_root, "Season " + _view.seasonNo, 880, 30, 170, 28, 14, TextAnchor.MiddleLeft,
                new Color(1f, .9f, .67f), true);
            CrossServerLegacyVisuals.Label(_root,
                "W " + _view.wins + "   L " + _view.losses + (_view.eliminated ? "   Eliminated" : string.Empty),
                880, 56, 250, 24, 13, TextAnchor.MiddleLeft, _view.eliminated ? new Color(1f, .45f, .35f) : Color.white, true);
        }

        void DrawBracket()
        {
            var entries = _table?.items ?? Array.Empty<KfzbTableEntry>();
            foreach (var point in LegacyBracket)
            {
                var match = entries.FirstOrDefault(x => x.legacyMatchId == point.legacyMatchId);
                var participantId = point.side == 1 ? match?.player1Id ?? 0 : match?.player2Id ?? 0;
                var participant = point.side == 1 ? match?.player1 : match?.player2;
                var selected = match != null && match.matchId == _selectedMatchId;
                var key = HeadKey(point.x, selected);
                var sprite = CrossServerLegacyVisuals.AtlasSprite("KFZB", key);
                var width = sprite != null ? sprite.rect.width : 50f;
                var height = sprite != null ? sprite.rect.height : 51f;
                var x = BaseX + point.x;
                var y = BaseY + point.y;

                CrossServerLegacyVisuals.AtlasImage(_root, "KFZB", key, x, y, width, height);
                if (match != null)
                {
                    var captured = match.matchId;
                    CrossServerLegacyVisuals.HitArea(_root, x, y, width, height, () => SelectMatch(captured));
                }

                var label = string.IsNullOrEmpty(participant) ? (participantId > 0 ? "#" + participantId : string.Empty) : participant;
                if (!string.IsNullOrEmpty(label))
                {
                    var leftSide = point.x < 500;
                    CrossServerLegacyVisuals.Label(_root, label,
                        leftSide ? x + width + 2 : x - 116, y + height * .5f - 10,
                        114, 20, 11, leftSide ? TextAnchor.MiddleLeft : TextAnchor.MiddleRight, Color.white, true);
                }

                if (match != null && match.state == 2 && participantId > 0 && match.winnerPlayerId == participantId)
                    CrossServerLegacyVisuals.AtlasImage(_root, "KFZB", "vs.jinji", x + width - 14, y - 10, 31, 31);
            }
        }

        void DrawChampion()
        {
            var final = (_table?.items ?? Array.Empty<KfzbTableEntry>()).FirstOrDefault(x => x.legacyMatchId == 1 && x.state == 2 && x.winnerPlayerId > 0);
            if (final == null) return;
            CrossServerLegacyVisuals.AtlasImage(_root, "KFZB", "vs.headwin", 560, 107, 158, 155);
            var winnerName = final.winnerPlayerId == final.player1Id ? final.player1 : final.player2;
            if (string.IsNullOrEmpty(winnerName)) winnerName = "#" + final.winnerPlayerId;
            CrossServerLegacyVisuals.Label(_root, winnerName, 565, 226, 148, 24, 15, TextAnchor.MiddleCenter,
                new Color(1f, .82f, .3f), true);
        }

        void DrawTopCommands()
        {
            CrossServerLegacyVisuals.SkinButton(_root, "Button23", "Đội hình", 1035, 93, 105, 34, () => { _showFormation = true; _showReward = false; Draw(); });
            CrossServerLegacyVisuals.SkinButton(_root, "Button23", "Thưởng", 1145, 93, 105, 34, () => { _showReward = true; _showFormation = false; Draw(); });
            if (_feastOpen)
                CrossServerLegacyVisuals.SkinButton(_root, "Button23", "Thịnh Yến", 1090, 132, 160, 34, () => KfzbFeastPanel.Open(_host, _api, _status));
        }

        void SelectMatch(long matchId)
        {
            _selectedMatchId = matchId;
            _showFormation = false;
            _showReward = false;
            Draw();
        }

        KfzbTableEntry SelectedTableMatch()
        {
            return (_table?.items ?? Array.Empty<KfzbTableEntry>()).FirstOrDefault(x => x.matchId == _selectedMatchId);
        }

        void DrawSelectedMatch()
        {
            var match = SelectedTableMatch();
            if (match == null && _view.match != null)
            {
                match = new KfzbTableEntry
                {
                    matchId = _view.match.id,
                    phase = _view.match.phase,
                    layer = _view.match.layer,
                    round = _view.match.round,
                    legacyMatchId = _view.match.legacyMatchId,
                    player1Id = _view.competitorId,
                    player1 = "",
                    player2Id = _view.match.opponentPlayerId,
                    player2 = "",
                    state = _view.match.state,
                    winnerPlayerId = _view.match.winnerPlayerId,
                    battleId = _view.match.battleId
                };
            }
            if (match == null) return;

            if (match.state == 2) DrawResult(match);
            else DrawBattleWindow(match);
        }

        void DrawBattleWindow(KfzbTableEntry match)
        {
            var x = 188f;
            var y = 235f;
            CrossServerLegacyVisuals.AtlasImage(_root, "KFZB", "war.result.bg", x, y, 905, 331);
            CrossServerLegacyVisuals.AtlasImage(_root, "KFZB", "war.start", x + 312, y + 18, 280, 66);
            CrossServerLegacyVisuals.AtlasImage(_root, "KFZB", "war.result.head", x + 336, y + 128, 65, 66);
            CrossServerLegacyVisuals.AtlasImage(_root, "KFZB", "war.result.head", x + 502, y + 128, 65, 66);

            var left = NameOrId(match.player1, match.player1Id);
            var right = NameOrId(match.player2, match.player2Id);
            CrossServerLegacyVisuals.Label(_root, left, x + 255, y + 201, 210, 24, 14, TextAnchor.MiddleCenter, Color.white, true);
            CrossServerLegacyVisuals.Label(_root, right, x + 440, y + 201, 210, 24, 14, TextAnchor.MiddleCenter, Color.white, true);
            CrossServerLegacyVisuals.Label(_root,
                "Round " + match.round + "   Match " + match.legacyMatchId + "   State " + match.state,
                x + 257, y + 245, 390, 28, 14, TextAnchor.MiddleCenter, new Color(1f, .86f, .5f), true);

            if (match.battleId > 0 && match.state == 1)
            {
                var battleId = match.battleId;
                CrossServerLegacyVisuals.AtlasButton(_root, "KFZB", "war.vsbutton.up", "war.vsbutton.over", "war.vsbutton.down",
                    x + 394, y + 283, 118, 37, () => BattlePanel.Open(_host, _api, _status, battleId));
            }
        }

        void DrawResult(KfzbTableEntry match)
        {
            var x = 188f;
            var y = 235f;
            CrossServerLegacyVisuals.AtlasImage(_root, "KFZB", "war.result.bg", x, y, 905, 331);
            CrossServerLegacyVisuals.AtlasImage(_root, "KFZB", "war.result.head", x + 336, y + 128, 65, 66);
            CrossServerLegacyVisuals.AtlasImage(_root, "KFZB", "war.result.head", x + 502, y + 128, 65, 66);

            var leftWin = match.winnerPlayerId > 0 && match.winnerPlayerId == match.player1Id;
            var rightWin = match.winnerPlayerId > 0 && match.winnerPlayerId == match.player2Id;
            CrossServerLegacyVisuals.AtlasImage(_root, "KFZB", leftWin ? "war.result.win" : "war.result.lose", x + 310, y + 111, 44, 42);
            CrossServerLegacyVisuals.AtlasImage(_root, "KFZB", rightWin ? "war.result.win" : "war.result.lose", x + 548, y + 111, 44, 42);

            CrossServerLegacyVisuals.Label(_root, NameOrId(match.player1, match.player1Id), x + 235, y + 201, 230, 24, 14, TextAnchor.MiddleCenter, Color.white, true);
            CrossServerLegacyVisuals.Label(_root, NameOrId(match.player2, match.player2Id), x + 440, y + 201, 230, 24, 14, TextAnchor.MiddleCenter, Color.white, true);
            CrossServerLegacyVisuals.Label(_root,
                "Winner " + match.winnerPlayerId + "   Round " + match.round,
                x + 255, y + 245, 395, 28, 14, TextAnchor.MiddleCenter, new Color(1f, .82f, .3f), true);
        }

        void DrawSignup()
        {
            const float x = 319f;
            const float y = 210f;
            CrossServerLegacyVisuals.AtlasImage(_root, "KFZB", "signup.bg", x, y, 643, 348);
            CrossServerLegacyVisuals.Label(_root, "Season " + _view.seasonNo + "   Lv " + _view.minLevel + "+", x + 162, y + 250, 320, 26, 15,
                TextAnchor.MiddleCenter, Color.white, true);
            var signup = CrossServerLegacyVisuals.SkinButton(_root, "Button23", "Đăng ký", x + 285, y + 310, 118, 31, Signup);
            signup.interactable = _view.eligible && !_busy;
            CrossServerLegacyVisuals.AtlasImage(_root, "KFZB", "signup.why.up", x + 450, y + 23, 22, 20);
        }

        void DrawFormation()
        {
            const float x = 188f;
            const float y = 235f;
            CrossServerLegacyVisuals.AtlasImage(_root, "KFZB", "war.result.bg", x, y, 905, 331);
            CrossServerLegacyVisuals.Label(_root, "Đội hình KFZB", x + 325, y + 24, 255, 32, 18, TextAnchor.MiddleCenter,
                new Color(1f, .82f, .3f), true);

            var generals = _roster?.military ?? Array.Empty<GeneralView>();
            for (var i = 0; i < Math.Min(10, generals.Length); i++)
            {
                var g = generals[i];
                var col = i % 5;
                var row = i / 5;
                var gx = x + 90 + col * 145;
                var gy = y + 82 + row * 95;
                CrossServerLegacyVisuals.AtlasImage(_root, "KFZB", "war.head.small", gx, gy, 54, 55);
                if (_selectedGenerals.Contains(g.id))
                    CrossServerLegacyVisuals.AtlasImage(_root, "KFZB", "vs.jinji", gx + 36, gy - 10, 31, 31);
                var id = g.id;
                CrossServerLegacyVisuals.HitArea(_root, gx, gy, 120, 72, () => ToggleGeneral(id));
                CrossServerLegacyVisuals.Label(_root, g.name, gx - 26, gy + 56, 110, 20, 12, TextAnchor.MiddleCenter, Color.white, true);
            }

            CrossServerLegacyVisuals.SkinButton(_root, "Button23", "Đồng bộ", x + 330, y + 275, 120, 34, Sync);
            CrossServerLegacyVisuals.SkinButton(_root, "Button23", "Đóng", x + 460, y + 275, 120, 34, () => { _showFormation = false; Draw(); });
        }

        void DrawReward()
        {
            const float x = 188f;
            const float y = 235f;
            CrossServerLegacyVisuals.AtlasImage(_root, "KFZB", "war.result.bg", x, y, 905, 331);
            CrossServerLegacyVisuals.Label(_root, "争霸赛奖励", x + 328, y + 22, 250, 32, 18, TextAnchor.MiddleCenter,
                new Color(1f, .82f, .3f), true);

            if (_reward != null)
            {
                if (!string.IsNullOrEmpty(_reward.title))
                    CrossServerLegacyVisuals.Label(_root, _reward.title, x + 290, y + 64, 330, 28, 16, TextAnchor.MiddleCenter,
                        new Color(1f, .87f, .46f), true);
                var rewardInfo = _reward.rewardInfo ?? Array.Empty<int>();
                var progress = string.Join("  ", rewardInfo.Select((value, index) => (index < _reward.doneNum ? "[x]" : "[ ]") + value));
                CrossServerLegacyVisuals.Label(_root, "点券  " + progress, x + 190, y + 108, 520, 38, 14, TextAnchor.MiddleCenter, Color.white, true);
                CrossServerLegacyVisuals.Label(_root,
                    "进度 " + _reward.doneNum + "/" + rewardInfo.Length + "   总计 " + _reward.totalTickets + "   待领取 " + _reward.pendingTickets,
                    x + 200, y + 151, 500, 26, 14, TextAnchor.MiddleCenter, new Color(.82f, .8f, .72f), true);
                if (_reward.eliminated)
                    CrossServerLegacyVisuals.Label(_root, "本轮已被淘汰" + (_reward.eliminatedLayer > 0 ? "  Layer " + _reward.eliminatedLayer : string.Empty),
                        x + 260, y + 187, 385, 24, 14, TextAnchor.MiddleCenter, new Color(1f, .45f, .35f), true);
                else if (_reward.eventEnded)
                    CrossServerLegacyVisuals.Label(_root, "争霸赛已结束", x + 260, y + 187, 385, 24, 14, TextAnchor.MiddleCenter, Color.gray, true);

                var claim = CrossServerLegacyVisuals.SkinButton(_root, "Button23", "领取奖励", x + 285, y + 235, 125, 34, ClaimReward);
                claim.interactable = _reward.pendingTickets > 0 && !_busy;
            }

            var treasureCount = _treasures?.items?.Length ?? 0;
            CrossServerLegacyVisuals.SkinButton(_root, "Button23", "武将宝物 (" + treasureCount + ")", x + 425, y + 235, 160, 34,
                () => GeneralTreasurePanel.Open(_host, _api, _status));
            CrossServerLegacyVisuals.SkinButton(_root, "Button23", "Đóng", x + 600, y + 235, 105, 34, () => { _showReward = false; Draw(); });
        }

        void ToggleGeneral(int generalId)
        {
            if (!_selectedGenerals.Add(generalId)) _selectedGenerals.Remove(generalId);
            Draw();
        }

        async void Signup()
        {
            if (_busy) return;
            _busy = true;
            try
            {
                _view = await _api.SignupKfzbAsync();
                _status("KFZB signup complete");
                await RefreshAfterAction();
            }
            catch (Exception ex) { _status(ex.Message); }
            finally { _busy = false; }
        }

        async void Sync()
        {
            if (_busy) return;
            if (_selectedGenerals.Count == 0) { _status("Chọn ít nhất một võ tướng."); return; }
            _busy = true;
            try
            {
                _view = await _api.SyncKfzbAsync(_selectedGenerals.OrderBy(x => x).ToArray());
                _status("KFZB formation synchronized");
                _showFormation = false;
                await RefreshAfterAction();
            }
            catch (Exception ex) { _status(ex.Message); }
            finally { _busy = false; }
        }

        async void ClaimReward()
        {
            if (_busy || _reward == null || _reward.pendingTickets <= 0) return;
            _busy = true;
            try
            {
                var result = await _api.ClaimKfzbRewardAsync();
                _status("争霸赛获得点券：" + result.ticketsGranted);
                await RefreshAfterAction();
            }
            catch (Exception ex) { _status(ex.Message); }
            finally { _busy = false; }
        }

        async Task RefreshAfterAction()
        {
            _busy = false;
            await Refresh();
            _busy = true;
        }

        static string HeadKey(float x, bool selected)
        {
            var suffix = selected ? ".selected" : ".normal";
            if (Math.Abs(x - 14f) < 1f || Math.Abs(x - 933f) < 1f) return "vs.head1" + suffix;
            if (Math.Abs(x - 184f) < 1f || Math.Abs(x - 758f) < 1f) return "vs.head2" + suffix;
            return "vs.head3" + suffix;
        }

        static string NameOrId(string name, long id)
        {
            return !string.IsNullOrEmpty(name) ? name : id > 0 ? "#" + id : "--";
        }
    }
}
