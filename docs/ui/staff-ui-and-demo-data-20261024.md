# Staff UI và dữ liệu Development — 24/10/2026

## Cách sửa các mục đã giao

| Mục | Kết quả |
| --- | --- |
| 1 | Seed gán và sửa `ApplicationUser.CreatedAt` mặc định; trang tài khoản dùng `formatDateOnly`, hiển thị `—` khi thiếu ngày hoặc năm ≤ 1900. DTO trước đây trả đúng giá trị mặc định chưa được seed. |
| 2 | Helper `getGenderLabel` dùng chung cho Nam/Nữ/Khác; thay các chỗ hiển thị giới tính thô và các bản ánh xạ lặp trong frontend ngoài vùng cấm. |
| 3 | Đủ nhãn cho 7 enum giao dịch kho, gồm `Initial` → “Tồn đầu kỳ”. |
| 4 | Mã đơn ưu tiên code API, dự phòng `RX-yyMMdd-id` với id tối thiểu 4 số. Danh sách, chi tiết, bàn làm việc và tham chiếu trong lịch sử kho dùng cùng helper; code API bệnh nhân dùng cùng định dạng. |
| 5 | 404, 403 và forbidden dùng Button primary của antd; có icon trạng thái và điều hướng về bàn làm việc theo vai trò. Giữ nguyên chữ trên nút. |
| 6 | Theo lựa chọn bổ sung của người dùng, giữ nguyên toàn bộ ô ngày native và không sửa PR Guard. |
| 7 | Kho và đơn thuốc dùng `dd/MM/yyyy HH:mm`, không giây; bỏ `#` trước mã lịch hẹn ở các bảng. |
| 8 | Chip và lựa chọn cơ sở lễ tân chỉ hiển thị tên cơ sở. |
| 9 | Ngày quá khứ cũng có ca 08:00–11:30 và 13:30–17:00, mỗi ca 7 khung; hàng đợi bác sĩ còn một nút “Làm mới”. |
| 10 | Lý do khám theo mã chuyên khoa, có biến thể; nội tổng quát có 8 lý do. Seed cập nhật các lý do mẫu được nhận diện mà giữ các nội dung khác. |
| 11 | 25 hóa đơn trong 30 ngày gần nhất: 20 đã thu, 3 chờ, 2 hủy; có ngày không giao dịch. Phí khám, CLS, thuốc, gói khám lấy giá danh mục và snapshot vào dòng hóa đơn. Thực thu DB mới: 9.400.000 ₫. Bảng ngày ẩn ngày không có giao dịch đã thu. |
| 12 | Hôm nay có 2 chờ thực hiện, 1 đang thực hiện, 1 chờ duyệt do bác sĩ khác chỉ định, 1 đã duyệt. Phiếu chờ duyệt giữ `InProgress/Pending`; người nhập không tự duyệt được. KPI “Đang thực hiện” của hệ thống tính cả phiếu đang chờ duyệt. |
| 13 | Đa số lô còn 6–24 tháng, 3 lô còn 20/35/50 ngày và 1 lô đã hết hạn. |
| 14 | Nghỉ phép 08:00–17:00 cùng ngày; giờ tạo đơn và giao dịch mẫu trong giờ làm việc. Ngày đặt và ngày trong mã APT không sau ngày khám. |

## Ô ngày và E2E

Đã grep `scripts/e2e`; không có ô `type="date"` được kịch bản E2E điền trực tiếp. Không sửa file E2E hoặc aria-label.

Giữ native theo lựa chọn của người dùng ở ReceptionBilling, DoctorQueue, TechnicianDashboard, AdminBilling, LabTurnaroundReport, DoctorExaminationWorkspace, BookAppointment, PackageRegistration, PatientAppointments, WalkInPatientRegistration và PharmacyBatches — tổng cộng 14 ô. Cách trình bày trong ô vẫn theo locale của trình duyệt.

## Kiểm thử

