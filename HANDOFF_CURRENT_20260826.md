# CTXD Remake — Current Handoff (2026-08-26)

> Đây là handoff authoritative mới nhất cho phiên tiếp theo. `HANDOFF_CURRENT_20260825.md` và `HANDOFF_NEXT_SESSION.md` chỉ còn giá trị lịch sử.

## Mục tiêu dự án
- Remake công nghệ, **không redesign** gameplay/UI/UX/art/animation/flow legacy.
- Client: Unity + C#, target Windows x64 + Android ARM64, một codebase.
- Server: C#/.NET / ASP.NET Core, server-authoritative.
- DB: PostgreSQL clean schema.
- Network: HTTPS + WebSocket.
- Legacy chỉ dùng làm source/reference; không phụ thuộc Flash/ActionScript runtime, Java 6, Apache/PHP, MySQL 5.5 hay legacy TCP ở thành phẩm.
- Chỉ migrate static definition/content cần thiết; không migrate account/player/runtime/history/session/cache cũ.

## Quy tắc làm việc đã chốt
- Ưu tiên **implementation/remake code thật**, không tăng tiến độ bằng audit/checklist/test/docs dư thừa.
- Backend/server đi trước một nhịp, client nối theo từng vertical slice.
- Không scan/audit lại toàn repo nếu không có blocker cụ thể.
- Không chạy lại preprocessing V3/V4/V5 nếu chưa có lý do trực tiếp.
- Không tự bịa rule/reward/timer/cost/static mapping. Thiếu evidence thì ghi `BLOCKED` và chuyển module khác.
- Chỉ build/startup validation tối thiểu sau block implementation đáng kể.
- Ưu tiên additive changes; không phá API/schema/runtime hiện có.
- Đường dẫn Windows phải giữ đúng casing:
  - legacy: `D:\Sever`
  - remake: `D:\SeverRMK`
  - working source cũ: `D:\SeverRMK\CTXD_Remake_Working`
  - legacy extracted: `D:\SeverRMK\LegacyReference`
  - remake input: `D:\SeverRMK\RemakeInput`
- Không overwrite `D:\Sever`.

## Repo / branch hiện tại
- GitHub: `hivongdc-star/CTXD`
- Branch đang làm: `codex/kfgz-extended-combat`
- Latest verified branch commit khi tạo handoff: `38f19d9537017c52bd091ea6210da04460caaa9e`
  - message: `Add reusable world battle reinforcement`
- Commit ngay trước:
  - `6cca7dcf2a6e940ec13e270479848fb10523c82a` — Auto Battle persistence migration.
- Pull request hiện có: PR #1, branch trên -> `main`.
- `main` cũ hơn đáng kể; **không tiếp tục từ main** nếu chưa merge branch này.

## Commercial completeness
- Báo bảo thủ: **~63–64% commercial-complete**.
- Không tăng % vì documentation/audit/test-only work.

## DONE — core hiện có
### Core game
- Auth / player flow / force selection / main city.
- Building / resource production.
- General / Tavern.
- Equipment store + inventory.
- Technology.
- World movement / visibility / conquest boundary.
- Battle engine server-authoritative, reinforcement/action/reward integration.
- Nation core, office/politics/civil-affair, Nation Trial, Nation Task, ranking/reward phần có evidence.
- Quest vertical slices + nhiều providers.
- Mail server + Unity + realtime + atomic/idempotent attachments.
- Market + black market + cooldown/recovery/special-city bonus.
- Chat COUNTRY/ONE2ONE + history/blacklist/realtime + Unity.
- Team core: teamTimes, deploy, type-1 duel, inspire/order/result/reward.
- Activities: scheduler, Online Gift, Daily Gift, Battle EXP, Level Growth EXP, Dragon, Iron, DSTQ phần có evidence.
- Pay/VIP entitlement + benefits đã có evidence.

