# Quy tắc nghiệp vụ (Business Rules)

## 1. Tài khoản, Identity, phân quyền và IsActive
- **BR-101**: Hệ thống có 4 vai trò (`IdentityRole<Guid>`): `Patient`, `Receptionist`, `Doctor`, `Admin`. Không lưu Role thành cột đơn mà dùng bảng `AspNetUserRoles`.
- **BR-102**: Bệnh nhân được tự đăng ký tài khoản. Nhân viên (Lễ tân, Bác sĩ, Admin) do Admin tạo.
- **BR-103**: Không được hard-delete các tài khoản đã phát sinh dữ liệu, chỉ sử dụng cờ `IsActive = false` để vô hiệu hóa.
- **BR-104**: Kiến trúc tài khoản: Sử dụng ASP.NET Core Identity. Bảng vật lý là `AspNetUsers`. `ApplicationUser` (kế thừa `IdentityUser<Guid>`) nằm ở tầng Infrastructure. Application định nghĩa và sử dụng `IIdentityService`. Domain hoàn toàn không phụ thuộc ASP.NET Core Identity.
- **BR-105**: Xác thực API sử dụng JWT.
- **BR-106**: Truy vấn kết hợp: Khi cần kết xuất dữ liệu Bác sĩ/Bệnh nhân với thông tin tài khoản, Infrastructure sẽ sử dụng LINQ join/projection và trả về DTO, không bắt buộc query hai lần.

## 2. Bệnh nhân
- **BR-201**: Quan hệ 1-1 logic giữa Tài khoản và Bệnh nhân thông qua cột `UserId` (kiểu Guid). `UserId` phải là Unique để đảm bảo 1 user chỉ có 1 hồ sơ. Entity `Patient` và `Doctor` trong Domain KHÔNG chứa navigation property trỏ tới `ApplicationUser`.
- **BR-202**: Bệnh nhân chỉ được xem và cập nhật hồ sơ của chính mình.
- **BR-203**: Email và Số điện thoại phải là duy nhất. Do ASP.NET Core Identity không mặc định đảm bảo `PhoneNumber` unique nên phải cấu hình kiểm tra hoặc đặt ràng buộc riêng khi triển khai.

## 3. Quan hệ nhiều–nhiều DoctorSpecialties và quy tắc chỉ một IsPrimary
- **BR-301**: Một bác sĩ có thể thuộc nhiều chuyên khoa thông qua bảng trung gian `DoctorSpecialties`.
- **BR-302**: Trong danh sách các chuyên khoa của một bác sĩ, chỉ được phép có duy nhất một chuyên khoa được đánh dấu là chuyên khoa chính (`IsPrimary = true`).

## 4. Specialties, SpecialtyCode, IsActive, AiEnabled và whitelist AI
- **BR-401**: Cột `SpecialtyCode` phải là duy nhất (Unique) trên toàn hệ thống.
- **BR-402**: Không hard-delete chuyên khoa. Dùng `IsActive = false` để ẩn khỏi danh sách đặt lịch nhưng vẫn bảo toàn lịch sử dữ liệu.
- **BR-403**: Whitelist AI: Hệ thống AI chỉ được phép gợi ý các chuyên khoa thỏa mãn đồng thời 3 điều kiện: 
  - Đang hoạt động (`IsActive = true`).
  - Được cho phép AI gợi ý (`AiEnabled = true`).
  - Có ít nhất 1 bác sĩ đang hoạt động thuộc chuyên khoa đó.

## 5. Lịch làm việc, lịch nghỉ và slot cố định 30 phút
- **BR-501**: Mỗi slot khám bệnh (`AppointmentSlots`) có độ dài cố định chính xác 30 phút.
- **BR-502**: Không sinh slot vượt ra ngoài khoảng thời gian làm việc của bác sĩ hoặc rơi vào thời gian nghỉ đã được Admin duyệt.
- **BR-503**: Nếu lịch làm việc bị tắt (`IsActive = false`), hệ thống không được sinh thêm slot mới từ lịch đó.

## 6. Kiểm tra chồng lấn
- **BR-601**: Thuật toán phát hiện trùng lặp/chồng lấn thời gian (overlapping) sử dụng nguyên tắc: 
  `NewStart < ExistingEnd AND NewEnd > ExistingStart`

## 7. Đặt lịch bằng transaction/atomic update và lỗi 409 SLOT_ALREADY_BOOKED
- **BR-701**: Toàn bộ quy trình Đặt lịch (Booking) bắt buộc phải sử dụng Transaction của Database để tránh race condition.
- **BR-702**: Nếu hệ thống phát hiện slot đã bị giữ (Booked), giao dịch lập tức bị hủy và API phải trả về lỗi `409 Conflict` với mã lỗi `SLOT_ALREADY_BOOKED`.

## 8. Toàn bộ trạng thái Appointment
- **BR-801**: Lịch hẹn (`Appointments`) chỉ được nằm trong các trạng thái sau:
  - `Pending`
  - `Confirmed`
  - `PendingReschedule`
  - `PendingCancellation`
  - `Cancelled`
  - `Completed`
  - `NoShow`

## 9. Chuyển trạng thái hợp lệ theo appointment-state.puml
- **BR-901**: Chỉ cho phép các luồng chuyển trạng thái sau:
  - Từ `Pending` $\rightarrow$ `Confirmed` / `PendingReschedule` / `PendingCancellation` / `Cancelled`
  - Từ `Confirmed` $\rightarrow$ `PendingReschedule` / `PendingCancellation` / `Completed` / `NoShow` / `Cancelled`
  - Từ `PendingReschedule` $\rightarrow$ `Confirmed` (thành công/rút lui/từ chối) / `Cancelled` (nếu hủy thành công)
  - Từ `PendingCancellation` $\rightarrow$ `Cancelled` (chấp nhận) / `Confirmed` (từ chối/rút lui)

