# CTXD REMAKE — MASTER REQUIREMENTS V2

> **Ưu tiên cao nhất:** REMAKE CÔNG NGHỆ, KHÔNG REDESIGN GAME.  
> Mục tiêu là thay toàn bộ nền tảng Flash/Java/MySQL cũ bằng công nghệ hiện đại, trong khi UI/UX, hình ảnh, flow và gameplay vẫn bám sát game cũ.

---

## 1. Mục tiêu chính

Remake CTXD thành hệ thống mới gồm:

- **Unity + C# Client**
  - Windows x64
  - Android ARM64
  - dùng chung một codebase

- **Server mới chạy trên Windows**
  - C# / .NET LTS
  - có một cửa sổ quản trị
  - có thể Start / Stop / Restart / Backup / xem Logs / chỉnh Config
  - có thể chạy nền nhưng người dùng chỉ cần quản lý qua một app

- **Database mới**
  - PostgreSQL
  - schema mới, sạch, dễ bảo trì
  - không phụ thuộc MySQL 5.5 / MyISAM / file dữ liệu legacy

- **Networking mới**
  - HTTPS cho request/response
  - WebSocket cho realtime / server push

Sản phẩm cuối không còn phụ thuộc:

- Adobe Flash
- Browser để chơi
- ActionScript runtime
- Java 6
- Apache/PHP legacy
- MySQL 5.5
- TCP protocol legacy

---

## 2. Nguyên tắc remake

### 2.1. Remake công nghệ, không remake nhận diện game

Giữ tối đa có thể:

- UI
- UX
- bố cục
- hình ảnh
- icon
- background
- portrait
- popup
- tooltip
- animation
- effect
- âm thanh
- flow thao tác
- gameplay
- progression
- map
- tướng
- item
- quest
- battle behavior

Thay:

- engine
- runtime
- client technology
- backend technology
- database technology
- network technology
- content pipeline
- asset loading
- deployment
- server management

### 2.2. Không tự redesign

Không được tự đổi:

- layout
- màu sắc
- style
- flow
- button placement
- UX pattern
- gameplay

chỉ vì thấy “hiện đại hơn”.

Nếu cần adaptation cho Android, chỉ làm ở mức:

- touch hitbox
- safe area
- Android back
- mobile keyboard
- scaling

Không đổi phong cách game.

---

## 3. Legacy Reference

Legacy đang có là nguồn để đọc và chuyển đổi, không phải nền tảng production cuối.

Các nguồn chính:

- SWF client
- JAR server
- XML config
- static data
- MySQL legacy
- JPG / PNG / MP3
- game Flash hiện đang chạy

Game Flash đang chạy là **reference behavior**.

Không cần giữ toàn bộ trạng thái người dùng/server cũ trong sản phẩm mới.

---

## 4. Chính sách dữ liệu legacy

### 4.1. Giữ

Chỉ giữ những dữ liệu cần để phục dựng game:

- static game definitions
- tướng
- item
- equipment
- building
- troop
- army
- skill / tactic
- technology
- quest/task
- world city definition
- world road definition
- NPC
- unlock condition
- gameplay formulas/config
- localization
- visual/audio assets
- server/game configuration có giá trị tham chiếu

### 4.2. Clean / loại bỏ

Các dữ liệu sau không cần migrate sang sản phẩm mới:

- account cũ
- user cũ
- player cũ
- inventory người chơi cũ
- equipment người chơi cũ
- general owned/progress cũ
- building state người chơi cũ
- resource balance người chơi cũ
- quest progress cũ
- mail cũ
- friend/social state cũ
- guild membership cũ
- ranking cũ
- battle history cũ
- login history cũ
- billing history cũ
- logs cũ
- online state cũ
- temporary tables
- cache
- session state
- event runtime state
- timer runtime state
- cross-server runtime state
- world runtime state không cần thiết
- nation runtime state không cần thiết
- temporary server flags
- old server operational history

### 4.3. Server state mới

Server remake phải bắt đầu từ **clean state**.

Ví dụ:

```text
Accounts: empty
Players: empty
Guilds: empty
Mail: empty
Rankings: empty
Battle history: empty
World runtime state: reset
Nation runtime state: reset
Event runtime state: reset
```

Chỉ seed những thứ thực sự cần để game chạy:

```text
Static content
World definitions
NPC definitions
Required bootstrap/system rows
Default server configuration
```

### 4.4. Không migrate dữ liệu bẩn chỉ vì nó đang tồn tại

Legacy database có nhiều dữ liệu của server đã từng chạy.

Không coi dữ liệu runtime cũ là “game content”.

Nếu một bảng chỉ chứa trạng thái phát sinh trong quá trình vận hành server cũ thì mặc định:

> **không migrate**

