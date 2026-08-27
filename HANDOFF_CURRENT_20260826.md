# CTXD Remake — Current Handoff

> **AUTHORITATIVE CONTEXT cho phiên tiếp theo.** Không dùng các handoff/checkpoint cũ hơn làm source of truth.

## 1. Mục tiêu và nguyên tắc cố định

- Remake công nghệ, **không redesign** gameplay/UI/UX/art/animation/flow legacy.
- Legacy/reference: `D:\Sever` — **read-only, không overwrite**.
- Remake root: `D:\SeverRMK`.
- Working source: `D:\SeverRMK\CTXD_Remake_Working`.
- Client: Unity + C#, Windows x64 + Android ARM64, một codebase.
- Server: .NET 8 / ASP.NET Core, server-authoritative.
- DB: PostgreSQL clean schema.
- Network: HTTPS + WebSocket.
- Legacy chỉ dùng làm bằng chứng/static/reference; thành phẩm không phụ thuộc Flash/ActionScript runtime, Java 6, Apache/PHP, MySQL 5.5 hay legacy TCP.
- Không migrate account/player/runtime/history/session/cache cũ.
- Ưu tiên implementation/code thật; không tăng % bằng audit/checklist/test/docs.
- Backend/server đi trước, Unity nối theo vertical slice.
- Không scan/audit toàn repo nếu không có blocker cụ thể.
- Không rerun preprocessing V3/V4/V5 vô cớ.
- Không tự bịa rule/reward/timer/cost/static mapping. Thiếu evidence => `BLOCKED`, chuyển phần khả thi khác.
- Chỉ build/startup validation sau block implementation đáng kể.
- Unity chỉ được coi PASS khi thực sự compile/runtime bằng Unity Editor/batch mode.

## 2. Repo / branch authoritative

- GitHub: `hivongdc-star/CTXD`
- Branch: `codex/kfgz-extended-combat`
- **Validated source HEAD trước commit handoff:** `a956d7166d7782b1695dc01b300bbc7fef189714`
  - message: `Remove temporary quest patch workflow`
- Commit source chính của Quest auxiliary: `3296668575ef2e13ad6442f05ef786796996371f`
  - message: `Complete legacy quest auxiliary events`
- Battle equipment parity commit chính: `254658d905b9d6ef73a45efd050b5ade051914e7`
- `main` vẫn cũ hơn; **không tiếp tục từ main** nếu branch này chưa merge.
- Khi mở phiên mới: fetch/pull branch này rồi đọc chính file handoff này trước khi code.

## 3. Commercial completeness

- Baseline hiện tại: **~72% commercial-complete**.
- Đây là % so với thành phẩm thương mại hoàn chỉnh, không phải % số file/code.
- Chỉ tăng khi có chức năng thật và integration thật.
- Server gameplay đã ở mức cao hơn tổng commercial %, nhưng Unity parity/build/release/hardening vẫn còn đáng kể.

## 4. DONE — core/game systems đã có

- Auth / player / force selection / main city.
- Building / resource production.
- General / Tavern.
- Equipment / Store / Inventory.
- Technology.
- World movement / fog / auto-path / conquest boundary.
- Battle server-authoritative: tactic / strategy / equipment / technology / reward / reinforcement.
- Nation core / office / politics / civil affair / Nation Trial / Nation Task / scheduled nation-world tasks.
- Quest core + nhiều provider.
- Mail server + Unity + realtime + atomic/idempotent attachment claim.
- Market / black market / recovery.
- Chat / Team core.
- Activities: Online / Daily / Battle EXP / Level EXP / Dragon / Iron / DSTQ + scheduler/push.
- Pay/VIP phần có authoritative evidence.
- Global level ranking exact legacy `rankId=1`, max 200.

## 5. DONE — cross-server hiện có

### KFWD
- Core lifecycle.
- Match settlement.
- Nguồn Tickets per-match đã nối idempotent khi có công thức authoritative.

### KFZB
- Core + support/Feast phần có evidence.
- Feast ticket column đã sửa đúng `tickets`.

### KFGZ
- season/phases/signup/sync.
- dynamic world/city/road.
- deployment/movement.
- battle handoff/result.
- timeout/settlement/ranking core.
- persistence/reconnect.
- resource/per-general restore.
- retreat 10% HP.
- occupy authoritative chỉ khi owner thực sự đổi.
- Rush / Fast Recruit / Phantom / Call-General-Reinforcement.
- Mubing lifecycle/worker.

## 6. DONE/PARTIAL — Auto Battle / Recruit Recovery / Farm

### Auto Battle
- migration `052_auto_battle.sql`.
- TechEffect59.
- start 50,000 food.
- duration 30 phút.
- worker 10s / scheduler wake 5s.
- attack/defense autoType, movement, reinforcement, battle handoff, advance.
- result legacy 1/2/3/4/5.
- EXP/lost tracking, timeout/settlement, manual stop.
- lock/re-read chống duplicate charge/race.
- Unity panel + realtime `auto-battle.updated`.