| Kiểm tra | Trước | Sau |
| --- | --- | --- |
| Backend toàn bộ | 2.099/2.099 | 2.102/2.102 |
| Backend nhóm ảnh hưởng trên Release cuối | — | 14/14 |
| Vitest | 1.446/1.446, 127 file | 1.469/1.469, 128 file |
| Frontend nhóm nhà thuốc và helper cuối | — | 40/40 |
| Frontend lint, `tsc -b`, build | — | Đạt, không lỗi; còn cảnh báo sẵn có |
| Backend build Debug/Release | — | Đạt |
| EF pending model changes | — | Không có thay đổi model |
| verify-ui toàn bộ | — | 51 route, 102/102 page-viewports, NEW/KNOWN/STALE = 0 |
| verify-ui nhà thuốc sau thay đổi cuối | — | 5 route, 10/10 page-viewports, NEW/KNOWN/STALE = 0 |

Test mới: `staffDisplayFormatting.test.tsx` có 23 trường hợp về giới tính, đủ enum kho, mã đơn, ngày mặc định và nút/điều hướng trang lỗi. `RealisticDevelopmentSeedTests.cs` có 3 test về seed thực tế/idempotent, ngày kê trong DTO kho, duyệt độc lập qua API và mã đơn bệnh nhân.

Test cũ chỉ đổi kỳ vọng cần thiết: mã lịch hẹn không `#`, mã RX, chip cơ sở không mã, ngày rỗng là `—`, và nghỉ phép đúng ca. Người dùng đã cho phép cập nhật trước PR và gắn `allow-test-changes` khi mở PR. Không skip test. Timeout chập chờn ở check-in được xử lý bằng chạy Vitest với 4 worker, không thay test.

## Ảnh và môi trường local

Đã đăng nhập lễ tân, bác sĩ, dược sĩ, kỹ thuật viên và admin trên web local; ảnh desktop rộng 1440 px:

- `screenshots/staff-reception.png`
- `screenshots/staff-doctor-appointments.png`
- `screenshots/staff-pharmacy-inventory.png`
- `screenshots/staff-pharmacy-prescriptions.png`
- `screenshots/staff-pharmacy.png`
- `screenshots/staff-diagnostics.png`
- `screenshots/staff-admin-accounts.png`
- `screenshots/staff-admin-billing.png`
- `screenshots/staff-404.png`
- `screenshots/staff-forbidden.png`

Backend local dùng DB `ClinicCareDemoBrand_20261024` qua cấu hình tiến trình, môi trường Development, bật seed lịch/khung giờ/lịch hẹn. Backend tại `http://localhost:5258`, frontend tại `http://localhost:5173`. Gemini giữ tắt. DB cũ không bị xóa; cập nhật seed tại chỗ không xóa dữ liệu. Không thay schema/migration hoặc vùng cấm.

## File thay đổi

Danh sách dưới đây gồm code, test, tài liệu này và ảnh được giao.

