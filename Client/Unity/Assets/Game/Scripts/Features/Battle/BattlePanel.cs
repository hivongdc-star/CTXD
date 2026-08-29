using System;
using System.Linq;
using System.Threading.Tasks;
using CTXD.Client.Features.FirstPlayable;
using CTXD.Client.Networking;
using UnityEngine;
using UnityEngine.UI;

namespace CTXD.Client.Features.Battle
{
    public sealed class BattlePanel : MonoBehaviour
    {
        static BattlePanel _open;
        ApiClient _api;
        Action<string> _status;
        RectTransform _hud;
        RectTransform _stage;
        BattleLegacyPresentation _presentation;
        long _battleId;
        BattleView _battle;
        bool _busy;
        int? _terrain;

        public static BattlePanel Open(RectTransform host, ApiClient api, Action<string> status, long battleId)
        {
            var go = new GameObject("BattlePanel");
            go.transform.SetParent(host, false);
            var panel = go.AddComponent<BattlePanel>();
            panel._api = api;
            panel._status = status;
            panel._battleId = battleId;
            _open = panel;
            panel.Build();
            _ = panel.RefreshAsync();
            return panel;
        }

        public static void RefreshOpenFromPush()
        {
            if (_open != null && !_open._busy) _ = _open.RefreshAsync();
        }

        void OnDestroy()
        {
            if (_open == this) _open = null;
        }

        void Build()
        {
            var blocker = LegacyUiFactory.Panel(transform, "BattleBlocker", Vector2.zero, Vector2.one, Color.black);
            _stage = StretchRect(blocker, "LegacyBattleStage");
            _hud = StretchRect(blocker, "BattleFunctionalHud");
            _presentation = gameObject.AddComponent<BattleLegacyPresentation>();
            _presentation.Initialize(_stage);
        }

        async Task RefreshAsync()
        {
            try
            {
                _battle = await _api.GetBattleAsync(_battleId);
                if (!_terrain.HasValue) _terrain = await ResolveLegacyTerrainAsync();
                Draw();
            }
            catch (Exception ex)
            {
                _status(ex.Message);
            }
        }

        async Task<int?> ResolveLegacyTerrainAsync()
        {
            try
            {
                var world = await _api.GetWorldAsync();
                var handoff = (world.battles ?? Array.Empty<WorldBattleHandoffView>()).FirstOrDefault(x => x.id == _battleId);
                if (handoff != null)
                {
                    if (handoff.battleType == 4) return 6;
                    if (handoff.battleType == 6 || handoff.battleType == 7) return 9;
                }

                var city = (world.cities ?? Array.Empty<WorldCityView>()).FirstOrDefault(x => x.id == _battle.cityId);
                return city != null && city.terrain > 0 ? city.terrain : (int?)null;
            }
            catch
            {
                // Background identity is optional presentation context. If it cannot be resolved
                // from an existing API, do not guess a terrain/background.
                return null;
            }
        }

