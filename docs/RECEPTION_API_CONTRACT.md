# Hợp Đồng Giao Diện Lập Trình Ứng Dụng: Phân Hệ Lễ Tân & Tiếp Nhận
## Reception Workspace API Contract & Specification

---

### 1. Quy Chuẩn Kỹ Thuật Chung (General Specifications)

- **Base URL**: `/api/v1`
- **Content-Type**: `application/json; charset=utf-8`
- **Xác thực (Authentication)**: `Authorization: Bearer <JWT_TOKEN>`
- **Tiêu chuẩn phản hồi (Standard Response Envelope)**:
```json
{
  "success": true,
  "message": "Thông điệp thành công hoặc mã lỗi",
  "data": { ... },
  "errors": []
}
```

---

### 2. Danh Mục Endpoint Tiếp Nhận & Điều Phối Lượt Khám (Reception & Visit Intake)

#### 2.1. Tiếp Nhận Người Bệnh & Cấp Số Thứ Tự (3-Step Walk-In Intake)
- **Phương thức**: `POST`
- **Đường dẫn**: `/api/v1/patient-visits/reception-intake`
- **Headers**:
  - `Idempotency-Key`: Chuỗi UUIDv4 (Bắt buộc). Dùng để chống tạo trùng bản ghi khi mạng chập chờn hoặc thao tác lặp.
- **Request Body DTO (`ReceptionIntakeRequest`)**:
```json
{
  "facilityId": 1,
  "departmentId": 10,
  "roomId": 101,
  "assignedDoctorId": 5,
  "priority": "Normal",
  "chiefComplaint": "Người bệnh ho sốt kéo dài 3 ngày",
  "existingPatientId": null,
  "newPatient": {
    "fullName": "NGUYỄN VĂN AN",
    "phoneNumber": null,
    "dateOfBirth": "2020-05-15",
    "gender": 0,
    "identityCardNumber": null,
    "address": "Phường Bến Nghé, Quận 1, TP.HCM",
    "emergencyContact": {
      "contactName": "Hoàng Thị Mai",
      "relationship": "Mẹ ruột",
      "phoneNumber": "0988112233",
      "isGuardian": true
    },
    "allergies": [
      {
        "allergen": "Penicillin",
        "severity": "Severe",
        "reaction": "Nổi mề đay, khó thở"
      }
    ]
  },
  "idempotencyKey": "9b1deb4d-3b7d-4bad-9bdd-2b0d7b3dcb6d"
}
```
- **Response Body DTO (`CheckInTicketDto`)**:
```json
{
  "success": true,
  "message": "Tiếp nhận và cấp số thứ tự thành công",
  "data": {
    "visitId": 501,
    "visitCode": "VIS-20260914-0001",
    "queueNumber": 1,
    "queueDisplay": "01",
    "patientId": 99,
    "patientName": "NGUYỄN VĂN AN",
    "medicalRecordNumber": "BN-2026-000099",
    "phoneNumber": "0988112233",
    "facilityId": 1,
    "facilityName": "Cơ sở Quận 1 - Trung tâm",
    "departmentId": 10,
    "departmentName": "Khoa Khám Bệnh Đa Khoa",
    "roomId": 101,
    "roomNumber": "P101",
    "assignedDoctorId": 5,
    "doctorName": "BS. Lê Trọng Nghĩa",
    "checkedInAtUtc": "2026-09-14T08:30:00Z",
    "receptionistName": "Trần Thu Hà",
    "status": "WaitingForDoctor",
    "priority": "Normal",
    "arrivalType": "WalkIn"
  }
}
```

#### 2.2. Tiếp Nhận Nhanh Lịch Hẹn Đã Đặt Trước (Fast Appointment Check-In)
- **Phương thức**: `POST`
- **Đường dẫn**: `/api/v1/patient-visits/appointments/{appointmentId}/check-in`
- **Mô tả**: Chuyển trạng thái lịch hẹn từ `Confirmed` sang `CheckedIn`, tự động tạo `PatientVisit`, cấp STT tại buồng khám của bác sĩ đã đặt hẹn.
- **Response**: Trả về `CheckInTicketDto` tương tự endpoint 2.1.