trừ khi có bằng chứng nó chứa definition/seed bắt buộc.

---

## 5. Database migration strategy

Không clone nguyên schema MySQL cũ sang PostgreSQL.

Flow:

```text
Legacy DB / XML
      ->
Phân loại
      ->
Extract static definitions
      ->
Normalize
      ->
Import vào schema mới
```

Không migrate:

```text
runtime user state
historical logs
old session data
old rankings
old operational state
```

Database mới phải được thiết kế theo domain rõ ràng.

Ví dụ:

```text
accounts

players
player_resources
player_buildings
player_generals
player_inventory
player_technologies
player_quests

guilds
guild_members

world_state
world_armies

battle_sessions
battle_results
```

Static content phải tách rõ với runtime state.

---

## 6. Không over-engineer test/check

Dự án này là **remake game**, không phải dự án xây framework test.

### Không yêu cầu

- hàng trăm test giả lập không cần thiết
- test mọi getter/setter
- snapshot test toàn bộ UI
- contract test phức tạp khi chưa cần
- test framework riêng
- pipeline CI/CD nặng ngay từ đầu
- benchmark không có mục tiêu
- formal verification
- nhiều lớp mock chỉ để tăng coverage

### Chỉ giữ kiểm tra thiết yếu

Mỗi milestone chỉ cần đủ để chắc chắn:

1. project build được
2. server start được
3. client connect được
4. feature chính chạy được
5. dữ liệu lưu đúng
6. không phá feature đã làm
7. Windows build chạy
8. Android build chạy

Các test tự động chỉ viết khi:

- logic quan trọng
- dễ regression
- battle/formula
- transaction
- persistence
- protocol/serialization
- migration

Không chạy theo mục tiêu coverage %.

---

## 7. Definition of Done đơn giản

Một feature được coi là xong khi:

- nhìn giống bản cũ ở mức hợp lý
- flow giống bản cũ
- gameplay đúng
- dữ liệu đúng
- server xử lý đúng
- Windows chạy
- Android chạy
- không có lỗi blocker

Không cần tạo một bộ test/check khổng lồ chỉ để đánh dấu “commercial”.

---

## 8. Kiến trúc Client

Mặc định:

```text
Unity + C#
```

Một codebase cho:

```text
Windows
Android
```

Cấu trúc:

```text
Client/
  Unity/
    Assets/Game/
      Core/
      Networking/
      Data/
      UI/
      Platform/
      Features/
        Login/
        Player/
        MainCity/
        Building/
        General/
        Tavern/
        Equipment/
        Technology/
        Quest/
        Mail/
        Store/
        Nation/
        World/
        Battle/
```

Không dùng một class manager khổng lồ.

---

## 9. Kiến trúc Server

Mặc định:

```text
.NET LTS
ASP.NET Core
C#
```

Server chạy trên Windows.

Người dùng chỉ cần một ứng dụng:

```text
CTXD Server
```

Giao diện tối thiểu:

- Dashboard
- Start
- Stop
- Restart
- Online players
- Logs
- Config
- Backup
- Maintenance

Bên trong có thể dùng background host/service, nhưng không làm trải nghiệm vận hành phức tạp.

---

## 10. Networking

Production:

```text
HTTPS
WebSocket
```

Không dùng TCP Flash cũ làm protocol cuối.

Legacy TCP chỉ dùng để:

- hiểu game cũ
- reverse command
- đối chiếu behavior

---

## 11. Server authoritative

Client chỉ gửi ý định.

Ví dụ:

```text
UpgradeBuilding(buildingId)
```

Server tự:

- đọc player state
- check requirement
- tính cost
- trừ resource
- update building
- save
- trả state mới

Client không được tự quyết:

- vàng
- tài nguyên
- level
- reward
- item
- battle result
- EXP
- progression

---

## 12. Static data importer

Không nhập dữ liệu bằng tay.

Phải viết importer để convert:

```text
Legacy XML / DB / sdata
      ->
New canonical game data
```

Các definition:

- GeneralDefinition
- BuildingDefinition
- ItemDefinition
- EquipmentDefinition
- TechnologyDefinition
- TroopDefinition
- ArmyDefinition
- Skill/TacticDefinition
- TaskDefinition
- WorldCityDefinition
- WorldRoadDefinition

Giữ LegacyId khi cần đối chiếu.

---

## 13. Asset migration

### Dùng lại trực tiếp nếu được

- JPG
- PNG
- MP3
- font
- bitmap

### SWF

Phải extract/convert:

```text
SWF
 ->
asset / symbol / animation / timeline
 ->
Unity
```

Không redraw nếu asset gốc dùng được.

Nếu timeline Flash không port 1:1 được thì recreate trong Unity nhưng hình ảnh và timing phải giống.

