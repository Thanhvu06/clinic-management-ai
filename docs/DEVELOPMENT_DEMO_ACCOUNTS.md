# Bảng tài khoản Demo ở môi trường Development

**CẢNH BÁO BẢO MẬT:** 
Các tài khoản và dữ liệu này **chỉ dành cho môi trường Development**. Tuyệt đối không được bật tính năng DemoSeed hoặc dùng các mật khẩu này ở môi trường Staging/Production.

## Cách bật/tắt tự động seed dữ liệu
Sửa file `src/backend/ClinicManagement.Api/appsettings.Development.json`:

```json
{
  "DemoSeed": {
    "Enabled": true
  }
}
```
Mỗi khi khởi động (chạy `dotnet run`), hệ thống sẽ kiểm tra và tự động tạo các dữ liệu còn thiếu.

## Danh sách tài khoản Demo chính

Mật khẩu chung cho tất cả các tài khoản demo: **`Demo@12345`**

| Vai trò (Role) | Email đăng nhập | Tên người dùng / Chức danh | Ghi chú |
| --- | --- | --- | --- |
| **Admin** | `admin@cliniccare.local` | Quản trị viên | Toàn quyền cấu hình phòng khám |
| **Receptionist** | `reception@cliniccare.local` | Lễ tân Nguyễn Thu Trang | Tiếp nhận & check-in bệnh nhân |
| **Pharmacist** | `pharmacist@cliniccare.local` | Dược sĩ Lâm Sàng | Cấp phát & quản lý kho thuốc |
| **LabSupervisor** | `labsupervisor@cliniccare.local` | Trưởng khoa Xét nghiệm Đỗ Thị Lan | Duyệt kết quả xét nghiệm tại cơ sở được phân công |
| **DiagnosticTechnician 2** | `technician.02@cliniccare.local` | Kỹ thuật viên Phạm Quốc Bảo | Lấy mẫu, nhập kết quả và duyệt phiếu do KTV khác nhập |
| **Patient** | `patient@cliniccare.local` | Bệnh nhân Demo | Hồ sơ đặt lịch mẫu |
| **Doctor (Chính)** | `doctor@cliniccare.local` | **BS.CKI Nguyễn Minh Khải** | **Nội Tổng Quát & Tim Mạch (12 năm KN)** |
| Doctor 2 | `bacsi.02@cliniccare.local` | BS Trần Thu Hà | Sản - Phụ Khoa |
| Doctor 3 | `bacsi.03@cliniccare.local` | BS.CKII Lê Hoàng Nam | Chấn Thương Chỉnh Hình |
| Doctor 4 | `bacsi.04@cliniccare.local` | ThS.BS Phạm Văn Hùng | Tai Mũi Họng |
| Doctor 5 | `bacsi.05@cliniccare.local` | BS Đinh Thị Yến | Da Liễu |
| Doctor 6 | `bacsi.06@cliniccare.local` | BS.CKI Vũ Quang Vinh | Thần Kinh |
| Doctor 7 | `bacsi.07@cliniccare.local` | TS.BS Bùi Hải Yến | Nội Tiết |
| Doctor 8 | `bacsi.08@cliniccare.local` | BS Đỗ Tuấn Anh | Nhãn Khoa |
| Doctor 9 | `bacsi.09@cliniccare.local` | BS.CKI Lý Kim Dung | Nhi Khoa |
| Doctor 10 | `bacsi.10@cliniccare.local` | BS Hoàng Văn Đạt | Tiêu Hóa |

## Chạy Backend / Frontend

- Backend: `dotnet run --project src/backend/ClinicManagement.Api` (đảm bảo profile là HTTP hoặc chạy với launch profile Development)
- Frontend: `cd src/frontend && npm run dev`


## Tự bổ sung lịch và hàng đợi demo 24/7

Khi môi trường là **Development**, `DemoSeed:Enabled=true` và `ClinicOperatingHours:Mode=TwentyFourSeven`, backend chạy top-up sau seed lúc khởi động, kể cả CSDL đã có marker khởi tạo. Hosted service tiếp tục bổ sung định kỳ. Khởi động lại backend sau khi đổi các khóa cấu hình bên dưới. Không đăng ký/chạy tác vụ này ở Testing, Staging hoặc Production; chế độ Standard cũng không top-up.

