# ĐẶC TẢ API HỆ THỐNG QUẢN LÝ PHÒNG KHÁM

## 1. Quy ước chung

### 1.1. Base URL

```text
/api/v1
```

Ví dụ:

```text
POST /api/v1/auth/login
GET /api/v1/appointments/my
```

### 1.2. Định dạng dữ liệu

- Request và response sử dụng JSON.
- Thời gian sử dụng chuẩn ISO 8601.
- Backend lưu thời gian theo UTC.
- Frontend hiển thị theo múi giờ Việt Nam.
- API key AI không được gửi xuống frontend.

### 1.3. Xác thực

Các API cần đăng nhập sử dụng JWT:

```http
Authorization: Bearer {access_token}
```

### 1.4. Response thành công

```json
{
  "success": true,
  "message": "Thao tác thành công",
  "data": {}
}
```

### 1.5. Response thất bại

```json
{
  "success": false,
  "message": "Dữ liệu không hợp lệ",
  "errorCode": "VALIDATION_ERROR",
  "errors": {
    "fieldName": [
      "Nội dung lỗi"
    ]
  }
}
```

### 1.6. Phân trang

Request:

```text
page=1
pageSize=10
```

Response:

```json
{
  "success": true,
  "data": {
    "items": [],
    "page": 1,
    "pageSize": 10,
    "totalItems": 0,
    "totalPages": 0
  }
}
```

### 1.7. HTTP Status Code

| Mã | Ý nghĩa |
|---:|---|
| 200 | Xử lý thành công |
| 201 | Tạo dữ liệu thành công |
| 204 | Thành công, không có nội dung trả về |
| 400 | Dữ liệu đầu vào không hợp lệ |
| 401 | Chưa đăng nhập |
| 403 | Không có quyền |
| 404 | Không tìm thấy dữ liệu |
| 409 | Xung đột dữ liệu hoặc slot đã được đặt |
| 422 | Vi phạm quy tắc nghiệp vụ |
| 500 | Lỗi hệ thống |
| 503 | Dịch vụ AI tạm thời không khả dụng |

---

# 2. API xác thực

## 2.1. Đăng ký bệnh nhân

```http
POST /api/v1/auth/register
```

Quyền: Public.

Request:

```json
{
  "fullName": "Nguyễn Văn A",
  "email": "vana@example.com",
  "phoneNumber": "0901234567",
  "password": "Example@123",
  "dateOfBirth": "2004-08-20",
  "gender": "Male",
  "address": "Bình Dương"
}
```

Quy tắc:

- Chỉ bệnh nhân được tự đăng ký.
- Email và số điện thoại không được trùng.
- Mật khẩu phải đạt yêu cầu của hệ thống.
- Tài khoản được gán vai trò `Patient`.

Response: `201 Created`.

## 2.2. Đăng nhập

```http
POST /api/v1/auth/login
```

Quyền: Public.

Request:

```json
{
  "emailOrPhone": "vana@example.com",
  "password": "Example@123"
}
```

Response:

```json
{
  "success": true,
  "message": "Đăng nhập thành công",
  "data": {
    "accessToken": "jwt-token",
    "expiresAt": "2026-08-20T12:00:00Z",
    "user": {
      "id": 1,
      "fullName": "Nguyễn Văn A",
      "role": "Patient"
    }
  }
}
```

## 2.3. Xem tài khoản hiện tại

```http
GET /api/v1/auth/me
```

Quyền: Đã đăng nhập.

## 2.4. Đăng xuất

```http
POST /api/v1/auth/logout
```

Quyền: Đã đăng nhập.

---

# 3. API hồ sơ bệnh nhân

## 3.1. Xem hồ sơ của tôi

```http
GET /api/v1/patients/me
```

Quyền: Patient.

## 3.2. Cập nhật hồ sơ của tôi

```http
PUT /api/v1/patients/me
```

Quyền: Patient.

Request:

```json
{
  "fullName": "Nguyễn Văn A",
  "phoneNumber": "0901234567",
  "dateOfBirth": "2004-08-20",
  "gender": "Male",
  "address": "Bình Dương"
}
```

