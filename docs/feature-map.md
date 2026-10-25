# Bản đồ tính năng

Đối chiếu code hiện tại. Đường dẫn đầy đủ từ gốc repo nằm trong liên kết; `pages/...` ở dưới `src/frontend/src/`. Khi trang gọi trực tiếp Axios, file API được ghi là `axiosClient.ts`. Vai trò ở đây mô tả luồng dùng; quyền từng endpoint xem [API](api/api-specification.md), điều kiện tài nguyên xem [luật nghiệp vụ](specifications/business-rules.md).

## Đăng nhập/đăng ký

- Vai trò: Khách; mọi tài khoản.
- Trang frontend: [Login.tsx](../src/frontend/src/pages/Login.tsx), [Register.tsx](../src/frontend/src/pages/Register.tsx).
- API frontend: [axiosClient.ts](../src/frontend/src/api/axiosClient.ts).
- Controller: [AuthController.cs](../src/backend/ClinicManagement.Api/Controllers/AuthController.cs).
- Endpoint chính: `POST /api/v1/auth/register`; `POST /api/v1/auth/login`; `GET /api/v1/auth/me`.
- Service/policy: [AuthenticationService.cs](../src/backend/ClinicManagement.Infrastructure/Authentication/AuthenticationService.cs).
- Entity / bảng chính: ApplicationUser / AspNetUsers; Patient / Patients.
- Test chính: [AuthTests.cs](../src/backend/ClinicManagement.IntegrationTests/AuthTests.cs).

## Đặt lịch và kiểm tra lịch trống

- Vai trò: Patient; khách xem lịch trống.
- Trang frontend: [BookAppointment.tsx](../src/frontend/src/pages/patient/BookAppointment.tsx), [DoctorDetail.tsx](../src/frontend/src/pages/public/DoctorDetail.tsx).
- API frontend: [axiosClient.ts](../src/frontend/src/api/axiosClient.ts).
- Controller: [AppointmentController.cs](../src/backend/ClinicManagement.Api/Controllers/AppointmentController.cs), [DoctorController.cs](../src/backend/ClinicManagement.Api/Controllers/DoctorController.cs).
- Endpoint chính: `POST /api/v1/appointments`; `GET /api/v1/doctors/{doctorId}/available-slots`; `GET /api/v1/doctors/{doctorId}/availability`.
- Service/policy: [AppointmentService.cs](../src/backend/ClinicManagement.Infrastructure/Appointments/AppointmentService.cs), [AppointmentAvailabilityPolicy.cs](../src/backend/ClinicManagement.Infrastructure/Appointments/AppointmentAvailabilityPolicy.cs).
- Entity / bảng chính: Appointment / Appointments; AppointmentSlot / AppointmentSlots; DoctorWorkSchedule / DoctorWorkSchedules.
- Test chính: [SlotPolicyTests.cs](../src/backend/ClinicManagement.IntegrationTests/SlotPolicyTests.cs), [SlotContractTests.cs](../src/backend/ClinicManagement.IntegrationTests/SlotContractTests.cs), [AppointmentConcurrencyTests.cs](../src/backend/ClinicManagement.IntegrationTests/AppointmentConcurrencyTests.cs).

## Đổi/hủy lịch (ChangeRequest)

- Vai trò: Patient; Receptionist, Admin xử lý.
- Trang frontend: [PatientAppointments.tsx](../src/frontend/src/pages/patient/PatientAppointments.tsx), [ReceptionChangeRequests.tsx](../src/frontend/src/pages/reception/ReceptionChangeRequests.tsx).
- API frontend: [axiosClient.ts](../src/frontend/src/api/axiosClient.ts).
- Controller: [AppointmentController.cs](../src/backend/ClinicManagement.Api/Controllers/AppointmentController.cs), [AppointmentChangeRequestController.cs](../src/backend/ClinicManagement.Api/Controllers/AppointmentChangeRequestController.cs), [ReceptionController.cs](../src/backend/ClinicManagement.Api/Controllers/ReceptionController.cs).
- Endpoint chính: `POST /api/v1/appointments/{id}/reschedule-requests`; `POST /api/v1/appointments/{id}/cancellation-requests`; `POST /api/v1/reception/change-requests/{id}/approve-reschedule`.
- Service/policy: [ChangeRequestService.cs](../src/backend/ClinicManagement.Infrastructure/Appointments/ChangeRequestService.cs).
- Entity / bảng chính: AppointmentChangeRequest / AppointmentChangeRequests; Appointment / Appointments.
- Test chính: [AppointmentChangeRequestTests.cs](../src/backend/ClinicManagement.IntegrationTests/AppointmentChangeRequestTests.cs).