#### 2.3. Lấy Lại Thông Tin Phiếu Khám Đã Cấp (Get Check-In Ticket)
- **Phương thức**: `GET`
- **Đường dẫn**: `/api/v1/patient-visits/{id}/ticket`
- **Response**: Trả về `CheckInTicketDto` để in lại phiếu A5/nhiệt khi cần.

---

### 3. Danh Mục Endpoint Bàn Làm Việc Lễ Tân (Reception Workspace Queries)

#### 3.1. Danh Sách Lịch Hẹn & Hàng Đợi Tiếp Nhận (Worklist Multi-Tab)
- **Phương thức**: `GET`
- **Đường dẫn**: `/api/v1/reception/appointments`
- **Query Parameters**:
  - `tab`: `today` (Mặc định - Ưu tiên tiếp nhận hôm nay) | `pending` (Chờ duyệt) | `upcoming` (Tương lai) | `recent` (Vừa khám xong) | `history` (Toàn bộ)
  - `facilityId`: ID cơ sở y tế đang trực (long, tùy chọn)
  - `search`: Từ khóa tìm kiếm đa năng (MRN, CCCD, Mã hẹn, Họ tên, SĐT)
  - `page`: Số trang (mặc định: 1)
  - `pageSize`: Kích thước trang (mặc định: 20)
- **Response**:
```json
{
  "success": true,
  "data": {
    "items": [
      {
        "id": 101,
        "appointmentCode": "APT-20260914-0012",
        "patientId": 45,
        "patientName": "Nguyễn Văn An",
        "patientPhone": "0901234567",
        "medicalRecordNumber": "BN-2026-000045",
        "nationalId": "079199000111",
        "doctorId": 10,
        "doctorName": "BS. Lê Trọng Nghĩa",
        "specialtyId": 2,
        "specialtyName": "Tim mạch",
        "appointmentDate": "2026-09-14",
        "startTime": "08:30",
        "endTime": "09:00",
        "reason": "Khám kiểm tra huyết áp định kỳ",
        "status": "Confirmed",
        "patientVisitId": null
      }
    ],
    "totalCount": 1,
    "pageNumber": 1,
    "pageSize": 20,
    "totalPages": 1
  }
}
```

#### 3.2. Thống Kê Tổng Quan Lễ Tân Trong Ngày (Reception Stats)
- **Phương thức**: `GET`
- **Đường dẫn**: `/api/v1/reception/stats?facilityId={id}`
- **Response**:
```json
{
  "success": true,
  "data": {
    "appointmentsToday": 28,
    "pendingAppointmentsToday": 4,
    "confirmedAppointmentsToday": 15,
    "completedAppointmentsToday": 9
  }
}
```

---

### 4. Danh Mục Cấu Hình Quản Trị Hệ Thống (Administrative Configuration)

#### 4.1. Phân Công Nhân Sự Theo Cơ Sở (Staff Facility Assignments)
- **Lấy danh sách phân công**: `GET /api/v1/facilities/{facilityId}/assignments`
- **Tạo mới phân công**: `POST /api/v1/facilities/{facilityId}/assignments`
  ```json
  {
    "userId": 25,
    "isPrimary": true
  }
  ```
- **Xóa phân công**: `DELETE /api/v1/facilities/{facilityId}/assignments/{assignmentId}`

#### 4.2. Cập Nhật Đơn Giá Dịch Vụ Cận Lâm Sàng (Diagnostic Service Pricing)
- **Phương thức**: `PUT`
- **Đường dẫn**: `/api/v1/diagnostic-services/{id}/price`
- **Request Body**:
  ```json
  {
    "unitPrice": 250000.00
  }
  ```

