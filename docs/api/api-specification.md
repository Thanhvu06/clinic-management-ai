# Đặc tả API

Các route dưới đây ghép trực tiếp từ thuộc tính controller/action hiện tại, bao gồm bí danh. Mỗi dòng là một cặp method/route. Vai trò trên cùng một thuộc tính là OR; thuộc tính ở controller và action cùng áp dụng. “Đã đăng nhập” yêu cầu JWT nhưng không giới hạn role ở thuộc tính. Service còn kiểm tra chủ sở hữu, cơ sở, người nhập/người chỉ định và trạng thái tài nguyên.

Phản hồi thành công dùng `ApiResponse` với `success`, `message` và `data` khi có; lỗi dùng `ApiErrorResponse` với `success`, `message`, `errorCode`, `errors`; xem [ApiResponse.cs](../../src/backend/ClinicManagement.Application/Common/Models/ApiResponse.cs). JWT qua `Authorization: Bearer ...`. Upload dùng multipart; tải nội dung đính kèm trả tệp. Ngày query dùng `yyyy-MM-dd`.

## Hợp đồng tiếp nhận và lâm sàng

- `POST /api/v1/patient-visits/intake`: body [PatientVisitDtos.cs](../../src/backend/ClinicManagement.Application/Visits/DTOs/PatientVisitDtos.cs). Header `Idempotency-Key` chỉ được đưa vào body khi `IdempotencyKey` trong body là null (`??=` trong controller). Service kiểm tra khóa; cùng khóa khác payload bị từ chối. Kết quả là phiếu khám, MRN, số thứ tự và thông tin cơ sở/khoa/phòng.
- Check-in qua `POST /api/v1/patient-visits/check-in-appointment` hoặc `POST /api/v1/reception/appointments/{id}/check-in`; in lại qua `GET /api/v1/patient-visits/{id}/ticket`.
- Lễ tân lọc lịch theo `tab` (nhóm ngày/trạng thái), `status`, `search`, `facilityId`, `page`, `pageSize`; tham số chính xác nằm trong [ReceptionController.cs](../../src/backend/ClinicManagement.Api/Controllers/ReceptionController.cs). Phân công nhân viên dùng nhóm route `/api/v1/admin/staff-assignments`.
- Bàn khám hỗ trợ cả lịch hẹn và lượt vãng lai: bệnh án, sinh hiệu/BMI, lịch sử và chênh lệch nhân trắc, đơn nháp, kiểm tra an toàn thuốc, chỉ định CLS. Hoàn tất bị chặn nếu còn kết quả đang xử lý hoặc chưa xác nhận xem; xem [DoctorAppointmentService.cs](../../src/backend/ClinicManagement.Infrastructure/Appointments/DoctorAppointmentService.cs).
- CLS: lấy/nhận/từ chối mẫu → nhập kết quả → hoàn tất; nếu có dịch vụ chưa hủy với `SpecimenType` khác null thì gửi duyệt và người có quyền khác duyệt, sau đó bác sĩ xác nhận xem. Delta check, tệp kết quả và TAT thuộc [DiagnosticWorkflowService.cs](../../src/backend/ClinicManagement.Infrastructure/Diagnostics/DiagnosticWorkflowService.cs). Quyền tải tệp được kiểm tra tại service; không chỉ dựa vào JWT.
- Nhà thuốc: `confirm-purchase` giữ kho; `dispense` kiểm tra điều kiện thanh toán. Giá CLS quản trị qua `/api/v1/admin/diagnostic-services/{id}/pricing`; hóa đơn lượt khám qua `/api/v1/reception/billing/invoices/visit`.

## AdminBillingController — Báo cáo doanh thu và phí khám

Nguồn: [AdminBillingController.cs](../../src/backend/ClinicManagement.Api/Controllers/AdminBillingController.cs).

| Method | Route | Vai trò | Mô tả |
| --- | --- | --- | --- |
| GET | `/api/v1/admin/billing/revenue` | Admin | Báo cáo doanh thu |
| GET | `/api/v1/admin/billing/specialties` | Admin | Liệt kê phí khám chuyên khoa |
| PATCH | `/api/v1/admin/billing/specialties/{id}/fee` | Admin | Cập nhật phí khám |

## AdminController — Người dùng, chuyên khoa, bác sĩ, kiểm toán

Nguồn: [AdminController.cs](../../src/backend/ClinicManagement.Api/Controllers/AdminController.cs).

| Method | Route | Vai trò | Mô tả |
| --- | --- | --- | --- |
| GET | `/api/v1/admin/stats` | Admin | Thống kê tổng quan |
| GET | `/api/v1/admin/audit-logs` | Admin | Tra cứu nhật ký kiểm toán |
| GET | `/api/v1/admin/users` | Admin | Liệt kê tài khoản |
| GET | `/api/v1/admin/users/{id}` | Admin | Xem tài khoản |
| POST | `/api/v1/admin/users` | Admin | Tạo tài khoản nhân viên |
| PATCH | `/api/v1/admin/users/{id}/status` | Admin | Bật/tắt tài khoản |
| GET | `/api/v1/admin/specialties` | Admin | Liệt kê chuyên khoa |
| GET | `/api/v1/admin/specialties/{id}` | Admin | Xem chuyên khoa |
| POST | `/api/v1/admin/specialties` | Admin | Tạo chuyên khoa |
| PUT | `/api/v1/admin/specialties/{id}` | Admin | Cập nhật chuyên khoa |
| GET | `/api/v1/admin/doctors` | Admin | Liệt kê bác sĩ |
| GET | `/api/v1/admin/doctors/{id}` | Admin | Xem bác sĩ |
| POST | `/api/v1/admin/doctors` | Admin | Tạo bác sĩ |
| PUT | `/api/v1/admin/doctors/{id}` | Admin | Cập nhật bác sĩ |
| PUT | `/api/v1/admin/doctors/{id}/specialties` | Admin | Phân chuyên khoa cho bác sĩ |