Bệnh nhân chỉ được cập nhật hồ sơ của chính mình.

---

# 4. API chuyên khoa

## 4.1. Danh sách chuyên khoa

```http
GET /api/v1/specialties
```

Quyền: Public.

Chỉ trả về chuyên khoa có `IsActive = true`.

## 4.2. Chi tiết chuyên khoa

```http
GET /api/v1/specialties/{specialtyId}
```

Quyền: Public.

## 4.3. Danh sách bác sĩ theo chuyên khoa

```http
GET /api/v1/specialties/{specialtyId}/doctors
```

Quyền: Public.

Query:

```text
page=1
pageSize=10
sortBy=name
```

Chỉ trả về bác sĩ:

- Có `IsActive = true`.
- Thuộc chuyên khoa được yêu cầu.
- Tài khoản người dùng đang hoạt động.

## 4.4. Gợi ý bác sĩ theo chuyên khoa

```http
GET /api/v1/specialties/{specialtyId}/recommended-doctors
```

Quyền: Public.

Query:

```text
fromDate=2026-08-21
days=14
```

Thuật toán xếp hạng được xử lý tại backend, không phải AI.

Response:

```json
{
  "success": true,
  "data": [
    {
      "doctorId": 10,
      "fullName": "Nguyễn Văn B",
      "experienceYears": 12,
      "experienceScore": 0.6,
      "availabilityScore": 1.0,
      "totalScore": 0.84,
      "earliestAvailableSlot": "2026-08-21T08:00:00+07:00"
    }
  ]
}
```

---

# 5. API bác sĩ và slot khám

## 5.1. Chi tiết bác sĩ

```http
GET /api/v1/doctors/{doctorId}
```

Quyền: Public.

## 5.2. Danh sách chuyên khoa của bác sĩ

```http
GET /api/v1/doctors/{doctorId}/specialties
```

Quyền: Public.

## 5.3. Slot trống của bác sĩ

```http
GET /api/v1/doctors/{doctorId}/available-slots
```

Quyền: Public.

Query:

```text
fromDate=2026-08-21
toDate=2026-08-27
specialtyId=1
```

Chỉ trả về slot:

- Chưa được đặt.
- Nằm trong lịch làm việc của bác sĩ.
- Không thuộc thời gian nghỉ đã được duyệt.
- Chưa qua thời gian hiện tại.
- Bác sĩ và chuyên khoa đang hoạt động.

Response:

```json
{
  "success": true,
  "data": [
    {
      "slotId": 105,
      "doctorId": 10,
      "slotDate": "2026-08-21",
      "startTime": "08:00:00",
      "endTime": "08:30:00"
    }
  ]
}
```

---

# 6. API AI gợi ý chuyên khoa

## 6.1. Gợi ý chuyên khoa

```http
POST /api/v1/ai/specialty-suggestions
```

Quyền: Patient.

Request:

```json
{
  "description": "Tôi bị đau họng, ho khan và hơi sốt."
}
```

Quy tắc:

- Không gửi tên, email, số điện thoại hoặc mã bệnh nhân sang AI.
- Chỉ gửi nội dung mô tả triệu chứng.
- Whitelist chỉ gồm chuyên khoa hoạt động, bật AI và có bác sĩ hoạt động.
- AI chỉ được trả tối đa 3 chuyên khoa.
- Backend phải kiểm tra lại toàn bộ kết quả AI.
- AI không được chẩn đoán hoặc kê đơn.

Response:

```json
{
  "success": true,
  "message": "Đã tạo gợi ý chuyên khoa",
  "data": {
    "suggestions": [
      {
        "rank": 1,
        "specialtyId": 2,
        "specialtyCode": "ENT",
        "specialtyName": "Tai Mũi Họng",
        "reason": "Các triệu chứng liên quan đến vùng họng và đường hô hấp trên."
      },
      {
        "rank": 2,
        "specialtyId": 4,
        "specialtyCode": "RESPIRATORY",
        "specialtyName": "Hô hấp",
        "reason": "Triệu chứng ho có thể cần khám chuyên khoa hô hấp."
      }
    ],
    "provider": "OpenAI",
    "promptVersion": "v1.0",
    "disclaimer": "Kết quả chỉ mang tính tham khảo và không phải chẩn đoán y khoa."
  }
}
```