## Tái khám

- Vai trò: Doctor đề xuất; Patient phản hồi.
- Trang frontend: [PatientRevisit.tsx](../src/frontend/src/pages/patient/PatientRevisit.tsx), [BookAppointment.tsx](../src/frontend/src/pages/patient/BookAppointment.tsx), [DoctorAppointmentDetail.tsx](../src/frontend/src/pages/doctor/DoctorAppointmentDetail.tsx).
- API frontend: [axiosClient.ts](../src/frontend/src/api/axiosClient.ts), [doctorApi.ts](../src/frontend/src/api/doctorApi.ts).
- Controller: [RevisitRequestController.cs](../src/backend/ClinicManagement.Api/Controllers/RevisitRequestController.cs), [DoctorAppointmentController.cs](../src/backend/ClinicManagement.Api/Controllers/DoctorAppointmentController.cs).
- Endpoint chính: `POST /api/v1/doctor/appointments/{id}/revisit-requests`; `POST /api/v1/revisit-requests/{id}/accept`; `POST /api/v1/revisit-requests/{id}/reject`.
- Service/policy: [RevisitService.cs](../src/backend/ClinicManagement.Infrastructure/Appointments/RevisitService.cs).
- Entity / bảng chính: RevisitRequest / RevisitRequests; Appointment / Appointments.
- Test chính: [RevisitWorkflowTests.cs](../src/backend/ClinicManagement.IntegrationTests/RevisitWorkflowTests.cs).

## Lịch làm việc và nghỉ phép bác sĩ

- Vai trò: Doctor; Admin.
- Trang frontend: [DoctorSchedule.tsx](../src/frontend/src/pages/doctor/DoctorSchedule.tsx), [DoctorLeaveRequests.tsx](../src/frontend/src/pages/doctor/DoctorLeaveRequests.tsx), [AdminWorkSchedules.tsx](../src/frontend/src/pages/admin/AdminWorkSchedules.tsx), [AdminLeaves.tsx](../src/frontend/src/pages/admin/AdminLeaves.tsx).
- API frontend: [doctorApi.ts](../src/frontend/src/api/doctorApi.ts), [axiosClient.ts](../src/frontend/src/api/axiosClient.ts).
- Controller: [DoctorWorkspaceController.cs](../src/backend/ClinicManagement.Api/Controllers/DoctorWorkspaceController.cs), [AdminWorkScheduleController.cs](../src/backend/ClinicManagement.Api/Controllers/AdminWorkScheduleController.cs), [DoctorLeaveController.cs](../src/backend/ClinicManagement.Api/Controllers/Leaves/DoctorLeaveController.cs), [AdminLeaveController.cs](../src/backend/ClinicManagement.Api/Controllers/Leaves/AdminLeaveController.cs).
- Endpoint chính: `GET /api/v1/doctor/schedule`; `POST /api/v1/admin/work-schedules/{scheduleId}/generate-slots`; `POST /api/v1/doctor/leave-requests`; `POST /api/v1/admin/leave-requests/{id}/approve`.
- Service/policy: [ScheduleService.cs](../src/backend/ClinicManagement.Infrastructure/Schedules/ScheduleService.cs), [DoctorLeaveService.cs](../src/backend/ClinicManagement.Infrastructure/Leaves/DoctorLeaveService.cs), [AdminLeaveService.cs](../src/backend/ClinicManagement.Infrastructure/Leaves/AdminLeaveService.cs).
- Entity / bảng chính: DoctorWorkSchedule / DoctorWorkSchedules; DoctorLeaveRequest / DoctorLeaveRequests; AppointmentSlot / AppointmentSlots.
- Test chính: [DoctorLeaveDateTimeTests.cs](../src/backend/ClinicManagement.IntegrationTests/DoctorLeaveDateTimeTests.cs), [PackagePhoneAndLeaveGuardsTests.cs](../src/backend/ClinicManagement.IntegrationTests/PackagePhoneAndLeaveGuardsTests.cs).

## Tiếp nhận/check-in/hàng đợi (PatientVisit)

