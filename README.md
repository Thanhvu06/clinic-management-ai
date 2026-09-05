# ClinicCare AI - Hệ Thống Quản Lý Phòng Khám Đa Khoa Thông Minh

ClinicCare AI là giải pháp phần mềm quản lý phòng khám đa khoa hiện đại, số hóa toàn diện quy trình tiếp đón, đặt lịch khám, khám chữa bệnh, kê đơn, cấp phát thuốc và tích hợp Trí tuệ nhân tạo (AI Gemini) hỗ trợ định tuyến chuyên khoa y tế chính xác và an toàn.

---

## Công nghệ sử dụng

- **Backend:** 
  - ASP.NET Core Web API .NET 10 LTS.
  - Entity Framework Core 10.
  - **Cơ sở dữ liệu:** Microsoft SQL Server (dành cho ứng dụng thật & môi trường chạy Local/Production); SQLite In-Memory (dành riêng cho Integration Tests).
  - ASP.NET Core Identity & JWT Authentication (Bearer Token).
  - Clean Architecture (Domain, Application, Infrastructure, WebApi).
- **Frontend:** 
  - React 19, TypeScript, Vite.
  - CSS Modules (Giao diện chuẩn y tế hiện đại, responsive, thẩm mỹ cao).
  - Lucide React Icons.
  - State Management: React Context (AuthContext, ChatContext, DialogContext).
- **Môi trường & Công cụ:**
  - Node.js: Node 24 LTS (xác định qua `.nvmrc` và `package.json engines`).
  - GitHub Actions CI/CD pipeline tự động kiểm thử và build.
- **AI Integration:** 
  - Google Gemini AI (Mô hình gemini-1.5-flash) phân tích triệu chứng và định tuyến chuyên khoa theo dữ liệu phòng khám thực tế.
  - Guardrails y tế nghiêm ngặt: không chẩn đoán, không kê đơn, lọc PII, đối chiếu whitelist chuyên khoa từ database, fallback chọn thủ công an toàn khi mất kết nối AI.
- **Kiểm thử tự động:** 
  - xUnit, WebApplicationFactory (Integration Tests cho luồng đặt lịch, JWT, bảo mật dữ liệu, cấp phát thuốc).
  - Vitest / React Testing Library cho giao diện frontend.

---

## Kiến trúc thư mục

```
clinic-management-ai/
├── .github/workflows/ci.yml       # GitHub Actions CI pipeline
├── .nvmrc                         # Node.js 24 LTS specification
├── .env.example                   # Biến môi trường mẫu
├── docs/                          # Tài liệu kỹ thuật, ERD, API, Business rules
│   ├── IMPLEMENTATION_PLAN.md
│   ├── data_dictionary.md
│   └── diagrams/
└── src/
    ├── backend/
    │   ├── ClinicManagement.Domain/          # Entities, Enums, Domain Rules
    │   ├── ClinicManagement.Application/     # DTOs, Interfaces, Logic
    │   ├── ClinicManagement.Infrastructure/  # EF Core, Migrations, AI Provider, Services
    │   ├── ClinicManagement.Api/             # Controllers, Program.cs, Middlewares
    │   └── ClinicManagement.IntegrationTests/# Integration Test Suite
    └── frontend/                             # React 19 Vite application
        ├── src/
        │   ├── api/                          # Axios Client & Interceptors
        │   ├── auth/                         # AuthContext, JWT handling
        │   ├── components/                   # Shared UI, Header, Chat Widget, Modals
        │   ├── contexts/                     # ChatContext, DialogContext
        │   ├── layouts/                      # PublicLayout, MainLayout
        │   └── pages/                        # Public, Patient, Reception, Doctor, Pharmacist, Admin
        └── package.json
```

---

## Phân quyền hệ thống (5 Roles)

1. **Bệnh nhân (Patient):**
   - Đăng ký, đăng nhập, quên mật khẩu (demo-safe).
   - Quản lý hồ sơ cá nhân.
   - Tìm kiếm chuyên khoa, bác sĩ, khung giờ khám trống (slot 30 phút).
   - Đặt lịch khám trực tuyến với cơ chế chống trùng slot (Database Transaction).
   - Tra cứu nhanh lịch hẹn cho khách vãng lai qua mã lịch hẹn hoặc số điện thoại.
   - Quản lý lịch hẹn, theo dõi timeline tiến trình, gửi yêu cầu dời lịch / hủy lịch.
   - Phản hồi đề xuất tái khám từ bác sĩ (Chấp nhận / Từ chối).
   - Xem kết quả khám bệnh, tóm tắt ca khám và danh sách đơn thuốc thật từ hệ thống.
   - Trò chuyện với Trợ lý AI và nhận gợi ý chuyên khoa phù hợp với triệu chứng.

2. **Lễ tân (Receptionist):**
   - Dashboard thống kê lịch khám hôm nay, lịch chờ duyệt, đã hoàn tất, yêu cầu đổi/hủy.
   - Quản lý danh sách lịch hẹn: tìm kiếm, lọc theo ngày/trạng thái, phân trang.
   - Xem chi tiết và lịch sử thay đổi (timeline), xác nhận lịch hẹn.
   - Xử lý phê duyệt / từ chối các yêu cầu dời hoặc hủy lịch của bệnh nhân.