### Cross-server
- KFWD core: signup/sync, scheduler, matchmaking, multi-round, Battle/result, timeout, ranking, history, API/push/Unity.
- KFZB core: signup/sync, phases, bracket, Battle/result, elimination, timeout, persistence, spectator, API/push/Unity.
- KFZB Feast/support phần có evidence.
- KFGZ core + extended combat đã đi xa hơn handoff 25/08:
  - season/phases/signup/competitor sync
  - dynamic world/city/road
  - deployment/movement
  - Battle handoff/result
  - timeout/settlement/ranking
  - persistence/reconnect
  - resource/per-general state restore
  - retreat theo legacy
  - occupy settlement chỉ cộng khi ownership thực sự đổi
  - Rush endpoint/service
  - Fast Recruit endpoint/service
  - Phantom endpoint/service
  - Call-General + KFGZ reinforcement endpoint/service
  - KFGZ Mubing lifecycle/worker
- BattleDrop resource settlement foundation; mapping 4/1004 -> iron + chống grant lặp.

## DONE mới trong phiên 25→26/08
### Global Level Ranking
Port đúng legacy `RankAction.send(rankId=1)`:
- Endpoint: `GET /api/rank/1`
- Authenticated.
- Tối đa 200 player.
- Semantics legacy: `ORDER BY player level DESC`; **không dùng EXP để phá hòa**, không thêm lực chiến/metric khác.
- Payload giữ các field tương đương `playerId / playerName / playerLv`.
- Unity đã có Rank API/model/panel và nút `Xếp hạng` ở main city.
- Server CI cho Rank block: **PASS** `dotnet build` + PostgreSQL migrations/startup.

### World Battle Reinforcement foundation
File mới:
- `Server/CTXD.Server/Services/WorldBattleReinforcementService.cs`

Đã hỗ trợ:
- join/reinforce world city battle type 3/14.
- xác định side theo force của player: attacker hoặc defender.
- general phải sẵn sàng, có quân, đúng battle city.
- chống duplicate general.
- materialize `battle_units` theo general/troop/equipment/technology/tactic/terrain hiện có.
- chuyển general sang battle state.

Foundation này được thêm để phục vụ Auto Battle/auto-defense và reusable cho quốc chiến thường.

### Auto Battle persistence foundation
Migration mới:
- `Database/Migrations/052_auto_battle.sql`

Table:
- `player_auto_battles`

State đã chuẩn bị gồm:
- force/target city/state/auto type
- exp/lost/result
- baseline exp/lost
- started/ends/need-check timestamps
- active due index.

## PARTIAL — ưu tiên tiếp tục ngay
### 1. Auto Battle / tự động quốc chiến — PRIORITY HIGHEST
Legacy đã reverse đủ nhiều để tiếp tục code ngay:
- lifecycle legacy: `start / stop / detail`.
- yêu cầu TechEffect **59**.
- start cost: **50,000 food**.
- duration: **30 phút**.
- daemon/worker cadence: **10 giây**.
- target ownership quyết định:
  - `autoType=1`: xue-zhan / công thành.
  - `autoType=2`: jian-shou / phòng thủ.
- timeout result:
  - attack timeout -> result `2`.
  - defense timeout -> result `5`.
- ownership change/other stop result theo legacy đã reverse: `1 / 3 / 4` tùy nhánh.
- `exp` phải dựa player EXP thực nhận từ battle reward.
- `lost` phải dựa quân tổn thất thực tế.
- Có thể lấy delta từ runtime hiện tại bằng baseline của `battle_rewards` + battle damage/rounds, tránh sửa sâu BattleService.

**Việc cần làm ngay:**
1. Tạo `AutoBattleService`.
2. Implement `Get/Detail`, `Start`, `Stop` với transaction.
3. Worker 10 giây:
   - kiểm tra timeout/ownership/result;
   - điều khiển general idle/movement vào target;
   - dùng `WorldBattleReinforcementService` để reinforce attacker/defender;
   - advance battle theo lifecycle authoritative hiện có.
4. Map API endpoints.
5. Nối Unity World panel tối thiểu theo legacy flow.
6. CI build + clean PostgreSQL migration/startup.

### Auto recruit/heal trong Auto Battle
- **PARTIAL/BLOCKED** nếu cần exact `TroopConscribeSpeed` mà chưa có authoritative mapping.
- Không tự dựng tốc độ hồi quân.
- Phần Auto Battle không phụ thuộc exact recruit-speed vẫn implement trước.