- Vai trò: Receptionist, Admin tiếp nhận; Doctor điều phối.
- Trang frontend: [ReceptionWorkspace.tsx](../src/frontend/src/pages/reception/ReceptionWorkspace.tsx), [WalkInPatientRegistration.tsx](../src/frontend/src/pages/reception/WalkInPatientRegistration.tsx), [DoctorQueue.tsx](../src/frontend/src/pages/doctor/DoctorQueue.tsx).
- API frontend: [patientVisitApi.ts](../src/frontend/src/api/patientVisitApi.ts).
- Controller: [PatientVisitsController.cs](../src/backend/ClinicManagement.Api/Controllers/PatientVisitsController.cs), [ReceptionController.cs](../src/backend/ClinicManagement.Api/Controllers/ReceptionController.cs).
- Endpoint chính: `POST /api/v1/patient-visits/intake`; `POST /api/v1/patient-visits/check-in-appointment`; `GET /api/v1/patient-visits/department-queue`; `GET /api/v1/patient-visits/{id}/ticket`.
- Service/policy: [PatientVisitService.cs](../src/backend/ClinicManagement.Infrastructure/Visits/PatientVisitService.cs).
- Entity / bảng chính: PatientVisit / PatientVisits; DailyQueueSequence / DailyQueueSequences.
- Test chính: [ReceptionWorkspaceRebuildTests.cs](../src/backend/ClinicManagement.IntegrationTests/ReceptionWorkspaceRebuildTests.cs), [DoctorCheckInVisitTests.cs](../src/backend/ClinicManagement.IntegrationTests/DoctorCheckInVisitTests.cs), [QueueDateAndIsolationTests.cs](../src/backend/ClinicManagement.IntegrationTests/QueueDateAndIsolationTests.cs).

## MPI hồ sơ bệnh nhân và mã MRN

- Vai trò: Admin, Receptionist; Doctor tra cứu và dị ứng.
- Trang frontend: [MpiPatientSearchModal.tsx](../src/frontend/src/pages/reception/MpiPatientSearchModal.tsx), [WalkInPatientRegistration.tsx](../src/frontend/src/pages/reception/WalkInPatientRegistration.tsx).
- API frontend: [mpiApi.ts](../src/frontend/src/api/mpiApi.ts).
- Controller: [MpiPatientsController.cs](../src/backend/ClinicManagement.Api/Controllers/Mpi/MpiPatientsController.cs).
- Endpoint chính: `GET /api/v1/mpi/patients`; `GET /api/v1/mpi/patients/by-mrn/{mrn}`; `POST /api/v1/mpi/patients/walk-in`.
- Service/policy: [MpiPatientService.cs](../src/backend/ClinicManagement.Infrastructure/Mpi/MpiPatientService.cs), [MrnGenerator.cs](../src/backend/ClinicManagement.Infrastructure/Mpi/MrnGenerator.cs).
- Entity / bảng chính: Patient / Patients; PatientAllergy / PatientAllergies; EmergencyContact / EmergencyContacts; MrnSequence / MrnSequences.
- Test chính: [MasterPatientIndexTests.cs](../src/backend/ClinicManagement.IntegrationTests/MasterPatientIndexTests.cs).

## Khám bệnh/bệnh án/sinh hiệu

- Vai trò: Doctor; Patient xem dữ liệu của mình.
- Trang frontend: [DoctorExaminationWorkspace.tsx](../src/frontend/src/pages/doctor/DoctorExaminationWorkspace.tsx), [PatientProfile.tsx](../src/frontend/src/pages/patient/PatientProfile.tsx).
- API frontend: [doctorApi.ts](../src/frontend/src/api/doctorApi.ts), [axiosClient.ts](../src/frontend/src/api/axiosClient.ts).
- Controller: [DoctorAppointmentController.cs](../src/backend/ClinicManagement.Api/Controllers/DoctorAppointmentController.cs), [DoctorVisitsController.cs](../src/backend/ClinicManagement.Api/Controllers/DoctorVisitsController.cs), [PatientController.cs](../src/backend/ClinicManagement.Api/Controllers/PatientController.cs).
- Endpoint chính: `PUT /api/v1/doctor/appointments/{id}/encounter`; `PUT /api/v1/doctor/visits/{id}/vitals`; `POST /api/v1/doctor/visits/{id}/complete`; `GET /api/v1/patients/me/vitals`.
- Service/policy: [DoctorAppointmentService.cs](../src/backend/ClinicManagement.Infrastructure/Appointments/DoctorAppointmentService.cs), [VisitCompletionCoordinator.cs](../src/backend/ClinicManagement.Infrastructure/Visits/VisitCompletionCoordinator.cs).
- Entity / bảng chính: VisitSummary / VisitSummaries; AppointmentVitalSigns / AppointmentVitalSigns; PatientVisit / PatientVisits.
- Test chính: [PatientVitalHistoryTests.cs](../src/backend/ClinicManagement.IntegrationTests/PatientVitalHistoryTests.cs), [OutpatientHardenFlowTests.cs](../src/backend/ClinicManagement.IntegrationTests/OutpatientHardenFlowTests.cs), [VisitGuardsAndDataFixesTests.cs](../src/backend/ClinicManagement.IntegrationTests/VisitGuardsAndDataFixesTests.cs).

