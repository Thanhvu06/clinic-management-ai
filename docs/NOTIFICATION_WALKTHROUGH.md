# Trung Tâm Thông Báo Trong Ứng Dụng (In-App Notification Center) & Chuẩn Hóa Lịch Khám

Tài liệu hướng dẫn kiến trúc, cơ chế hoạt động, kiểm thử và nghiệm thu tính năng **Trung tâm thông báo (In-App Notifications)** cùng các chuẩn hóa nghiệp vụ đặt lịch khám trên hệ thống **ClinicCare AI**.

---

## 1. Tổng Quan Kiến Trúc & Nghiệp Vụ

Module Thông báo trong ứng dụng được thiết kế hoạt động tập trung, hoàn toàn không dựa vào mock data hay dịch vụ bên ngoài (SMS/Email giả lập), tuân thủ cách ly bảo mật theo người dùng và chống trùng lặp thông báo qua `DedupeKey`.

### 1.1 Cấu Trúc Thực Thể `Notification`
- **Bảng DB:** `Notifications` (Entity Framework Core Migration `20260906050423_AddInAppNotificationsAndConstraints`).
- **Các trường chính:**
  - `Id`: `long` (Khóa chính tự tăng).
  - `UserId`: `Guid` (Khóa ngoại liên kết `AspNetUsers.Id`, cách ly dữ liệu triệt để).
  - `Type`: `NotificationType` (Enum 7 nhóm nghiệp vụ).
  - `Title`: `nvarchar(200)` (Tiêu đề thông báo).
  - `Message`: `nvarchar(1000)` (Nội dung chi tiết thông báo).
  - `LinkUrl`: `nvarchar(500)` (Đường dẫn điều hướng trong web app khi nhấp).
  - `IsRead`: `bool` (Trạng thái đã đọc).
  - `DedupeKey`: `nvarchar(256)` (Chống tạo trùng lặp với unique filtered index `WHERE DedupeKey IS NOT NULL`).
  - `CreatedAtUtc`: `datetime2` (Thời điểm phát sinh thông báo UTC).
- **Chỉ mục hiệu năng:**
  - Composite Index: `IX_Notifications_UserId_IsRead_CreatedAtUtc` phục vụ truy vấn đếm chưa đọc và phân trang cực nhanh.
  - Unique Filtered Index: `IX_Notifications_DedupeKey` bảo vệ tính toàn vẹn sự kiện.

---

## 2. 7 Kịch Bản Nghiệp Vụ Kích Hoạt Thông Báo Thật

1. **Đặt lịch khám thành công (`AppointmentService.cs`):**
   - Người bệnh nhận thông báo xác nhận lịch khám kèm mã hẹn.
   - Nhóm lễ tân đang hoạt động nhận thông báo có ca khám mới cần chuẩn bị đón tiếp.
   - `DedupeKey`: `appt_booked_pat_{AppointmentId}` và `appt_booked_rec_{AppointmentId}_{UserId}`.

2. **Yêu cầu đổi / hủy lịch khám (`ChangeRequestService.cs`):**
   - Khi bệnh nhân gửi yêu cầu đổi/hủy: toàn bộ lễ tân nhận thông báo để xử lý.
   - Khi lễ tân duyệt/từ chối yêu cầu: bệnh nhân nhận thông báo kết quả phê duyệt.

3. **Hoàn tất ca khám lâm sàng & Kê đơn thuốc (`DoctorAppointmentService.cs`):**
   - Bác sĩ hoàn thành khám trên Clinical Examination Workspace: bệnh nhân nhận thông báo hoàn tất khám và sẵn sàng nhận đơn thuốc tại quầy dược.
   - Bác sĩ chỉ định tái khám: bệnh nhân nhận thông báo nhắc lịch tái khám.

4. **Cấp phát thuốc tại quầy dược (`PharmacyService.cs`):**
   - Dược sĩ xác nhận hoàn tất cấp phát thuốc: bệnh nhân nhận thông báo đã nhận thuốc đầy đủ kèm hướng dẫn sử dụng.

