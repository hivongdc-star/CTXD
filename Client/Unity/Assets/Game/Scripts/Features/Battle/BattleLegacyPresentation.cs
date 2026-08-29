using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CTXD.Client.Networking;
using UnityEngine;
using UnityEngine.UI;

namespace CTXD.Client.Features.Battle
{
    /// <summary>
    /// Presentation-only reconstruction of the legacy War/FightArea path.
    /// No combat result is calculated here: BattleRoundView remains authoritative.
    /// </summary>
    public sealed class BattleLegacyPresentation : MonoBehaviour
    {
        public const float LegacyWidth = 1280f;
        public const float LegacyHeight = 768f;
        public const float FightIntervalSeconds = .5f; // FightVS.INTERVAL_TIME = 500
        public const float DamageLabelDelaySeconds = .2f; // FightVS.takeAction delayedCall(0.2)

        const float WarX = 380f;
        const float WarY = 250f;
        const float SideWidth = 99f;
        const float SideHeight = 52f;
        const float AttX = 300f;
        const float AttY = 200f;
        const float DefX = 300f;
        const float DefY = 200f;

        RectTransform _stage;
        RawImage _background;
        readonly Dictionary<long, UnitVisual> _unitVisuals = new Dictionary<long, UnitVisual>();
        readonly Dictionary<string, Sprite[]> _frames = new Dictionary<string, Sprite[]>(StringComparer.Ordinal);
        readonly Dictionary<string, Sprite> _singleSprites = new Dictionary<string, Sprite>(StringComparer.Ordinal);

        public void Initialize(RectTransform stage)
        {
            _stage = stage;
        }

        public bool SetSnapshot(BattleView battle, int? terrain)
        {
            if (_stage == null || battle == null) return false;
            ClearStage();
            var hasLegacyVisual = TryCreateBackground(terrain);

            // Legacy FightArea owns three FightVS lanes. The remake BattleView does not expose
            // legacy per-row ArmyVO data, so only the authoritative current front pair is bound.
            // Empty lanes remain empty instead of duplicating/inventing troop rows.
            var attacker = (battle.attackers ?? Array.Empty<BattleUnitView>()).FirstOrDefault(x => !x.dead && x.hp > 0);
            var defender = (battle.defenders ?? Array.Empty<BattleUnitView>()).FirstOrDefault(x => !x.dead && x.hp > 0);
            if (attacker != null) hasLegacyVisual |= TryCreateUnit(attacker, true, 0);
            if (defender != null) hasLegacyVisual |= TryCreateUnit(defender, false, 0);
            return hasLegacyVisual;
        }

        public Task PlayRoundAsync(BattleView before, BattleView after, BattleRoundView round, int? terrain)
        {
            if (_stage == null || before == null || after == null || round == null)
            {
                SetSnapshot(after, terrain);
                return Task.CompletedTask;
            }

            var completion = new TaskCompletionSource<bool>();
            StartCoroutine(PlayRound(before, after, round, terrain, completion));
            return completion.Task;
        }

        IEnumerator PlayRound(BattleView before, BattleView after, BattleRoundView round, int? terrain, TaskCompletionSource<bool> completion)
        {
            try
            {
                SetSnapshot(before, terrain);
                _unitVisuals.TryGetValue(round.attackerUnitId, out var attacker);
                _unitVisuals.TryGetValue(round.defenderUnitId, out var defender);

                var attackerTicks = round.attackerTicks ?? Array.Empty<int>();
                var defenderTicks = round.defenderTicks ?? Array.Empty<int>();
                var tickCount = Math.Max(attackerTicks.Length, defenderTicks.Length);

                for (var tick = 0; tick < tickCount; tick++)
                {
                    if (attacker != null) attacker.BeginAction(3, FightIntervalSeconds);
                    if (defender != null) defender.BeginAction(3, FightIntervalSeconds);

                    var elapsed = 0f;
                    while (elapsed < FightIntervalSeconds)
                    {
                        elapsed += Time.unscaledDeltaTime;
                        if (attacker != null) attacker.Tick(Time.unscaledDeltaTime);
                        if (defender != null) defender.Tick(Time.unscaledDeltaTime);
                        yield return null;
                    }
                }

                if (round.winnerSide == 1 && attacker != null)
                    attacker.BeginAction(5, attacker.NaturalDuration(5));
                else if (round.winnerSide == 2 && defender != null)
                    defender.BeginAction(5, defender.NaturalDuration(5));

                // FightVS removes the defeated Army at fightEnd. The authoritative after-view
                // is applied immediately; a winner animation keeps playing only when its source
                // frames exist and the unit remains the front unit after the round.
                SetSnapshot(after, terrain);
            }
            finally
            {
                completion.TrySetResult(true);
            }
        }

