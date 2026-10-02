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