## ICD-10

- Vai trò: Doctor, Receptionist, Admin tra cứu; Doctor lưu chẩn đoán.
- Trang frontend: [DoctorExaminationWorkspace.tsx](../src/frontend/src/pages/doctor/DoctorExaminationWorkspace.tsx).
- API frontend: [icd10Api.ts](../src/frontend/src/api/icd10Api.ts).
- Controller: [Icd10Controller.cs](../src/backend/ClinicManagement.Api/Controllers/Icd10Controller.cs), [DoctorVisitsController.cs](../src/backend/ClinicManagement.Api/Controllers/DoctorVisitsController.cs).
- Endpoint chính: `GET /api/v1/icd10`; `PUT /api/v1/doctor/visits/{id}/encounter`.
- Service/policy: [DoctorAppointmentService.cs](../src/backend/ClinicManagement.Infrastructure/Appointments/DoctorAppointmentService.cs).
- Entity / bảng chính: Icd10Code / Icd10Codes; VisitSummary / VisitSummaries.
- Test chính: [Icd10CatalogTests.cs](../src/backend/ClinicManagement.IntegrationTests/Icd10CatalogTests.cs).

## Kê đơn và an toàn kê đơn (PrescriptionSafety)

- Vai trò: Doctor; Patient xem đơn.
- Trang frontend: [DoctorExaminationWorkspace.tsx](../src/frontend/src/pages/doctor/DoctorExaminationWorkspace.tsx), [PatientPrescriptions.tsx](../src/frontend/src/pages/patient/PatientPrescriptions.tsx).
- API frontend: [doctorApi.ts](../src/frontend/src/api/doctorApi.ts), [axiosClient.ts](../src/frontend/src/api/axiosClient.ts).
- Controller: [DoctorAppointmentController.cs](../src/backend/ClinicManagement.Api/Controllers/DoctorAppointmentController.cs), [DoctorVisitsController.cs](../src/backend/ClinicManagement.Api/Controllers/DoctorVisitsController.cs), [PatientController.cs](../src/backend/ClinicManagement.Api/Controllers/PatientController.cs).
- Endpoint chính: `POST /api/v1/doctor/visits/{id}/prescription/safety-check`; `PUT /api/v1/doctor/visits/{id}/prescription-draft`; `GET /api/v1/patients/me/prescriptions`.
- Service/policy: [DoctorAppointmentService.cs](../src/backend/ClinicManagement.Infrastructure/Appointments/DoctorAppointmentService.cs), [PrescriptionSafetyService.cs](../src/backend/ClinicManagement.Infrastructure/Prescriptions/PrescriptionSafetyService.cs).
- Entity / bảng chính: Prescription / Prescriptions; PrescriptionItem / PrescriptionItems; DrugInteractionRule / DrugInteractionRules; DrugClassMember / DrugClassMembers; DrugAllergyCrossReaction / DrugAllergyCrossReactions.
- Test chính: [PrescriptionSafetyTests.cs](../src/backend/ClinicManagement.IntegrationTests/PrescriptionSafetyTests.cs).

## Cận lâm sàng/LIS