        void Draw()
        {
            if (_hud == null || _battle == null) return;
            LegacyUiFactory.DestroyChildren(_hud);
            _presentation.SetSnapshot(_battle, _terrain);

            // Functional controls are intentionally kept separate from the reconstructed stage.
            // They preserve the existing API surface while War.swf HUD symbols are migrated.
            LegacyUiFactory.PixelLabel(_hud, "CHIẾN TRƯỜNG · Thành #" + _battle.cityId, 23, TextAnchor.MiddleCenter,
                new Color(1f, .78f, .25f), 410, 12, 460, 38);
            LegacyUiFactory.PixelButton(_hud, "Đóng", 1180, 12, 72, 28, () => { if (!_busy) Destroy(gameObject); });

            DrawFunctionalRoster(_battle.attackers ?? Array.Empty<BattleUnitView>(), true);
            DrawFunctionalRoster(_battle.defenders ?? Array.Empty<BattleUnitView>(), false);

            var state = _battle.status == 0 ? "Round " + _battle.roundNo : _battle.winnerSide == 1 ? "CÔNG THẮNG" : "THỦ THẮNG";
            LegacyUiFactory.PixelLabel(_hud, state, 20, TextAnchor.MiddleCenter, Color.white, 535, 620, 210, 36);

            if (_battle.status == 0)
            {
                LegacyUiFactory.PixelButton(_hud, "Tiến hành round", 535, 665, 210, 45, async () => await AdvanceAsync());
                LegacyUiFactory.PixelButton(_hud, "Deploy Team", 1035, 665, 150, 35, async () => await DeployTeamAsync());

                var own = (_battle.attackers ?? Array.Empty<BattleUnitView>()).FirstOrDefault(x => !x.dead && !x.isNpc);
                if (own != null)
                {
                    if (own.tacticAvailable)
                        LegacyUiFactory.PixelButton(_hud, "Tactic #" + own.tacticId, 470, 635, 130, 28, async () => await ChooseAsync(own, 1, 0));
                    var choices = own.allowedStrategyIds ?? Array.Empty<int>();
                    for (var i = 0; i < Math.Min(3, choices.Length); i++)
                    {
                        var strategy = choices[i];
                        LegacyUiFactory.PixelButton(_hud, "Strategy " + strategy, 610 + i * 135, 635, 128, 28,
                            async () => await ChooseAsync(own, 2, strategy));
                    }
                }
            }

            var last = (_battle.rounds ?? Array.Empty<BattleRoundView>()).LastOrDefault();
            if (last != null)
                LegacyUiFactory.PixelLabel(_hud, "Sát thương công: " + last.attackerDamage + " · thủ: " + last.defenderDamage,
                    15, TextAnchor.MiddleCenter, new Color(.9f, .8f, .65f), 445, 720, 390, 25);
        }

        void DrawFunctionalRoster(BattleUnitView[] units, bool attacker)
        {
            var x = attacker ? 18 : 1010;
            var title = attacker ? "PHE CÔNG" : "PHE THỦ";
            LegacyUiFactory.PixelLabel(_hud, title, 17, TextAnchor.MiddleCenter,
                attacker ? new Color(1f, .4f, .3f) : new Color(.3f, .65f, 1f), x, 62, 250, 28);
            for (var i = 0; i < Math.Min(6, units.Length); i++)
            {
                var unit = units[i];
                var y = 96 + i * 43;
                LegacyUiFactory.PixelLabel(_hud, unit.name + (unit.isNpc ? " [NPC]" : ""), 14, TextAnchor.MiddleLeft,
                    unit.dead ? Color.gray : Color.white, x, y, 250, 20);
            }
        }

        async Task AdvanceAsync()
        {
            if (_busy) return;
            _busy = true;
            try
            {
                var before = _battle;
                var result = await _api.AdvanceBattleAsync(_battleId);
                _battle = result;
                var round = (result.rounds ?? Array.Empty<BattleRoundView>())
                    .LastOrDefault(x => before == null || x.roundNo > before.roundNo);
                if (round != null)
                    await _presentation.PlayRoundAsync(before, result, round, _terrain);
                Draw();
                _status(_battle.status == 0 ? "Server đã xử lý round." : "Battle đã kết thúc; World đang cập nhật.");
            }
            catch (Exception ex)
            {
                _status(ex.Message);
            }
            finally
            {
                _busy = false;
            }
        }

        async Task ChooseAsync(BattleUnitView unit, int action, int strategy)
        {
            if (_busy) return;
            _busy = true;
            try
            {
                _battle = await _api.ChooseBattleActionAsync(_battleId, unit.generalId, action, strategy);
                Draw();
                _status(action == 1 ? "Server đã chọn tactic." : "Server đã chọn strategy.");
            }
            catch (Exception ex)
            {
                _status(ex.Message);
            }
            finally
            {
                _busy = false;
            }
        }

        async Task DeployTeamAsync()
        {
            if (_busy) return;
            _busy = true;
            try
            {
                var teams = await _api.GetTeamsAsync();
                var team = (teams.items ?? Array.Empty<TeamView>()).FirstOrDefault(x => x.isOwner);
                if (team == null) throw new Exception("No owned team is ready.");
                var result = await _api.DeployTeamAsync(team.id, _battleId, team.members.Length, 0);
                _battle = result.battle;
                Draw();
                _status("Deployed " + result.deployed + " team generals.");
            }
            catch (Exception ex)
            {
                _status(ex.Message);
            }
            finally
            {
                _busy = false;
            }
        }

        static RectTransform StretchRect(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            return rt;
        }
    }
}
