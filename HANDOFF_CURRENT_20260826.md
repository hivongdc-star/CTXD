# CTXD Remake — Current Handoff (2026-08-26)

> Đây là handoff authoritative mới nhất cho phiên tiếp theo. Không dùng handoff 24/08 hoặc 25/08 làm source of truth.

## Mục tiêu dự án
- Remake công nghệ, **không redesign** gameplay/UI/UX/art/animation/flow legacy.
- Client: Unity + C#, target Windows x64 + Android ARM64, một codebase.
- Server: C#/.NET 8 / ASP.NET Core, server-authoritative.
- DB: PostgreSQL clean schema.
- Network: HTTPS + WebSocket.
- Legacy chỉ dùng làm source/reference; thành phẩm không phụ thuộc Flash/ActionScript runtime, Java 6, Apache/PHP, MySQL 5.5 hay legacy TCP.
- Chỉ migrate static definition/content cần thiết; không migrate account/player/runtime/history/session/cache cũ.

## Quy tắc làm việc đã chốt
- Ưu tiên **implementation/remake code thật**, không tăng tiến độ bằng audit/checklist/test/docs dư thừa.
- Backend/server đi trước một nhịp, client nối theo từng vertical slice.
- Không scan/audit lại toàn repo nếu không có blocker cụ thể.
- Không chạy lại preprocessing V3/V4/V5 nếu chưa có lý do trực tiếp.
- Không tự bịa rule/reward/timer/cost/static mapping. Thiếu evidence thì ghi `BLOCKED` và chuyển module khác.
- Chỉ build/startup validation tối thiểu sau block implementation đáng kể.
- Ưu tiên additive changes; không phá API/schema/runtime hiện có.
- Đường dẫn Windows:
  - legacy: `D:\Sever`
  - remake: `D:\SeverRMK`
  - working source: `D:\SeverRMK\CTXD_Remake_Working`
  - legacy extracted: `D:\SeverRMK\LegacyReference`
  - remake input: `D:\SeverRMK\RemakeInput`
- Không overwrite `D:\Sever`.

## Repo / branch hiện tại
- GitHub: `hivongdc-star/CTXD`
- Branch authoritative: `codex/kfgz-extended-combat`
- HEAD trước khi cập nhật handoff này: `1120b232c3afa0e687d115b3d198fd338b9640d6`
  - message: `Add safe Farm early-claim flow`
- PR #1: branch trên -> `main`.
- `main` vẫn cũ hơn đáng kể; **không tiếp tục từ main** nếu chưa merge branch này.
- Local của user đã đồng bộ bằng:
  - `git fetch origin`
  - `git switch codex/kfgz-extended-combat`
  - `git reset --hard origin/codex/kfgz-extended-combat`
- Local xác nhận HEAD `1120b23` và server đã chạy được trên máy user.

## Commercial completeness
- Mốc hiện tại: **~66% commercial-complete**.
- Giữ cách tính liên tục từ đây; không tự đổi mẫu số.
- Chỉ tăng khi có functionality thật; docs/audit/test-only không tăng %.

## Server status tổng thể
- Server **chạy được**, nhưng chưa phải 100% server game.
- Ước lượng riêng server gameplay hiện khoảng **80–85%** chức năng cần remake.
- Đã có boot thật, PostgreSQL, migrations, canonical legacy data, API/gameplay đã port.
- User đã chạy local server thành công; phần cần kiểm tiếp chủ yếu là Unity compile/runtime và các gameplay chưa port.

## DONE — core hiện có
### Core game
- Auth / player / force selection / main city.
- Building / resource production.
- General / Tavern.
- Equipment / Store / Inventory.
- Technology.
- World movement / fog / auto-path / conquest boundary.
- Battle server-authoritative: tactic/strategy/equipment/technology/reward/reinforcement.
- Nation core / office / politics / civil affair / Nation Trial / Nation Task / scheduled nation/world tasks / ranking/reward phần có evidence.
- Quest nhiều providers.
- Mail server + Unity + realtime + atomic/idempotent attachments.
- Market / black market / recovery.
- Chat / Team core.
- Activities: Online/Daily/Battle EXP/Level EXP/Dragon/Iron/DSTQ + scheduler/push.
- Pay/VIP phần có evidence.
- Global level rank exact legacy `rankId=1`: `/api/rank/1`, max 200, sort level desc only, Unity panel.

### Cross-server
- KFWD core.
- KFZB core + support/Feast phần có evidence.
- KFGZ core + extended combat:
  - season/phases/signup/sync
  - dynamic world/city/road
  - deployment/movement
  - battle handoff/result
  - timeout/settlement/ranking
  - persistence/reconnect
  - resource/per-general restore
  - retreat 10% HP
  - occupy authoritative only on real owner change
  - Rush
  - Fast Recruit
  - Phantom
  - Call-General/Reinforcement
  - Mubing lifecycle/worker
- BattleDrop resource settlement foundation; mapping 4/1004 -> iron + duplicate protection.

