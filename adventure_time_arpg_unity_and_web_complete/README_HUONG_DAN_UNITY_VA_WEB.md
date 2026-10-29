# ADVENTURE TIME: REALMS OF OOO — 2D PIXEL ARPG (UNITY + BẢN WEB `/play` DÙNG CHUNG DỮ LIỆU)

## 1. Kiến Trúc Tổng Quan & Quyền Truy Cập

Dự án đồng bộ dữ liệu bản đồ và nội dung giữa **Unity Editor (Map Studio)** và **Phiên bản Web dành cho người chơi (`/play`)**:

1. **Thiết kế & Chỉnh sửa Map trong Unity Editor (`Map Studio`)**:
   - Không có trang Admin web. Nhà phát triển thiết kế, chỉnh sửa và kiểm định bản đồ trực tiếp trong Unity Editor qua công cụ **Map Studio**:
     `Window -> Adventure Time ARPG -> Map Studio (Thiết Kế Dungeon & Đồng Bộ Web)` (`Assets/Editor/AdventureTimeMapEditorWindow.cs`).
   - Hỗ trợ vẽ **Sàn phòng (`floor`)**, **Hành lang (`path`)**, **Tường 2.5D (`wall`)**, **Cửa phòng (`door`)**, **Cửa vào (`spawn`)**, **Cửa ra (`portal`)**, **Quái (`enemy`)**, **Boss (`boss`)**, **Rương thường & Rương kho báu (`chest` / `royal_chest`)**, **Sự kiện Đài Cổ Ngữ (`shrine`)**, và **Vật trang trí (`prop`)**.
   - Tích hợp nút **Sinh Map SKP Đa Phòng** (tạo cấu trúc nhiều phòng nối bằng hành lang, nhánh phụ kho báu, nhánh sự kiện và phòng Boss lấy cảm hứng từ *Soul Knight Prequel*), **Xem trước đường đi BFS** trực tiếp trên lưới và **Lưu Map & Cập Nhật Dữ Liệu Chung**.

2. **Trang `/play` Chỉ Dành Cho Người Chơi (Read-Only)**:
   - Người chơi tại `http://localhost:8080/play` (hoặc mở trực tiếp `Web_HTML5_25D_ARPG/adventure-time-arpg-25d.html`) chỉ chơi nội dung đã chuẩn bị sẵn: chọn 1 trong 5 nhân vật, khám phá hầm ngục đa phòng, đánh quái và Boss, né đòn, dùng kỹ năng/tuyệt kỹ, nhặt trang bị, lên cấp và qua màn.
   - Không có giao diện chỉnh sửa map trên web, không có API ghi dữ liệu và không nhúng mật khẩu hay khóa bí mật trong mã trình duyệt.

## 2. Cấu Trúc Các File Đã Hoàn Thiện

- `Unity_Project_AdventureTime_25D/`:
  - `Assets/Sprites/Original/`: Giữ nguyên 100% các sprite sheet gốc.
  - `Assets/Sprites/Normalized/`:
    - `chibi_6heroes_32cols_64x64_normalized.png`: Đã xóa toàn bộ nền xám/checkerboard dính sau lưng Finn và các nhân vật, bảo toàn pixel sáng (mũ trắng của Finn, mắt Jake, da Marceline, vũ khí, đường viền), lật chuẩn cột đi sang trái (`col 8..10`), chuẩn hóa chiều cao `58px`, điểm đặt chân `y = 60px` (`Pivot = (0.5, 0.0625)`), `PPU = 64`.
    - `chibi_18_enemies_bosses_64x64_normalized.png`: Đã xử lý sạch nền xám/trắng thừa của toàn bộ 18 quái và Boss (bao gồm Lemon Guard, Goliad, Ice King...), giữ nguyên chi tiết râu, xương, lông và hiệu ứng.
    - `tileset_all_chapters_64x64.png`: Đã làm sạch các nhãn chữ thừa trên ô sàn, tường, rương và vật trang trí của cả 4 Chương.
  - `Assets/Audio/come_along_with_me_bgm.wav`: Nhạc nền *"Come Along With Me"* chuẩn WAV PCM.
  - `Assets/Editor/AdventureTimeMapEditorWindow.cs`: Công cụ **Map Studio** trong Unity Editor.
  - `Assets/Editor/SpriteSheetAutoSlicerAndNormalizer.cs`: Công cụ tự động cấu hình `TextureImporter` (`FilterMode.Point`, `Uncompressed`, `PPU = 64`) và cắt lưới `64×64` với `Pivot (0.5, 0.0625)`.
  - `Assets/Scripts/CustomMapDataAndValidator.cs`: Mô hình dữ liệu bản đồ đa phòng, bộ sinh dungeon kiểu *Soul Knight Prequel* và thuật toán kiểm tra đường đi BFS tới cửa ra.
  - `Assets/Scripts/PlayerController4DirAndCombat.cs`: Bộ điều khiển 4 hướng với ngưỡng đầu vào (`inputDeadzone = 0.18f`), chống lật mặt liên tục (`axisHysteresis = 0.15f`), giữ hướng hợp lệ cuối cùng khi dừng, chỉ chạy khung đi bộ khi thực sự di chuyển và đồng bộ hướng nhìn với đòn đánh.
  - `Assets/Scripts/DistinctHeroSkillSystem.cs`: Hệ thống kỹ năng và tuyệt kỹ riêng cho 5 nhân vật (Finn chém kiếm combo / bão kiếm, Jake đấm vươn dài / hóa khổng lồ, Marceline phóng dơi & hút máu hồi HP, Flame Princess cầu lửa gây cháy DoT / thiên thạch dung nham, Princess Bubblegum triệu hồi Lính Chuối Hộ Vệ Banana Guards & khiên/laser hoàng gia).
  - `Assets/Scripts/GameOverAndStageFlowController.cs`: Xử lý khi HP về 0, dừng điều khiển/chiến đấu và hiển thị màn hình Game Over với 2 lựa chọn **Về sảnh chờ** (dọn sạch trạng thái trận đấu, giữ save) và **Chơi lại màn này** (chống phát thưởng lặp).
  - `Assets/Scripts/MobileTouchHUDController.cs`: Giao diện cảm ứng vùng an toàn (`Screen.safeArea`) với Joystick và 6 nút (Đánh thường, Kỹ năng, Tuyệt kỹ, Né, Bình máu, Tương tác) dùng chung tọa độ hiển thị và vùng nhận chạm, giữ nguyên điều khiển bàn phím PC.
  - `Assets/Scripts/SharedGameDataSyncClient.cs`: Đồng bộ dữ liệu chỉ-đọc (`StreamingAssets/shared_game_data.json` và `/api/public/game-data`).