---

## 14. UI / UX fidelity

Mỗi màn hình phải dựa vào game Flash thật.

Các màn hình chính:

- Login
- Role
- MainCity
- Tavern
- Equipment
- Technology
- World
- Battle
- Nation
- Mail
- Store
- các activity liên quan

Không cần xây hệ thống screenshot test tự động phức tạp.

Chỉ cần:

```text
mở Flash
so với Unity
chỉnh cho giống
```

---

## 15. Android

Android và Windows dùng cùng server và cùng player data.

Android chỉ adaptation tối thiểu:

- touch
- safe area
- scaling
- back button
- soft keyboard

Không redesign UI.

---

## 16. Flow remake thực tế

### Phase 0 — Audit nhanh

Không dành hàng tháng chỉ để viết tài liệu.

Chỉ cần xác định đủ để bắt đầu:

- entry point
- module map
- asset map
- static data
- DB classification
- commands
- protocol
- core gameplay dependencies

Sau đó bắt đầu code.

### Phase 1 — Foundation

Tạo:

```text
Client Unity
Server .NET
PostgreSQL
Shared contracts
Importer
```

### Phase 2 — Clean data baseline

Tạo database mới:

```text
0 account
0 player
0 guild
0 mail
0 rankings
0 battle history
clean world runtime state
```

Import:

```text
static definitions
required bootstrap data
```

### Phase 3 — First playable

Flow đầu tiên:

```text
Start server
 ->
Open client
 ->
Login
 ->
Create player
 ->
Main City
 ->
View resources
 ->
Upgrade building
 ->
Save
 ->
Relogin
 ->
State còn đúng
```

### Phase 4 — Core systems

Thứ tự:

1. Player
2. MainCity
3. Building
4. General
5. Tavern
6. Equipment
7. Technology
8. Quest
9. Mail
10. Store

### Phase 5 — World

- World map
- city
- road
- army movement
- nation
- world events

### Phase 6 — Battle

- battle rules
- troop
- general
- damage
- skill
- tactic
- animation
- reward

### Phase 7 — Activities / social / cross-server

Sau khi core game ổn.

### Phase 8 — Commercial hardening

Chỉ lúc game đã gần hoàn chỉnh mới thêm:

- production deployment
- security hardening
- monitoring
- backup
- patch/content update
- performance optimization
- release pipeline

---

## 17. Codex working rules

Codex phải tập trung vào **làm sản phẩm**, không viết tài liệu/test quá mức.

### Khi nhận task

1. đọc requirement liên quan
2. inspect legacy liên quan
3. code
4. build/run
5. sửa lỗi
6. báo kết quả

### Không được tự ý

- redesign
- rebalance
- đổi gameplay
- đổi ID
- tạo quá nhiều abstraction
- tạo framework test riêng
- tạo microservices sớm
- thêm dependency không cần
- migrate user/server runtime data cũ
- sửa legacy original

### Ưu tiên

```text
working game
>
clean architecture
>
tests cần thiết
>
documentation
```

Không được đảo thành:

```text
documentation/tests
>
game
```

---

## 18. Automation cho chủ dự án

Chủ dự án không biết lập trình.

Cần tạo các script đơn giản:

```text
START_SERVER.bat
STOP_SERVER.bat
BUILD_WINDOWS.bat
BUILD_ANDROID.bat
IMPORT_DATA.bat
BACKUP_DB.bat
COLLECT_LOGS.bat
```

Chỉ thêm script khi thật sự giúp giảm thao tác.

Không tạo 20 script chỉ để “đủ quy trình”.

---

## 19. Legacy original

Bản game/server đang chạy phải giữ làm reference.

Không clear trực tiếp bản đang chơi.

Tạo clone/dev nếu cần reverse runtime.

Dữ liệu user/state cũ:

- không migrate sang sản phẩm mới
- không coi là content
- không cần bảo tồn trừ khi dùng tạm để phân tích

---

## 20. Target cuối

```text
                CTXD Server Windows
                  .NET + PostgreSQL
                        |
                HTTPS / WebSocket
                 _______|_______
                |               |
                |               |
        CTXD Windows       CTXD Android
           Unity              Unity
```

Người chơi nhìn thấy:

> CTXD cũ về giao diện, hình ảnh, UX và gameplay.

Bên trong là:

> client/server/database/network hiện đại.

---

# ONE-LINE DIRECTIVE

> **Ưu tiên tuyệt đối là chuyển CTXD từ Flash/Java/MySQL legacy sang Unity + .NET + PostgreSQL, giữ nguyên bản sắc game cũ; clean toàn bộ user data và trạng thái server động không cần thiết, và chỉ dùng test/check ở mức tối thiểu cần để chắc chắn game chạy đúng.**
