# Kế hoạch triển khai hoàn thiện hệ thống ClinicCare AI

Tài liệu này đặc tả chi tiết kế hoạch các giai đoạn (phases), danh sách các tệp tin thay đổi/thêm mới, các API và migration cần thực hiện để phát triển repository `Thanhvu06/clinic-management-ai` thành hệ thống quản lý phòng khám ClinicCare hoàn chỉnh, chuyên nghiệp và sẵn sàng demo.

---

## Tổng quan các Phase

1. **Phase 1: Chuẩn hóa môi trường, cấu hình khởi động, CI/CD và tài liệu**
   - Cấu hình Node 24 LTS (`.nvmrc`, `package.json`).
   - Cung cấp `.env.example`, `appsettings.Development.json` chuẩn hóa (SQL Server Connection String, JWT secret, AI config).
   - Thiết lập auto-migrate database an toàn trong `Program.cs`.
   - Thiết lập GitHub Actions CI workflow (`.github/workflows/ci.yml`).
   - Cập nhật `README.md` chính xác với stack thực tế.
   - Sửa test rỗng `UnitTest1.cs`.

2. **Phase 2: Schema Domain, Migration và Seed Data cho Gói khám & Dược phẩm**
   - Tạo Entity `HealthPackage` trong Domain.
   - Bổ sung `DbSet<HealthPackage>` trong `AppDbContext`.
   - Tạo EF Core Migration `AddHealthPackages`.
   - Nâng cấp `DevelopmentDataSeeder.cs`: seed Gói chăm sóc sức khỏe ClinicCare, danh mục thuốc thực tế, đơn thuốc mẫu (chờ cấp và đã cấp), giao dịch kho mẫu.

3. **Phase 3: Giao diện công khai (DIAG-inspired & ClinicCare branded) và Luồng Bệnh nhân**
   - Header hai tầng chuyên nghiệp: Top bar tiện ích (hotline, giờ làm việc, tra cứu lịch hẹn, đổi ngôn ngữ) và Navigation chính.
   - Landing page hiện đại: Hero với 3 CTA rõ ràng (Đặt lịch, Tìm chuyên khoa, Tra cứu lịch hẹn), danh mục chuyên khoa card, khu Gói chăm sóc sức khỏe ClinicCare, đội ngũ bác sĩ, mạng lưới điểm khám, quy trình 4 bước, FAQ tương tác và Footer hoàn chỉnh.
   - Modal tra cứu nhanh lịch hẹn cho khách vãng lai/bệnh nhân bằng mã hẹn hoặc số điện thoại.
   - API công khai cho Gói khám (`GET /api/v1/health-packages`).
   - Tính năng Quên mật khẩu an toàn mức demo cho Patient.
   - Kết nối API đơn thuốc thật cho bệnh nhân (`GET /api/v1/patients/me/prescriptions`), loại bỏ mock data, xem chi tiết và in đơn thuốc chuẩn.

4. **Phase 4: Hoàn thiện nghiệp vụ Bác sĩ, Lễ tân và Quản trị viên**
   - Bác sĩ: Kê đơn thuốc thật khi hoàn tất ca khám (`POST /api/v1/doctor/appointments/{id}/prescription`), chọn từ danh mục thuốc khả dụng.
   - Lễ tân: Dashboard hiển thị số liệu thực tế, tra cứu, xác nhận lịch, xử lý đổi/hủy với thông báo lỗi nghiệp vụ rõ ràng.
   - Quản trị viên: Quản lý Gói khám sức khỏe (`AdminHealthPackages.tsx`), Quản lý thuốc và kho (`AdminMedicines.tsx`), Quản lý Audit Log (`AdminAuditLogs.tsx`).

5. **Phase 5: Hoàn thiện Module Dược sĩ (Pharmacy Module)**
   - API nghiệp vụ Dược: Dashboard dược sĩ, danh sách đơn chờ cấp, chi tiết đơn kèm kiểm tra tồn kho, xác nhận cấp phát thuốc nguyên tử trong database transaction (trừ kho, ghi `MedicineStockTransaction`, đổi trạng thái đơn, không cấp trùng, chặn thiếu tồn).
   - Quản lý danh mục thuốc và tồn kho, nhập kho/điều chỉnh kho.
   - Giao diện Pharmacist: `PharmacistDashboard.tsx`, `PharmacistPrescriptions.tsx`, `PharmacistMedicines.tsx`, `PharmacistInventory.tsx`.
   - Route guard và sidebar cho role `Pharmacist`.

6. **Phase 6: Kiểm thử tự động toàn diện và nghiệm thu**
   - Integration tests: JWT 401/403, Đặt lịch chống trùng slot, Phân quyền cô lập dữ liệu Patient/Doctor, AI whitelist/fallback, Cấp thuốc (đủ tồn, thiếu tồn, không cấp lại).
   - Frontend test: Route guard và luồng đặt lịch/cấp thuốc bằng Vitest.
   - Thực thi `dotnet test` và `npm run lint && npm run build && npm test`.
   - Commit chuẩn Conventional Commits cho từng phase.