Nếu AI lỗi:

```json
{
  "success": false,
  "message": "AI tạm thời không khả dụng. Vui lòng chọn chuyên khoa thủ công.",
  "errorCode": "AI_SERVICE_UNAVAILABLE"
}
```

AI lỗi không được làm gián đoạn quy trình đặt lịch.

---

# 7. API lịch hẹn của bệnh nhân

## 7.1. Tạo lịch hẹn

```http
POST /api/v1/appointments
```

Quyền: Patient.

Request:

```json
{
  "doctorId": 10,
  "specialtyId": 2,
  "appointmentSlotId": 105,
  "reason": "Đau họng kéo dài nhiều ngày."
}
```

`reason` không bắt buộc. Nếu nhập phải từ 10–500 ký tự.

Backend phải kiểm tra:

- Bệnh nhân đang hoạt động.
- Hồ sơ bệnh nhân phải có đủ `Gender` và `DateOfBirth`. Nếu thiếu, trả về lỗi `VALIDATION_ERROR`.
- Bác sĩ đang hoạt động.
- Chuyên khoa đang hoạt động.
- Bác sĩ thuộc chuyên khoa.
- Slot thuộc bác sĩ đã chọn.
- Slot nằm trong lịch làm việc.
- Bác sĩ không nghỉ.
- Bệnh nhân không có lịch trùng giờ.
- Slot còn trống.

Xử lý trong transaction:

```text
Kiểm tra slot
→ Khóa slot
→ Tạo Appointment trạng thái Pending
→ Gán ActiveAppointmentId cho slot
→ Ghi AppointmentHistory
→ Commit
```

Response: `201 Created`.

```json
{
  "success": true,
  "message": "Đặt lịch thành công, vui lòng chờ lễ tân xác nhận",
  "data": {
    "appointmentId": 501,
    "appointmentCode": "APT-20260820-0501",
    "status": "Pending"
  }
}
```

Nếu slot đã được đặt:

```text
409 Conflict
SLOT_ALREADY_BOOKED
```

## 7.2. Danh sách lịch của tôi

```http
GET /api/v1/appointments/my
```

Quyền: Patient.

Query:

```text
status=Confirmed
page=1
pageSize=10
```

## 7.3. Chi tiết lịch hẹn

```http
GET /api/v1/appointments/{appointmentId}
```

Quyền:

- Bệnh nhân sở hữu lịch.
- Bác sĩ phụ trách.
- Lễ tân.
- Admin.

## 7.4. Gửi yêu cầu đổi lịch

```http
POST /api/v1/appointments/{appointmentId}/reschedule-requests
```

Quyền: Patient sở hữu lịch.

Request:

```json
{
  "requestedSlotId": 205,
  "reason": "Tôi bận vào thời gian đã đặt."
}
```

Kết quả:

- Tạo `AppointmentChangeRequests`.
- `RequestType = Reschedule`.
- `Status = Pending`.
- Lịch chuyển thành `PendingReschedule`.
- Slot cũ vẫn được giữ.
- Slot mong muốn chưa bị khóa.

## 7.5. Gửi yêu cầu hủy lịch

```http
POST /api/v1/appointments/{appointmentId}/cancellation-requests
```

Quyền: Patient sở hữu lịch.

Request:

```json
{
  "reason": "Tôi không thể đến khám."
}
```

Kết quả:

- Tạo `AppointmentChangeRequests`.
- `RequestType = Cancellation`.
- `Status = Pending`.
- Lịch chuyển thành `PendingCancellation`.
- Slot hiện tại vẫn được giữ.

## 7.6. Rút yêu cầu đổi hoặc hủy

```http
POST /api/v1/appointment-change-requests/{requestId}/withdraw
```

Quyền: Bệnh nhân tạo yêu cầu.

Điều kiện:

- Yêu cầu đang ở trạng thái `Pending`.
- Lễ tân chưa xử lý yêu cầu.