## AdminDiagnosticPricingController — Giá dịch vụ cận lâm sàng

Nguồn: [AdminDiagnosticPricingController.cs](../../src/backend/ClinicManagement.Api/Controllers/AdminDiagnosticPricingController.cs).

| Method | Route | Vai trò | Mô tả |
| --- | --- | --- | --- |
| GET | `/api/v1/admin/diagnostic-services/pricing` | Admin | Lấy giá dịch vụ |
| PUT | `/api/v1/admin/diagnostic-services/{id}/pricing` | Admin | Cập nhật giá dịch vụ |

## AdminStaffAssignmentsController — Phân công cơ sở

Nguồn: [AdminStaffAssignmentsController.cs](../../src/backend/ClinicManagement.Api/Controllers/AdminStaffAssignmentsController.cs).

| Method | Route | Vai trò | Mô tả |
| --- | --- | --- | --- |
| GET | `/api/v1/admin/staff-assignments` | Admin | Tra cứu dữ liệu phân công cơ sở |
| POST | `/api/v1/admin/staff-assignments` | Admin | Tạo mới dữ liệu phân công cơ sở |
| DELETE | `/api/v1/admin/staff-assignments/{id}` | Admin | Xóa dữ liệu phân công cơ sở |

## AdminWorkScheduleController — Lịch làm việc

Nguồn: [AdminWorkScheduleController.cs](../../src/backend/ClinicManagement.Api/Controllers/AdminWorkScheduleController.cs).

| Method | Route | Vai trò | Mô tả |
| --- | --- | --- | --- |
| GET | `/api/v1/admin/doctors/{doctorId}/work-schedules` | Admin | Lấy lịch làm việc của bác sĩ |
| POST | `/api/v1/admin/doctors/{doctorId}/work-schedules` | Admin | Tạo mới dữ liệu lịch làm việc |
| PUT | `/api/v1/admin/work-schedules/{scheduleId}` | Admin | Cập nhật dữ liệu lịch làm việc |
| PATCH | `/api/v1/admin/work-schedules/{scheduleId}/status` | Admin | Cập nhật dữ liệu lịch làm việc |
| POST | `/api/v1/admin/work-schedules/{scheduleId}/generate-slots` | Admin | Sinh khung giờ từ lịch làm việc |

## AppointmentChangeRequestController — Yêu cầu đổi/hủy của bệnh nhân

Nguồn: [AppointmentChangeRequestController.cs](../../src/backend/ClinicManagement.Api/Controllers/AppointmentChangeRequestController.cs).

| Method | Route | Vai trò | Mô tả |
| --- | --- | --- | --- |
| GET | `/api/v1/appointment-change-requests` | Patient | Xem yêu cầu đổi/hủy cá nhân |
| POST | `/api/v1/appointment-change-requests/{id}/withdraw` | Patient | Rút yêu cầu đổi/hủy |

## AppointmentController — Lịch hẹn bệnh nhân

Nguồn: [AppointmentController.cs](../../src/backend/ClinicManagement.Api/Controllers/AppointmentController.cs).

| Method | Route | Vai trò | Mô tả |
| --- | --- | --- | --- |
| GET | `/api/v1/appointments/lookup` | Công khai | Tra cứu bằng mã lịch và số điện thoại |
| POST | `/api/v1/appointments` | Patient | Tạo mới dữ liệu lịch hẹn bệnh nhân |
| GET | `/api/v1/appointments/my` | Patient | Xem lịch hẹn của bệnh nhân |
| GET | `/api/v1/appointments/{id}` | Patient | Xem chi tiết lịch hẹn cá nhân |
| GET | `/api/v1/appointments/{id}/history` | Patient | Xem lịch sử lịch hẹn |
| POST | `/api/v1/appointments/{id}/reschedule-requests` | Patient | Gửi yêu cầu đổi lịch |
| POST | `/api/v1/appointments/{id}/cancellation-requests` | Patient | Gửi yêu cầu hủy lịch |

## AuthController — Tài khoản

Nguồn: [AuthController.cs](../../src/backend/ClinicManagement.Api/Controllers/AuthController.cs).

| Method | Route | Vai trò | Mô tả |
| --- | --- | --- | --- |
| POST | `/api/v1/auth/register` | Công khai | Đăng ký tài khoản bệnh nhân |
| POST | `/api/v1/auth/login` | Công khai | Đăng nhập và cấp JWT |
| GET | `/api/v1/auth/me` | Đã đăng nhập | Xem tài khoản hiện tại |
| POST | `/api/v1/auth/logout` | Đã đăng nhập | Đăng xuất |
| POST | `/api/v1/auth/forgot-password` | Công khai | Yêu cầu đặt lại mật khẩu |
| POST | `/api/v1/auth/reset-password` | Công khai | Đặt lại mật khẩu |

## DiagnosticAttachmentsController — Nội dung đính kèm

Nguồn: [DiagnosticAttachmentsController.cs](../../src/backend/ClinicManagement.Api/Controllers/DiagnosticAttachmentsController.cs).

| Method | Route | Vai trò | Mô tả |
| --- | --- | --- | --- |
| GET | `/api/v1/diagnostic-attachments/{id}/content` | Đã đăng nhập | Tải nội dung tệp theo quyền sở hữu/tài nguyên |

## DiagnosticCatalogController — Danh mục cận lâm sàng

Nguồn: [DiagnosticCatalogController.cs](../../src/backend/ClinicManagement.Api/Controllers/DiagnosticCatalogController.cs).

| Method | Route | Vai trò | Mô tả |
| --- | --- | --- | --- |
| GET | `/api/v1/diagnostic-services` | Doctor, DiagnosticTechnician, Admin | Tra cứu dịch vụ cận lâm sàng |

## DiagnosticTechnicianController — Thực hiện cận lâm sàng