- `docs/ui/screenshots/staff-404.png`
- `docs/ui/screenshots/staff-admin-accounts.png`
- `docs/ui/screenshots/staff-admin-billing.png`
- `docs/ui/screenshots/staff-diagnostics.png`
- `docs/ui/screenshots/staff-doctor-appointments.png`
- `docs/ui/screenshots/staff-forbidden.png`
- `docs/ui/screenshots/staff-pharmacy-inventory.png`
- `docs/ui/screenshots/staff-pharmacy-prescriptions.png`
- `docs/ui/screenshots/staff-pharmacy.png`
- `docs/ui/screenshots/staff-reception.png`
- `docs/ui/staff-ui-and-demo-data-20261024.md`
- `src/backend/ClinicManagement.Application/Pharmacy/DTOs/PharmacyDtos.cs`
- `src/backend/ClinicManagement.Infrastructure/Patients/PatientService.cs`
- `src/backend/ClinicManagement.Infrastructure/Persistence/DevelopmentAppointmentSeed.cs`
- `src/backend/ClinicManagement.Infrastructure/Persistence/DevelopmentDataSeeder.cs`
- `src/backend/ClinicManagement.Infrastructure/Persistence/MedicineBatchDevelopmentSeed.cs`
- `src/backend/ClinicManagement.Infrastructure/Persistence/RealisticDevelopmentSeed.cs`
- `src/backend/ClinicManagement.Infrastructure/Pharmacy/PharmacyService.cs`
- `src/backend/ClinicManagement.IntegrationTests/DemoRunTimeAndStockTests.cs`
- `src/backend/ClinicManagement.IntegrationTests/RealisticDevelopmentSeedTests.cs`
- `src/frontend/src/components/common/RoleHomeButton.tsx`
- `src/frontend/src/pages/admin/AdminBilling.tsx`
- `src/frontend/src/pages/admin/AdminUsers.tsx`
- `src/frontend/src/pages/common/DiagnosticResultPrint.tsx`
- `src/frontend/src/pages/common/DiagnosticVerificationReview.tsx`
- `src/frontend/src/pages/Dashboards.tsx`
- `src/frontend/src/pages/doctor/DiagnosticOrderPrint.tsx`
- `src/frontend/src/pages/doctor/DoctorAppointmentDetail.tsx`
- `src/frontend/src/pages/doctor/DoctorAppointments.tsx`
- `src/frontend/src/pages/doctor/DoctorDashboard.tsx`
- `src/frontend/src/pages/doctor/DoctorExaminationWorkspace.tsx`
- `src/frontend/src/pages/doctor/DoctorQueue.tsx`
- `src/frontend/src/pages/Forbidden.tsx`
- `src/frontend/src/pages/patient/BookAppointment.tsx`
- `src/frontend/src/pages/patient/PatientAppointments.tsx`
- `src/frontend/src/pages/patient/PatientDiagnosticResults.tsx`
- `src/frontend/src/pages/patient/PatientPrescriptions.tsx`
- `src/frontend/src/pages/pharmacy/PharmacyDashboard.tsx`
- `src/frontend/src/pages/pharmacy/PharmacyInventory.tsx`
- `src/frontend/src/pages/pharmacy/PharmacyPrescriptions.tsx`
- `src/frontend/src/pages/reception/MpiPatientSearchModal.tsx`
- `src/frontend/src/pages/reception/ReceptionAppointments.tsx`
- `src/frontend/src/pages/reception/ReceptionChangeRequests.tsx`
- `src/frontend/src/pages/reception/ReceptionWorkspace.tsx`
- `src/frontend/src/pages/reception/WalkInPatientRegistration.tsx`
- `src/frontend/src/pages/technician/SpecimenLabelPrint.tsx`
- `src/frontend/src/pages/technician/TechnicianDashboard.tsx`
- `src/frontend/src/pages/technician/TechnicianOrderDetail.tsx`
- `src/frontend/src/test/appointmentAudienceLabels.test.tsx`
- `src/frontend/src/test/appointmentChangeRequests.test.tsx`
- `src/frontend/src/test/metricStrip.test.tsx`
- `src/frontend/src/test/patientAppointmentsScrollTimer.test.tsx`
- `src/frontend/src/test/patientAppointmentsUpcoming.test.tsx`
- `src/frontend/src/test/patientPagesBatch1.test.tsx`
- `src/frontend/src/test/patientPagesBatch2.test.tsx`
- `src/frontend/src/test/patientPagesBatch3.test.tsx`
- `src/frontend/src/test/pharmacyOpenPrescriptionById.test.tsx`
- `src/frontend/src/test/pharmacyPrescriptions.test.tsx`
- `src/frontend/src/test/receptionWorkspaceRebuild.test.tsx`
- `src/frontend/src/test/staffDisplayFormatting.test.tsx`
- `src/frontend/src/utils/auditLogLabels.ts`
- `src/frontend/src/utils/formatters.ts`
- `src/frontend/src/utils/genderLabels.ts`
- `src/frontend/src/utils/prescriptionCode.ts`
- `src/frontend/src/utils/stockTransactionLabels.ts`
