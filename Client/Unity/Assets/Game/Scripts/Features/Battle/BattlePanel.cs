using System;
using System.Linq;
using System.Threading.Tasks;
using CTXD.Client.Networking;
using UnityEngine;

namespace CTXD.Client.Features.Battle
{
    /// <summary>
    /// Battle API coordinator. The visual target is the legacy War scene implemented by
    /// BattleLegacyPresentation; this class does not calculate combat state client-side.
    /// </summary>
    public sealed class BattlePanel : MonoBehaviour
    {
        static BattlePanel _open;

        ApiClient _api;
        Action<string> _status;
        RectTransform _stage;
        BattleLegacyPresentation _presentation;
        BattleLegacyCatalog _catalog;
        long _battleId;
        long _playerId;
        BattleView _battle;
        bool _busy;
        int? _terrain;

        public static BattlePanel Open(RectTransform host, ApiClient api, Action<string> status, long battleId)
        {
            var go = new GameObject("BattlePanel", typeof(RectTransform));
            go.transform.SetParent(host, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

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
            var go = new GameObject("LegacyWarStage", typeof(RectTransform));
            go.transform.SetParent(transform, false);
            _stage = (RectTransform)go.transform;
            _stage.anchorMin = Vector2.zero;
            _stage.anchorMax = Vector2.one;
            _stage.offsetMin = Vector2.zero;
            _stage.offsetMax = Vector2.zero;

            _catalog = BattleLegacyCatalog.Load();
            _presentation = gameObject.AddComponent<BattleLegacyPresentation>();
            _presentation.Initialize(_stage, _catalog);
            _presentation.OnAttack = () => { if (!_busy) _ = AdvanceAsync(); };
            _presentation.OnLegion = () => { if (!_busy) _ = DeployTeamAsync(); };
            _presentation.OnClose = () => { if (!_busy) Destroy(gameObject); };
            _presentation.OnTactic = unit => { if (!_busy && unit != null) _ = ChooseAsync(unit, 1, 0); };
            _presentation.OnStrategy = strategy =>
            {
                if (_busy || _battle == null) return;
                var own = OwnFront();
                if (own != null) _ = ChooseAsync(own, 2, strategy);
            };
        }

        async Task RefreshAsync()
        {
            if (_api == null) return;
            try
            {
                if (_playerId == 0)
                {
                    var player = await _api.GetPlayerAsync();
                    if (player != null) _playerId = player.id;
                }

                _battle = await _api.GetBattleAsync(_battleId);
                if (!_terrain.HasValue) _terrain = await ResolveLegacyTerrainAsync();
                _presentation.SetSnapshot(_battle, _terrain, _playerId, true);
            }
            catch (Exception ex)
            {
                _status?.Invoke(ex.Message);
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

                var city = (world.cities ?? Array.Empty<WorldCityView>()).FirstOrDefault(x => _battle != null && x.id == _battle.cityId);
                return city != null && city.terrain > 0 ? city.terrain : (int?)null;
            }
            catch
            {
                // Missing presentation context must not fabricate a terrain/background.
                return null;
            }
        }

        BattleUnitView OwnFront()
        {
            if (_battle == null || _playerId == 0) return null;
            return ( _battle.attackers ?? Array.Empty<BattleUnitView>())
                .Concat(_battle.defenders ?? Array.Empty<BattleUnitView>())
                .Where(x => !x.dead && x.hp > 0 && !x.isNpc && x.playerId == _playerId)
                .OrderBy(x => x.sequence)
                .FirstOrDefault();
        }

        async Task AdvanceAsync()
        {
            if (_busy || _battle == null) return;
            _busy = true;
            try
            {
                var before = _battle;
                var result = await _api.AdvanceBattleAsync(_battleId);
                _battle = result;
                var round = (result.rounds ?? Array.Empty<BattleRoundView>())
                    .Where(x => before == null || x.roundNo > before.roundNo)
                    .OrderBy(x => x.roundNo)
                    .LastOrDefault();

                if (round != null)
                    await _presentation.PlayRoundAsync(before, result, round, _terrain, _playerId);
                else
                    _presentation.SetSnapshot(result, _terrain, _playerId);
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

        async Task ChooseAsync(BattleUnitView unit, int action, int strategy)
        {
            if (_busy || unit == null) return;
            _busy = true;
            try
            {
                _battle = await _api.ChooseBattleActionAsync(_battleId, unit.generalId, action, strategy);
                _presentation.SetSnapshot(_battle, _terrain, _playerId);
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

        async Task DeployTeamAsync()
        {
            if (_busy) return;
            _busy = true;
            try
            {
                var teams = await _api.GetTeamsAsync();
                var team = (teams.items ?? Array.Empty<TeamView>()).FirstOrDefault(x => x.isOwner);
                if (team == null) throw new Exception("No owned team is ready.");
                var count = team.members != null ? team.members.Length : 0;
                var result = await _api.DeployTeamAsync(team.id, _battleId, count, 0);
                _battle = result.battle;
                _presentation.SetSnapshot(_battle, _terrain, _playerId, true);
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
    }
}