- Vai trò: Doctor chỉ định và xem; DiagnosticTechnician nhập; DiagnosticTechnician, LabSupervisor, Doctor duyệt theo điều kiện service; Patient xem kết quả đã công bố.
- Trang frontend: [TechnicianDashboard.tsx](../src/frontend/src/pages/technician/TechnicianDashboard.tsx), [TechnicianOrderDetail.tsx](../src/frontend/src/pages/technician/TechnicianOrderDetail.tsx), [DiagnosticVerificationReview.tsx](../src/frontend/src/pages/common/DiagnosticVerificationReview.tsx), [LabTurnaroundReport.tsx](../src/frontend/src/pages/lab/LabTurnaroundReport.tsx), [PatientDiagnosticResults.tsx](../src/frontend/src/pages/patient/PatientDiagnosticResults.tsx).
- API frontend: [diagnosticApi.ts](../src/frontend/src/api/diagnosticApi.ts).
- Controller: [DoctorDiagnosticOrderController.cs](../src/backend/ClinicManagement.Api/Controllers/DoctorDiagnosticOrderController.cs), [DiagnosticTechnicianController.cs](../src/backend/ClinicManagement.Api/Controllers/DiagnosticTechnicianController.cs), [DiagnosticVerificationController.cs](../src/backend/ClinicManagement.Api/Controllers/DiagnosticVerificationController.cs), [DiagnosticAttachmentsController.cs](../src/backend/ClinicManagement.Api/Controllers/DiagnosticAttachmentsController.cs), [LabReportsController.cs](../src/backend/ClinicManagement.Api/Controllers/LabReportsController.cs), [PatientDiagnosticController.cs](../src/backend/ClinicManagement.Api/Controllers/PatientDiagnosticController.cs).
- Endpoint chính: `POST /api/v1/doctor/visits/{visitId}/diagnostic-orders`; `PUT /api/v1/diagnostics/orders/{orderId}/items/{itemId}/result`; `POST /api/v1/diagnostic-verification/orders/{orderId}/verify`; `GET /api/v1/lab-reports/turnaround`.
- Service/policy: [DiagnosticWorkflowService.cs](../src/backend/ClinicManagement.Infrastructure/Diagnostics/DiagnosticWorkflowService.cs).
- Entity / bảng chính: DiagnosticOrder / DiagnosticOrders; DiagnosticOrderItem / DiagnosticOrderItems; DiagnosticResult / DiagnosticResults; DiagnosticServiceParameter / DiagnosticServiceParameters; DiagnosticResultValue / DiagnosticResultValues; DiagnosticResultAttachment / DiagnosticResultAttachments.
- Test chính: [DiagnosticOrderWorkflowTests.cs](../src/backend/ClinicManagement.IntegrationTests/DiagnosticOrderWorkflowTests.cs), [DiagnosticVerificationWorkflowTests.cs](../src/backend/ClinicManagement.IntegrationTests/DiagnosticVerificationWorkflowTests.cs), [DiagnosticDeltaCheckTests.cs](../src/backend/ClinicManagement.IntegrationTests/DiagnosticDeltaCheckTests.cs), [DiagnosticAttachmentTests.cs](../src/backend/ClinicManagement.IntegrationTests/DiagnosticAttachmentTests.cs), [LabTurnaroundReportTests.cs](../src/backend/ClinicManagement.IntegrationTests/LabTurnaroundReportTests.cs).

## Nhà thuốc: cấp phát và giữ chỗ

- Vai trò: Pharmacist, Admin; Receptionist xác nhận mua.
- Trang frontend: [PharmacyPrescriptions.tsx](../src/frontend/src/pages/pharmacy/PharmacyPrescriptions.tsx), [PharmacyDashboard.tsx](../src/frontend/src/pages/pharmacy/PharmacyDashboard.tsx).
- API frontend: [axiosClient.ts](../src/frontend/src/api/axiosClient.ts).
- Controller: [PharmacyController.cs](../src/backend/ClinicManagement.Api/Controllers/PharmacyController.cs).
- Endpoint chính: `POST /api/v1/pharmacy/prescriptions/{id}/confirm-purchase`; `POST /api/v1/pharmacy/prescriptions/{id}/dispense`.
- Service/policy: [PharmacyService.cs](../src/backend/ClinicManagement.Infrastructure/Pharmacy/PharmacyService.cs), [PrescriptionPaymentEligibilityService.cs](../src/backend/ClinicManagement.Infrastructure/Pharmacy/PrescriptionPaymentEligibilityService.cs).
- Entity / bảng chính: Prescription / Prescriptions; MedicineStockTransaction / MedicineStockTransactions; Invoice / Invoices.
- Test chính: [PharmacyDispenseTests.cs](../src/backend/ClinicManagement.IntegrationTests/PharmacyDispenseTests.cs), [BillingTests.cs](../src/backend/ClinicManagement.IntegrationTests/BillingTests.cs).

## Kho thuốc theo lô