## PARTIAL / BLOCKED static-data families
Các family dưới đây có service/schema legacy nhưng static rows authoritative hiện không tìm thấy trong `gcld_sdata`; archive chủ yếu chỉ còn `.frm`:
- Farm
- Mine
- Treasure
- Weapon-related static family
- Prison/Slave static coefficients

### Farm evidence đã reverse được nhưng chưa đủ reward rows
Có thể tin cậy:
- methods: `investFarm`, `start/doStart`, `stop`, `getFarmInfo`, `startAll`, `getRecoverCostGold`, `recoverGold`, `stopAll`, `rewardPlayerGeneral`, `getReward`.
- farm action type `0/1/2/3` map general state `25/26/27/28`.
- buff sau kết thúc: **30 phút**.
- `getBuff()` trả **50**.
- invest queue cộng **10 phút**, chặn nếu queue vượt **1 giờ**.
- charge item id `78`: farm invest CD, `param=10`, `cost=1` (10 phút / 1 gold).
- charge item id `82`: farm gold training, `cost=5`.

Thiếu:
- authoritative row values cho Farm reward/time/food coefficients.
=> Giữ Farm `PARTIAL/BLOCKED`, không dựng reward giả.

## Các BLOCKED cũ vẫn giữ
- KFGZ ranking/title/end reward: authoritative reward strings nằm coordinator DB ngoài source hiện có.
- KFWD day/treasure reward: thiếu gateway rows / ranking-day / KfwdRankTreasure mapping chắc chắn.
- KFZB treasure reward mapping chưa đủ evidence.
- Feast room cần coordinator cung cấp organizer rank authoritative.
- VIP6_1 còn phụ thuộc premium-equipment/Jinpin mapping chưa chắc chắn.
- Một số Quest providers còn phụ thuộc gameplay chưa port.
- Một số Iron source/provider phụ có thể còn thiếu nếu mapping không xuất hiện.

## Build/runtime status
- Rank server block đã được GitHub Actions xác nhận **PASS**:
  - `dotnet build Server/CTXD.Server/CTXD.Server.csproj --configuration Release`
  - apply migrations + PostgreSQL startup PASS.
- Latest Auto Battle migration + world reinforcement commits cần **CI verification lại** sau khi AutoBattleService/worker được nối hoàn chỉnh; không ghi PASS giả cho latest HEAD nếu chưa kiểm tra.
- Unity Rank panel đã commit nhưng Unity Editor compile toàn project vẫn chưa được xác nhận trong môi trường này.

## Điểm bắt đầu chính xác cho phiên sau
1. **Không scan/audit toàn repo.**
2. Checkout/đọc branch `codex/kfgz-extended-combat`, latest từ `38f19d9537017c52bd091ea6210da04460caaa9e` hoặc commit mới hơn nếu có.
3. Đọc:
   - `Database/Migrations/052_auto_battle.sql`
   - `Server/CTXD.Server/Services/WorldBattleReinforcementService.cs`
   - `WorldService.cs`
   - `BattleService.cs`
   - legacy AutoBattle Action/Service trực tiếp liên quan.
4. Implement ngay `AutoBattleService + worker + endpoints`.
5. Nối Unity WorldPanel sau khi server lifecycle chạy.
6. Auto-recruit exact speed thiếu evidence thì giữ BLOCKED; không để nó chặn phần Auto Battle còn lại.
7. Build + PostgreSQL startup sau block lớn.
8. Khi Auto Battle đủ vertical slice, chuyển ngay module gameplay lớn tiếp theo có evidence; tránh quay lại static family mất row nếu chưa tìm được authoritative source mới.

## Không làm ở phiên sau
- Không quay về `main` cũ và code đè lên branch current.
- Không rerun V3/V4/V5 preprocessing chỉ để tìm lại dữ liệu đã biết là thiếu.
- Không tăng % bằng test/docs/audit.
- Không tự invent Farm/Mine/Treasure/Prison coefficients.
- Không redesign UI/gameplay.

## Cách báo cuối phiên
Ngắn, theo format:
- `DONE`
- `PARTIAL`
- `BLOCKED`
- `MODULE`
- `BUILD`
- `COMMERCIAL %` (conservative)
