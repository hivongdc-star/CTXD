# CTXD Remake — Current Handoff (2026-08-25)

## Mục tiêu dự án
- Remake công nghệ, không redesign.
- Client: Unity + C#, Windows x64 + Android ARM64.
- Server: C#/.NET, server-authoritative.
- DB: PostgreSQL.
- Network: HTTPS + WebSocket.
- Giữ UI/UX/art/animation/gameplay/flow theo legacy Flash.
- Không migrate account/runtime data cũ.
- Legacy chỉ dùng làm reference; không tự bịa rule/reward/timer/mapping.

## Workflow đã chốt
- Ưu tiên implementation thật, không audit/scan lại toàn project.
- Chỉ đọc Java/ActionScript/static data trực tiếp liên quan module đang làm.
- Không chạy lại preprocessing V3/V4/V5.
- Không test suite/checklist/docs dư thừa; chỉ build/check tối thiểu sau block lớn.
- Blocker cục bộ thì chuyển nhánh khác; không dừng sớm nếu còn việc có evidence.
- Server đi trước, client nối theo vertical slice.
- DONE chỉ khi lifecycle chính + persistence/result/reward + integration/client cần thiết đủ; foundation/happy path = PARTIAL.
- % tính bảo thủ theo commercial-complete functionality.

## Repo hiện tại
- GitHub: `hivongdc-star/CTXD`
- Source remake: root repo hiện tại.
- Legacy reference có trong `LegacyReference/GCLDServer/`.
- `HANDOFF_NEXT_SESSION.md` là checkpoint rất cũ (~16%); không dùng làm current state.

## Trạng thái hiện tại — ~62% commercial

### DONE / core đã chạy
- World core đến Battle boundary.
- Battle core server-authoritative.
- Nation core + nation tasks chính, ranking/end-state/reward đã chốt phần có evidence.
- Quest vertical slices + nhiều gameplay providers; còn một số provider phụ thuộc gameplay chưa port.
- Mail server + Unity + realtime + atomic/idempotent attachments.
- Store/Market core, black-market, special-city bonus, cooldown/recovery transaction.
- Social Chat COUNTRY/ONE2ONE + history/realtime/blacklist + Unity.
- Social Team core: teamTimes, deploy, type-1 duel, inspire/order/reward/result/API/push/Unity.
- Activities core: scheduler, Online Gift, Daily Gift, Battle EXP, Level Growth EXP, Dragon/Iron/DSTQ phần có evidence.
- Pay/VIP runtime entitlement + VIP2–7 benefits có evidence; VIP 7_1 → teamTimes.
- KFWD core: signup/sync, phase scheduler, matchmaking, multi-round, Battle handoff/result, timeout, ranking, win history, API/push/Unity.
- KFZB core: signup/sync, phases, bracket, Battle/result, elimination, timeout, persistence, spectator, API/push/Unity.
- KFZB support claim + Feast cards + title settlement phần có evidence.
- KFGZ core + extended combat lifecycle:
  - season/phases/signup/competitor sync
  - dynamic world/city/road
  - deployment/movement
  - Battle handoff/result
  - timeout/settlement/ranking
  - persistence/reconnect
  - resource + per-general state restore
  - retreat theo legacy: city kề cùng phe, đích không có battle, mất 10% HP, auto-resolve khi phe rút hết
  - occupy settlement chỉ cộng khi ownership thực sự đổi
  - API/push/Unity
- KFZB Feast vertical slice:
  - organizer sync
  - window 1 ngày
  - room 10 người
  - timeout 3 phút
  - drink 500 gold
  - card resolution
  - title/ticket
  - idempotent ledger
  - API/push/Unity
- BattleDrop resource settlement foundation; mapping 4/1004 → iron đã nối + ledger chống grant lặp.

## PARTIAL / việc tiếp theo
### KFGZ ưu tiên cao nhất
- Rush.
- Fast recruit.
- Phantom.
- Call-general.
- Cần reverse đúng battle-team/resource-token foundation rồi implement reusable tối thiểu.

### Cross-server/reward còn thiếu evidence
- KFGZ ranking/title/end reward: reward strings nằm trong coordinator DB ngoài, source hiện không có giá trị.
- KFWD day/treasure reward thiếu gateway rows/ranking-day/KfwdRankTreasure mapping chắc chắn.
- KFZB treasure reward mapping chưa đủ evidence.
- Feast room chỉ mở khi coordinator cung cấp authoritative organizer rank.

### Nợ cũ
- VIP6_1 phụ thuộc premium-equipment/Jinpin mapping chưa chắc chắn.
- Một số Quest providers còn phụ thuộc gameplay chưa port.
- Một số Iron source/provider phụ có thể còn thiếu nếu mapping chưa xuất hiện.

## Build/runtime status
- Server build gần nhất: PASS, 0 warnings, 0 errors.
- Unity code đã được nối nhiều vertical slice nhưng chưa xác nhận Unity Editor compile trong môi trường gần nhất.
- Một lần runtime migration check bị block vì PostgreSQL local `127.0.0.1:5432` không chạy; không coi đó là code blocker nếu chỉ đang implementation.

## Điểm tiếp tục chính xác cho phiên mới
1. Không scan/audit lại repo.
2. Mở `KfgzService.cs` + migration KFGZ hiện tại và legacy trực tiếp liên quan rush/fast recruit/phantom/call-general.
3. Port các foundation battle-team/resource-token cần thiết, tối thiểu và reusable.
4. Chốt tối đa KFGZ extended combat có evidence.
5. Mapping reward thiếu chắc chắn thì giữ BLOCKED, không bịa.
6. Nếu KFGZ đủ core/extended gameplay, chuyển ngay module gameplay lớn kế tiếp có mapping legacy rõ.
7. Build server sau block lớn; sửa compile error nếu có rồi tiếp tục.

## Cách báo cuối phiên
Chỉ báo ngắn:
- DONE
- PARTIAL
- BLOCKED
- MODULE
- BUILD
- COMMERCIAL % (conservative)