- Vai trò: Pharmacist, Admin.
- Trang frontend: [PharmacyBatches.tsx](../src/frontend/src/pages/pharmacy/PharmacyBatches.tsx), [PharmacyInventory.tsx](../src/frontend/src/pages/pharmacy/PharmacyInventory.tsx).
- API frontend: [pharmacyBatchApi.ts](../src/frontend/src/api/pharmacyBatchApi.ts), [axiosClient.ts](../src/frontend/src/api/axiosClient.ts).
- Controller: [PharmacyBatchesController.cs](../src/backend/ClinicManagement.Api/Controllers/PharmacyBatchesController.cs), [PharmacyController.cs](../src/backend/ClinicManagement.Api/Controllers/PharmacyController.cs).
- Endpoint chính: `GET /api/v1/pharmacy/batches`; `POST /api/v1/pharmacy/batches`; `POST /api/v1/pharmacy/batches/{id}/dispose`; `POST /api/v1/pharmacy/inventory/adjust`.
- Service/policy: [MedicineBatchService.cs](../src/backend/ClinicManagement.Infrastructure/Pharmacy/MedicineBatchService.cs), [MedicineBatchAllocator.cs](../src/backend/ClinicManagement.Infrastructure/Pharmacy/MedicineBatchAllocator.cs), [UsableStock.cs](../src/backend/ClinicManagement.Infrastructure/Pharmacy/UsableStock.cs).
- Entity / bảng chính: MedicineBatch / MedicineBatches; Medicine / Medicines; MedicineStockTransaction / MedicineStockTransactions.
- Test chính: [MedicineBatchTests.cs](../src/backend/ClinicManagement.IntegrationTests/MedicineBatchTests.cs), [UsableStockTests.cs](../src/backend/ClinicManagement.IntegrationTests/UsableStockTests.cs).

## Danh mục thuốc và trang thuốc công khai

- Vai trò: Khách tra cứu; Admin, Pharmacist quản lý.
- Trang frontend: [MedicinePrices.tsx](../src/frontend/src/pages/public/MedicinePrices.tsx), [MedicineDetail.tsx](../src/frontend/src/pages/public/MedicineDetail.tsx), [AdminMedicines.tsx](../src/frontend/src/pages/admin/AdminMedicines.tsx), [MedicineCategoryManager.tsx](../src/frontend/src/pages/admin/MedicineCategoryManager.tsx).
- API frontend: [medicineApi.ts](../src/frontend/src/api/medicineApi.ts).
- Controller: [PublicMedicinesController.cs](../src/backend/ClinicManagement.Api/Controllers/PublicMedicinesController.cs), [MedicineController.cs](../src/backend/ClinicManagement.Api/Controllers/MedicineController.cs), [MedicineCategoriesController.cs](../src/backend/ClinicManagement.Api/Controllers/MedicineCategoriesController.cs), [MedicineImagesController.cs](../src/backend/ClinicManagement.Api/Controllers/MedicineImagesController.cs).
- Endpoint chính: `GET /api/v1/public/medicines`; `GET /api/v1/public/medicines/{id}`; `POST /api/v1/admin/medicines`; `POST /api/v1/admin/medicines/{id}/image`.
- Service/policy: [MedicineService.cs](../src/backend/ClinicManagement.Infrastructure/Medicines/MedicineService.cs), [MedicineCatalogService.cs](../src/backend/ClinicManagement.Infrastructure/Medicines/MedicineCatalogService.cs).
- Entity / bảng chính: Medicine / Medicines; MedicineCategory / MedicineCategories.
- Test chính: [MedicineCatalogTests.cs](../src/backend/ClinicManagement.IntegrationTests/MedicineCatalogTests.cs), [MedicineDetailTests.cs](../src/backend/ClinicManagement.IntegrationTests/MedicineDetailTests.cs), [MedicineCatalogPersistenceTests.cs](../src/backend/ClinicManagement.IntegrationTests/MedicineCatalogPersistenceTests.cs).

## Gói khám