Nguồn: [DiagnosticTechnicianController.cs](../../src/backend/ClinicManagement.Api/Controllers/DiagnosticTechnicianController.cs).

| Method | Route | Vai trò | Mô tả |
| --- | --- | --- | --- |
| GET | `/api/v1/diagnostics/orders/verification-queue` | DiagnosticTechnician | Lấy hàng đợi duyệt kết quả |
| POST | `/api/v1/diagnostics/orders/{orderId}/verify` | DiagnosticTechnician | Duyệt kết quả |
| POST | `/api/v1/diagnostics/orders/{orderId}/return` | DiagnosticTechnician | Trả kết quả để sửa |
| POST | `/api/v1/diagnostics/orders/{orderId}/items/{itemId}/specimen/collect` | DiagnosticTechnician | Ghi nhận lấy mẫu |
| POST | `/api/v1/diagnostics/orders/{orderId}/items/{itemId}/specimen/receive` | DiagnosticTechnician | Tiếp nhận mẫu |
| POST | `/api/v1/diagnostics/orders/{orderId}/items/{itemId}/specimen/reject` | DiagnosticTechnician | Từ chối mẫu |
| POST | `/api/v1/diagnostics/orders/{orderId}/items/{itemId}/attachments` | DiagnosticTechnician | Tải tệp kết quả lên |
| DELETE | `/api/v1/diagnostics/orders/{orderId}/items/{itemId}/attachments/{attachmentId}` | DiagnosticTechnician | Xóa tệp kết quả |
| GET | `/api/v1/diagnostics/orders` | DiagnosticTechnician | Liệt kê chỉ định |
| GET | `/api/v1/diagnostics/orders/stats` | DiagnosticTechnician | Thống kê tổng quan |
| GET | `/api/v1/diagnostics/orders/{orderId}` | DiagnosticTechnician | Xem chi tiết chỉ định |
| POST | `/api/v1/diagnostics/orders/{orderId}/start` | DiagnosticTechnician | Bắt đầu thực hiện chỉ định |
| PUT | `/api/v1/diagnostics/orders/{orderId}/items/{itemId}/result` | DiagnosticTechnician | Nhập kết quả dịch vụ |
| POST | `/api/v1/diagnostics/orders/{orderId}/complete` | DiagnosticTechnician | Gửi hoàn tất để duyệt |

## DiagnosticVerificationController — Duyệt cận lâm sàng

Nguồn: [DiagnosticVerificationController.cs](../../src/backend/ClinicManagement.Api/Controllers/DiagnosticVerificationController.cs).

| Method | Route | Vai trò | Mô tả |
| --- | --- | --- | --- |
| GET | `/api/v1/diagnostic-verification/stats` | DiagnosticTechnician, LabSupervisor, Doctor | Thống kê dữ liệu duyệt cận lâm sàng |
| GET | `/api/v1/diagnostic-verification/queue` | DiagnosticTechnician, LabSupervisor, Doctor | Xem hàng đợi dữ liệu duyệt cận lâm sàng |
| GET | `/api/v1/diagnostic-verification/orders/{orderId}` | DiagnosticTechnician, LabSupervisor, Doctor | Xem phiếu dữ liệu duyệt cận lâm sàng |
| POST | `/api/v1/diagnostic-verification/orders/{orderId}/verify` | DiagnosticTechnician, LabSupervisor, Doctor | Duyệt kết quả |
| POST | `/api/v1/diagnostic-verification/orders/{orderId}/return` | DiagnosticTechnician, LabSupervisor, Doctor | Trả kết quả để sửa |

## DoctorAppointmentController — Khám theo lịch hẹn

Nguồn: [DoctorAppointmentController.cs](../../src/backend/ClinicManagement.Api/Controllers/DoctorAppointmentController.cs).

| Method | Route | Vai trò | Mô tả |
| --- | --- | --- | --- |
| GET | `/api/v1/doctor/appointments` | Doctor | Lấy lịch khám của bác sĩ |
| GET | `/api/v1/doctor/appointments/queue` | Doctor | Xem hàng đợi khám |
| GET | `/api/v1/doctor/queue` | Doctor | Xem hàng đợi khám |
| GET | `/api/v1/doctor/appointments/{id}` | Doctor | Xem chi tiết lịch hẹn |
| GET | `/api/v1/doctor/appointments/{id}/history` | Doctor | Xem lịch sử lịch hẹn |
| GET | `/api/v1/doctor/appointments/{id}/patient-context` | Doctor | Xem bối cảnh lâm sàng của bệnh nhân |
| GET | `/api/v1/doctor/appointments/{id}/clinical-context` | Doctor | Xem bối cảnh lâm sàng của bệnh nhân |
| POST | `/api/v1/doctor/appointments/{id}/check-in` | Doctor | Check-in lịch hẹn |
| POST | `/api/v1/doctor/appointments/{id}/start-consultation` | Doctor | Bắt đầu khám |
| POST | `/api/v1/doctor/appointments/{id}/complete` | Doctor | Hoàn tất khám theo lịch hẹn |
| POST | `/api/v1/doctor/appointments/{id}/noshow` | Doctor | Ghi nhận vắng mặt |
| POST | `/api/v1/doctor/appointments/{id}/revisit-requests` | Doctor | Đề xuất tái khám |
| GET | `/api/v1/doctor/appointments/{id}/encounter` | Doctor | Xem bệnh án |
| PUT | `/api/v1/doctor/appointments/{id}/encounter` | Doctor | Lưu bệnh án |
| GET | `/api/v1/doctor/appointments/{id}/vitals` | Doctor | Xem sinh hiệu |
| PUT | `/api/v1/doctor/appointments/{id}/vitals` | Doctor | Lưu sinh hiệu |
| POST | `/api/v1/doctor/appointments/{id}/prescription/safety-check` | Doctor | Kiểm tra an toàn kê đơn |
| GET | `/api/v1/doctor/appointments/{id}/prescription` | Doctor | Xem đơn nháp |
| GET | `/api/v1/doctor/appointments/{id}/prescription-draft` | Doctor | Xem đơn nháp |
| PUT | `/api/v1/doctor/appointments/{id}/prescription` | Doctor | Lưu đơn nháp |
| PUT | `/api/v1/doctor/appointments/{id}/prescription-draft` | Doctor | Lưu đơn nháp |
| POST | `/api/v1/doctor/appointments/{id}/prescription` | Doctor | Lưu đơn nháp (bí danh POST) |
| POST | `/api/v1/doctor/appointments/{id}/prescription-draft` | Doctor | Lưu đơn nháp (bí danh POST) |

