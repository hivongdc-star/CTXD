# CTXD Remake — Source Checkpoint Package

Đây là checkpoint bàn giao source, canonical data, migration, tool reverse/import, Unity assets đã trích xuất cần cho phần hiện tại và legacy JAR/reference tối thiểu.

## Đọc theo thứ tự

1. `CTXD_REMAKE_MASTER_REQUIREMENTS_V2.md`
2. `HANDOFF_NEXT_SESSION.md`
3. `SESSION_REPORT_20260824.md`

## Thư mục

- `Client/Unity` — Unity C# client + legacy assets hiện đã đưa vào project.
- `Server/CTXD.Server` — game server mới.
- `Server/CTXD.Admin` — Windows admin app foundation.
- `Database/Migrations` — PostgreSQL schema/migrations.
- `Data/Canonical` — static game data đã convert.
- `Tools` — importer/SWF extraction/symbol map tools.
- `LegacyReference` — reference tối thiểu dùng để reverse tiếp, không phải production runtime.
- `Generated` — output asset extraction/symbol mapping đã tạo.
- `Scripts` — Windows helper scripts.

## Lưu ý

Checkpoint này chưa được xác nhận build-pass bằng Unity/.NET thật trong sandbox. Không xóa các source/data hiện có chỉ để scaffold lại từ đầu.