- Vai trò: Khách xem; Patient đăng ký; Receptionist, Admin xử lý.
- Trang frontend: [HealthPackagesList.tsx](../src/frontend/src/pages/public/HealthPackagesList.tsx), [PackageRegistration.tsx](../src/frontend/src/pages/patient/PackageRegistration.tsx), [ReceptionPackageRegistrations.tsx](../src/frontend/src/pages/reception/ReceptionPackageRegistrations.tsx), [AdminHealthPackages.tsx](../src/frontend/src/pages/admin/AdminHealthPackages.tsx).
- API frontend: [axiosClient.ts](../src/frontend/src/api/axiosClient.ts).
- Controller: [HealthPackageController.cs](../src/backend/ClinicManagement.Api/Controllers/HealthPackageController.cs), [PatientHealthPackageRegistrationController.cs](../src/backend/ClinicManagement.Api/Controllers/PatientHealthPackageRegistrationController.cs), [ReceptionHealthPackageRegistrationController.cs](../src/backend/ClinicManagement.Api/Controllers/ReceptionHealthPackageRegistrationController.cs).
- Endpoint chính: `GET /api/v1/health-packages`; `POST /api/v1/patient/health-package-registrations`; `POST /api/v1/reception/health-package-registrations/{id}/confirm`.
- Service/policy: [HealthPackageService.cs](../src/backend/ClinicManagement.Infrastructure/HealthPackages/HealthPackageService.cs), [HealthPackageRegistrationService.cs](../src/backend/ClinicManagement.Infrastructure/HealthPackages/HealthPackageRegistrationService.cs).
- Entity / bảng chính: HealthPackage / HealthPackages; HealthPackageRegistration / HealthPackageRegistrations.
- Test chính: [PackagePhoneAndLeaveGuardsTests.cs](../src/backend/ClinicManagement.IntegrationTests/PackagePhoneAndLeaveGuardsTests.cs).

## Thanh toán/hóa đơn

- Vai trò: Receptionist, Admin thu tiền; Patient xem; Admin báo cáo.
- Trang frontend: [ReceptionBilling.tsx](../src/frontend/src/pages/reception/ReceptionBilling.tsx), [PatientInvoices.tsx](../src/frontend/src/pages/patient/PatientInvoices.tsx), [AdminBilling.tsx](../src/frontend/src/pages/admin/AdminBilling.tsx).
- API frontend: [billingApi.ts](../src/frontend/src/api/billingApi.ts).
- Controller: [ReceptionBillingController.cs](../src/backend/ClinicManagement.Api/Controllers/ReceptionBillingController.cs), [PatientBillingController.cs](../src/backend/ClinicManagement.Api/Controllers/PatientBillingController.cs), [AdminBillingController.cs](../src/backend/ClinicManagement.Api/Controllers/AdminBillingController.cs), [AdminDiagnosticPricingController.cs](../src/backend/ClinicManagement.Api/Controllers/AdminDiagnosticPricingController.cs).
- Endpoint chính: `POST /api/v1/reception/billing/invoices/visit`; `POST /api/v1/reception/billing/invoices/{id}/pay`; `GET /api/v1/admin/billing/revenue`.
- Service/policy: [BillingService.cs](../src/backend/ClinicManagement.Infrastructure/Billing/BillingService.cs).
- Entity / bảng chính: Invoice / Invoices; InvoiceItem / InvoiceItems; Payment / Payments; IdempotencyRecord / IdempotencyRecords.
- Test chính: [BillingTests.cs](../src/backend/ClinicManagement.IntegrationTests/BillingTests.cs).

## Thông báo

- Vai trò: Tài khoản đã đăng nhập, chỉ dữ liệu của mình.
- Trang frontend: [NotificationsPage.tsx](../src/frontend/src/pages/common/NotificationsPage.tsx).
- API frontend: [notificationApi.ts](../src/frontend/src/api/notificationApi.ts).
- Controller: [NotificationController.cs](../src/backend/ClinicManagement.Api/Controllers/NotificationController.cs).
- Endpoint chính: `GET /api/v1/notifications`; `GET /api/v1/notifications/unread-count`; `PATCH /api/v1/notifications/{id}/read`; `PATCH /api/v1/notifications/read-all`.
- Service/policy: [NotificationService.cs](../src/backend/ClinicManagement.Infrastructure/Notifications/NotificationService.cs).
- Entity / bảng chính: Notification / Notifications.
- Test chính: [NotificationTests.cs](../src/backend/ClinicManagement.IntegrationTests/NotificationTests.cs), [NotificationFilterTests.cs](../src/backend/ClinicManagement.IntegrationTests/NotificationFilterTests.cs).

## Quản trị: người dùng, cơ sở, báo cáo