## DoctorController — Danh mục bác sĩ và lịch trống

Nguồn: [DoctorController.cs](../../src/backend/ClinicManagement.Api/Controllers/DoctorController.cs).

| Method | Route | Vai trò | Mô tả |
| --- | --- | --- | --- |
| GET | `/api/v1/doctors` | Công khai | Liệt kê bác sĩ |
| GET | `/api/v1/doctors/{doctorId}` | Công khai | Xem bác sĩ |
| GET | `/api/v1/doctors/{doctorId}/specialties` | Công khai | Lấy chuyên khoa của bác sĩ |
| GET | `/api/v1/doctors/{doctorId}/available-slots` | Công khai | Lấy khung giờ còn trống |
| GET | `/api/v1/doctors/{doctorId}/availability` | Công khai | Lấy lịch khả dụng theo ngày |

## DoctorDiagnosticOrderController — Chỉ định và xem kết quả

Nguồn: [DoctorDiagnosticOrderController.cs](../../src/backend/ClinicManagement.Api/Controllers/DoctorDiagnosticOrderController.cs).

| Method | Route | Vai trò | Mô tả |
| --- | --- | --- | --- |
| POST | `/api/v1/doctor/appointments/{appointmentId}/diagnostic-orders` | Doctor | Tạo chỉ định từ lịch hẹn |
| GET | `/api/v1/doctor/appointments/{appointmentId}/diagnostic-orders` | Doctor | Xem chỉ định theo lịch hẹn |
| POST | `/api/v1/doctor/visits/{visitId}/diagnostic-orders` | Doctor | Tạo chỉ định từ lượt khám |
| GET | `/api/v1/doctor/visits/{visitId}/diagnostic-orders` | Doctor | Xem chỉ định theo lượt khám |
| GET | `/api/v1/doctor/diagnostic-orders/{orderId}` | Doctor | Xem chi tiết chỉ định |
| POST | `/api/v1/doctor/diagnostic-orders/{orderId}/review` | Doctor | Bác sĩ xác nhận đã xem kết quả |
| POST | `/api/v1/doctor/diagnostic-orders/{orderId}/cancel` | Doctor | Hủy chỉ định |

## DoctorVisitsController — Khám theo lượt

Nguồn: [DoctorVisitsController.cs](../../src/backend/ClinicManagement.Api/Controllers/DoctorVisitsController.cs).

| Method | Route | Vai trò | Mô tả |
| --- | --- | --- | --- |
| POST | `/api/v1/doctor/visits/{id}/prescription/safety-check` | Doctor | Kiểm tra an toàn kê đơn |
| GET | `/api/v1/doctor/visits/{id}/patient-context` | Doctor | Xem bối cảnh lâm sàng của bệnh nhân |
| GET | `/api/v1/doctor/visits/{id}/clinical-context` | Doctor | Xem bối cảnh lâm sàng của bệnh nhân |
| POST | `/api/v1/doctor/visits/{id}/start-consultation` | Doctor | Bắt đầu khám |
| POST | `/api/v1/doctor/visits/{id}/complete` | Doctor | Hoàn tất lượt khám |
| GET | `/api/v1/doctor/visits/{id}/encounter` | Doctor | Xem bệnh án |
| PUT | `/api/v1/doctor/visits/{id}/encounter` | Doctor | Lưu bệnh án |
| GET | `/api/v1/doctor/visits/{id}/vitals` | Doctor | Xem sinh hiệu |
| PUT | `/api/v1/doctor/visits/{id}/vitals` | Doctor | Lưu sinh hiệu |
| GET | `/api/v1/doctor/visits/{id}/prescription-draft` | Doctor | Xem đơn nháp |
| PUT | `/api/v1/doctor/visits/{id}/prescription-draft` | Doctor | Lưu đơn nháp |

## DoctorWorkspaceController — Bàn làm việc bác sĩ

Nguồn: [DoctorWorkspaceController.cs](../../src/backend/ClinicManagement.Api/Controllers/DoctorWorkspaceController.cs).

| Method | Route | Vai trò | Mô tả |
| --- | --- | --- | --- |
| GET | `/api/v1/doctor/dashboard` | Doctor | Lấy tổng quan bàn làm việc |
| GET | `/api/v1/doctor/schedule` | Doctor | Xem lịch làm việc |

## HealthPackageController — Danh mục gói khám

Nguồn: [HealthPackageController.cs](../../src/backend/ClinicManagement.Api/Controllers/HealthPackageController.cs).

| Method | Route | Vai trò | Mô tả |
| --- | --- | --- | --- |
| GET | `/api/v1/health-packages` | Công khai | Tra cứu dữ liệu danh mục gói khám |
| GET | `/api/v1/health-packages/{id}` | Công khai | Tra cứu dữ liệu danh mục gói khám |
| GET | `/api/v1/admin/health-packages` | Admin | Tra cứu dữ liệu danh mục gói khám |
| POST | `/api/v1/admin/health-packages` | Admin | Tạo mới dữ liệu danh mục gói khám |
| PUT | `/api/v1/admin/health-packages/{id}` | Admin | Cập nhật dữ liệu danh mục gói khám |
| DELETE | `/api/v1/admin/health-packages/{id}` | Admin | Xóa dữ liệu danh mục gói khám |

