# Xây dựng website quản lý phòng khám tích hợp AI hỗ trợ tư vấn đặt lịch

## Mục tiêu
Dự án nhằm xây dựng một hệ thống phần mềm quản lý phòng khám hiện đại, giúp số hóa quy trình đặt lịch khám, quản lý hồ sơ, lịch làm việc và tích hợp Trí tuệ nhân tạo (AI) để hỗ trợ bệnh nhân chọn chuyên khoa phù hợp dựa trên mô tả triệu chứng.

## Công nghệ sử dụng
- **Backend:** ASP.NET Core Web API 10.0, Entity Framework Core (SQLite Database), JWT Authentication.
- **Frontend:** React 18, Vite, TypeScript, CSS Modules.
- **AI Integration:** Google Gemini AI API (Phân tích triệu chứng & gợi ý chuyên khoa).
- **Kiểm thử:** XUnit, WebApplicationFactory (Integration Tests).

## Kiến trúc thư mục
- [`src/backend/`](file:///src/backend/): Chứa toàn bộ mã nguồn API backend (Domain, Application, Infrastructure, WebApi) xây dựng theo mô hình Clean Architecture và các unit/integration test.
- [`src/frontend/`](file:///src/frontend/): Chứa mã nguồn giao diện React/Vite (components, pages, api hooks).
- [`docs/`](file:///docs/): Chứa tài liệu phân tích thiết kế, đặc tả API, cơ sở dữ liệu và các sơ đồ kiến trúc.

## Chức năng hệ thống
Hệ thống được chia theo 4 vai trò:
1. **Bệnh nhân:** Đăng ký, quản lý hồ sơ cá nhân, đặt lịch khám có sự hỗ trợ của AI, xem lịch sử và kết quả khám bệnh, yêu cầu đổi/hủy lịch.
2. **Lễ tân:** Xác nhận lịch hẹn, hỗ trợ bệnh nhân tại quầy, xử lý các yêu cầu đổi/hủy lịch từ bệnh nhân, theo dõi timeline khám bệnh.
3. **Bác sĩ:** Quản lý lịch làm việc của bản thân, thực hiện ca khám và lưu tóm tắt kết quả, chỉ định tái khám, xin phép nghỉ đột xuất/có kế hoạch.
4. **Quản trị viên:** Quản lý danh mục chuyên khoa, quản lý tài khoản nhân viên (Bác sĩ, Lễ tân), sinh ca khám (Work Schedules), duyệt yêu cầu nghỉ của bác sĩ.

## Hoạt động của tính năng AI
- AI phân tích mô tả triệu chứng của bệnh nhân và gợi ý **tối đa 3 chuyên khoa** phù hợp nhất.
- AI được thiết kế với cơ chế guardrails: **không chẩn đoán, không kê đơn, không tự điều trị hay tự động đặt lịch**.
- Đầu ra của AI chỉ ánh xạ dựa trên **whitelist** các chuyên khoa đang hoạt động tại phòng khám.
- Frontend cam kết **không gửi PII** (Thông tin định danh cá nhân) vào mô hình ngôn ngữ.
- Khi AI gặp sự cố (unavailable, quota limits), hệ thống tự động fallback cho phép bệnh nhân **chọn chuyên khoa thủ công**.

## Hướng dẫn cài đặt và chạy (Local Development)

### 1. Điều kiện tiên quyết
- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- [Node.js 20+](https://nodejs.org/)
- Git

### 2. Chạy Backend
Mở Terminal ở thư mục gốc:
```bash
# 1. Di chuyển vào thư mục dự án
cd src/backend/ClinicManagement.Api

# 2. Cấu hình AI Secret Key
# (Tuyệt đối không hardcode. Dùng User Secrets trong .NET)
dotnet user-secrets set "AiProvider:ApiKey" "YOUR_API_KEY_HERE"
dotnet user-secrets set "AiProvider:IsEnabled" "true"

# 3. Kích hoạt tính năng sinh dữ liệu Demo
# Đảm bảo appsettings.Development.json có cờ:
# "DemoSeed": { "Enabled": true }

# 4. Chạy dự án (Tự động restore và build)
dotnet run
```
*Lưu ý: API chạy trên `http://localhost:5258`. Truy cập thẳng vào `http://localhost:5258/` có thể trả về `404 Not Found` vì hệ thống không khai báo route gốc, vui lòng sử dụng Frontend hoặc gọi trực tiếp API.*

### 3. Chạy Frontend
Mở Terminal khác ở thư mục gốc:
```bash
cd src/frontend

# 1. Cài đặt dependencies
npm install

# 2. Cấu hình biến môi trường
# Tạo file .env nếu chưa có
# VITE_API_BASE_URL=http://localhost:5258

# 3. Khởi động server
npm run dev
```
Truy cập giao diện tại: `http://localhost:5173`

## Lệnh Build / Test
```bash
# Backend Test
dotnet build src/backend/ClinicManagement.sln
dotnet test src/backend/ClinicManagement.sln

# Frontend Build
cd src/frontend && npm run build
```

## Tài khoản Demo
Khi chạy với cờ `DemoSeed:Enabled = true`, hệ thống tự sinh các tài khoản sau:
- **Quản trị viên:** `admin@cliniccare.local` / `Demo@12345`
- **Lễ tân:** `reception@cliniccare.local` / `Demo@12345`
- **Bác sĩ:** `doctor@cliniccare.local` / `Demo@12345`
- **Bệnh nhân:** `patient@cliniccare.local` / `Demo@12345`

> **Cảnh báo:** Dữ liệu Seed này chỉ dùng cho môi trường `Development`. Tuyệt đối không dùng cho `Production` hoặc `Staging`.

## Tài liệu tham khảo
- [Đặc tả API (API Specification)](file:///docs/api/api-specification.md)
- [Luật nghiệp vụ (Business Rules)](file:///docs/specifications/business-rules.md)
- [Thiết kế cơ sở dữ liệu (Database)](file:///docs/database/database-tables.md)
- [Biểu đồ (Sơ đồ Use Case, ERD, Activity)](file:///docs/diagrams/)

## Lưu ý Bảo mật
1. **Tuyệt đối không commit** các thông tin nhạy cảm: `appsettings.json` (chứa db thật, key thật), file `.env`, mật khẩu thật, chuỗi kết nối (Connection String) hoặc Database production.
2. AI chỉ đóng vai trò phân luồng thông tin, **không phải là công cụ y tế thay thế bác sĩ**.
3. Các môi trường Test/Prod yêu cầu triển khai Reverse Proxy, HTTPS và quản lý Secret Key tập trung.
