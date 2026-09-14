# Kịch Bản Kiểm Thử Chấp Nhận Người Dùng (UAT Matrix)
## Reception Workspace & Connected Outpatient Care Rebuild

---

### 1. Tổng Quan Ma Trận Kiểm Thử

Bộ kiểm thử chấp nhận người dùng (User Acceptance Testing - UAT) gồm **27 kịch bản kiểm thử toàn diện** được thiết kế để thẩm định chất lượng phân hệ Lễ tân và Luồng khám ngoại trú liên thông tại Bệnh viện / Phòng khám đa cơ sở ClinicCare.

---

### 2. Ma Trận 27 Kịch Bản UAT (Comprehensive UAT Matrix)

| STT | Mã Kịch Bản | Phân Nhóm Nghiệp Vụ | Tiền Điều Kiện | Các Bước Thực Hiện | Kết Quả Mong Đợi | Trạng Thái |
|:---:|:---|:---|:---|:---|:---|:---:|
| 1 | `UAT-INTAKE-01` | Tiếp nhận vãng lai | Lễ tân đã đăng nhập vào Cơ sở Quận 1 | 1. Vào Tiếp nhận người bệnh<br>2. Chọn tab "Đăng ký hồ sơ mới"<br>3. Điền đầy đủ thông tin chuẩn BYT<br>4. Chọn khoa, phòng, bác sĩ, lý do khám<br>5. Nhấn "Xác nhận tiếp nhận & Cấp STT" | Cấp STT phòng khám theo ngày, sinh mã BN (MRN) dạng `BN-2026-XXXXXX`, hiển thị modal phiếu khám A5/in nhiệt | PASS |
| 2 | `UAT-INTAKE-02` | Tra cứu hồ sơ cũ (MPI) | Đã có bệnh nhân `BN-2026-000001` trong hệ thống | 1. Tại Bước 1, nhập MRN hoặc CCCD vào ô tìm kiếm<br>2. Chọn kết quả trả về<br>3. Chuyển sang Bước 2 & 3 tiếp nhận | Tái sử dụng chính xác hồ sơ định danh cũ, không tạo thêm bản ghi `Patient`, STT cấp mới cho ngày hiện tại | PASS |
| 3 | `UAT-INTAKE-03` | Người bệnh không có SĐT riêng (Trẻ em/Người già) | Lễ tân đăng ký cho bệnh nhi 3 tuổi | 1. Điền họ tên, ngày sinh bé<br>2. Bỏ trống SĐT cá nhân<br>3. Nhập tên và SĐT mẹ ruột vào phần Liên hệ khẩn cấp<br>4. Hoàn tất tiếp nhận | Hệ thống chấp nhận hợp lệ nhờ quy tắc Emergency Contact Phone Fallback, không chặn form | PASS |
| 4 | `UAT-INTAKE-04` | Thiếu toàn bộ thông tin liên hệ | Lễ tân đăng ký bệnh nhân mới | 1. Bỏ trống cả SĐT người bệnh và SĐT người liên hệ khẩn cấp<br>2. Bấm "Tiếp tục" | Bị chặn lại ở Bước 1 kèm cảnh báo: "Vui lòng nhập ít nhất một số điện thoại liên hệ" | PASS |
| 5 | `UAT-INTAKE-05` | Phân luồng theo chuyên khoa | Bác sĩ chuyên khoa Tim mạch đang có lịch trực | 1. Chọn Khoa Nội - Tim Mạch<br>2. Xem danh sách bác sĩ và phòng khả dụng | Chỉ hiển thị các buồng khám và bác sĩ thuộc khoa Tim mạch tại cơ sở trực | PASS |
| 6 | `UAT-INTAKE-06` | In phiếu khám đa định dạng | Vừa cấp STT thành công | 1. Tại modal phiếu khám, chọn xem định dạng A5<br>2. Chuyển sang định dạng in nhiệt 80mm | Render đầy đủ mã vạch barcode, mã QR lượt khám, thông tin bệnh nhân, phòng khám và số thứ tự | PASS |
| 7 | `UAT-MPI-01` | Tránh tạo tài khoản ảo | Đăng ký người bệnh vãng lai tại quầy | 1. Hoàn tất tiếp nhận cho người bệnh vãng lai<br>2. Kiểm tra cơ sở dữ liệu `Patients` và `AspNetUsers` | Bản ghi `Patient` có `UserId == null`. Không sinh tài khoản ảo rác trong bảng `AspNetUsers` | PASS |
| 8 | `UAT-MPI-02` | Cảnh báo trùng lặp định danh | Người bệnh đã có CCCD trong hệ thống | 1. Nhập số CCCD đã tồn tại ở form tạo mới<br>2. Bấm Tìm kiếm/Kiểm tra | Hệ thống gợi ý mở hồ sơ y bạ MPI có sẵn thay vì cho phép tạo mới hồ sơ trùng | PASS |
| 9 | `UAT-MPI-03` | Quản lý tiền sử dị ứng | Người bệnh có tiền sử dị ứng Penicillin | 1. Khai báo dị ứng mức độ Nặng (Severe) tại Bước 1<br>2. Hoàn tất tiếp nhận | Tiền sử dị ứng hiển thị nổi bật trên phiếu khám và đồng bộ ngay vào màn hình khám của bác sĩ | PASS |
| 10 | `UAT-MPI-04` | Tiếp nhận từ gói khám sức khỏe | Người bệnh vãng lai đăng ký gói khám tại quầy | 1. Mở danh sách đăng ký gói khám<br>2. Lễ tân thực hiện tiếp đón | Lượt đăng ký của người bệnh vãng lai (`UserId == null`) vẫn hiển thị đầy đủ để lễ tân tiếp đón | PASS |
| 11 | `UAT-IDEM-01` | Gửi lặp cùng Key cùng Payload | Kết nối mạng chập chờn, lễ tân nhấn gửi 2 lần | 1. Gửi request `POST /reception-intake` với Idempotency-Key `K1`<br>2. Ngay sau đó gửi lại chính xác request đó với cùng `K1` | Server trả về cùng mã STT và thông tin phiếu tiếp nhận đã cấp, không tạo thêm lượt khám trùng | PASS |
| 12 | `UAT-IDEM-02` | Gửi lặp cùng Key khác Payload | Lỗi client gửi nhầm cùng một key cho 2 người khác nhau | 1. Gửi request `K1` cho bệnh nhân A<br>2. Gửi request `K1` cho bệnh nhân B | Server từ chối request thứ 2 với mã lỗi HTTP 409 `IDEMPOTENCY_KEY_REUSED_WITH_DIFFERENT_PAYLOAD` | PASS |
| 13 | `UAT-IDEM-03` | Va chạm đồng thời (Concurrency Collision) | 2 luồng song song cùng gửi một Idempotency-Key | 1. Kích hoạt đồng thời 2 thread cùng gửi chung key | Database bắt lỗi `DbUpdateException`, thread thứ 2 nhận lại kết quả từ thread thứ nhất hoặc 409 | PASS |
| 14 | `UAT-SCOPE-01` | Chặn truy cập chéo cơ sở | Lễ tân chỉ được phân công trực tại Cơ sở 1 | 1. Lễ tân đăng nhập bằng tài khoản Cơ sở 1<br>2. Thử truy vấn hoặc tiếp nhận bệnh nhân vào Cơ sở 2 | Server chặn thao tác và trả về HTTP 403 `ACCESS_DENIED_TO_FACILITY_RESOURCE` | PASS |
| 15 | `UAT-SCOPE-02` | Nhân viên đa cơ sở (Multi-facility Staff) | Bác sĩ được phân công trực ở cả Cơ sở 1 và Cơ sở 2 | 1. Bác sĩ chuyển cơ sở làm việc trên thanh điều hướng<br>2. Xem danh sách hàng đợi | Hệ thống cho phép truy cập hợp lệ cả 2 cơ sở được phân công | PASS |
| 16 | `UAT-SCOPE-03` | Toàn quyền Quản trị viên (Admin Bypass) | Đăng nhập tài khoản SystemAdmin | 1. Quản trị viên chuyển qua lại mọi cơ sở trong hệ thống | Quản trị viên có quyền truy cập và cấu hình toàn bộ các cơ sở | PASS |
| 17 | `UAT-WORK-01` | Bộ lọc Tab linh hoạt | Có nhiều lịch hẹn ở các trạng thái khác nhau | 1. Lễ tân chuyển qua lại giữa các tab `Hôm nay`, `Chờ xác nhận`, `Sắp tới`, `Lịch sử` | Danh sách lịch hẹn cập nhật tức thì tương ứng theo đúng tiêu chí lọc của từng tab | PASS |
| 18 | `UAT-WORK-02` | Sắp xếp ưu tiên hành động (Actionable-First) | Có lịch `Confirmed`, `Pending` và `Completed` | 1. Quan sát thứ tự hiển thị tại tab Hôm nay | Lịch `Confirmed` (chờ tiếp đón) và `Pending` (chờ xác nhận) hiển thị lên đầu danh sách | PASS |
| 19 | `UAT-WORK-03` | Tiếp nhận nhanh từ lịch hẹn (Fast Check-in) | Người bệnh đã đặt lịch khám trước có mặt tại quầy | 1. Nhấn nút "Tiếp nhận" trực tiếp trên dòng lịch hẹn | Hệ thống tự động chuyển lịch sang `CheckedIn`, tạo `PatientVisit` và cấp STT ngay lập tức | PASS |
| 20 | `UAT-WORK-04` | Tìm kiếm tức thời trên toàn bảng (Smart Search) | Danh sách tiếp nhận có hơn 50 dòng | 1. Nhập số điện thoại hoặc MRN vào ô tìm kiếm nhanh | Bảng dữ liệu lọc chính xác dòng người bệnh tương ứng | PASS |
| 21 | `UAT-ADMIN-01` | Phân công cơ sở cho nhân viên | Quản trị viên muốn chuyển cơ sở cho lễ tân | 1. Vào Quản lý Cơ sở -> Tab Phân công<br>2. Thêm phân công nhân viên vào cơ sở mới | Bản ghi được lưu vào `FacilityUserAssignments`, nhân viên lập tức có quyền truy cập cơ sở mới | PASS |
| 22 | `UAT-ADMIN-02` | Cập nhật đơn giá cận lâm sàng | Phòng khám điều chỉnh giá chụp X-quang | 1. Vào Quản lý Biểu phí -> Tab Bảng giá Cận lâm sàng<br>2. Sửa giá dịch vụ và lưu | Đơn giá mới phản ánh ngay khi bác sĩ chỉ định và khi lễ tân xuất hóa đơn thu tiền | PASS |
| 23 | `UAT-ADMIN-03` | Cập nhật đơn giá thuốc bán lẻ | Giá nhập thuốc thay đổi | 1. Vào Quản lý Thuốc -> Sửa đơn giá bán lẻ của thuốc<br>2. Lưu thay đổi | Đơn thuốc mới kê áp dụng chính xác đơn giá vừa cập nhật | PASS |
| 24 | `UAT-PHARM-01` | Giữ tồn kho khi xác nhận mua | Người bệnh đồng ý mua đơn thuốc tại nhà thuốc | 1. Dược sĩ nhấn "Xác nhận mua thuốc"<br>2. Kiểm tra tồn kho khả dụng | Tồn kho khả dụng bị trừ ngay lập tức (`Stock Reservation`), đơn thuốc chuyển sang `InBilling` | PASS |
| 25 | `UAT-PHARM-02` | Chặn cấp phát khi chưa nộp tiền | Đơn thuốc ở trạng thái `InBilling` chưa thanh toán | 1. Dược sĩ cố tình nhấn "Cấp phát thuốc" | Bị chặn với mã lỗi `PRESCRIPTION_NOT_PAID`, bắt buộc qua quầy thu ngân thanh toán trước | PASS |
| 26 | `UAT-BILL-01` | Chống thu trùng (Anti-Double-Billing) | Chỉ định cận lâm sàng đã lập hóa đơn | 1. Lễ tân thử lập thêm hóa đơn thứ 2 cho cùng một chỉ định | Filtered Unique Index và logic kiểm tra chặn lại, báo lỗi đã có hóa đơn hiệu lực | PASS |
| 27 | `UAT-BILL-02` | Bảo toàn hành trình khám đang hoạt động | Người bệnh thanh toán tạm tính phí xét nghiệm máu | 1. Người bệnh đóng tiền chỉ định xét nghiệm<br>2. Thu ngân xác nhận thu tiền | Trạng thái lượt khám vẫn giữ nguyên `WaitingForDiagnostics`, không bị kết thúc sớm thành `Completed` | PASS |