## Icd10Controller — Danh mục ICD-10

Nguồn: [Icd10Controller.cs](../../src/backend/ClinicManagement.Api/Controllers/Icd10Controller.cs).

| Method | Route | Vai trò | Mô tả |
| --- | --- | --- | --- |
| GET | `/api/v1/icd10` | Doctor, Receptionist, Admin | Tìm kiếm dữ liệu danh mục icd-10 |

## LabReportsController — Báo cáo TAT

Nguồn: [LabReportsController.cs](../../src/backend/ClinicManagement.Api/Controllers/LabReportsController.cs).

| Method | Route | Vai trò | Mô tả |
| --- | --- | --- | --- |
| GET | `/api/v1/lab-reports/turnaround` | LabSupervisor | Báo cáo thời gian trả kết quả |

## AdminLeaveController — Duyệt nghỉ phép

Nguồn: [AdminLeaveController.cs](../../src/backend/ClinicManagement.Api/Controllers/Leaves/AdminLeaveController.cs).

| Method | Route | Vai trò | Mô tả |
| --- | --- | --- | --- |
| GET | `/api/v1/admin/leave-requests` | Admin | Liệt kê đơn nghỉ |
| GET | `/api/v1/admin/leave-requests/{id}` | Admin | Xem đơn nghỉ |
| POST | `/api/v1/admin/leave-requests/{id}/approve` | Admin | Duyệt nghỉ phép |
| POST | `/api/v1/admin/leave-requests/{id}/reject` | Admin | Từ chối nghỉ phép |

## DoctorLeaveController — Nghỉ phép bác sĩ

Nguồn: [DoctorLeaveController.cs](../../src/backend/ClinicManagement.Api/Controllers/Leaves/DoctorLeaveController.cs).

| Method | Route | Vai trò | Mô tả |
| --- | --- | --- | --- |
| GET | `/api/v1/doctor/leave-requests` | Doctor | Xem đơn nghỉ của bác sĩ |
| GET | `/api/v1/doctor/leave-requests/{id}` | Doctor | Xem đơn nghỉ |
| GET | `/api/v1/doctor/leave-requests/preview` | Doctor | Xem trước lịch bị ảnh hưởng bởi nghỉ phép |
| GET | `/api/v1/doctor/leave-requests/preview-impact` | Doctor | Xem trước lịch bị ảnh hưởng bởi nghỉ phép |
| POST | `/api/v1/doctor/leave-requests` | Doctor | Tạo mới dữ liệu nghỉ phép bác sĩ |
| POST | `/api/v1/doctor/leave-requests/{id}/withdraw` | Doctor | Rút đơn nghỉ phép |

## LocationsController — Địa điểm phòng khám

Nguồn: [LocationsController.cs](../../src/backend/ClinicManagement.Api/Controllers/LocationsController.cs).

| Method | Route | Vai trò | Mô tả |
| --- | --- | --- | --- |
| GET | `/api/v1/locations` | Công khai | Tra cứu dữ liệu địa điểm phòng khám |
| GET | `/api/v1/locations/{id}` | Công khai | Tra cứu dữ liệu địa điểm phòng khám |

## MedicineCategoriesController — Nhóm thuốc

Nguồn: [MedicineCategoriesController.cs](../../src/backend/ClinicManagement.Api/Controllers/MedicineCategoriesController.cs).

| Method | Route | Vai trò | Mô tả |
| --- | --- | --- | --- |
| GET | `/api/v1/admin/medicine-categories` | Admin, Pharmacist | Tra cứu dữ liệu nhóm thuốc |
| POST | `/api/v1/admin/medicine-categories` | Admin, Pharmacist | Tạo mới dữ liệu nhóm thuốc |
| PUT | `/api/v1/admin/medicine-categories/{id}` | Admin, Pharmacist | Cập nhật dữ liệu nhóm thuốc |
| PATCH | `/api/v1/admin/medicine-categories/{id}/toggle-status` | Admin, Pharmacist | Bật/tắt dữ liệu nhóm thuốc |

## MedicineController — Danh mục thuốc nội bộ

Nguồn: [MedicineController.cs](../../src/backend/ClinicManagement.Api/Controllers/MedicineController.cs).

| Method | Route | Vai trò | Mô tả |
| --- | --- | --- | --- |
| GET | `/api/v1/medicines/active` | Doctor, Pharmacist, Admin | Tra cứu dữ liệu danh mục thuốc nội bộ |
| GET | `/api/v1/admin/medicines` | Admin, Pharmacist | Tra cứu dữ liệu danh mục thuốc nội bộ |
| GET | `/api/v1/admin/medicines/{id}` | Admin, Pharmacist | Tra cứu dữ liệu danh mục thuốc nội bộ |
| POST | `/api/v1/admin/medicines` | Admin, Pharmacist | Tạo mới dữ liệu danh mục thuốc nội bộ |
| PUT | `/api/v1/admin/medicines/{id}` | Admin, Pharmacist | Cập nhật dữ liệu danh mục thuốc nội bộ |
| PATCH | `/api/v1/admin/medicines/{id}/toggle-status` | Admin, Pharmacist | Bật/tắt thuốc |

## MedicineImagesController — Ảnh thuốc

Nguồn: [MedicineImagesController.cs](../../src/backend/ClinicManagement.Api/Controllers/MedicineImagesController.cs).

| Method | Route | Vai trò | Mô tả |
| --- | --- | --- | --- |
| POST | `/api/v1/admin/medicines/{id}/image` | Admin, Pharmacist | Tải ảnh thuốc lên |
| DELETE | `/api/v1/admin/medicines/{id}/image` | Admin, Pharmacist | Xóa dữ liệu |

## MpiPatientsController — Hồ sơ MPI

Nguồn: [MpiPatientsController.cs](../../src/backend/ClinicManagement.Api/Controllers/Mpi/MpiPatientsController.cs).