- Vai trò: Admin; các vai trò khác chỉ truy cập danh mục theo controller.
- Trang frontend: [AdminUsers.tsx](../src/frontend/src/pages/admin/AdminUsers.tsx), [AdminFacilities.tsx](../src/frontend/src/pages/admin/AdminFacilities.tsx), [AdminAuditLogs.tsx](../src/frontend/src/pages/admin/AdminAuditLogs.tsx), [AdminBilling.tsx](../src/frontend/src/pages/admin/AdminBilling.tsx).
- API frontend: [organizationApi.ts](../src/frontend/src/api/organizationApi.ts), [billingApi.ts](../src/frontend/src/api/billingApi.ts), [axiosClient.ts](../src/frontend/src/api/axiosClient.ts).
- Controller: [AdminController.cs](../src/backend/ClinicManagement.Api/Controllers/AdminController.cs), [AdminStaffAssignmentsController.cs](../src/backend/ClinicManagement.Api/Controllers/AdminStaffAssignmentsController.cs), [FacilitiesController.cs](../src/backend/ClinicManagement.Api/Controllers/Organization/FacilitiesController.cs), [DepartmentsController.cs](../src/backend/ClinicManagement.Api/Controllers/Organization/DepartmentsController.cs), [HospitalRoomsController.cs](../src/backend/ClinicManagement.Api/Controllers/Organization/HospitalRoomsController.cs), [BedsController.cs](../src/backend/ClinicManagement.Api/Controllers/Organization/BedsController.cs).
- Endpoint chính: `GET /api/v1/admin/stats`; `GET /api/v1/admin/audit-logs`; `POST /api/v1/admin/users`; `POST /api/v1/admin/staff-assignments`; `GET /api/v1/facilities`.
- Service/policy: [AdminUserService.cs](../src/backend/ClinicManagement.Infrastructure/Admin/AdminUserService.cs), [OrganizationService.cs](../src/backend/ClinicManagement.Infrastructure/Organization/OrganizationService.cs), [FacilityAuthorizationService.cs](../src/backend/ClinicManagement.Infrastructure/Common/FacilityAuthorizationService.cs).
- Entity / bảng chính: ApplicationUser / AspNetUsers; Facility / Facilities; Department / Departments; Room / Rooms; StaffFacilityAssignment / StaffFacilityAssignments; SystemAuditLog / SystemAuditLogs.
- Test chính: [AdminTests.cs](../src/backend/ClinicManagement.IntegrationTests/AdminTests.cs), [OrganizationHierarchyTests.cs](../src/backend/ClinicManagement.IntegrationTests/OrganizationHierarchyTests.cs), [AuditAuthorizationTests.cs](../src/backend/ClinicManagement.IntegrationTests/AuditAuthorizationTests.cs).

## Xử lý hết hạn nghiệp vụ

- Worker và các quy tắc: [Expiry](../src/backend/ClinicManagement.Infrastructure/Expiry).
- Cấu hình: [ExpiryRulesOptions.cs](../src/backend/ClinicManagement.Application/Common/Expiry/ExpiryRulesOptions.cs); mục `ExpiryRules` trong appsettings.
- API điều kiện đặt online: [OnlineBookingEligibilityController.cs](../src/backend/ClinicManagement.Api/Controllers/OnlineBookingEligibilityController.cs).
- Seed: [ExpiryDemoSeed.cs](../src/backend/ClinicManagement.Infrastructure/Persistence/ExpiryDemoSeed.cs).
- Test: [ExpiryRuleTests.cs](../src/backend/ClinicManagement.IntegrationTests/ExpiryRuleTests.cs), [ExpiryApiTests.cs](../src/backend/ClinicManagement.IntegrationTests/ExpiryApiTests.cs), [ExpiryWorkerTests.cs](../src/backend/ClinicManagement.IntegrationTests/ExpiryWorkerTests.cs), [expiryRules.test.tsx](../src/frontend/src/test/expiryRules.test.tsx).
- Mốc, trạng thái và cách vận hành: [Quy tắc hết hạn](expiry-rules.md).

## Trợ lý AI đặt lịch

**Vùng cấm, cần mở khóa.**

- [ChatConsultationRedirect.tsx](../src/frontend/src/components/chat/ChatConsultationRedirect.tsx) — URL tư vấn cũ mở bong bóng chat rồi chuyển về trang trước hoặc trang chủ.
- [MedicalChatWidget.tsx](../src/frontend/src/components/MedicalChatWidget.tsx)
- [ChatContext.tsx](../src/frontend/src/contexts/ChatContext.tsx)
- [useAiBookingFlow.ts](../src/frontend/src/hooks/useAiBookingFlow.ts)
- [aiCopilotApi.ts](../src/frontend/src/api/aiCopilotApi.ts)
- [AiBookingWizardController.cs](../src/backend/ClinicManagement.Api/Controllers/AI/AiBookingWizardController.cs)
- [AI](../src/backend/ClinicManagement.Application/AI)
- [AI](../src/backend/ClinicManagement.Infrastructure/AI)
- [tools](../src/tools)
- [ai](ai)