### Recruit Recovery
- migration `053_recruit_recovery.sql`.
- `Troop.Conscribe.BaseSpeed=40000`.
- recruit token semantics, passive/token recovery, food cost.
- city-area speed, TechEffect8/28, partial/full behavior.
- Auto Battle gọi recovery trước move/join/battle.
- PARITY GAP: legacy `player_resource_addition` vào recruit/building type5 chưa có runtime tương ứng => contribution hiện 0.

### Farm / Truân Điền
- migration `054_world_farm.sql`.
- canonical `farm.json`, `farm_coe.json`.
- city Wei254 / Shu253 / Wu206, open lv30.
- invest 10,000 copper + 1,000 EXP; CD/clear CD đúng legacy evidence.
- token 1701/type20.
- states 24–28, start/stop/claim/stop-all, partial reward.
- food/general EXP rewards, completion buff 30 phút.
- Battle buff +50% Player EXP + General EXP đúng player+general, không damage/copper, không phantom.
- Unity Farm panel/API đã nối.
- PARTIAL: ChargeItem82 semantic chưa đủ evidence; `FARMING_GENERAL_NUMBER=20` chưa chốt enforcement point; Unity full compile chưa xác nhận.

## 7. DONE/PARTIAL — Mine / Treasure / Weapon / Prison-Slave

### Mine / Khoáng Trường
- canonical 162 rows.
- migration `055_world_mine.sql`.
- server service/endpoints/worker.
- battle type 6/7, terrain 9.
- personal/force ownership, rush 15 phút, x2 production stage, abandon, auto settlement.
- capture payout 50/50 theo legacy evidence.
- force daily harvest.
- Unity minimal vertical slice + realtime.
- PARTIAL: stone item1401 phụ thuộc Blacksmith condition/runtime chưa port đủ để grant an toàn.

### Treasure
- canonical 10 rows.
- migration `056_treasures.sql`.
- inventory endpoint + Unity panel/realtime.
- Politics type5 drop exact 0.001 với function20 gate.
- Battle ATT/DEF/ATT_BASE/DEF_BASE effects.
- acquisition hook hiện có ghi Quest event theo treasure type.
- PARTIAL: Incense/Search/Store acquisition chỉ nối khi owning gameplay có authoritative evidence.

### Weapon
- migration `057_weapons.sql` và runtime Weapon slice đã tồn tại.
- Quest providers hiện có: `arms_weapon_on`, `check_arms_weapon`, `weapon_make_done`.
- Không invent gem/socket rule nếu legacy evidence chưa đủ.

### Prison / Slave
- migration `059_prison.sql`, `060_prison_followup.sql`, `061_slave_activity.sql`.
- Quest branch `804 / Builded_Limbo`, reward copper 2000, claim idempotent.
- Trial lash override / `try_gold` / `trail_gold`, 24h next-lash effective level.
- SlaveEvent type9, rewards authoritative, capture/lash state, prisoner lash EXP, expiry return reward.
- Unity minimal panel đã nối.
- BLOCKED `prison_reward/labor`: static có nhưng chưa tìm thấy runtime caller đủ để port flow/reward chính xác.

## 8. DONE — Battle equipment refresh-skill parity

Files/data chính:
- `Data/Canonical/equip_skill.json`
- `Data/Canonical/equip_skill_effect.json`
- `Server/CTXD.Server/Services/EquipmentSkillEffectService.cs`
- `Server/CTXD.Server/Services/BattleService.cs`
- migration `062_battle_equipment_skills.sql`.

Đã port theo legacy bytecode/static:
- Store refresh-skill roll theo `skill_type`, `skill_num`, default level.
- Battle effect theo **từng general đang mặc trang bị**.
- `ATT` -> Attack.
- `DEF` -> Defense.
- `BLOOD` -> Max HP.
- `ATT_B / DEF_B` -> direct normal-damage bonus/subtraction.
- `TACTIC_ATT / TACTIC_DEF` -> direct tactic-damage bonus/subtraction.
- `ATT_B/DEF_B` và `TACTIC_*` dùng legacy clamp đã reverse, không dùng công thức tự nghĩ.
- Effect được snapshot vào `battle_units` khi general vào trận, tránh thay đồ giữa trận làm đổi snapshot.

### Suit / Proset
- Static đã reverse được:
  - 8 bộ thường 501–508.
  - 3 bộ Chân 511–513.
  - 6 skill yêu cầu theo vị trí.
  - ATT/DEF/BLOOD của set.
  - technology open effect key 48.
- **BLOCKED implementation hoàn chỉnh:** legacy yêu cầu `specialSkillId` + quenching special-skill state trên từng món; remake chưa có prerequisite runtime này.
- Không tạo shortcut “đủ 6 món là thành suit”, vì sẽ sai legacy.

## 9. DONE — Quest auxiliary block vừa hoàn tất

Migration/runtime:
- `Database/Migrations/063_quest_aux_events.sql`.
- `Server/CTXD.Server/Services/QuestEventLedger.cs`.
- Quest event được **scope theo current_task_id**, không lưu kiểu global làm nhiệm vụ tương lai tự hoàn tất sai.

Provider đã nối:
- `world_mine_iron_visit`
  - mở/xem Iron Mine khi task đang active.
