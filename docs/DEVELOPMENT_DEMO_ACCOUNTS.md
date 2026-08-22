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

## Danh sách tài khoản

Mật khẩu chung cho tất cả các tài khoản demo: **`Demo@12345`**

| Vai trò (Role) | Email đăng nhập | Tên người dùng |
| --- | --- | --- |
| **Admin** | `admin@cliniccare.local` | Admin Demo |
| **Receptionist** | `reception@cliniccare.local` | Receptionist Demo |
| **Doctor** | `doctor@cliniccare.local` | Doctor Demo 1 |
| **Doctor** | `doctor2@cliniccare.local` | Doctor Demo 2 |
| **Patient** | `patient@cliniccare.local` | Patient Demo |

## Chạy Backend / Frontend

- Backend: `dotnet run --project src/backend/ClinicManagement.Api` (đảm bảo profile là HTTP hoặc chạy với launch profile Development)
- Frontend: `cd src/frontend && npm run dev`
