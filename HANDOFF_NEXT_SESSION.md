# CTXD Remake — Bàn giao cho phiên tiếp theo

## Chỉ thị ưu tiên

Đọc `CTXD_REMAKE_MASTER_REQUIREMENTS_V2.md` trước khi sửa code.

Mục tiêu dự án là **remake code/công nghệ**, không redesign game:

- Client mới: Unity + C#, Windows x64 + Android ARM64.
- Server mới: C#/.NET trên Windows.
- Database mới: PostgreSQL sạch.
- Network mới: HTTPS + WebSocket.
- UI/UX/art/animation/gameplay/flow phải bám CTXD Flash cũ.
- Không migrate account/player/mail/rank/log/runtime-state cũ.
- Không tạo framework test/check lớn; ưu tiên implementation thật.
- `LegacyReference` là dữ liệu tham chiếu, không phải production dependency cuối.

## Trạng thái checkpoint

Checkpoint hiện tại là source đang phát triển của first playable và các subsystem tiếp theo.

### Đã có source

**Server**

- Auth/Register/Login/session.
- Player/force/name/picture flow.
- Random name theo `NameData.xml`.
- MainCity.
- Building upgrade + completion worker.
- Passive resource production.
- Tutorial/task progression cơ bản.
- General roster.
- Tavern refresh/lock/recruit.
- Equipment store/inventory/equip/unequip/sell.
- Technology inject/research/completion.
- WebSocket push.
- Windows Admin app foundation.

**Unity**

- FirstPlayable runtime app.
- Login/player/main-city flow.
- Legacy UI factory.
- Tavern panel.
- General roster panel.
- Equipment panel.
- Technology panel.
- Realtime WebSocket client.
- Asset import/editor build helpers.

**Data**

Canonical importer hiện tạo:

- 80 Building
- 236 Task
- 2,466 General
- 34 General Recruit
- 115 Equipment
- 131 Technology
- 803 Troop
- 228 Tactic
- 254 World City
- 393 World Road
- 36 Tavern transitions
- 13 Equip Suit rules
- 108 Store Item rules
- 49 Store transitions
- General positions, constants, charge items, names, items, functions, serial data.

## Thay đổi quan trọng của phiên vừa bàn giao

### 1. Technology effect được tách thành projection riêng

Mới: `Server/CTXD.Server/Services/TechnologyEffectService.cs`.

Nó tái hiện behavior của legacy `TechEffectCache`: chỉ cộng effect của `player_technologies.status = 5`, theo `key_id` và parameter.

Mục đích: Resource/General/World/Battle có thể đọc TechEffect mà không tạo dependency cycle với `TechnologyService`.

### 2. General slot đã nối Technology thật

`GeneralService` hiện tính:

- Civil General max = slot mở theo level + TechEffect key **32**.
- Military General max = slot mở theo level + TechEffect key **27**.

`TavernService` đã dùng max slot async này khi recruit và khi trả Tavern state.

Legacy evidence đã reverse bằng `javap` từ `gcld.jar`: General/Tavern path gọi `TechEffectCache.getTechEffect(playerId,27/32)`.

### 3. Resource production đã nối Technology thật

Reverse trực tiếp `BuildingOutputCache` + `ResourceService` từ `gcld.jar` cho thấy:

- Resource type 3 (Food) nhận production tech key **6**.
- Tech output = `baseBuildingOutput * TechEffect(6) / 100`, truncate integer.
- Resource capacity buildings (`outputType=4`) nhận tech key **20**.
- Capacity = `baseCapacity * (1 + TechEffect(20)/100)`, truncate.
- Legacy fallback khi chưa có warehouse vẫn là base 10,000 rồi mới áp tech capacity.

`ResourceProductionService` hiện thực hiện đúng các rule trên.

### 4. Technology completion không áp bonus ngược thời gian

Trước khi chuyển một tech `status 4 -> 5`, server settle passive resources đến đúng `research_complete_at` bằng effect cũ. Sau đó mới enable effect mới.

Điều này tránh trường hợp player offline lâu rồi tech vừa hoàn thành nhưng toàn bộ interval trước đó bị tính bằng production/capacity bonus mới.

### 5. Technology completion push các projection bị ảnh hưởng

`TechnologyCompletionWorker` sau completion push:

- `technology.updated`
- `generals.updated`
- `maincity.updated`

### 6. Technology UI progress

`TechnologyPanel` đổi progress image từ `Image.Type.Sliced` sang `Image.Type.Simple` để không yêu cầu sprite border/sliced metadata trên bitmap Flash extract.

## Validation đã chạy trong sandbox

- `python Tools/legacy_static_import.py` — PASS.
- Import lại toàn bộ canonical data — PASS.
- `python -m py_compile Tools/*.py` — PASS.
- Không có merge-conflict marker trong Server/Unity Scripts/Database.
- Kiểm tra coarse brace balance cho 38 file C# — không phát hiện file bị cụt.

**Chưa thể gọi là build-pass** vì sandbox phiên này không có `dotnet`, `csc` hoặc Unity Editor.

Không được báo build thành công nếu chưa chạy trên toolchain thật.

## Điểm tiếp tục chính xác

Không quay lại Phase 0, không viết roadmap mới.

Thứ tự ưu tiên phiên kế:

1. Review nhanh các file vừa đổi để tránh compile issue khi toolchain có sẵn:
   - `TechnologyEffectService.cs`
   - `ResourceProductionService.cs`
   - `GeneralService.cs`
   - `TavernService.cs`
   - `TechnologyService.cs`
   - `TechnologyCompletionWorker.cs`
   - `TechnologyPanel.cs`
2. Tiếp tục port **Technology effects chỉ cho subsystem đã tồn tại**; không bịa effect cho World/Battle chưa port.
3. Hoàn thiện tutorial/quest progression đi qua General/Tavern/Equipment/Technology.
4. Chuyển sang **World/Nation foundation** từ legacy `world_city`, `world_road`, Java WorldAction/WorldService và SWF World/World2.
5. Chưa bắt đầu Battle full trước khi World movement/state foundation tồn tại.

## Known limitations

- Chưa compile/run bằng Unity/.NET thật trong checkpoint này.
- Officer/resource-addition bonus chưa port vào BuildingOutputCache remake.
- Recruitment speed tech key 8 thuộc troop conscription, chưa port vì troop-conscription subsystem chưa có.
- Nhiều Technology effects liên quan World/Battle/Gem/Quenching chưa nối; giữ đúng nguyên tắc port khi subsystem tương ứng được triển khai.
- UI fidelity đang dùng asset Flash extract nhưng chưa phải toàn bộ màn hình 1:1.
- World/Nation/Battle/Activity/Cross-server còn ở giai đoạn rất sớm.

## Tiến độ checkpoint

Ước lượng toàn dự án so với **thành phẩm CTXD đủ chuẩn thương mại**: **~16% / 100%**.

Không tăng % chỉ dựa vào số file. Phần lớn trọng lượng còn lại nằm ở World/Nation, Battle, toàn bộ visual fidelity, content systems còn thiếu, Android validation và production hardening.
