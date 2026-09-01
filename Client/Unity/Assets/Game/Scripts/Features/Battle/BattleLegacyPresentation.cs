using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CTXD.Client.Networking;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace CTXD.Client.Features.Battle
{
    /// <summary>
    /// Presentation-only reconstruction of War.swf / WarLegion.swf / skill SWFs.
    /// HP, damage, death, tactic, strategy and winner state are consumed from BattleView/BattleRoundView only.
    /// </summary>
    public sealed class BattleLegacyPresentation : MonoBehaviour
    {
        public const float LegacyWidth = 1280f;
        public const float LegacyHeight = 768f;
        public const float FightIntervalSeconds = .5f;
        public const float DamageDelaySeconds = .2f;
        public const float FormationEntrySeconds = 1.4f;

        const float CenterWidth = 1000f;
        const float CenterHeight = 600f;
        const float CenterOffsetX = (LegacyWidth - CenterWidth) * .5f;
        const float CenterOffsetY = (LegacyHeight - CenterHeight) * .5f;
        const float WarX = 380f;
        const float WarY = 250f;
        const float SideWidth = 99f;
        const float SideHeight = 52f;

        const float AttXPer = -1.7f;
        const float AttYPer = 1.7f;
        const float AttRowXPer = -.75f;
        const float AttRowYPer = .75f;
        const float DefXPer = 1.7f;
        const float DefYPer = -1.7f;
        const float DefRowXPer = .75f;
        const float DefRowYPer = -.75f;

        const float FightAttXPer = -.41f;
        const float FightAttYPer = .42f;
        const float FightDefXPer = .4f;
        const float FightDefYPer = -.4f;

        const float StrategyX = 585f;
        const float StrategyY = 383f;

        RectTransform _stage;
        RectTransform _backgroundLayer;
        RectTransform _formationLayer;
        RectTransform _fxLayer;
        RectTransform _hudLayer;
        RectTransform _resultLayer;
        BattleLegacyCatalog _catalog;
        readonly Dictionary<long, UnitVisual> _units = new Dictionary<long, UnitVisual>();
        readonly Dictionary<string, Sprite[]> _frameCache = new Dictionary<string, Sprite[]>(StringComparer.Ordinal);
        AudioSource _music;
        AudioSource _effects;
        TaskCompletionSource<bool> _playbackCompletion;
        long _playerId;

        public Action OnAttack;
        public Action OnLegion;
        public Action OnClose;
        public Action<BattleUnitView> OnTactic;
        public Action<int> OnStrategy;

        public void Initialize(RectTransform stage, BattleLegacyCatalog catalog)
        {
            _stage = stage;
            _catalog = catalog ?? BattleLegacyCatalog.Load();
            _backgroundLayer = Layer("LegacyWarBackground");
            _formationLayer = Layer("LegacyWarForms");
            _fxLayer = Layer("LegacyWarEffects");
            _hudLayer = Layer("LegacyWarHud");
            _resultLayer = Layer("LegacyWarResult");
            _music = gameObject.AddComponent<AudioSource>();
            _music.loop = true;
            _music.playOnAwake = false;
            _effects = gameObject.AddComponent<AudioSource>();
            _effects.loop = false;
            _effects.playOnAwake = false;
        }

        void OnDisable()
        {
            CancelPlayback();
            if (_music != null) _music.Stop();
        }

        public void SetSnapshot(BattleView battle, int? terrain, long playerId, bool animateFormation = false)
        {
            CancelPlayback();
            _playerId = playerId;
            RebuildSnapshot(battle, terrain, animateFormation);
        }

        public Task PlayRoundAsync(BattleView before, BattleView after, BattleRoundView round, int? terrain, long playerId)
        {
            if (_stage == null || before == null || after == null || round == null)
            {
                SetSnapshot(after, terrain, playerId);
                return Task.CompletedTask;
            }

            CancelPlayback();
            _playerId = playerId;
            _playbackCompletion = new TaskCompletionSource<bool>();
            StartCoroutine(PlayRound(before, after, round, terrain, _playbackCompletion));
            return _playbackCompletion.Task;
        }

        IEnumerator PlayRound(BattleView before, BattleView after, BattleRoundView round, int? terrain, TaskCompletionSource<bool> completion)
        {
            RebuildSnapshot(before, terrain, false);
            _units.TryGetValue(round.attackerUnitId, out var attacker);
            _units.TryGetValue(round.defenderUnitId, out var defender);
            var attackerData = FindUnit(before, round.attackerUnitId);
            var defenderData = FindUnit(before, round.defenderUnitId);

            if (attackerData != null && attackerData.selectedAction == 1 && attackerData.tacticId > 0)
            {
                PlayEffect("battle_wujiang_skill");
                yield return PlayTactic(attackerData.tacticId, attacker != null ? attacker.Center : FightPoint(true));
            }
            else if (attackerData != null && attackerData.selectedAction == 2 && attackerData.strategyId != 0)
            {
                PlayStrategyAudio(attackerData.strategyId);
                yield return PlayFrames("LegacyVisual/Battle/UI/Strategy/NewAttack", FightPoint(false), 24f, 1f);
            }

            PlayEffect("battle_jiaofeng");
            if (attacker != null) attacker.BeginAction(3, FightIntervalSeconds);
            if (defender != null) defender.BeginAction(4, FightIntervalSeconds);

            var elapsed = 0f;
            var impactShown = false;
            while (elapsed < FightIntervalSeconds)
            {
                var delta = Time.unscaledDeltaTime;
                elapsed += delta;
                if (attacker != null) attacker.Tick(delta);
                if (defender != null) defender.Tick(delta);
                if (!impactShown && elapsed >= DamageDelaySeconds)
                {
                    impactShown = true;
                    StartCoroutine(PlayFrames("LegacyVisual/Battle/UI/War/Impact", defender != null ? defender.Center : FightPoint(false), 20f, 1f));
                    var toAttacker = Sum(round.attackerTicks, round.attackerDamage);
                    var toDefender = Sum(round.defenderTicks, round.defenderDamage);
                    if (toAttacker > 0) ShowDamage(toAttacker, attacker != null ? attacker.Center : FightPoint(true));
                    if (toDefender > 0) ShowDamage(toDefender, defender != null ? defender.Center : FightPoint(false));
                }
                yield return null;
            }

            RebuildSnapshot(after, terrain, false);
            var deadAttacker = FindUnit(after, round.attackerUnitId);
            var deadDefender = FindUnit(after, round.defenderUnitId);
            if (deadAttacker != null && (deadAttacker.dead || deadAttacker.hp <= 0) && _units.TryGetValue(deadAttacker.id, out var av))
                av.BeginAction(5, .55f);
            if (deadDefender != null && (deadDefender.dead || deadDefender.hp <= 0) && _units.TryGetValue(deadDefender.id, out var dv))
                dv.BeginAction(5, .55f);

            var endElapsed = 0f;
            while (endElapsed < .55f && after.status == 0)
            {
                var delta = Time.unscaledDeltaTime;
                endElapsed += delta;
                foreach (var unit in _units.Values) unit.Tick(delta);
                yield return null;
            }

            if (after.status != 0) ShowResult(after);
            completion.TrySetResult(true);
            if (ReferenceEquals(_playbackCompletion, completion)) _playbackCompletion = null;
        }

        void RebuildSnapshot(BattleView battle, int? terrain, bool animateFormation)
        {
            if (_stage == null || battle == null) return;
            Clear(_backgroundLayer); Clear(_formationLayer); Clear(_fxLayer); Clear(_hudLayer); Clear(_resultLayer);
            _units.Clear();

            if (terrain.HasValue && terrain.Value > 0)
                CreateImage(_backgroundLayer, "LegacyVisual/Battle/Backgrounds/" + terrain.Value, 0, 0, LegacyWidth, LegacyHeight, true);

            CreateImage(_hudLayer, "LegacyVisual/Battle/UI/War/vs_title", 545, 22, 190, 70, true, false);
            CreateImage(_formationLayer, "LegacyVisual/Battle/UI/FightVS/bg", CenterOffsetX + WarX - 42, CenterOffsetY + WarY - 20, 84, 58, true, false);

            var attackers = (battle.attackers ?? Array.Empty<BattleUnitView>()).OrderBy(x => x.sequence).ToArray();
            var defenders = (battle.defenders ?? Array.Empty<BattleUnitView>()).OrderBy(x => x.sequence).ToArray();
            foreach (var unit in attackers) CreateUnit(unit, true, animateFormation);
            foreach (var unit in defenders) CreateUnit(unit, false, animateFormation);

            CreateSpriteButton(_hudLayer, "LegacyVisual/Battle/UI/War/back", 1191, 17, OnClose);

            if (battle.status == 0)
            {
                CreateSpriteButton(_hudLayer, "LegacyVisual/Battle/UI/War/attack", 559, 656, OnAttack);
                CreateSpriteButton(_hudLayer, "LegacyVisual/Battle/UI/War/legion", 905, 651, OnLegion);
                var own = FrontOwned(battle, _playerId);
                if (own != null)
                {
                    BindTacticPortrait(own);
                    BuildStrategyPanel(own);
                }
                EnsureBattleMusic();
            }
            else
            {
                if (_music != null) _music.Stop();
                ShowResult(battle);
            }
        }

        void CreateUnit(BattleUnitView data, bool attacker, bool animateFormation)
        {
            var troopType = _catalog != null ? _catalog.TroopType(data.troopId) : 0;
            if (troopType <= 0) return;
            var side = attacker ? "att" : "def";
            var idle = LoadFrames("LegacyVisual/Battle/Soldiers/" + side + troopType + "/action1");
            if (idle.Length == 0) return; // No fabricated soldier fallback (notably att21).

            var position = FormationPoint(data.sequence, attacker);
            var soldier = CreateRawImage(_formationLayer, side + troopType + "_" + data.id, idle[0], position, new Vector2(92, 92));
            var visual = new UnitVisual(this, data, soldier, side, troopType, position);
            _units[data.id] = visual;

            if (animateFormation && !data.dead && data.hp > 0) StartCoroutine(visual.PlayEntry(FormationEntrySeconds));
            else if (data.dead || data.hp <= 0) visual.BeginAction(5, .01f);

            CreatePortraitAndHp(visual, attacker);
        }

        void CreatePortraitAndHp(UnitVisual unit, bool attacker)
        {
            var pic = _catalog != null ? _catalog.GeneralPic(unit.Data.generalId) : null;
            var framePath = attacker ? "LegacyVisual/Battle/UI/War/att_portrait_frame" : "LegacyVisual/Battle/UI/War/def_portrait_frame";
            var offset = attacker ? new Vector2(-58, -73) : new Vector2(58, -73);
            var center = unit.Center + offset;
            var frame = CreateImageCentered(_hudLayer, framePath, center, new Vector2(52, 52), true, false);
            if (!string.IsNullOrEmpty(pic))
                CreateImageCentered(_hudLayer, "LegacyVisual/Battle/Portraits/" + pic, center, new Vector2(44, 44), true, false);

            var hpCenter = center + new Vector2(0, 34);
            CreateImageCentered(_hudLayer, "LegacyVisual/Battle/UI/War/hp_frame", hpCenter, new Vector2(72, 12), false, false);
            var hp = CreateImageCentered(_hudLayer, attacker ? "LegacyVisual/Battle/UI/War/hp_red" : "LegacyVisual/Battle/UI/War/hp_blue", hpCenter, new Vector2(68, 8), false, false);
            if (hp != null)
            {
                hp.type = Image.Type.Filled;
                hp.fillMethod = Image.FillMethod.Horizontal;
                hp.fillOrigin = 0;
                hp.fillAmount = unit.Data.maxHp > 0 ? Mathf.Clamp01((float)unit.Data.hp / unit.Data.maxHp) : 0f;
            }
            unit.PortraitFrame = frame;
        }

        void BindTacticPortrait(BattleUnitView own)
        {
            if (!own.tacticAvailable || own.tacticId <= 0 || !_units.TryGetValue(own.id, out var visual) || visual.PortraitFrame == null) return;
            var button = visual.PortraitFrame.gameObject.AddComponent<Button>();
            button.targetGraphic = visual.PortraitFrame;
            button.onClick.AddListener(() => OnTactic?.Invoke(own));
        }

        void BuildStrategyPanel(BattleUnitView own)
        {
            var choices = own.allowedStrategyIds ?? Array.Empty<int>();
            if (choices.Length == 0) return;
            CreateImage(_hudLayer, "LegacyVisual/Battle/UI/Strategy/bg", StrategyX, StrategyY, 190, 165, true, false);
            AddStrategyButton("charge", 54, -6, FindStrategy(choices, 30));
            AddStrategyButton("prud", 119, 95, FindStrategy(choices, 10));
            AddStrategyButton("divers", 0, 95, FindStrategy(choices, 20));
        }

        void AddStrategyButton(string name, float x, float y, int strategyId)
        {
            if (strategyId == 0) return;
            CreateSpriteButton(_hudLayer, "LegacyVisual/Battle/UI/Strategy/" + name, StrategyX + x, StrategyY + y,
                () => OnStrategy?.Invoke(strategyId));
        }

        void ShowResult(BattleView battle)
        {
            Clear(_resultLayer);
            var side = PlayerSide(battle, _playerId);
            if (side == 0 || battle.winnerSide == 0) return;
            var won = side == battle.winnerSide;
            CreateImage(_resultLayer, won ? "LegacyVisual/Battle/UI/WarResult/bg_0" : "LegacyVisual/Battle/UI/WarResult/bg_1", 410, 205, 460, 260, true, false);
            CreateImage(_resultLayer, won ? "LegacyVisual/Battle/UI/WarResult/title_0" : "LegacyVisual/Battle/UI/WarResult/title_1", 515, 225, 250, 95, true, false);
            CreateSpriteButton(_resultLayer, "LegacyVisual/Battle/UI/WarResult/confirm", 565, 397, OnClose);
            PlayEffect(won ? "battle_dasheng" : "battle_lose");
        }

        IEnumerator PlayTactic(int tacticId, Vector2 center)
        {
            var key = _catalog != null ? _catalog.TacticSkillKey(tacticId) : null;
            if (string.IsNullOrEmpty(key)) yield break;
            var duration = _catalog != null ? Mathf.Max(.2f, _catalog.TacticDurationMs(tacticId) / 1000f) : 1f;
            yield return PlayFrames("LegacyVisual/Battle/Skills/" + key + "/Frames", center, 25f, duration);
        }

        IEnumerator PlayFrames(string path, Vector2 center, float fps, float durationScale)
        {
            var frames = LoadFrames(path);
            if (frames.Length == 0) yield break;
            var image = CreateRawImage(_fxLayer, path.Replace('/', '_'), frames[0], center, new Vector2(640, 480));
            image.preserveAspect = true;
            var frameTime = durationScale > 0f ? durationScale / frames.Length : 1f / Mathf.Max(1f, fps);
            for (var i = 0; i < frames.Length; i++)
            {
                if (image == null) yield break;
                image.sprite = frames[i];
                image.SetNativeSize();
                var elapsed = 0f;
                while (elapsed < frameTime) { elapsed += Time.unscaledDeltaTime; yield return null; }
            }
            if (image != null) Destroy(image.gameObject);
        }

        void ShowDamage(int damage, Vector2 center)
        {
            StartCoroutine(AnimateDamage(Math.Max(0, damage).ToString(), center));
        }

        IEnumerator AnimateDamage(string value, Vector2 center)
        {
            var root = new GameObject("LegacyDamage", typeof(RectTransform));
            root.transform.SetParent(_fxLayer, false);
            var rt = (RectTransform)root.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0, 1);
            rt.pivot = new Vector2(.5f, .5f);
            rt.anchoredPosition = new Vector2(center.x, -center.y);
            var x = -(value.Length - 1) * 7f;
            foreach (var ch in value)
            {
                var sprite = Resources.Load<Sprite>("LegacyVisual/Battle/UI/War/DamageDigits/" + ch);
                if (sprite != null)
                {
                    var img = CreateRawImage(rt, "damage_" + ch, sprite, new Vector2(x, 0), new Vector2(18, 24));
                    img.rectTransform.anchorMin = img.rectTransform.anchorMax = new Vector2(.5f, .5f);
                    img.rectTransform.anchoredPosition = new Vector2(x, 0);
                }
                x += 14f;
            }
            var elapsed = 0f;
            while (elapsed < .65f)
            {
                elapsed += Time.unscaledDeltaTime;
                rt.anchoredPosition += new Vector2(0, 22f * Time.unscaledDeltaTime);
                yield return null;
            }
            if (root != null) Destroy(root);
        }

        Sprite[] LoadFrames(string path)
        {
            if (_frameCache.TryGetValue(path, out var cached)) return cached;
            var frames = Resources.LoadAll<Sprite>(path).OrderBy(x => FrameNumber(x.name)).ToArray();
            _frameCache[path] = frames;
            return frames;
        }

        static int FrameNumber(string name)
        {
            var i = name.LastIndexOf('_');
            return i >= 0 && int.TryParse(name.Substring(i + 1), out var n) ? n : int.MaxValue;
        }

        Vector2 FormationPoint(int sequence, bool attacker)
        {
            var lane = Mathf.Abs(sequence) % 3;
            var rank = Mathf.Max(0, sequence / 3);
            var baseX = CenterOffsetX + WarX + SideWidth * lane;
            var baseY = CenterOffsetY + WarY + SideHeight * lane;
            var xPer = attacker ? AttXPer : DefXPer;
            var yPer = attacker ? AttYPer : DefYPer;
            var rowX = attacker ? AttRowXPer : DefRowXPer;
            var rowY = attacker ? AttRowYPer : DefRowYPer;
            return new Vector2(baseX + xPer * SideWidth + rank * rowX * SideWidth,
                baseY + yPer * SideHeight + rank * rowY * SideHeight);
        }

        static Vector2 FightPoint(bool attacker)
        {
            var x = CenterOffsetX + WarX + (attacker ? FightAttXPer : FightDefXPer) * SideWidth;
            var y = CenterOffsetY + WarY + (attacker ? FightAttYPer : FightDefYPer) * SideHeight;
            return new Vector2(x, y);
        }

        static int FindStrategy(int[] choices, int family)
        {
            return choices.FirstOrDefault(x => Mathf.Abs(x % 100) >= family && Mathf.Abs(x % 100) < family + 10);
        }

        static BattleUnitView FrontOwned(BattleView battle, long playerId)
        {
            var all = (battle.attackers ?? Array.Empty<BattleUnitView>()).Concat(battle.defenders ?? Array.Empty<BattleUnitView>());
            return all.Where(x => !x.dead && x.hp > 0 && !x.isNpc && x.playerId == playerId).OrderBy(x => x.sequence).FirstOrDefault();
        }

        static int PlayerSide(BattleView battle, long playerId)
        {
            if ((battle.attackers ?? Array.Empty<BattleUnitView>()).Any(x => !x.isNpc && x.playerId == playerId)) return 1;
            if ((battle.defenders ?? Array.Empty<BattleUnitView>()).Any(x => !x.isNpc && x.playerId == playerId)) return 2;
            return 0;
        }

        static BattleUnitView FindUnit(BattleView battle, long id)
        {
            return (battle.attackers ?? Array.Empty<BattleUnitView>()).Concat(battle.defenders ?? Array.Empty<BattleUnitView>()).FirstOrDefault(x => x.id == id);
        }

        static int Sum(int[] ticks, int aggregate)
        {
            if (ticks == null || ticks.Length == 0) return Math.Max(0, aggregate);
            var total = 0;
            foreach (var tick in ticks) total += Math.Max(0, tick);
            return total;
        }

        void PlayStrategyAudio(int strategyId)
        {
            var family = Mathf.Abs(strategyId % 100);
            if (family >= 30 && family < 40) PlayEffect("battle_tuji");
            else if (family >= 10 && family < 20) PlayEffect("battle_fangshou");
            else PlayEffect("battle_gongji");
        }

        void EnsureBattleMusic()
        {
            if (_music == null || _music.isPlaying) return;
            var clip = Resources.Load<AudioClip>("LegacyVisual/Battle/Audio/battle_1");
            if (clip == null) return;
            _music.clip = clip;
            _music.Play();
        }

        void PlayEffect(string name)
        {
            if (_effects == null) return;
            var clip = Resources.Load<AudioClip>("LegacyVisual/Battle/Audio/" + name);
            if (clip != null) _effects.PlayOneShot(clip);
        }

        RectTransform Layer(string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(_stage, false);
            var rt = (RectTransform)go.transform;
            Stretch(rt);
            return rt;
        }

        Image CreateImage(Transform parent, string path, float x, float y, float width, float height, bool preserveAspect, bool raycast = false)
        {
            var sprite = Resources.Load<Sprite>(path);
            if (sprite == null) return null;
            var go = new GameObject(path.Substring(path.LastIndexOf('/') + 1), typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            PixelRect(rt, x, y, width, height);
            var image = go.GetComponent<Image>();
            image.sprite = sprite; image.preserveAspect = preserveAspect; image.raycastTarget = raycast;
            return image;
        }

        Image CreateImageCentered(Transform parent, string path, Vector2 center, Vector2 size, bool preserveAspect, bool raycast)
        {
            var sprite = Resources.Load<Sprite>(path);
            if (sprite == null) return null;
            return CreateRawImage(parent, path.Substring(path.LastIndexOf('/') + 1), sprite, center, size, raycast, preserveAspect);
        }

        Image CreateRawImage(Transform parent, string name, Sprite sprite, Vector2 center, Vector2 size, bool raycast = false, bool preserveAspect = true)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0, 1);
            rt.pivot = new Vector2(.5f, .5f);
            rt.anchoredPosition = new Vector2(center.x, -center.y);
            rt.sizeDelta = size;
            var image = go.GetComponent<Image>(); image.sprite = sprite; image.raycastTarget = raycast; image.preserveAspect = preserveAspect;
            return image;
        }

        Button CreateSpriteButton(Transform parent, string basePath, float x, float y, UnityAction click)
        {
            var normal = Resources.Load<Sprite>(basePath + "_up");
            if (normal == null) return null;
            var go = new GameObject(basePath.Substring(basePath.LastIndexOf('/') + 1), typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0, 1); rt.pivot = new Vector2(0, 1); rt.anchoredPosition = new Vector2(x, -y);
            rt.sizeDelta = new Vector2(normal.rect.width, normal.rect.height);
            var image = go.GetComponent<Image>(); image.sprite = normal; image.preserveAspect = true;
            var button = go.GetComponent<Button>(); button.targetGraphic = image;
            var state = button.spriteState;
            state.highlightedSprite = Resources.Load<Sprite>(basePath + "_over") ?? normal;
            state.pressedSprite = Resources.Load<Sprite>(basePath + "_down") ?? state.highlightedSprite;
            state.selectedSprite = state.highlightedSprite;
            button.spriteState = state;
            if (click != null) button.onClick.AddListener(click);
            return button;
        }

        static void PixelRect(RectTransform rt, float x, float y, float width, float height)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(0, 1); rt.pivot = new Vector2(0, 1);
            rt.anchoredPosition = new Vector2(x, -y); rt.sizeDelta = new Vector2(width, height);
        }

        static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
        }

        static void Clear(Transform root)
        {
            if (root == null) return;
            for (var i = root.childCount - 1; i >= 0; i--) Destroy(root.GetChild(i).gameObject);
        }

        void CancelPlayback()
        {
            StopAllCoroutines();
            if (_playbackCompletion != null)
            {
                _playbackCompletion.TrySetResult(true);
                _playbackCompletion = null;
            }
        }

        sealed class UnitVisual
        {
            readonly BattleLegacyPresentation _owner;
            readonly Image _soldier;
            readonly string _side;
            readonly int _troopType;
            Sprite[] _playing = Array.Empty<Sprite>();
            float _frameTime;
            float _elapsed;
            int _index;

            public BattleUnitView Data { get; }
            public Vector2 Center { get; }
            public Image PortraitFrame { get; set; }

            public UnitVisual(BattleLegacyPresentation owner, BattleUnitView data, Image soldier, string side, int troopType, Vector2 center)
            {
                _owner = owner; Data = data; _soldier = soldier; _side = side; _troopType = troopType; Center = center;
            }

            public IEnumerator PlayEntry(float duration)
            {
                var final = _soldier.rectTransform.anchoredPosition;
                var start = final + new Vector2(_side == "att" ? -130f : 130f, _side == "att" ? -130f : 130f);
                _soldier.rectTransform.anchoredPosition = start;
                BeginAction(2, duration);
                var elapsed = 0f;
                while (elapsed < duration)
                {
                    var delta = Time.unscaledDeltaTime; elapsed += delta; Tick(delta);
                    _soldier.rectTransform.anchoredPosition = Vector2.Lerp(start, final, Mathf.Clamp01(elapsed / duration));
                    yield return null;
                }
                _soldier.rectTransform.anchoredPosition = final;
                BeginAction(1, .01f);
            }

            public void BeginAction(int action, float duration)
            {
                var frames = _owner.LoadFrames("LegacyVisual/Battle/Soldiers/" + _side + _troopType + "/action" + action);
                if (frames.Length == 0) return;
                _playing = frames; _index = 0; _elapsed = 0f; _frameTime = Mathf.Max(.001f, duration / frames.Length); ApplyFrame();
            }

            public void Tick(float delta)
            {
                if (_playing.Length <= 1) return;
                _elapsed += delta;
                while (_elapsed >= _frameTime && _index < _playing.Length - 1)
                {
                    _elapsed -= _frameTime; _index++; ApplyFrame();
                }
            }

            void ApplyFrame()
            {
                if (_soldier == null || _playing.Length == 0) return;
                _soldier.sprite = _playing[_index]; _soldier.preserveAspect = true;
            }
        }
    }
}
