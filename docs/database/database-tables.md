# Danh sách bảng cơ sở dữ liệu

## Ghi chú chung

Hệ thống sử dụng cơ sở dữ liệu SQL Server cho website quản lý phòng khám tích hợp AI API.

AI trong hệ thống chỉ có nhiệm vụ gợi ý chuyên khoa tham khảo, không chẩn đoán bệnh, không kê đơn và không thay thế bác sĩ.

---

# 1. Nhóm tài khoản và phân quyền

## Users

Lưu thông tin đăng nhập chung cho tất cả tài khoản.

Nếu dùng ASP.NET Core Identity, bảng này có thể tương ứng với `AspNetUsers`.

Các cột chính:

- Id
- FullName
- Email
- PhoneNumber
- PasswordHash
- Role
- IsActive
- CreatedAt
- UpdatedAt

Vai trò:

- Patient
- Receptionist
- Doctor
- Admin

Ghi chú:

- Nhân viên như lễ tân, bác sĩ, admin do Admin tạo tài khoản.
- Bệnh nhân có thể tự đăng ký tài khoản.

---

## Patients

Lưu thông tin riêng của bệnh nhân.

Các cột chính:

- Id
- UserId
- Gender
- DateOfBirth
- Address
- CreatedAt
- UpdatedAt

Ràng buộc:

- `UserId` là khóa ngoại tới `Users.Id`.
- `UserId` phải unique để đảm bảo 1 user chỉ có 1 hồ sơ bệnh nhân.

---

## Doctors

Lưu thông tin riêng của bác sĩ.

Các cột chính:

- Id
- UserId
- AcademicTitle
- ExperienceYears
- Description
- IsActive
- CreatedAt
- UpdatedAt

Ràng buộc:

- `UserId` là khóa ngoại tới `Users.Id`.
- `UserId` phải unique để đảm bảo 1 user chỉ có 1 hồ sơ bác sĩ.
- `ExperienceYears >= 0`.

Ghi chú:

- Không lưu `SpecialtyId` trực tiếp trong bảng `Doctors`.
- Bác sĩ có thể thuộc nhiều chuyên khoa thông qua bảng `DoctorSpecialties`.

---

# 2. Nhóm chuyên khoa

## Specialties

Lưu danh sách chuyên khoa của phòng khám.

Các cột chính:

- Id
- SpecialtyCode
- Name
- Description
- IsActive
- AiEnabled
- CreatedAt
- UpdatedAt

Ràng buộc:

- `SpecialtyCode` unique.

Ghi chú:

- Không hard-delete chuyên khoa.
- `IsActive = false`: chuyên khoa bị ẩn khỏi hệ thống đặt lịch nhưng vẫn giữ dữ liệu lịch sử.
- `AiEnabled = false`: chuyên khoa chưa được AI gợi ý.
- AI chỉ được gợi ý chuyên khoa thỏa điều kiện:
  - `IsActive = true`
  - `AiEnabled = true`
  - Có ít nhất 1 bác sĩ hoạt động thuộc chuyên khoa đó.

---

## DoctorSpecialties

Bảng trung gian thể hiện quan hệ nhiều-nhiều giữa bác sĩ và chuyên khoa.

Các cột chính:

- DoctorId
- SpecialtyId
- IsPrimary

Ràng buộc:

- Khóa chính có thể là `(DoctorId, SpecialtyId)`.
- `DoctorId` là khóa ngoại tới `Doctors.Id`.
- `SpecialtyId` là khóa ngoại tới `Specialties.Id`.

Ghi chú:

- Một bác sĩ có thể thuộc nhiều chuyên khoa.
- `IsPrimary = true` dùng để đánh dấu chuyên khoa chính của bác sĩ.

---

# 3. Nhóm lịch làm việc và slot khám

## DoctorWorkSchedules

Lưu lịch làm việc của bác sĩ theo ngày.

Các cột chính:

- Id
- DoctorId
- WorkDate
- StartTime
- EndTime
- IsActive
- CreatedAt
- UpdatedAt

Ràng buộc:

- `DoctorId` là khóa ngoại tới `Doctors.Id`.
- `StartTime < EndTime`.

Ghi chú:

- Lịch làm việc dùng để sinh ra các slot khám 30 phút.
- Nếu lịch làm việc bị tắt, hệ thống không sinh thêm slot mới từ lịch đó.

---

## AppointmentSlots

Lưu các slot khám cố định 30 phút.

Các cột chính:

- Id
- DoctorId
- SlotDate
- StartTime
- EndTime
- IsBooked
- ActiveAppointmentId
- CreatedAt
- UpdatedAt

Ràng buộc:

- `DoctorId` là khóa ngoại tới `Doctors.Id`.
- `ActiveAppointmentId` nullable, liên kết tới lịch hẹn đang giữ slot.
- `UNIQUE(DoctorId, SlotDate, StartTime)`.
- `StartTime < EndTime`.

Ghi chú:

- Mỗi slot là 30 phút.
- `IsBooked = false`: slot còn trống.
- `IsBooked = true`: slot đang được giữ bởi một lịch hẹn còn hiệu lực.
- Khi đặt lịch phải cập nhật slot trong transaction.
- Không tạo unique constraint trực tiếp trên `Appointments.AppointmentSlotId`, vì lịch đã hủy vẫn cần lưu lịch sử nhưng slot phải được giải phóng.

---

## DoctorLeaveRequests

Lưu yêu cầu nghỉ của bác sĩ.

Các cột chính:

- Id
- DoctorId
- StartDateTime
- EndDateTime
- Reason
- Status
- AdminNote
- CreatedAt
- UpdatedAt

Trạng thái:

- Pending
- PendingAppointmentResolution
- Approved
- Rejected
- Cancelled

Ràng buộc:

- `DoctorId` là khóa ngoại tới `Doctors.Id`.
- `StartDateTime < EndDateTime`.

Ghi chú:

- Bác sĩ chỉ gửi yêu cầu nghỉ.
- Admin duyệt hoặc từ chối.
- Nếu có lịch hẹn bị ảnh hưởng, yêu cầu chuyển sang `PendingAppointmentResolution` để lễ tân xử lý lịch trước.

---

# 4. Nhóm lịch hẹn

## Appointments

Lưu thông tin lịch hẹn khám.

Các cột chính:

- Id
- AppointmentCode
- PatientId
- DoctorId
- SpecialtyId
- AppointmentSlotId
- AppointmentDate
- StartTime
- EndTime
- Reason
- Status
- CreatedAt
- UpdatedAt

Trạng thái:

- Pending
- Confirmed
- PendingReschedule
- PendingCancellation
- Cancelled
- Completed
- NoShow

Ràng buộc:

- `PatientId` là khóa ngoại tới `Patients.Id`.
- `DoctorId` là khóa ngoại tới `Doctors.Id`.
- `SpecialtyId` là khóa ngoại tới `Specialties.Id`.
- `AppointmentSlotId` là khóa ngoại tới `AppointmentSlots.Id`.
- `StartTime < EndTime`.

Ghi chú:

- Vẫn giữ `SpecialtyId` trong `Appointments`.
- Lý do: đây là chuyên khoa bệnh nhân chọn lúc đặt lịch, dùng để thống kê và lưu lịch sử.
- Vì bác sĩ có thể thuộc nhiều chuyên khoa nên không nên suy ra chuyên khoa chỉ từ `DoctorId`.
- `Reason` không bắt buộc; nếu nhập thì 10–500 ký tự.
- Lịch mới tạo có trạng thái `Pending`, chờ lễ tân xác nhận.

---

## AppointmentHistory

Lưu lịch sử thay đổi trạng thái và thao tác với lịch hẹn.

Các cột chính:

- Id
- AppointmentId
- Action
- OldStatus
- NewStatus
- Note
- PerformedByUserId
- CreatedAt

Action enum:

- Created
- Confirmed
- RescheduleRequested
- Rescheduled
- CancelRequested
- Cancelled
- Completed
- NoShow
- RevisitCreated

Ràng buộc:

- `AppointmentId` là khóa ngoại tới `Appointments.Id`.
- `PerformedByUserId` là khóa ngoại tới `Users.Id`.

Ghi chú:

- Mọi thao tác đặt, xác nhận, đổi, hủy, hoàn thành, NoShow đều phải ghi lịch sử.
- Không cần thêm trạng thái `Rescheduled`, vì thao tác đổi lịch được lưu trong `AppointmentHistory`.

---
## AppointmentChangeRequests

Lưu yêu cầu đổi hoặc hủy lịch do bệnh nhân gửi.

Các cột chính:

- Id
- AppointmentId
- RequestType
- RequestedSlotId
- Reason
- Status
- RequestedByUserId
- ProcessedByUserId
- CreatedAt
- ProcessedAt

`RequestType` gồm:

- `Reschedule`
- `Cancellation`

`Status` gồm:

- `Pending`
- `Approved`
- `Rejected`
- `Withdrawn`

Ràng buộc:

- `AppointmentId` là khóa ngoại tới `Appointments.Id`.
- `RequestedSlotId` nullable, khóa ngoại tới `AppointmentSlots.Id`.
- `RequestedByUserId` là khóa ngoại tới `Users.Id`.
- `ProcessedByUserId` nullable, khóa ngoại tới `Users.Id`.
- Yêu cầu đổi lịch phải có `RequestedSlotId`.
- Yêu cầu hủy lịch không bắt buộc có `RequestedSlotId`.
- Một lịch hẹn chỉ được có tối đa một yêu cầu đang ở trạng thái `Pending`.

Ghi chú:

- Slot cũ vẫn được giữ khi yêu cầu đổi lịch đang chờ xử lý.
- Slot mới bệnh nhân chọn chỉ là slot mong muốn, chưa bị khóa ngay.
- Khi lễ tân xử lý, hệ thống phải kiểm tra lại slot mới.
- Nếu yêu cầu bị rút, `Status` chuyển thành `Withdrawn`.
# 5. Nhóm tóm tắt khám và tái khám

## VisitSummaries

Lưu tóm tắt kết quả khám do bác sĩ nhập.

Các cột chính:

- Id
- AppointmentId
- DoctorId
- Summary
- FollowUpInstruction
- RevisitRequestId
- CreatedAt
- UpdatedAt

Ràng buộc:

- `AppointmentId` là khóa ngoại tới `Appointments.Id`.
- `DoctorId` là khóa ngoại tới `Doctors.Id`.
- `RevisitRequestId` nullable, khóa ngoại tới `RevisitRequests.Id`.

Ghi chú:

- Không gọi là chẩn đoán.
- Đây chỉ là ghi chú tóm tắt kết quả khám trong phạm vi đồ án.
- Nếu bác sĩ có tạo đề xuất tái khám, có thể liên kết qua `RevisitRequestId`.

---

## RevisitRequests

Lưu đề xuất tái khám do bác sĩ tạo.

Các cột chính:

- Id
- AppointmentId
- PatientId
- DoctorId
- SuggestedDate
- Note
- Status
- NewAppointmentId
- CreatedAt
- UpdatedAt

Trạng thái:

- PendingPatientResponse
- Accepted
- Rejected
- ConvertedToAppointment

Ràng buộc:

- `AppointmentId` là khóa ngoại tới `Appointments.Id`.
- `PatientId` là khóa ngoại tới `Patients.Id`.
- `DoctorId` là khóa ngoại tới `Doctors.Id`.
- `NewAppointmentId` nullable, khóa ngoại tới `Appointments.Id`.

Ghi chú:

- Bác sĩ tạo đề xuất tái khám.
- Bệnh nhân đồng ý hoặc từ chối.
- Nếu bệnh nhân đồng ý và chọn slot, hệ thống tạo lịch hẹn mới trạng thái `Pending`.
- Khi đó `NewAppointmentId` lưu lịch hẹn mới được tạo từ đề xuất tái khám.

---

# 6. Nhóm AI

## AiSuggestionLogs

Lưu log tối thiểu của lần gọi AI.

Các cột chính:

- Id
- PatientId
- InputText
- SuggestedSpecialtiesJson
- SelectedSpecialtyId
- Provider
- PromptVersion
- CreatedAt

Ràng buộc:

- `PatientId` là khóa ngoại tới `Patients.Id`.
- `SelectedSpecialtyId` nullable, khóa ngoại tới `Specialties.Id`.

Ghi chú:

- Không gửi họ tên, số điện thoại, email hoặc dữ liệu định danh sang AI API.
- AI chỉ gợi ý chuyên khoa, không chẩn đoán bệnh.
- `SuggestedSpecialtiesJson` lưu Top 3 chuyên khoa AI trả về.
- `PromptVersion` dùng để biết hệ thống đang dùng phiên bản prompt nào.

---

# 7. Nhóm báo cáo và audit

## SystemAuditLogs

Lưu lịch sử thao tác quan trọng trong hệ thống.

Các cột chính:

- Id
- UserId
- Action
- EntityName
- EntityId
- Description
- CreatedAt

Ràng buộc:

- `UserId` là khóa ngoại tới `Users.Id`.

Ghi chú:

- Dùng để audit các thao tác quan trọng như tạo tài khoản, cập nhật bác sĩ, cập nhật chuyên khoa, duyệt lịch nghỉ, xuất báo cáo.