## 10. AppointmentChangeRequests
- **BR-1001**: Yêu cầu thay đổi (`RequestType`) bao gồm: `Reschedule` (Đổi lịch) và `Cancellation` (Hủy lịch).
- **BR-1002**: Trạng thái yêu cầu (`Status`) bao gồm: `Pending`, `Approved`, `Rejected`, `Withdrawn`.
- **BR-1003**: Tại bất kỳ thời điểm nào, một lịch hẹn chỉ được có TỐI ĐA MỘT yêu cầu đang ở trạng thái `Pending`.
- **BR-1004**: Trong lúc yêu cầu đang `Pending`, slot cũ (hiện tại) của lịch hẹn vẫn phải được giữ chặt, chưa bị giải phóng.

## 11. Quy trình đổi lịch
- **BR-1101**: Quá trình duyệt cấp phép đổi lịch phải nằm trong 1 Transaction nguyên tử với luồng:
  1. Giữ slot cũ.
  2. Kiểm tra và khóa slot mới.
  3. Cập nhật thông tin `Appointment`.
  4. Giải phóng slot cũ.
  5. Chuyển lịch thành `Confirmed`.
  6. Đánh dấu Request là `Approved`.
  7. Ghi history.
  8. Commit Transaction.

## 12. Hoàn thành khám, NoShow và VisitSummary
- **BR-1201**: Bác sĩ tạo `VisitSummaries` (tóm tắt kết quả) khi hoàn thành buổi khám, lịch hẹn tự động chuyển thành `Completed`. Không sử dụng từ "chẩn đoán bệnh".
- **BR-1202**: Bác sĩ có quyền đánh dấu `NoShow` nếu đã quá thời gian bắt đầu mà bệnh nhân không có mặt.

## 13. Đề xuất tái khám
- **BR-1301**: Bác sĩ có thể tạo đề xuất tái khám (`RevisitRequests`).
- **BR-1302**: Khi bệnh nhân đồng ý đề xuất và chọn slot, lịch hẹn mới được tạo luôn mang trạng thái `Pending` và bắt buộc phải qua bước Lễ tân xác nhận.

## 14. DoctorLeaveRequests và PendingAppointmentResolution
- **BR-1401**: Bác sĩ gửi yêu cầu nghỉ với trạng thái mặc định là `Pending`.
- **BR-1402**: Nếu hệ thống phát hiện có lịch hẹn trùng với khoảng thời gian xin nghỉ, yêu cầu nghỉ phải bị chuyển thành `PendingAppointmentResolution` để chờ Lễ tân giải quyết các lịch hẹn đó trước khi Admin có thể duyệt.

## 15. Thuật toán xếp hạng bác sĩ
- **BR-1501**: Sử dụng công thức sau tại Backend để tính điểm và xếp hạng bác sĩ:
  - `ExperienceScore = min(ExperienceYears, 20) / 20`
  - `TotalScore = 0.4 * ExperienceScore + 0.6 * AvailabilityScore`

## 16. AI
- **BR-1601**: AI chỉ gợi ý tối đa Top 3 chuyên khoa dựa trên các chuyên khoa nằm trong whitelist.
- **BR-1602**: Tuyệt đối không gửi thông tin định danh bệnh nhân (PII như Tên, SDT, Email) sang API của hệ thống AI.
- **BR-1603**: AI không được chẩn đoán và không được kê đơn.
- **BR-1604**: Nếu API của AI gặp lỗi, hệ thống phải tự động fallback, cho phép bệnh nhân chọn khoa thủ công, tuyệt đối không làm gián đoạn việc đặt lịch.

## 17. AppointmentHistory và SystemAuditLogs
- **BR-1701**: Bảng `AppointmentHistory` ghi vết mọi thay đổi của lịch hẹn. Cột `Action` CHỈ BAO GỒM chính xác các giá trị:
  - `Created`
  - `Confirmed`
  - `RescheduleRequested`
  - `Rescheduled`
  - `CancelRequested`
  - `Cancelled`
  - `Completed`
  - `NoShow`
  - `RevisitCreated`
- **BR-1702**: Các thao tác nhạy cảm từ Admin/Hệ thống (tạo user, cập nhật bác sĩ/khoa, duyệt lịch nghỉ,...) phải ghi lại vào `SystemAuditLogs`.

## 18. Bảo mật, backend validation, secret và rate limit
- **BR-1801**: Mọi API yêu cầu phân quyền phải thực hiện validate JWT và kiểm tra chặt chẽ `Role`. Validate Role và quyền sở hữu (ví dụ BN chỉ xem lịch của BN, Bác sĩ chỉ xem lịch của mình).
- **BR-1802**: Mọi dữ liệu đẩy lên từ client phải được Backend validation.
- **BR-1803**: Không lưu trữ mật khẩu thuần túy mà không băm. Không lộ API keys, Secrets trong source code hay logs.
- **BR-1804**: Các API kết nối ra bên ngoài như gọi AI bắt buộc phải gắn Rate Limit.

---

# Các quyết định CHƯA CHỐT

- Quan hệ vòng AppointmentSlots.ActiveAppointmentId và Appointments.AppointmentSlotId.
- Có giữ VisitSummaries.RevisitRequestId hay không.
- Filtered unique index cho một change request Pending.
- Cách đảm bảo mỗi bác sĩ chỉ có một IsPrimary.
- Danh sách tên và SpecialtyCode chính xác của 7 khoa.
- Grace period trước khi đánh dấu NoShow.
- Thời hạn tối thiểu/tối đa khi đổi hoặc hủy lịch.
- Kiểu Domain của Gender.