        bool TryCreateBackground(int? terrain)
        {
            if (!terrain.HasValue || terrain.Value <= 0) return false;
            var texture = Resources.Load<Texture2D>("LegacyVisual/Battle/Background/" + terrain.Value);
            if (texture == null) return false;

            var go = new GameObject("LegacyWarBackground", typeof(RectTransform), typeof(RawImage));
            go.transform.SetParent(_stage, false);
            var rt = (RectTransform)go.transform;
            Stretch(rt);
            _background = go.GetComponent<RawImage>();
            _background.texture = texture;
            _background.raycastTarget = false;
            return true;
        }

        bool TryCreateUnit(BattleUnitView unit, bool attacker, int laneIndex)
        {
            var actionFrames = LoadFrames(attacker ? "att" : "def", unit.troopId, 1);
            if (actionFrames.Length == 0) return false;

            laneIndex = Mathf.Clamp(laneIndex, 0, 2);
            var laneX = WarX + SideWidth * laneIndex;
            var laneY = WarY + SideHeight * laneIndex;
            var x = attacker ? laneX - AttX : laneX + DefX;
            var y = attacker ? laneY + AttY : laneY - DefY;

            var imageGo = new GameObject((attacker ? "att" : "def") + unit.troopId + "_" + unit.id, typeof(RectTransform), typeof(Image));
            imageGo.transform.SetParent(_stage, false);
            var image = imageGo.GetComponent<Image>();
            image.raycastTarget = false;
            image.preserveAspect = true;
            image.sprite = actionFrames[0];
            image.SetNativeSize();

            var rt = (RectTransform)imageGo.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(.5f, .5f);
            rt.anchoredPosition = new Vector2(x, -y);

            _unitVisuals[unit.id] = new UnitVisual(this, image, attacker ? "att" : "def", unit.troopId);
            return true;
        }

        Sprite[] LoadFrames(string side, int troopId, int action)
        {
            var key = side + troopId + "/action" + action;
            if (_frames.TryGetValue(key, out var cached)) return cached;

            var textures = Resources.LoadAll<Texture2D>("LegacyVisual/Battle/Soldiers/" + key);
            var ordered = textures
                .OrderBy(t => NumericName(t.name))
                .Select(t => SpriteFor("Soldiers/" + key + "/" + t.name, t))
                .Where(s => s != null)
                .ToArray();
            _frames[key] = ordered;
            return ordered;
        }

        Sprite SpriteFor(string key, Texture2D texture)
        {
            if (texture == null) return null;
            if (_singleSprites.TryGetValue(key, out var sprite) && sprite != null) return sprite;
            sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(.5f, .5f), 100f);
            sprite.name = texture.name;
            _singleSprites[key] = sprite;
            return sprite;
        }

        static int NumericName(string value)
        {
            return int.TryParse(value, out var number) ? number : int.MaxValue;
        }

        void ClearStage()
        {
            StopAllCoroutines();
            _unitVisuals.Clear();
            _background = null;
            for (var i = _stage.childCount - 1; i >= 0; i--) Destroy(_stage.GetChild(i).gameObject);
        }

        static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        sealed class UnitVisual
        {
            readonly BattleLegacyPresentation _owner;
            readonly Image _image;
            readonly string _side;
            readonly int _troopId;
            Sprite[] _playing = Array.Empty<Sprite>();
            float _frameTime;
            float _elapsed;
            int _index;

            public UnitVisual(BattleLegacyPresentation owner, Image image, string side, int troopId)
            {
                _owner = owner;
                _image = image;
                _side = side;
                _troopId = troopId;
            }

            public void BeginAction(int action, float duration)
            {
                var frames = _owner.LoadFrames(_side, _troopId, action);
                if (frames.Length == 0) return;
                _playing = frames;
                _index = 0;
                _elapsed = 0f;
                _frameTime = Math.Max(.001f, duration / frames.Length);
                ApplyFrame();
            }

            public float NaturalDuration(int action)
            {
                // Soldier movie clips run inside the same legacy SWF. The reconstructed basic
                // fight path only relies on the 500 ms FightVS cadence. Until per-SWF frame-rate
                // metadata is imported, keep the action bounded to that authoritative cadence.
                return FightIntervalSeconds;
            }

            public void Tick(float delta)
            {
                if (_playing.Length <= 1 || _frameTime <= 0f) return;
                _elapsed += delta;
                while (_elapsed >= _frameTime && _index < _playing.Length - 1)
                {
                    _elapsed -= _frameTime;
                    _index++;
                    ApplyFrame();
                }
            }

            void ApplyFrame()
            {
                if (_image == null || _playing.Length == 0) return;
                _image.sprite = _playing[_index];
                _image.SetNativeSize();
            }
        }
    }
}