| Method | Route | Vai trò | Mô tả |
| --- | --- | --- | --- |
| GET | `/api/v1/mpi/patients` | Admin, Receptionist, Doctor | Tìm kiếm dữ liệu hồ sơ mpi |
| GET | `/api/v1/mpi/patients/{id}` | Admin, Receptionist, Doctor | Xem hồ sơ bệnh nhân |
| GET | `/api/v1/mpi/patients/by-mrn/{mrn}` | Admin, Receptionist, Doctor | Tra hồ sơ theo MRN |
| POST | `/api/v1/mpi/patients/walk-in` | Admin, Receptionist | Tạo hồ sơ MPI vãng lai |
| PUT | `/api/v1/mpi/patients/{id}` | Admin, Receptionist | Cập nhật hồ sơ MPI |
| POST | `/api/v1/mpi/patients/{id}/allergies` | Admin, Receptionist, Doctor | Thêm dị ứng |
| DELETE | `/api/v1/mpi/patients/{id}/allergies/{allergyId}` | Admin, Doctor, Receptionist | Xóa dị ứng |

## NotificationController — Thông báo cá nhân

Nguồn: [NotificationController.cs](../../src/backend/ClinicManagement.Api/Controllers/NotificationController.cs).

| Method | Route | Vai trò | Mô tả |
| --- | --- | --- | --- |
| GET | `/api/v1/notifications` | Đã đăng nhập | Lấy thông báo cá nhân |
| GET | `/api/v1/notifications/unread-count` | Đã đăng nhập | Đếm thông báo chưa đọc |
| PATCH | `/api/v1/notifications/{id}/read` | Đã đăng nhập | Đánh dấu đã đọc |
| PATCH | `/api/v1/notifications/read-all` | Đã đăng nhập | Đánh dấu tất cả đã đọc |

## BedsController — Giường

Nguồn: [BedsController.cs](../../src/backend/ClinicManagement.Api/Controllers/Organization/BedsController.cs).

| Method | Route | Vai trò | Mô tả |
| --- | --- | --- | --- |
| GET | `/api/v1/beds/by-room/{roomId}` | Đã đăng nhập | Tra cứu dữ liệu giường |
| GET | `/api/v1/beds/by-department/{departmentId}` | Đã đăng nhập | Tra cứu dữ liệu giường |
| POST | `/api/v1/beds` | Admin | Tạo mới dữ liệu giường |
| PATCH | `/api/v1/beds/{id}/status` | Admin, Doctor, Receptionist | Cập nhật dữ liệu giường |

## DepartmentsController — Khoa

Nguồn: [DepartmentsController.cs](../../src/backend/ClinicManagement.Api/Controllers/Organization/DepartmentsController.cs).

| Method | Route | Vai trò | Mô tả |
| --- | --- | --- | --- |
| GET | `/api/v1/departments` | Công khai | Tra cứu dữ liệu khoa |
| GET | `/api/v1/departments/{id}` | Công khai | Tra cứu dữ liệu khoa |
| POST | `/api/v1/departments` | Admin | Tạo mới dữ liệu khoa |

## FacilitiesController — Cơ sở và tòa nhà

Nguồn: [FacilitiesController.cs](../../src/backend/ClinicManagement.Api/Controllers/Organization/FacilitiesController.cs).

| Method | Route | Vai trò | Mô tả |
| --- | --- | --- | --- |
| GET | `/api/v1/facilities/my` | Đã đăng nhập | Lấy cơ sở được phân công |
| GET | `/api/v1/facilities` | Công khai | Tra cứu dữ liệu cơ sở và tòa nhà |
| GET | `/api/v1/facilities/{id}` | Công khai | Tra cứu dữ liệu cơ sở và tòa nhà |
| POST | `/api/v1/facilities` | Admin | Tạo mới dữ liệu cơ sở và tòa nhà |
| PUT | `/api/v1/facilities/{id}` | Admin | Cập nhật dữ liệu cơ sở và tòa nhà |
| PATCH | `/api/v1/facilities/{id}/toggle-status` | Admin | Bật/tắt trạng thái |
| GET | `/api/v1/facilities/{id}/buildings` | Công khai | Tra cứu dữ liệu cơ sở và tòa nhà |
| POST | `/api/v1/facilities/{id}/buildings` | Admin | Tạo mới dữ liệu cơ sở và tòa nhà |

## HospitalRoomsController — Phòng

Nguồn: [HospitalRoomsController.cs](../../src/backend/ClinicManagement.Api/Controllers/Organization/HospitalRoomsController.cs).

| Method | Route | Vai trò | Mô tả |
| --- | --- | --- | --- |
| GET | `/api/v1/hospital-rooms` | Đã đăng nhập | Tra cứu dữ liệu phòng |
| GET | `/api/v1/hospital-rooms/{id}` | Đã đăng nhập | Tra cứu dữ liệu phòng |
| POST | `/api/v1/hospital-rooms` | Admin | Tạo mới dữ liệu phòng |

## PatientBillingController — Hóa đơn cá nhân

Nguồn: [PatientBillingController.cs](../../src/backend/ClinicManagement.Api/Controllers/PatientBillingController.cs).

| Method | Route | Vai trò | Mô tả |
| --- | --- | --- | --- |
| GET | `/api/v1/patient/invoices` | Patient | Xem hóa đơn cá nhân |
| GET | `/api/v1/patient/invoices/{id}` | Patient | Xem chi tiết hóa đơn cá nhân |

## PatientController — Hồ sơ cá nhân

Nguồn: [PatientController.cs](../../src/backend/ClinicManagement.Api/Controllers/PatientController.cs).

| Method | Route | Vai trò | Mô tả |
| --- | --- | --- | --- |
| GET | `/api/v1/patients/me` | Patient | Xem hồ sơ cá nhân |
| PUT | `/api/v1/patients/me` | Patient | Cập nhật hồ sơ cá nhân |
| GET | `/api/v1/patients/me/prescriptions` | Patient | Xem đơn thuốc cá nhân |
| GET | `/api/v1/patients/me/vitals` | Patient | Xem lịch sử sinh hiệu cá nhân |