## DONE — Auto Battle + Recruit Recovery
### Auto Battle
Files chính:
- `Server/CTXD.Server/Services/AutoBattleService.cs`
- `AutoBattleWorker.cs`
- `AutoBattleEndpoints.cs`
- Unity `Networking/AutoBattleApi.cs`
- Unity `Features/World/AutoBattlePanel.cs`
- migration `052_auto_battle.sql`

Đã port:
- TechEffect 59.
- start cost 50,000 food.
- duration 30 phút.
- worker cadence 10s, scheduler wake 5s.
- attack/defense autoType theo ownership/active battle.
- movement/reinforcement/battle handoff/advance.
- result `1/2/3/4/5` theo legacy.
- EXP/lost tracking runtime.
- timeout/ownership/result settlement.
- manual stop.
- duplicate charge/race protection bằng lock + re-read.
- Unity panel + realtime `auto-battle.updated`.

### Recruit Recovery
Files/data:
- `Server/CTXD.Server/Services/RecruitRecoveryService.cs`
- migration `053_recruit_recovery.sql`
- `Data/Canonical/troop_conscribe_speed.json`
- `Data/Canonical/world_city_area.json`

Legacy exact evidence đã port:
- `Troop.Conscribe.BaseSpeed = 40000`.
- recruit tokens: base 20/day; consume level >=4 thêm +10; max semantics 100.
- chargeitem 13 param 90 phút/token.
- passive recovery + token recovery + food consumption.
- city-area recruit speed.
- TechEffect8 và TechEffect28 handling.
- legacy partial/full recovery behavior, kể cả timestamp quirk.
- Auto Battle gọi recovery trước khi general move/join/battle.

Known parity gap:
- legacy `player_resource_addition` contribution vào recruit/building type5 chưa có runtime system tương ứng trong remake => hiện contribution = 0.

## DONE/PARTIAL — World Farm / Truân Điền
### Static source đã giải blocker
Snapshot deploy `gcld.3.1/sdata.zip` chứa nguyên static table, không chỉ `.frm`.
Đã tìm và port authoritative:
- `farm`: 13 levels.
- `farm_coe`.
- chargeitem 78/86 liên quan Farm.

### Server Farm đã implement
Files chính:
- `Data/Canonical/farm.json`
- `Data/Canonical/farm_coe.json`
- `Database/Migrations/054_world_farm.sql`
- `Server/CTXD.Server/Services/FarmService.cs`
- `Server/CTXD.Server/Services/FarmEndpoints.cs`

Đã port:
- Farm city: Wei 254 / Shu 253 / Wu 206.
- open level 30.
- initial Farm lv1, invest 0.
- invest: 10,000 copper + 1,000 player EXP.
- invest CD step 10 phút, queue cap 1 giờ.
- clear invest CD: ChargeItem 78, `ceil(remaining / 10 min) * 1 gold`.
- token item 1701/type20.
- Farm action type 0/1/2/3 -> general states 25/26/27/28; idle state 24.
- food/time/reward coefficients từ static legacy.
- early stop reward theo elapsed fraction.
- early claim gold theo ChargeItem 86.
- gold-action request-key ledger chống double charge.
- full/partial reward settlement.
- Farm food reward hoặc General EXP reward theo type.
- completion tạo buff 30 phút.
- auto-start khi general vào Farm city theo legacy flow.
- state24 có thể rời Farm city qua World movement.

### Battle Farm buff đã ghép
Commit: `2d234eeb082e5ae49b4f5800dc01ec5c5206a622`
- Legacy bytecode xác nhận `WorldFarmService.getBuff()/100` được cộng additive.
- Remake áp `+50%` vào **Player EXP và General EXP** của đúng `player + general`.
- Không tăng damage/copper.
- Không áp phantom.

### Unity Farm đã nối
Files:
- `Client/Unity/Assets/Game/Scripts/Networking/FarmApi.cs`
- `Client/Unity/Assets/Game/Scripts/Features/World/FarmPanel.cs`
- `WorldPanel.cs` có nút `Truân Điền`.

Client hiện có:
- xem Farm state.
- đầu tư.
- xóa invest CD.
- chọn loại Farm.
- chọn general.
- start/stop/claim/stop-all.
- xem buff +50% EXP.
- early-claim an toàn: xem giá trước, nhấn lần hai xác nhận rồi server mới trừ vàng.

Farm còn PARTIAL nhỏ:
- ChargeItem 82 semantic chưa đủ evidence, chưa invent.
- `FARMING_GENERAL_NUMBER=20` đã thấy constant nhưng chưa xác định chính xác enforcement point, chưa ép.
- Unity Editor compile toàn project chưa được xác nhận.

## Build/runtime status
- GitHub Actions server build đã nhiều lần PASS:
  - `dotnet build Server/CTXD.Server/CTXD.Server.csproj --configuration Release --nologo`
  - PostgreSQL migrations + server startup `/health` PASS cho các block server trước HEAD.