3. **Bác sĩ (Doctor – Doctor Clinical Workspace):**
   - Dashboard ca khám trực tuyến trong ngày, KPI thời gian thực (chờ khám, đang khám, hoàn tất, vắng mặt).
   - Hàng đợi bệnh nhân (Queue) với phân luồng tiếp nhận nhanh và trạng thái sinh hiệu.
   - Không gian khám bệnh lâm sàng chuyên sâu: Dấu hiệu sinh tồn với tính toán BMI tự động theo chuẩn WHO châu Á, ghi nhận bệnh án điện tử (Chief Complaint, ICD-10, Clinical Findings, Treatment Plan).
   - Kê đơn thuốc điện tử tương tác kho dược thời gian thực, lưu bản nháp hoặc phát hành đơn nguyên tử khi hoàn tất ca khám.
   - Kiểm soát đồng thời lạc quan (Optimistic Concurrency Control với RowVersion token) ngăn chặn xung đột dữ liệu.
   - Quản lý lịch trực tuần và đăng ký nghỉ phép có tính năng xem trước (preview) số lượng lịch hẹn bệnh nhân bị ảnh hưởng.
   - Xem chi tiết tại: [`docs/DOCTOR_CLINICAL_WORKSPACE.md`](docs/DOCTOR_CLINICAL_WORKSPACE.md).

4. **Dược sĩ (Pharmacist - Module Pharmacy):**
   - Dashboard tổng quan kho dược: đơn chờ cấp phát, đơn đã cấp trong ngày, cảnh báo thuốc sắp hết hàng.
   - Quản lý danh mục thuốc: mã thuốc, tên, đơn vị tính, tồn kho hiện tại, ngưỡng tồn kho tối thiểu.
   - Xem danh sách và chi tiết đơn thuốc chờ cấp phát kèm kiểm tra tồn kho tức thì.
   - Xác nhận cấp phát thuốc với cơ chế giao dịch nguyên tử (Atomic Database Transaction): trừ kho, tạo `MedicineStockTransaction`, cập nhật trạng thái đơn sang `Dispensed`.
   - Chặn nghiêm ngặt việc cấp trùng đơn hoặc cấp khi kho không đủ số lượng.
   - Xem lịch sử giao dịch biến động kho thuốc (nhập, cấp, điều chỉnh).

5. **Quản trị viên (Admin):**
   - Dashboard KPI toàn diện: ca khám, bệnh nhân mới, bác sĩ hoạt động, đơn thuốc.
   - Quản lý tài khoản và phân quyền nhân viên (Bác sĩ, Lễ tân, Dược sĩ, Admin).
   - Quản lý danh mục chuyên khoa và liên kết Bác sĩ - Chuyên khoa (Many-to-Many).
   - Quản lý lịch làm việc của bác sĩ và tự động sinh slots khám 30 phút.
   - Quản lý danh mục Gói chăm sóc sức khỏe ClinicCare.
   - Quản lý danh mục thuốc và tồn kho dược phẩm.
   - Duyệt / từ chối yêu cầu nghỉ phép của bác sĩ có cảnh báo lịch khám bị ảnh hưởng.
   - Nhật ký kiểm toán hệ thống (Audit Log).

---

## Hướng dẫn cài đặt và chạy Local

### 1. Yêu cầu tiên quyết
- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- [Node.js 24 LTS](https://nodejs.org/) (`node -v` >= 24.0.0)
- SQL Server (LocalDB đi kèm Visual Studio, SQL Server Express hoặc Docker SQL Server)
- Git

### 2. Cấu hình Backend
Mở Terminal tại thư mục dự án:
```bash
cd src/backend/ClinicManagement.Api

# Tùy chọn: Nếu muốn dùng Gemini AI thật, cấu hình User Secrets (không commit key):
dotnet user-secrets set "AiProvider:ApiKey" "YOUR_API_KEY_HERE"
dotnet user-secrets set "AiProvider:IsEnabled" "true"
```
Hệ thống đã cấu hình sẵn chuỗi kết nối mặc định `Server=(localdb)\mssqllocaldb;Database=ClinicManagementDb;...` trong `appsettings.Development.json`. Khi khởi động lần đầu, backend tự động chạy migration EF Core để tạo bảng và seed dữ liệu demo.

Chạy Backend:
```bash
dotnet run
```
API lắng nghe tại: `http://localhost:5258` (Swagger/OpenAPI tài liệu tại: `http://localhost:5258/openapi/v1.json`).

### 3. Cấu hình và chạy Frontend
Mở một Terminal khác:
```bash
cd src/frontend

# 1. Cài đặt thư viện
npm install

# 2. Khởi chạy Vite Dev Server
npm run dev
```
Truy cập giao diện ứng dụng tại: `http://localhost:5173`

---

## Tài khoản Demo

Khi chạy ở môi trường `Development`, hệ thống đã tự sinh sẵn các tài khoản demo sau với mật khẩu chung: **`Demo@12345`**

| Vai trò | Email đăng nhập | Mật khẩu |
|---|---|---|
| **Quản trị viên** | `admin@cliniccare.local` | `Demo@12345` |
| **Bác sĩ** | `doctor@cliniccare.local` | `Demo@12345` |
| **Lễ tân** | `reception@cliniccare.local` | `Demo@12345` |
| **Dược sĩ** | `pharmacist@cliniccare.local` | `Demo@12345` |
| **Bệnh nhân** | `patient@cliniccare.local` | `Demo@12345` |

---

## Lệnh Build & Test

```bash
# 1. Kiểm thử và Build Backend
dotnet build src/backend/ClinicManagement.sln
dotnet test src/backend/ClinicManagement.sln

# 2. Kiểm thử và Build Frontend
cd src/frontend
npm run lint
npm run build
npm test
```

---

## Lưu ý Bảo mật
1. Tuyệt đối không commit API Key thật, JWT secret key sản xuất hoặc connection string production lên git.
2. Tất cả các endpoint nghiệp vụ đều được kiểm soát phân quyền chặt chẽ trên Backend (JWT Authorize role-based).
3. AI chỉ đóng vai trò phân luồng thông tin tham khảo, không thay thế chẩn đoán y khoa.