## PatientDiagnosticController — Kết quả cận lâm sàng cá nhân

Nguồn: [PatientDiagnosticController.cs](../../src/backend/ClinicManagement.Api/Controllers/PatientDiagnosticController.cs).

| Method | Route | Vai trò | Mô tả |
| --- | --- | --- | --- |
| GET | `/api/v1/patients/me/diagnostic-orders` | Patient | Xem chỉ định đã công bố cho bệnh nhân |
| GET | `/api/v1/patients/me/diagnostic-orders/{orderId}` | Patient | Xem chi tiết kết quả đã công bố |

## PatientHealthPackageRegistrationController — Đăng ký gói của bệnh nhân

Nguồn: [PatientHealthPackageRegistrationController.cs](../../src/backend/ClinicManagement.Api/Controllers/PatientHealthPackageRegistrationController.cs).

| Method | Route | Vai trò | Mô tả |
| --- | --- | --- | --- |
| POST | `/api/v1/patient/health-package-registrations` | Patient | Đăng ký dữ liệu đăng ký gói của bệnh nhân |
| GET | `/api/v1/patient/health-package-registrations` | Patient | Tra cứu dữ liệu đăng ký gói của bệnh nhân |
| GET | `/api/v1/patient/health-package-registrations/{id}` | Patient | Tra cứu dữ liệu đăng ký gói của bệnh nhân |
| POST | `/api/v1/patient/health-package-registrations/{id}/cancel` | Patient | Hủy dữ liệu đăng ký gói của bệnh nhân |

## PatientVisitsController — Tiếp nhận và điều phối

Nguồn: [PatientVisitsController.cs](../../src/backend/ClinicManagement.Api/Controllers/PatientVisitsController.cs).

| Method | Route | Vai trò | Mô tả |
| --- | --- | --- | --- |
| POST | `/api/v1/patient-visits/intake` | Receptionist, Admin | Tiếp nhận và cấp phiếu khám |
| POST | `/api/v1/patient-visits/walk-in` | Receptionist, Admin | Tiếp nhận bệnh nhân vãng lai |
| POST | `/api/v1/patient-visits/check-in-appointment` | Receptionist, Admin | Check-in lịch hẹn |
| GET | `/api/v1/patient-visits/{id}` | Doctor, Receptionist, Admin, Pharmacist, DiagnosticTechnician | Xem chi tiết lượt khám |
| GET | `/api/v1/patient-visits/{id}/ticket` | Receptionist, Admin | Lấy phiếu khám để in lại |
| GET | `/api/v1/patient-visits/department-queue` | Doctor, Receptionist, Admin | Xem hàng đợi của khoa |
| POST | `/api/v1/patient-visits/{id}/assign-doctor` | Doctor, Receptionist, Admin | Phân bác sĩ |
| PUT | `/api/v1/patient-visits/{id}/status` | Doctor, Receptionist, Admin | Chuyển trạng thái lượt khám |

## PharmacyBatchesController — Lô thuốc

Nguồn: [PharmacyBatchesController.cs](../../src/backend/ClinicManagement.Api/Controllers/PharmacyBatchesController.cs).

| Method | Route | Vai trò | Mô tả |
| --- | --- | --- | --- |
| GET | `/api/v1/pharmacy/batches` | Pharmacist, Admin | Liệt kê dữ liệu lô thuốc |
| POST | `/api/v1/pharmacy/batches` | Pharmacist, Admin | Tạo mới dữ liệu lô thuốc |
| POST | `/api/v1/pharmacy/batches/{id}/dispose` | Pharmacist, Admin | Hủy tồn lô thuốc |

## PharmacyController — Nhà thuốc

Nguồn: [PharmacyController.cs](../../src/backend/ClinicManagement.Api/Controllers/PharmacyController.cs).

| Method | Route | Vai trò | Mô tả |
| --- | --- | --- | --- |
| GET | `/api/v1/pharmacy/dashboard` | Pharmacist, Admin | Thống kê nhà thuốc |
| GET | `/api/v1/pharmacy/prescriptions` | Pharmacist, Admin | Liệt kê đơn thuốc |
| GET | `/api/v1/pharmacy/prescriptions/{id}` | Pharmacist, Admin | Xem chi tiết đơn thuốc |
| POST | `/api/v1/pharmacy/prescriptions/{id}/confirm-purchase` | Pharmacist, Receptionist, Admin | Xác nhận mua và giữ thuốc |
| POST | `/api/v1/pharmacy/prescriptions/{id}/dispense` | Pharmacist, Admin | Cấp phát đơn thuốc |
| GET | `/api/v1/pharmacy/inventory-transactions` | Pharmacist, Admin | Xem lịch sử giao dịch kho |
| POST | `/api/v1/pharmacy/inventory/adjust` | Pharmacist, Admin | Điều chỉnh tồn kho |

## PublicMedicinesController — Thuốc công khai

Nguồn: [PublicMedicinesController.cs](../../src/backend/ClinicManagement.Api/Controllers/PublicMedicinesController.cs).

| Method | Route | Vai trò | Mô tả |
| --- | --- | --- | --- |
| GET | `/api/v1/public/medicines` | Công khai | Tra cứu thuốc dữ liệu thuốc công khai |
| GET | `/api/v1/public/medicines/{id}` | Công khai | Xem chi tiết thuốc dữ liệu thuốc công khai |
| GET | `/api/v1/public/medicine-categories` | Công khai | Tra cứu nhóm thuốc dữ liệu thuốc công khai |

## ReceptionBillingController — Thu ngân

Nguồn: [ReceptionBillingController.cs](../../src/backend/ClinicManagement.Api/Controllers/ReceptionBillingController.cs).