| Khóa trong section `DemoSeed` | Mặc định | Ý nghĩa |
|---|---:|---|
| `Enabled` | `true` trong Development | Cho phép seed/top-up demo. |
| `TopUpIntervalMinutes` | `60` | Chu kỳ bổ sung sau lần khởi động; giới hạn 1–1440 phút. |
| `MinUpcomingConfirmed` | `12` | Số lịch Confirmed tối thiểu trong 24 giờ tới của ba bác sĩ demo; giới hạn 0–100. |
| `MinPendingConfirmations` | `3` | Số lịch Pending tối thiểu trong cùng khoảng thời gian; giới hạn 0–100. |
| `DemoPatientDayStartHour` | `7` | Giờ Việt Nam bắt đầu được chọn cho bệnh nhân `patient@cliniccare.local`, tính từ giờ này. |
| `DemoPatientDayEndHour` | `20` | Giờ Việt Nam kết thúc khung chọn; giờ bắt đầu lịch phải nhỏ hơn giá trị này. |
| `StaleAfterHours` | `48` | Ngưỡng tuổi dữ liệu để dọn phiếu chỉ định đang xử lý, đơn thuốc chờ cấp và lượt khám chưa kết thúc. |

Trước khi bổ sung, tác vụ dọn phiếu chỉ định đang xử lý, đơn thuốc chờ cấp và lượt khám chưa kết thúc đã quá ngưỡng `StaleAfterHours`. Hóa đơn chưa thanh toán (`Unpaid`) không bị dọn, kể cả khi đã quá 48 giờ; các dòng hóa đơn và đăng ký gói khám đi kèm cũng được giữ nguyên. Hóa đơn này vẫn có nút **Thu tiền** trong hàng đợi, không khiến lượt khám đã hoàn tất quay lại hàng đợi chờ lập hóa đơn.

Top-up chỉ thêm phần còn thiếu, phân bổ cho các tài khoản bác sĩ `doctor@cliniccare.local`, `bacsi.02@cliniccare.local`, `bacsi.03@cliniccare.local`. Hôm nay có ít nhất một lượt WaitingForDoctor cho BS Nguyễn Minh Khải. Thời gian dùng giờ Việt Nam từ clock nghiệp vụ. Lịch mới bắt đầu sau ít nhất 30 phút và tuân thủ điều kiện tuổi/giới tính chuyên khoa của dữ liệu demo.

Bệnh nhân `patient@cliniccare.local` chỉ được top-up bổ sung lịch có giờ bắt đầu trong khung mặc định **07:00 ≤ giờ bắt đầu < 20:00**. Hai khóa giờ nhận giá trị 0–23 cho giờ bắt đầu và 1–24 cho giờ kết thúc; nếu khung không hợp lệ thì dùng lại 7–20. Nếu không có slot hợp lệ trong 24 giờ tới, tác vụ tìm tiếp các slot đã tồn tại trước lượt top-up với lịch làm việc đang hoạt động, tối đa 7 ngày tính từ hiện tại; không có thì ghi warning. Phần tìm tiếp không tự sinh thêm ca hoặc slot ngoài cửa sổ bổ sung 24 giờ. Lịch ban ngày đã có trong cửa sổ này được giữ để tránh bổ sung trùng. Không sửa hoặc hủy lịch đêm và lịch sử Vắng mặt đã có. Các bệnh nhân demo khác vẫn được bổ sung 24/7 trong cửa sổ 24 giờ như trước. Seed khởi tạo CSDL mới giữ dữ liệu mẫu ban đầu trong 1–6 giờ tới; khung ban ngày áp dụng cho top-up sau seed.

Chỉ chọn các danh tính bệnh nhân demo được seed sẵn. Ngoài bước dọn dữ liệu cũ nêu trên, không xóa/sửa lịch, lượt khám hoặc lịch sử đã có, không dùng lại cặp bệnh nhân–ngày đã có lịch/lượt khám, không lấy slot đã đặt, slot có lịch đang giữ chỗ, lịch nghỉ hoặc lịch làm việc ngừng hoạt động. Chỉ đánh dấu slot trống của lịch mới là đã đặt. Giao dịch Serializable và khóa trong worker giúp tránh các lượt top-up chồng nhau. Chạy lại ngay không sinh thêm khi các mức tối thiểu đã đủ.

Nếu thiếu lịch làm việc/slot, tác vụ chỉ thêm các ca và slot còn thiếu của ba bác sĩ demo cho hôm nay và ngày cuối cửa sổ 24 giờ; không bật lại ca đã ngừng hoạt động và không đồng bộ lại cờ slot cũ. Cơ chế này độc lập với `GenerateSlots`, vốn điều khiển seed lịch tổng quát ban đầu. Seed mới vẫn giữ dữ liệu mẫu ban đầu trải đều 24 giờ; top-up duy trì mức tối thiểu sau đó. Không cần xóa CSDL hoặc reset demo khi qua ngày.

Lịch đã hết hạn vẫn để tác vụ hết hạn chuyển Vắng mặt/hủy bình thường. Nếu không còn đủ bệnh nhân–ngày hoặc slot hợp lệ, top-up ghi warning và giữ nguyên dữ liệu đã có; lỗi bổ sung không làm API dừng và sẽ thử lại ở chu kỳ sau. Không dùng cơ chế này cho dữ liệu Production.