#### 4.3. Cập Nhật Đơn Giá Bán Lẻ Thuốc (Medicine Unit Pricing)
- **Phương thức**: `PUT`
- **Đường dẫn**: `/api/v1/medicines/{id}/price`
- **Request Body**:
  ```json
  {
    "unitPrice": 45000.00
  }
  ```

---

### 5. Danh Mục Chu Trình Dược & Viện Phí (Pharmacy & Billing Safety)

#### 5.1. Xác Nhận Mua Thuốc & Giữ Tồn Kho (Confirm Pharmacy Purchase)
- **Phương thức**: `POST`
- **Đường dẫn**: `/api/v1/prescriptions/{id}/confirm-purchase`
- **Mô tả**: Kiểm tra tồn kho, trừ tồn khả dụng và chuyển đơn thuốc sang trạng thái `InBilling`.

#### 5.2. Cấp Phát Thuốc Có Kiểm Tra Thanh Toán (Dispense Prescription)
- **Phương thức**: `POST`
- **Đường dẫn**: `/api/v1/prescriptions/{id}/dispense`
- **Chốt chặn an toàn**: Trả về `PRESCRIPTION_NOT_PAID` (HTTP 400) nếu đơn thuốc chưa có hóa đơn ở trạng thái `Paid`.

#### 5.3. Tạo Hóa Đơn Bổ Sung Lượt Khám (Create Visit Supplementary Invoice)
- **Phương thức**: `POST`
- **Đường dẫn**: `/api/v1/billing/reception/invoices/from-visit`
- **Request Body**:
  ```json
  {
    "patientVisitId": 501
  }
  ```
- **Chốt chặn an toàn**:
  - Trả về `PENDING_INVOICE_EXISTS` nếu lượt khám có hóa đơn trước đó chưa thanh toán.
  - Áp dụng Filtered Unique Index `IX_Invoices_ReferenceType_ReferenceId_Active` chống thu trùng.

---

### 6. Bảng Tra Cứu Toàn Bộ Mã Lỗi Hệ Thống (Error Code Catalog)

| Mã Lỗi Hệ Thống | HTTP Status | Ý Nghĩa Nghiệp Vụ | Giải Pháp Khắc Phục |
|:---|:---:|:---|:---|
| `IDEMPOTENCY_KEY_REUSED_WITH_DIFFERENT_PAYLOAD` | 409 Conflict | Client gửi lại cùng một Idempotency-Key nhưng payload dữ liệu khác lần trước | Sinh UUID mới cho mỗi lượt tiếp nhận người bệnh khác nhau |
| `FACILITY_REQUIRED` | 400 Bad Request | Thiếu ID cơ sở y tế khi tiếp nhận hoặc truy vấn | Bắt buộc chọn cơ sở trực trên giao diện tiếp tân |
| `ACCESS_DENIED_TO_FACILITY_RESOURCE` | 403 Forbidden | Nhân viên không được phân công làm việc tại cơ sở này | Quản trị viên phân công cơ sở trong mục Quản lý Cơ sở |
| `PRESCRIPTION_NOT_PAID` | 400 Bad Request | Đơn thuốc chưa được thanh toán tại quầy thu ngân viện phí | Người bệnh phải hoàn tất đóng tiền tại thu ngân trước khi nhận thuốc |
| `PENDING_INVOICE_EXISTS` | 400 Bad Request | Lượt khám đang có hóa đơn trước chưa thanh toán, không thể lập hóa đơn mới | Thu tiền hóa đơn cũ trước khi xuất hóa đơn bổ sung |
| `DOUBLE_BILLING_PREVENTED` | 409 Conflict | Đơn thuốc hoặc chỉ định cận lâm sàng đã có hóa đơn hiệu lực | Kiểm tra lịch sử thu ngân, không tạo trùng hóa đơn |
| `STOCK_RESERVATION_FAILED` | 400 Bad Request | Tồn kho thuốc khả dụng không đủ đáp ứng số lượng kê đơn | Kiểm tra lại kho dược hoặc điều chỉnh số lượng |