Kết quả:

- Yêu cầu chuyển thành `Withdrawn`.
- Lịch hẹn chuyển lại `Confirmed`.
- Slot cũ tiếp tục được giữ.

## 7.7. Xem lịch sử thay đổi

```http
GET /api/v1/appointments/{appointmentId}/history
```

Quyền: Người có quyền xem lịch hẹn.

---

# 8. API dành cho lễ tân

## 8.1. Danh sách lịch cần xử lý

```http
GET /api/v1/reception/appointments
```

Quyền: Receptionist.

Query:

```text
status=Pending
date=2026-08-21
page=1
pageSize=20
```

## 8.2. Xác nhận lịch hẹn

```http
POST /api/v1/reception/appointments/{appointmentId}/confirm
```

Quyền: Receptionist.

Điều kiện:

- Lịch đang ở trạng thái `Pending`.
- Slot vẫn hợp lệ.
- Bác sĩ vẫn hoạt động và không nghỉ.

Kết quả:

- Lịch chuyển thành `Confirmed`.
- Ghi `AppointmentHistory`.

## 8.3. Danh sách yêu cầu đổi/hủy

```http
GET /api/v1/reception/change-requests
```

Query:

```text
requestType=Reschedule
status=Pending
page=1
pageSize=20
```

## 8.4. Chấp nhận yêu cầu đổi lịch

```http
POST /api/v1/reception/change-requests/{requestId}/approve-reschedule
```

Request:

```json
{
  "newSlotId": 205,
  "note": "Đã liên hệ và thống nhất với bệnh nhân."
}
```

Xử lý trong transaction:

```text
Kiểm tra yêu cầu còn Pending
→ Giữ slot cũ
→ Kiểm tra slot mới
→ Khóa slot mới
→ Cập nhật lịch hẹn
→ Giải phóng slot cũ
→ Chuyển lịch về Confirmed
→ Chuyển yêu cầu thành Approved
→ Ghi AppointmentHistory
→ Commit
```

Nếu slot mới không còn trống:

- Không thay đổi lịch cũ.
- Slot cũ vẫn được giữ.
- Yêu cầu chưa được phê duyệt.
- Lễ tân chọn slot khác hoặc từ chối yêu cầu.

## 8.5. Chấp nhận yêu cầu hủy lịch

```http
POST /api/v1/reception/change-requests/{requestId}/approve-cancellation
```

Kết quả:

- Lịch chuyển thành `Cancelled`.
- Slot được giải phóng.
- `ActiveAppointmentId` của slot được xóa.
- Yêu cầu chuyển thành `Approved`.
- Ghi `AppointmentHistory`.

## 8.6. Từ chối yêu cầu đổi/hủy

```http
POST /api/v1/reception/change-requests/{requestId}/reject
```

Request:

```json
{
  "reason": "Không thể xử lý yêu cầu trong thời gian này."
}
```

Kết quả:

- Yêu cầu chuyển thành `Rejected`.
- Lịch hẹn chuyển lại `Confirmed`.
- Slot cũ tiếp tục được giữ.

## 8.7. Tra cứu bệnh nhân

```http
GET /api/v1/reception/patients/search
```

Query:

```text
keyword=0901234567
```

Có thể tìm theo:

- Mã bệnh nhân.
- Họ tên.
- Số điện thoại.

## 8.8. Xem lịch bị ảnh hưởng bởi yêu cầu nghỉ

```http
GET /api/v1/reception/leave-requests/{requestId}/affected-appointments
```

Quyền: Receptionist.

---

# 9. API dành cho bác sĩ

## 9.1. Xem lịch khám của tôi

```http
GET /api/v1/doctor/appointments
```

Quyền: Doctor.

Query:

```text
date=2026-08-21
status=Confirmed
```

Bác sĩ chỉ xem được lịch của chính mình.

## 9.2. Xem chi tiết lịch khám

```http
GET /api/v1/doctor/appointments/{appointmentId}
```

Quyền: Bác sĩ phụ trách lịch.

## 9.3. Hoàn thành buổi khám