- `Backend_Map_Server/map_server.py`: Server chỉ-đọc phục vụ `/play` và đồng bộ dữ liệu map từ Unity Map Studio.
- `Web_HTML5_25D_ARPG/adventure-time-arpg-25d.html`: Phiên bản chơi trên trình duyệt tại `/play`.

## 3. Hướng Dẫn Mở Project, Dùng Map Studio, Chơi và Cập Nhật Bản Web

### A. Mở Project và Map Studio trong Unity
1. Mở **Unity Hub** -> **Add project from disk** -> chọn thư mục `Unity_Project_AdventureTime_25D`.
2. Trên thanh menu Unity Editor, chọn:
   - `Window -> Adventure Time ARPG -> Normalize & Slice All 64x64 Sprite Sheets` để tự động cắt toàn bộ sprite sheet `64×64` với `PPU = 64` và `Pivot = (0.5, 0.0625)`.
   - `Window -> Adventure Time ARPG -> Map Studio (Thiết Kế Dungeon & Đồng Bộ Web)` để mở **Map Studio**.
3. Trong **Map Studio**:
   - Chọn màn chơi (`Ải 1-1` đến `Ải 4-3`), bấm **Sinh Map SKP Đa Phòng** hoặc dùng cọ vẽ Sàn, Hành lang, Tường, Cửa, Cửa vào, Cửa ra, Quái, Boss, Rương, Sự kiện Đài Cổ Ngữ, Vật trang trí.
   - Bật **Xem Trước Đường Đi BFS** để kiểm tra lối đi từ Cửa vào tới Cửa ra.
   - Bấm **Lưu Map & Cập Nhật Dữ Liệu Chung** để lưu vào `Assets/Resources/StageMapOverrides/` và `Assets/StreamingAssets/StageMapOverrides/`.

### B. Cách Cập Nhật Bản Web Sau Khi Sửa Nội Dung Trong Unity
Sau khi lưu map trong Unity Map Studio, chạy lệnh đồng bộ và khởi động server `/play`:
```bash
# Đồng bộ toàn bộ map override từ Unity Map Studio sang dữ liệu chung của Web:
python3 Backend_Map_Server/map_server.py --sync-from-unity

# Khởi chạy server chỉ-đọc cho người chơi:
python3 Backend_Map_Server/map_server.py
```
Mở trình duyệt tại `http://localhost:8080/play` để chơi ngay với dữ liệu map vừa cập nhật từ Unity.

## 6. Cơ Chế Mở Màn Tuần Tự, Kho Túi Đồ Sau Trận & Lò Ghép 3 Đổi 1
- **Mở khóa màn chơi tuần tự từ Màn 1 (`Ải 1-1`)**: Khi mới vào game, người chơi bắt buộc bắt đầu từ **Ải 1-1** (`isStageUnlocked(1, 1) == true`), toàn bộ 11 ải sau (`1-2` đến `4-3`) đều bị khóa (`🔒 Đang Khóa`). Chỉ khi chiến thắng màn hiện tại thì màn kế tiếp mới mở khóa, và người chơi chỉ có thể chọn chơi những màn đã vượt qua hoặc màn hiện tại đang mở.
- **Tự động lưu chiến lợi phẩm sau trận vào Kho Túi Đồ (`backpack` 60 ô)**: Mọi trang bị rơi ra từ quái, rương thường, rương hoàng gia và Boss trong màn chơi (kể cả các vật phẩm còn nằm trên sàn khi vừa hạ xong quái cuối cùng) đều được tự động thu hồi và cất vào **Kho Túi Đồ cá nhân** sau trận, đồng thời liệt kê chi tiết trên bảng tổng kết qua màn.
- **Xóa bỏ tạo 3 đồ miễn phí & Thay bằng Lò Ghép 3 Đổi 1 (`fuseThreeItemsIntoOneRandom` / `FuseThreeItemsIntoOneRandomItem`)**: Đã gỡ bỏ hoàn toàn nút *"Tạo Thử 3 Đồ Hiếm"*. Trong **Kho Túi Đồ**, người chơi phải chọn và **bỏ 3 món đồ bất kỳ** từ Kho Túi Đồ vào 3 ô nguyên liệu để tiêu hủy và **random ra 1 món trang bị khác** (khác loại mẫu gốc và có tỷ lệ tăng bậc phẩm chất).