5. **Duyệt / Từ chối đơn nghỉ phép của bác sĩ (`AdminLeaveService.cs`):**
   - Quản trị viên duyệt đơn nghỉ: bác sĩ nhận thông báo lịch nghỉ đã được duyệt.
   - Quản trị viên từ chối đơn nghỉ: bác sĩ nhận thông báo lý do từ chối.

6. **Đăng ký gói khám sức khỏe (`HealthPackageRegistrationService.cs`):**
   - Bệnh nhân đăng ký gói khám: bệnh nhân nhận thông báo xác nhận kèm mã đăng ký; lễ tân nhận thông báo liên hệ bệnh nhân.
   - Lễ tân xác nhận hoặc hủy đăng ký: bệnh nhân nhận thông báo cập nhật trạng thái.

7. **Nhắc lịch khám & Trạng thái hàng đợi:**
   - Hệ thống sẵn sàng gửi nhắc hẹn và cập nhật tiến trình khám theo loại `AppointmentReminder`.

---

## 3. Danh Sách RESTful API

Tất cả endpoint yêu cầu header `Authorization: Bearer <accessToken>`. Người dùng chỉ đọc và thao tác trên thông báo của chính mình:

| HTTP Method | Route | Mô tả |
|-------------|-------|-------|
| `GET` | `/api/v1/notifications?page=1&pageSize=10&isRead=` | Danh sách thông báo phân trang, hỗ trợ lọc theo trạng thái đã đọc |
| `GET` | `/api/v1/notifications/unread-count` | Lấy số lượng thông báo chưa đọc phục vụ badge chuông |
| `PATCH` | `/api/v1/notifications/{id}/read` | Đánh dấu 1 thông báo cụ thể là đã đọc |
| `PATCH` | `/api/v1/notifications/read-all` | Đánh dấu toàn bộ thông báo của người dùng hiện tại là đã đọc |

---

## 4. Giao Diện Người Dùng (Frontend UI/UX)

- **Component `NotificationBell` (`src/frontend/src/components/common/NotificationBell.tsx`):**
  - Tích hợp tại thanh điều hướng chính (`PublicLayout.tsx`) và thanh tiêu đề quản trị (`MainLayout.tsx`).
  - Badge số lượng chưa đọc động (hiển thị `99+` nếu vượt quá 99).
  - Popover xem nhanh 5 thông báo mới nhất kèm định dạng thời gian thân thiện (tiếng Việt: "vừa xong", "5 phút trước",...).
  - Thao tác đánh dấu đã đọc trực tiếp trên từng mục hoặc "Đánh dấu tất cả là đã đọc".
  - Tự động thăm dò chu kỳ 45s và làm mới ngay lập tức khi người dùng quay lại tab trình duyệt (`window.focus`).
- **Trang Chi Tiết Thông Báo (`src/frontend/src/pages/common/NotificationsPage.tsx`):**
  - Route dành cho Bệnh nhân: `/patient/notifications`.
  - Route dành cho Nhân viên y tế (Bác sĩ, Lễ tân, Dược sĩ, Admin): `/notifications`.
  - Bộ lọc tabs: **Tất cả**, **Chưa đọc**, **Đã đọc**.
  - Phân trang đầy đủ, nút làm mới danh sách và đánh dấu tất cả đã đọc.

---

## 5. Chuẩn Hóa Lịch Khám & Khung Giờ (Doctor & Schedule Normalization)

1. **Khắc phục lỗi trùng lặp lịch khám:**
   - SQL CTE migration dọn dẹp triệt để các bản ghi trùng lặp trước khi thiết lập Unique Constraint:
     `CREATE UNIQUE INDEX [IX_DoctorWorkSchedules_DoctorId_WorkDate_StartTime_EndTime] ON [DoctorWorkSchedules] ([DoctorId], [WorkDate], [StartTime], [EndTime])`.
2. **Quy tắc chiếm giữ slot khám (`HoldsSlot()`):**
   - Đã thống nhất policy trong `AppointmentStatusExtensions.cs`:
     - Trạng thái giữ slot: `Pending`, `Confirmed`, `InProgress`.
     - Trạng thái giải phóng slot: `Completed`, `Cancelled`, `NoShow`.
     - Chuyển cấu trúc lookup sang dạng mảng (`AppointmentStatus[]`) tương thích 100% với cơ chế biên dịch SQL `IN (...)` của EF Core.