```http
POST /api/v1/doctor/appointments/{appointmentId}/complete
```

Request:

```json
{
  "summary": "Bệnh nhân đã được thăm khám và tư vấn.",
  "followUpInstruction": "Theo dõi sức khỏe và quay lại nếu triệu chứng kéo dài."
}
```

Điều kiện:

- Bác sĩ đang đăng nhập là bác sĩ phụ trách.
- Lịch đang ở trạng thái `Confirmed`.
- Đã đến thời gian khám.
- Có nội dung tóm tắt kết quả khám.

Kết quả:

- Tạo `VisitSummaries`.
- Lịch chuyển thành `Completed`.
- Ghi `AppointmentHistory`.

## 9.4. Đánh dấu NoShow

```http
POST /api/v1/doctor/appointments/{appointmentId}/no-show
```

Điều kiện:

- Bác sĩ là người phụ trách.
- Lịch đang ở trạng thái `Confirmed`.
- Đã qua thời gian bắt đầu khám.

Kết quả:

- Lịch chuyển thành `NoShow`.
- Ghi `AppointmentHistory`.

## 9.5. Tạo đề xuất tái khám

```http
POST /api/v1/doctor/appointments/{appointmentId}/revisit-request
```

Request:

```json
{
  "suggestedDate": "2026-09-20",
  "note": "Đề nghị tái khám sau một tháng."
}
```

Điều kiện:

- Lịch đã `Completed`.
- Bác sĩ là người phụ trách lịch.

Kết quả:

- Tạo `RevisitRequests`.
- Trạng thái là `PendingPatientResponse`.
- Ghi lịch sử và thông báo cho bệnh nhân.

---

# 10. API tái khám của bệnh nhân

## 10.1. Danh sách đề xuất tái khám

```http
GET /api/v1/revisit-requests/my
```

Quyền: Patient.

## 10.2. Chi tiết đề xuất tái khám

```http
GET /api/v1/revisit-requests/{requestId}
```

Quyền: Bệnh nhân nhận đề xuất hoặc bác sĩ tạo đề xuất.

## 10.3. Đồng ý và chọn lịch tái khám

```http
POST /api/v1/revisit-requests/{requestId}/accept
```

Request:

```json
{
  "appointmentSlotId": 305
}
```

Xử lý:

```text
Kiểm tra đề xuất
→ Kiểm tra slot
→ Khóa slot
→ Tạo lịch mới trạng thái Pending
→ Lưu NewAppointmentId
→ Chuyển yêu cầu thành ConvertedToAppointment
→ Ghi lịch sử
→ Commit
```

Lịch mới vẫn phải chờ lễ tân xác nhận.

## 10.4. Từ chối tái khám

```http
POST /api/v1/revisit-requests/{requestId}/reject
```

Kết quả: yêu cầu chuyển thành `Rejected`.

---

# 11. API yêu cầu nghỉ

## 11.1. Bác sĩ tạo yêu cầu nghỉ

```http
POST /api/v1/doctor/leave-requests
```

Request:

```json
{
  "startDateTime": "2026-08-25T08:00:00+07:00",
  "endDateTime": "2026-08-25T17:00:00+07:00",
  "reason": "Việc cá nhân."
}
```

Trạng thái ban đầu: `Pending`.

## 11.2. Bác sĩ xem yêu cầu nghỉ

```http
GET /api/v1/doctor/leave-requests
```

## 11.3. Bác sĩ hủy yêu cầu nghỉ

```http
POST /api/v1/doctor/leave-requests/{requestId}/cancel
```

Chỉ được hủy yêu cầu đang `Pending`.

## 11.4. Admin xem yêu cầu nghỉ

```http
GET /api/v1/admin/leave-requests
```

Query:

```text
status=Pending
page=1
pageSize=20
```

## 11.5. Admin kiểm tra lịch bị ảnh hưởng

```http
GET /api/v1/admin/leave-requests/{requestId}/affected-appointments
```

Nếu có lịch bị ảnh hưởng:

- Yêu cầu chuyển thành `PendingAppointmentResolution`.
- Lễ tân xử lý các lịch hẹn liên quan.
- Admin chỉ duyệt sau khi xử lý xong.

## 11.6. Admin duyệt yêu cầu nghỉ

```http
POST /api/v1/admin/leave-requests/{requestId}/approve
```

Kết quả:

- Yêu cầu chuyển thành `Approved`.
- Không sinh slot trong thời gian nghỉ.
- Các slot trống đã sinh trong thời gian nghỉ bị vô hiệu hóa.
- Ghi `SystemAuditLogs`.

## 11.7. Admin từ chối yêu cầu nghỉ

```http
POST /api/v1/admin/leave-requests/{requestId}/reject
```

Request:

```json
{
  "adminNote": "Không thể duyệt do thiếu bác sĩ trực."
}
```

Kết quả: yêu cầu chuyển thành `Rejected`.

---

# 12. API quản trị tài khoản

## 12.1. Danh sách tài khoản

```http
GET /api/v1/admin/users
```

Query:

```text
role=Doctor
isActive=true
page=1
pageSize=20
```

## 12.2. Tạo tài khoản nhân viên

```http
POST /api/v1/admin/users
```

Quyền: Admin.

Admin được tạo tài khoản:

- Receptionist.
- Doctor.
- Admin.

## 12.3. Chi tiết tài khoản

```http
GET /api/v1/admin/users/{userId}
```

## 12.4. Cập nhật tài khoản

```http
PUT /api/v1/admin/users/{userId}
```

## 12.5. Khóa hoặc mở tài khoản

```http
PATCH /api/v1/admin/users/{userId}/status
```

Request:

```json
{
  "isActive": false
}
```

Không hard-delete tài khoản đã phát sinh dữ liệu.

---

# 13. API quản trị bác sĩ

## 13.1. Danh sách bác sĩ

```http
GET /api/v1/admin/doctors
```

## 13.2. Tạo hồ sơ bác sĩ

```http
POST /api/v1/admin/doctors
```

Request:

```json
{
  "userId": 20,
  "academicTitle": "Thạc sĩ",
  "experienceYears": 10,
  "description": "Bác sĩ chuyên khoa",
  "specialties": [
    {
      "specialtyId": 2,
      "isPrimary": true
    },
    {
      "specialtyId": 4,
      "isPrimary": false
    }
  ]
}
```

## 13.3. Cập nhật bác sĩ

```http
PUT /api/v1/admin/doctors/{doctorId}
```

## 13.4. Cập nhật trạng thái bác sĩ

```http
PATCH /api/v1/admin/doctors/{doctorId}/status
```

Không hard-delete bác sĩ đã phát sinh lịch hẹn.

---

# 14. API quản trị chuyên khoa

## 14.1. Danh sách chuyên khoa

```http
GET /api/v1/admin/specialties
```

Bao gồm cả chuyên khoa ngừng hoạt động.

## 14.2. Tạo chuyên khoa

```http
POST /api/v1/admin/specialties
```

Request:

```json
{
  "specialtyCode": "ENT",
  "name": "Tai Mũi Họng",
  "description": "Khám các vấn đề tai, mũi và họng.",
  "isActive": true,
  "aiEnabled": true
}
```

## 14.3. Cập nhật chuyên khoa

```http
PUT /api/v1/admin/specialties/{specialtyId}
```

Không cho sửa `SpecialtyCode` nếu chuyên khoa đã phát sinh dữ liệu.

## 14.4. Bật hoặc tắt chuyên khoa

```http
PATCH /api/v1/admin/specialties/{specialtyId}/status
```

## 14.5. Bật hoặc tắt gợi ý AI

```http
PATCH /api/v1/admin/specialties/{specialtyId}/ai-enabled
```

Request:

```json
{
  "aiEnabled": false
}
```

---

# 15. API lịch làm việc và slot

## 15.1. Xem lịch làm việc của bác sĩ

```http
GET /api/v1/admin/doctors/{doctorId}/work-schedules
```

## 15.2. Tạo lịch làm việc

```http
POST /api/v1/admin/doctors/{doctorId}/work-schedules
```

Request:

```json
{
  "workDate": "2026-08-25",
  "startTime": "08:00:00",
  "endTime": "17:00:00"
}
```

## 15.3. Cập nhật lịch làm việc

```http
PUT /api/v1/admin/work-schedules/{scheduleId}
```

## 15.4. Ngừng lịch làm việc

```http
PATCH /api/v1/admin/work-schedules/{scheduleId}/status
```

Không được ngừng lịch làm việc nếu còn lịch hẹn chưa được xử lý.

## 15.5. Sinh slot 30 phút

```http
POST /api/v1/admin/work-schedules/{scheduleId}/generate-slots
```

Quy tắc:

- Mỗi slot dài 30 phút.
- Không sinh slot trùng.
- Không sinh trong thời gian nghỉ đã duyệt.
- Không sinh slot vượt ngoài lịch làm việc.

---

# 16. API dashboard và báo cáo

## 16.1. Dashboard tổng quan

```http
GET /api/v1/admin/dashboard/overview
```

Query:

```text
fromDate=2026-08-01
toDate=2026-08-31
```

Dữ liệu gồm:

- Tổng số lịch hẹn.
- Số lịch theo trạng thái.
- Số lượt đặt theo ngày.
- Phân bổ lịch theo chuyên khoa.
- Tỷ lệ hủy.
- Tỷ lệ NoShow.

## 16.2. Xuất báo cáo Excel

```http
GET /api/v1/admin/reports/appointments/export-excel
```

Query:

```text
fromDate=2026-08-01
toDate=2026-08-31
specialtyId=2
status=Completed
```

Response: file `.xlsx`.

---

# 17. Mã lỗi nghiệp vụ

| ErrorCode | HTTP | Ý nghĩa |
|---|---:|---|
| `VALIDATION_ERROR` | 400 | Dữ liệu không hợp lệ |
| `UNAUTHORIZED` | 401 | Chưa đăng nhập |
| `FORBIDDEN` | 403 | Không có quyền |
| `RESOURCE_NOT_FOUND` | 404 | Không tìm thấy dữ liệu |
| `SLOT_ALREADY_BOOKED` | 409 | Slot đã được đặt |
| `PATIENT_TIME_CONFLICT` | 409 | Bệnh nhân có lịch trùng giờ |
| `INVALID_APPOINTMENT_STATUS` | 409 | Trạng thái lịch không hợp lệ |
| `ACTIVE_CHANGE_REQUEST_EXISTS` | 409 | Đã có yêu cầu đổi/hủy đang chờ |
| `INVALID_CHANGE_REQUEST` | 422 | Yêu cầu đổi/hủy không hợp lệ |
| `DOCTOR_NOT_AVAILABLE` | 422 | Bác sĩ không làm việc hoặc đang nghỉ |
| `SPECIALTY_NOT_AVAILABLE` | 422 | Chuyên khoa không hoạt động |
| `LEAVE_HAS_AFFECTED_APPOINTMENTS` | 422 | Lịch nghỉ còn lịch hẹn chưa xử lý |
| `REVISIT_REQUEST_INVALID` | 422 | Đề xuất tái khám không hợp lệ |
| `AI_RESPONSE_INVALID` | 422 | AI trả kết quả không hợp lệ |
| `AI_SERVICE_UNAVAILABLE` | 503 | API AI tạm thời không khả dụng |

---

# 18. Yêu cầu bảo mật API

- Kiểm tra JWT trên backend.
- Kiểm tra role cho từng endpoint.
- Bệnh nhân chỉ xem dữ liệu của mình.
- Bác sĩ chỉ xử lý lịch được phân công.
- Lễ tân không được truy cập chức năng Admin.
- API AI phải có rate limit.
- Không ghi API key vào source code hoặc log.
- Không trả `PasswordHash` trong response.
- Validate toàn bộ DTO tại backend.
- Không tin tưởng dữ liệu từ frontend.
- Ghi audit các thao tác quan trọng.
- API đặt lịch, đổi lịch và tái khám phải sử dụng transaction.
- Kiểm tra lại quyền sở hữu tài nguyên ở từng request.