- Commit Farm battle buff đã PASS build + migration/startup ở CI.
- User đã pull/reset đúng HEAD và xác nhận server chạy local thành công.
- CI workflow hiện chỉ cover server; **không coi Unity compile là PASS** nếu chưa mở/batch compile Unity thực tế.
- Unity project declares `6000.0.0f1`.

## BLOCKER STATIC-DATA CŨ ĐÃ ĐƯỢC GỠ
`sdata.zip` deploy đã xác nhận có authoritative tables:
- `mine` — 162 rows.
- `treasure`.
- `arms_weapon`.
- `prison_lv`.
- `prison_reward`.
- `prison_lash_reward`.
- `prison_degree`.
- `prison_catch_prob`.

=> Mine / Treasure / Weapon / Prison-Slave không còn bị BLOCKED chỉ vì thiếu static rows.

## MODULE TIẾP THEO — Mine / Khoáng Trường
Đã reverse đủ foundation để implementation server-first ngay:
- legacy runtime/service/action tồn tại: `getMineInfo / rush / abandon / mine`.
- 162 mine rows.
- mine type 1/2 = iron; type 3/4 = gem.
- 5 mine pages.
- battle type legacy:
  - personal mine = 6.
  - force/group mine = 7.
- Rush chỉ trong 15 phút đầu.
- Rush làm production stage sau chạy x2.
- hết thời gian tự settlement.
- bị chiếm: chủ cũ nhận 50% accumulated output.
- iron mine có stone item 1401 khi Blacksmith condition thỏa.
- force mine có daily harvest nếu nation đang sở hữu.
- output/time/stone formulas đã reverse từ bytecode/static.

**Việc tiếp theo:** implement Mine migration + canonical import + service + endpoints + Battle integration + worker/settlement + Unity minimal slice, rồi build/startup validation.

## Các PARTIAL/BLOCKED còn đáng chú ý
- KFGZ ranking/title/end reward: coordinator reward strings ngoài source hiện có.
- KFWD day/treasure reward chưa đủ mapping chắc chắn.
- KFZB treasure/title reward mapping chưa đủ.
- Feast organizer/rank phần cần authoritative coordinator data.
- VIP6_1 premium/Jinpin mapping chưa đủ.
- Quest providers phụ còn tùy gameplay chưa port.
- Battle special equipment/suit effects chưa full parity.
- precise SWF frame animation mapping chưa full.
- production/commercial hardening: monitoring/backup/security/liveops/release pipeline chưa hoàn chỉnh.

## Điểm bắt đầu chính xác cho phiên sau
1. Không scan/audit toàn repo.
2. Dùng branch `codex/kfgz-extended-combat`, HEAD `1120b23` hoặc commit mới hơn nếu handoff update tạo commit mới.
3. Farm không cần làm lại trừ khi có bug cụ thể.
4. Bắt đầu trực tiếp **Mine** từ legacy evidence đã reverse.
5. Chỉ đọc Java/ActionScript/static liên quan Mine nếu cần chốt công thức còn thiếu.
6. Implement migration/service/API/Battle/lifecycle trước, Unity nối sau.
7. Build server + PostgreSQL startup sau block lớn.
8. Sau Mine chuyển Treasure / Weapon / Prison theo evidence, không quay lại audit.

## Không làm
- Không code từ `main` cũ.
- Không rerun preprocessing V3/V4/V5 vô cớ.
- Không tăng % bằng docs/tests/audit.
- Không invent gameplay/static.
- Không redesign UI/UX/gameplay.
- Không overwrite `D:\Sever`.

## Cách báo cuối phiên
- `DONE`
- `PARTIAL`
- `BLOCKED`
- `MODULE`
- Session 2026-08-26 implementation update:
  - DONE Mine: canonical 162 rows, migration `055_world_mine.sql`, server service/endpoints/worker, battle type 6/7 terrain 9, ownership settlement, rush/abandon/auto-settle, 50/50 capture payout, force daily harvest, Unity minimal vertical slice + realtime.
  - PARTIAL Mine: stone 1401 is not granted because authoritative Blacksmith runtime/unlock state is not present; Unity Editor compile remains unverified.
  - DONE/PARTIAL Treasure: canonical 10 rows, migration `056_treasures.sql`, inventory endpoint, Politics type5 drop at exact `0.001` with function 20 gate, Battle ATT/DEF/BASE effects, Unity inventory panel + realtime. Incense/Search/Store acquisition hooks remain pending with their owning gameplay.
  - BUILD: `dotnet build CTXD.Server.csproj` PASS, 0 warnings / 0 errors. Startup stopped because PostgreSQL `127.0.0.1:5432` was offline in this environment.
  - NEXT: Weapon / Binh Khi server-first from `arms_weapon` and `WeaponService.java`; migration/state, forge blueprint type6 + exact costs, serial/crit upgrade, Battle ATT/DEF/HP, then Unity minimal slice. Do not invent gem socket rules.
  - COMMERCIAL: new baseline **~68%**.
- `BUILD`
- `COMMERCIAL %` — giữ baseline hiện tại ~66%, chỉ tăng bằng functionality thật.