- `world_mine_iron_own`
  - legacy xác nhận **phát động một lần iron-mine action/battle là đủ**, không cần thắng.
- `world_treasure_type`
  - param `0` nhận mọi treasure type.
  - param khác 0 yêu cầu **đúng type**.
- `sell_equip`
  - event được ghi từ flow `TutorialService` đang có khi bán trang bị thành công.
- `tavern_refresh`
  - event refresh + fallback legacy: Tavern đang có refresh cooldown active thì evaluator có thể hoàn tất.

Đã nối event từ:
- `MineEndpoints`.
- `TreasureService.TryAcquireAsync`.
- `TutorialService.TryCompleteAsync` cho các action event hiện hữu như sell/tavern.
- `QuestService` evaluator cho 5 target trên.

Known exact parity gap:
- `TaskRequestWorldTreasureByType.check()` legacy còn fallback `hasGottenBox(...)` dựa vào `PlayerWorld.boxispicked` + route/nation-specific treasure boxes.
- Remake hiện chưa có runtime state tương đương đủ authoritative để port fallback này.
- Event acquire-by-type đã đúng; **không invent** phần “all boxes already picked”.

## 10. BUILD / runtime validation mới nhất

Validated source HEAD: `a956d7166d7782b1695dc01b300bbc7fef189714`.

GitHub Actions server-build run:
- run id: `33057267974`.
- `dotnet build Server/CTXD.Server/CTXD.Server.csproj --configuration Release --nologo` — **PASS**.
- PostgreSQL container init — PASS.
- apply all migrations, bao gồm `062` + `063` — **PASS**.
- server startup/health step — **PASS**.
- cleanup — PASS.

Temporary workflow dùng để patch file dài đã được **xóa khỏi repo**; không còn workflow rác để phiên sau xử lý.

Unity:
- project declares `6000.0.0f1`.
- **Unity full compile/runtime vẫn chưa được xác nhận**.
- Không báo Unity PASS cho tới khi thực sự chạy Editor/batch compile.

## 11. PARTIAL / BLOCKED còn quan trọng

- Suit/proset: thiếu `specialSkillId` + quenching special-skill runtime prerequisite.
- Quest world treasure: thiếu exact `boxispicked`/nation-route fallback cho trường hợp đã lấy hết box trước khi task check.
- Quest providers còn lại: chỉ port provider nào owning gameplay/runtime đã có evidence.
- RecruitRecovery: `player_resource_addition` contribution chưa có runtime.
- Farm: ChargeItem82 + exact `FARMING_GENERAL_NUMBER=20` enforcement chưa chốt.
- Mine stone1401: Blacksmith condition/runtime chưa đủ.
- KFGZ ranking/title/end reward: coordinator reward strings chưa đủ authoritative mapping.
- KFWD day/final/treasure reward: mapping chưa đủ.
- KFZB treasure/title reward: mapping chưa đủ.
- Feast organizer/rank: cần authoritative coordinator data.
- VIP6_1/Jinpin mapping chưa đủ.
- precise SWF frame/timeline/effect parity chưa full.
- Unity scene/prefab/serialized refs/full compile, Windows build, Android build chưa final validation.
- Commercial hardening: monitoring / backup / security / liveops / release pipeline / performance/deployment chưa hoàn chỉnh.

## 12. Điểm bắt đầu chính xác cho phiên sau

1. Dùng branch `codex/kfgz-extended-combat`; **không dùng main**.
2. Đọc file này trước, không audit lại toàn repo.
3. Không làm lại Mine/Treasure/Weapon/Prison/Battle equipment skill/Quest auxiliary vừa hoàn tất.
4. Ưu tiên tiếp **Quest provider gaps có authoritative runtime hiện hữu**; chỉ đọc đúng target/service liên quan.
5. Nếu Quest target phụ thuộc gameplay chưa port hoặc thiếu evidence, bỏ qua ngay và chuyển sang prerequisite khả thi, ưu tiên:
   - equipment `specialSkillId` / quenching special-skill nếu reverse đủ để mở Suit/Proset;
   - sau đó các cross-server reward source chỉ khi mapping + lifecycle đều đủ.
6. Server-first; Unity nối sau vertical slice.
7. Build server + migrations/startup sau block lớn.
8. Gần cuối dự án mới dành một lượt local/Codex lớn cho Unity Editor compile/runtime, scene/prefab refs và Windows/Android build.

## 13. Không làm

- Không code từ `main` cũ.
- Không rerun preprocessing V3/V4/V5 vô cớ.
- Không scan/audit toàn repo để “tìm việc”.
- Không tạo test/checklist/checkpoint/handoff phụ nếu không thật sự cần.
- Không tăng commercial % bằng docs/test/audit.
- Không invent gameplay/reward/static.
- Không redesign UI/UX/gameplay.
- Không overwrite `D:\Sever`.

## 14. Báo cuối phiên chuẩn

Chỉ cần ngắn:
- `DONE`
- `PARTIAL`
- `BLOCKED`
- `BUILD`
- `NEXT`
- `COMMERCIAL %`