3. **Chuẩn hóa Hợp đồng API Khung giờ (`AvailableSlotDto`):**
   - Thống nhất duy nhất thuộc tính canonical `slotId: long` (loại bỏ trường thừa `Id`).
4. **Bảo mật hàng đợi khám (`DoctorQueue`):**
   - Đảm bảo bác sĩ chỉ xem hàng đợi của ngày hiện tại và chỉ bệnh nhân thuộc quyền phụ trách của bác sĩ đó.

---

## 6. Kết Quả Kiểm Thử Tự Động (Automated Tests)

### Backend (Integration Tests - xUnit + EF Core SQLite In-Memory):
- **Số lượng test:** 60/60 PASSED (100%)
  - `NotificationTests.cs`:
    - `SendNotificationAsync_CreatesNotification_Successfully`
    - `SendNotificationAsync_WithSameDedupeKey_DoesNotDuplicate`
    - `GetNotificationsAsync_FiltersByUser_And_RespectsPagination`
    - `MarkAsReadAsync_UpdatesIsRead_And_DecrementsUnreadCount`
    - `MarkAllAsReadAsync_MarksOnlyCurrentUserNotifications`
    - `NotifyRoleUsersAsync_CreatesNotificationForActiveUsersWithRole`
  - `SlotPolicyTests.cs`:
    - `HoldingSlotStatuses_ContainsPendingConfirmedInProgress`
    - `ReleasedSlotStatuses_ContainsCompletedCancelledNoShow`
    - `AvailableSlots_ExcludesSlotsWithAppointmentsInHoldingStatuses`
    - `AvailableSlots_IncludesSlotsWithAppointmentsInReleasedStatuses`
  - `SlotContractTests.cs`:
    - `AvailableSlotDto_SerializesOnlySlotId`
  - `QueueDateAndIsolationTests.cs`:
    - `GetDoctorQueue_ReturnsOnlyTodayAppointments_ForCurrentDoctor`
  - `SeedDataIntegrityTests.cs`: 11 tests kiểm tra tính toàn vẹn dữ liệu seeder.

### Frontend (Unit & Component Tests - Vitest + React Testing Library):
- **Số lượng test:** 40/40 PASSED (100% across 7 test suites)
  - `notificationBell.test.tsx`: 5 test cases kiểm tra chuông thông báo, badge đếm, popover, thao tác đánh dấu đã đọc.
  - `notifications.test.tsx`: 5 test cases kiểm tra render danh sách, lọc tabs, phân trang, thông báo trống.
  - `doctorNameHelper.test.ts`, `doctorQueue.test.tsx`, `login.test.tsx`, `changeRequest.test.tsx`, `doctorSchedule.test.tsx`.

---

## 7. Hướng Dẫn Nghiệm Thu Thực Tế (Live Acceptance)

1. **Truy cập ứng dụng:** Mở trình duyệt tại `http://localhost:5173/patient/notifications`.
2. **Tài khoản kiểm thử demo:**
   - **Bệnh nhân:** `patient@cliniccare.local` / Mật khẩu: `Demo@12345`
   - **Lễ tân:** `reception@cliniccare.local` / Mật khẩu: `Demo@12345`
   - **Bác sĩ:** `doctor@cliniccare.local` / Mật khẩu: `Demo@12345`
   - **Dược sĩ:** `pharmacist@cliniccare.local` / Mật khẩu: `Demo@12345`
   - **Quản trị viên:** `admin@cliniccare.local` / Mật khẩu: `Demo@12345`
3. **Thao tác mẫu:**
   - Đăng nhập tài khoản Bệnh nhân -> Đặt lịch khám hoặc Đăng ký gói khám.
   - Quan sát chuông thông báo trên thanh header: Badge đếm tăng lên tương ứng.
   - Bấm vào chuông thông báo -> Xem popover danh sách -> Bấm "Xem tất cả" để vào trang `/patient/notifications`.
   - Đăng nhập tài khoản Lễ tân: Kiểm tra thông báo nghiệp vụ mới xuất hiện tức thì.