| Method | Route | Vai trò | Mô tả |
| --- | --- | --- | --- |
| GET | `/api/v1/reception/billing/invoices` | Receptionist, Admin | Liệt kê hóa đơn |
| GET | `/api/v1/reception/billing/invoices/{id}` | Receptionist, Admin | Xem chi tiết hóa đơn |
| POST | `/api/v1/reception/billing/invoices/appointment` | Receptionist, Admin | Lập hóa đơn lịch hẹn |
| POST | `/api/v1/reception/billing/invoices/visit` | Receptionist, Admin | Lập hóa đơn lượt khám |
| GET | `/api/v1/reception/billing/unbilled-visits` | Receptionist, Admin | Lấy lượt khám chưa thu đủ |
| POST | `/api/v1/reception/billing/invoices/health-package` | Receptionist, Admin | Lập hóa đơn gói khám |
| POST | `/api/v1/reception/billing/invoices/{id}/pay` | Receptionist, Admin | Thu tiền hóa đơn |
| PATCH | `/api/v1/reception/billing/invoices/{id}/cancel` | Receptionist, Admin | Hủy hóa đơn |
| GET | `/api/v1/reception/billing/kpi` | Receptionist, Admin | Thống kê thu ngân hôm nay |

## ReceptionController — Lễ tân

Nguồn: [ReceptionController.cs](../../src/backend/ClinicManagement.Api/Controllers/ReceptionController.cs).

| Method | Route | Vai trò | Mô tả |
| --- | --- | --- | --- |
| GET | `/api/v1/reception/stats` | Receptionist, Admin | Thống kê tổng quan |
| GET | `/api/v1/reception/appointments` | Receptionist, Admin | Liệt kê lịch hẹn |
| GET | `/api/v1/reception/appointments/{id}` | Receptionist, Admin | Xem chi tiết lịch hẹn |
| GET | `/api/v1/reception/appointments/{id}/history` | Receptionist, Admin | Xem lịch sử lịch hẹn |
| POST | `/api/v1/reception/appointments/{id}/confirm` | Receptionist, Admin | Xác nhận lịch hẹn |
| POST | `/api/v1/reception/appointments/{id}/check-in` | Receptionist, Admin | Check-in lịch hẹn |
| POST | `/api/v1/reception/walk-in` | Receptionist, Admin | Tiếp nhận bệnh nhân vãng lai |
| GET | `/api/v1/reception/change-requests` | Receptionist, Admin | Liệt kê yêu cầu đổi/hủy |
| GET | `/api/v1/reception/change-requests/{id}` | Receptionist, Admin | Xem yêu cầu đổi/hủy |
| POST | `/api/v1/reception/change-requests/{id}/approve-reschedule` | Receptionist, Admin | Duyệt đổi lịch |
| POST | `/api/v1/reception/change-requests/{id}/approve-cancellation` | Receptionist, Admin | Duyệt hủy lịch |
| POST | `/api/v1/reception/change-requests/{id}/reject` | Receptionist, Admin | Từ chối yêu cầu thay đổi |

## ReceptionHealthPackageRegistrationController — Xử lý đăng ký gói

Nguồn: [ReceptionHealthPackageRegistrationController.cs](../../src/backend/ClinicManagement.Api/Controllers/ReceptionHealthPackageRegistrationController.cs).

| Method | Route | Vai trò | Mô tả |
| --- | --- | --- | --- |
| GET | `/api/v1/reception/health-package-registrations` | Receptionist, Admin | Tra cứu dữ liệu xử lý đăng ký gói |
| POST | `/api/v1/reception/health-package-registrations/{id}/confirm` | Receptionist, Admin | Xác nhận dữ liệu xử lý đăng ký gói |
| POST | `/api/v1/reception/health-package-registrations/{id}/cancel` | Receptionist, Admin | Hủy dữ liệu xử lý đăng ký gói |

## RevisitRequestController — Phản hồi tái khám

Nguồn: [RevisitRequestController.cs](../../src/backend/ClinicManagement.Api/Controllers/RevisitRequestController.cs).

| Method | Route | Vai trò | Mô tả |
| --- | --- | --- | --- |
| GET | `/api/v1/revisit-requests` | Patient | Xem đề xuất tái khám cá nhân |
| GET | `/api/v1/revisit-requests/{id}` | Patient | Xem đề xuất tái khám |
| POST | `/api/v1/revisit-requests/{id}/accept` | Patient | Chấp nhận đề xuất và đặt lịch tái khám |
| POST | `/api/v1/revisit-requests/{id}/reject` | Patient | Từ chối đề xuất tái khám |

## SpecialtyController — Chuyên khoa

Nguồn: [SpecialtyController.cs](../../src/backend/ClinicManagement.Api/Controllers/SpecialtyController.cs).

| Method | Route | Vai trò | Mô tả |
| --- | --- | --- | --- |
| GET | `/api/v1/specialties` | Công khai | Liệt kê chuyên khoa |
| GET | `/api/v1/specialties/{specialtyId}` | Công khai | Xem chuyên khoa |
| GET | `/api/v1/specialties/{specialtyId}/doctors` | Công khai | Lấy bác sĩ theo chuyên khoa |
| GET | `/api/v1/specialties/{specialtyId}/recommended-doctors` | Công khai | Lấy bác sĩ được gợi ý theo lịch trống |

## AI — vùng cấm, cần mở khóa

- [AiBookingWizardController.cs](../../src/backend/ClinicManagement.Api/Controllers/AI/AiBookingWizardController.cs)
- [AiRoleCopilotController.cs](../../src/backend/ClinicManagement.Api/Controllers/AI/AiRoleCopilotController.cs)
- [AiSpecialtyController.cs](../../src/backend/ClinicManagement.Api/Controllers/AI/AiSpecialtyController.cs)
- [AiToolGatewayController.cs](../../src/backend/ClinicManagement.Api/Controllers/AI/AiToolGatewayController.cs)
