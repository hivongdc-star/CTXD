# Báo cáo phiên — CTXD Remake

## Kết quả chính

Phiên này tiếp tục implementation, tập trung nối **Technology effect vào gameplay đang tồn tại** thay vì mở thêm framework/test.

### Hoàn thành

- Thêm `TechnologyEffectService` để đọc/sum TechEffect completed từ PostgreSQL theo semantics legacy.
- Nối TechEffect key 27/32 vào giới hạn Military/Civil General.
- Tavern recruit và Tavern response dùng giới hạn General có Technology bonus.
- Reverse `BuildingOutputCache.class` và `ResourceService.class` trực tiếp từ `gcld.jar` bằng `javap`.
- Nối TechEffect key 6 vào Food production.
- Nối TechEffect key 20 vào resource storage capacity.
- Sửa boundary khi research hoàn thành để bonus Technology không bị áp retroactive lên passive resource interval trước completion.
- Technology completion worker push lại Technology/General/MainCity projections.
- Sửa Technology progress bar sang Simple image.
- Chạy lại canonical importer thành công.
- Python tooling compile thành công.

## Không tuyên bố hoàn thành

- .NET server chưa được build trong sandbox vì không có .NET SDK/C# compiler.
- Unity chưa được mở/compile bằng Unity Editor trong sandbox.
- World/Battle chưa phải subsystem hoàn chỉnh.

## Tiến độ

**~16% / 100%** so với bản thương mại hoàn chỉnh.
