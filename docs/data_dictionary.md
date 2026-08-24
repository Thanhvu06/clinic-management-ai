# Từ điển Dữ liệu (Data Dictionary) - ClinicCare AI

*Lưu ý: Để đảm bảo an toàn cho dự án, toàn bộ Database vật lý giữ nguyên tên tiếng Anh để khớp với Entity Framework Core Migrations, tránh gây mất dữ liệu thật (Data Loss) do phải DROP/CREATE bảng. Tài liệu này cung cấp ánh xạ song ngữ để phục vụ việc thuyết trình và tra cứu.*

## 1. Tài khoản và phân quyền

### `AspNetUsers` (Tài khoản người dùng)
Mục đích: Lưu trữ thông tin đăng nhập và cơ bản của mọi tài khoản trong hệ thống.
* **Id** (int) `PK`: Mã định danh người dùng.
* **FullName** (nvarchar): Họ tên đầy đủ.
* **Email** (nvarchar): Địa chỉ email (dùng để đăng nhập).
* **PhoneNumber** (nvarchar): Số điện thoại.
* **PasswordHash** (nvarchar): Mật khẩu đã được mã hóa.
* **IsActive** (bit): Trạng thái hoạt động.
* **CreatedAt / UpdatedAt** (datetime): Thời gian tạo và cập nhật.

### `AspNetRoles` (Vai trò)
Mục đích: Lưu trữ danh mục các vai trò trong hệ thống (Patient, Doctor, Receptionist, Admin).
* **Id** (int) `PK`: Mã định danh vai trò.
* **Name** (nvarchar): Tên vai trò.
* **NormalizedName** (nvarchar): Tên chuẩn hóa để tìm kiếm.

### `AspNetUserRoles` (Phân quyền người dùng)
Mục đích: Bảng trung gian thể hiện quan hệ nhiều-nhiều giữa `AspNetUsers` và `AspNetRoles`.
* **UserId** (int) `PK, FK` -> `AspNetUsers(Id)`: Mã người dùng.
* **RoleId** (int) `PK, FK` -> `AspNetRoles(Id)`: Mã vai trò.

---

## 2. Bệnh nhân, bác sĩ và chuyên khoa

### `Patients` (Bệnh nhân)
Mục đích: Lưu trữ hồ sơ y tế cơ bản của bệnh nhân. Quan hệ 1-1 với `AspNetUsers`.
* **Id** (int) `PK`: Mã hồ sơ bệnh nhân.
* **UserId** (int) `FK, UNIQUE` -> `AspNetUsers(Id)`: Mã tài khoản.
* **Gender** (varchar): Giới tính (Male, Female, Other).
* **DateOfBirth** (date): Ngày sinh.
* **Address** (nvarchar): Địa chỉ.

### `Doctors` (Bác sĩ)
Mục đích: Lưu trữ hồ sơ chuyên môn của bác sĩ. Quan hệ 1-1 với `AspNetUsers`.
* **Id** (int) `PK`: Mã hồ sơ bác sĩ.
* **UserId** (int) `FK, UNIQUE` -> `AspNetUsers(Id)`: Mã tài khoản.
* **AcademicTitle** (nvarchar): Học hàm, học vị (BS, TS, ThS).
* **ExperienceYears** (int): Số năm kinh nghiệm.
* **Description** (nvarchar): Giới thiệu chuyên môn.
* **IsActive** (bit): Trạng thái hoạt động.

### `Specialties` (Chuyên khoa)
Mục đích: Lưu trữ danh mục chuyên khoa khám bệnh.
* **Id** (int) `PK`: Mã chuyên khoa.
* **SpecialtyCode** (varchar) `UNIQUE`: Mã tra cứu nội bộ.
* **Name** (nvarchar): Tên chuyên khoa.
* **Description** (nvarchar): Mô tả.
* **AiEnabled** (bit): Cờ đánh dấu chuyên khoa này có được phép cho AI gợi ý hay không.

### `DoctorSpecialties` (Phân bổ Bác sĩ & Chuyên khoa)
Mục đích: Bảng trung gian thể hiện quan hệ nhiều-nhiều. Một bác sĩ có thể thuộc nhiều chuyên khoa.
* **DoctorId** (int) `PK, FK` -> `Doctors(Id)`
* **SpecialtyId** (int) `PK, FK` -> `Specialties(Id)`
* **IsPrimary** (bit): Đánh dấu đây là chuyên khoa chính của bác sĩ.

---

## 3. Lịch làm việc và khung giờ

### `DoctorWorkSchedules` (Lịch làm việc bác sĩ)
Mục đích: Đăng ký ca làm việc tổng quát theo ngày của bác sĩ.
* **Id** (int) `PK`: Mã lịch làm việc.
* **DoctorId** (int) `FK` -> `Doctors(Id)`
* **WorkDate** (date): Ngày làm việc.
* **StartTime / EndTime** (time): Giờ bắt đầu và kết thúc ca.
* **IsActive** (bit): Có hiệu lực.

### `AppointmentSlots` (Khung giờ khám)
Mục đích: Chia nhỏ lịch làm việc thành các slot cụ thể để bệnh nhân đặt (ví dụ mỗi slot 30 phút).
* **Id** (int) `PK`: Mã khung giờ.
* **DoctorId** (int) `FK` -> `Doctors(Id)`
* **SlotDate** (date): Ngày khám.
* **StartTime / EndTime** (time): Khung giờ (VD: 08:00 - 08:30).
* **IsBooked** (bit): Đã có người đặt chưa.

### `DoctorLeaveRequests` (Yêu cầu nghỉ phép)
Mục đích: Bác sĩ xin nghỉ đột xuất hoặc có kế hoạch.
* **Id** (int) `PK`: Mã đơn nghỉ.
* **DoctorId** (int) `FK` -> `Doctors(Id)`
* **StartDateTime / EndDateTime** (datetime): Bắt đầu và kết thúc khoảng thời gian nghỉ.
* **Reason** (nvarchar): Lý do nghỉ.
* **Status** (varchar): Trạng thái duyệt (Pending, Approved, Rejected).
* **AdminNote** (nvarchar): Ghi chú của quản trị viên khi duyệt.

---

## 4. Lịch hẹn và lịch sử

### `Appointments` (Lịch hẹn)
Mục đích: Lưu trữ giao dịch khám bệnh giữa bệnh nhân và bác sĩ. Quan hệ 1-1 với `AppointmentSlots`.
* **Id** (int) `PK`: Mã quản lý lịch hẹn.
* **AppointmentCode** (varchar) `UNIQUE`: Mã giao tiếp với khách hàng (VD: APP-1234).
* **PatientId** (int) `FK` -> `Patients(Id)`
* **DoctorId** (int) `FK` -> `Doctors(Id)`
* **SpecialtyId** (int) `FK` -> `Specialties(Id)`
* **AppointmentSlotId** (int) `FK` -> `AppointmentSlots(Id)`
* **AppointmentDate** (date): Ngày khám.
* **StartTime / EndTime** (time): Giờ khám.
* **Reason** (nvarchar): Triệu chứng / Lý do đi khám.
* **Status** (int): Trạng thái hiện tại (Pending, Confirmed, Completed, Cancelled).

### `AppointmentChangeRequests` (Yêu cầu thay đổi lịch hẹn)
Mục đích: Lưu trữ yêu cầu đổi giờ khám hoặc hủy lịch từ cả 2 phía.
* **Id** (int) `PK`: Mã yêu cầu.
* **AppointmentId** (int) `FK` -> `Appointments(Id)`
* **RequestType** (int): Loại (Reschedule, Cancel).
* **RequestedSlotId** (int) `FK` -> `AppointmentSlots(Id)`: Slot mới nếu đổi lịch (Có thể NULL nếu là hủy).
* **Reason** (nvarchar): Lý do thay đổi.
* **Status** (int): Trạng thái xử lý.
* **RequestedByUserId** (int) `FK` -> `AspNetUsers(Id)`: Người tạo yêu cầu.
* **ProcessedByUserId** (int) `FK` -> `AspNetUsers(Id)`: Người duyệt (Có thể NULL).

### `AppointmentHistory` (Lịch sử lịch hẹn)
Mục đích: Dấu vết Audit để theo dõi vòng đời thay đổi trạng thái của Lịch hẹn.
* **Id** (int) `PK`: Mã lịch sử.
* **AppointmentId** (int) `FK` -> `Appointments(Id)`
* **Action** (varchar): Hành động (VD: Created, Cancelled, Completed).
* **OldStatus / NewStatus** (int): Thay đổi từ trạng thái cũ sang mới.
* **Note** (nvarchar): Ghi chú.
* **PerformedByUserId** (int) `FK` -> `AspNetUsers(Id)`: Ai thực hiện thao tác.

---

## 5. Kết quả khám và tái khám

### `VisitSummaries` (Tóm tắt buổi khám)
Mục đích: Hồ sơ y tế, chẩn đoán, lời dặn bác sĩ sau khi khám xong. Quan hệ 1-1 với `Appointments`.
* **Id** (int) `PK`: Mã kết quả khám.
* **AppointmentId** (int) `FK` -> `Appointments(Id)`
* **DoctorId** (int) `FK` -> `Doctors(Id)`
* **Summary** (nvarchar): Tóm tắt buổi khám.
* **FollowUpInstruction** (nvarchar): Hướng dẫn theo dõi sau khám/tái khám.

### `RevisitRequests` (Đề xuất/Yêu cầu tái khám)
Mục đích: Bác sĩ chỉ định hoặc hẹn bệnh nhân quay lại khám định kỳ.
* **Id** (int) `PK`: Mã đề xuất.
* **AppointmentId** (int) `FK` -> `Appointments(Id)`: Lịch hẹn gốc sinh ra đề xuất.
* **PatientId** (int) `FK` -> `Patients(Id)`
* **DoctorId** (int) `FK` -> `Doctors(Id)`
* **SuggestedDate** (date): Ngày đề xuất tái khám.
* **Note** (nvarchar): Lời dặn dò tái khám.
* **Status** (int): Trạng thái (Pending, Booked, Ignored).
* **NewAppointmentId** (int) `FK` -> `Appointments(Id)`: Trỏ tới lịch hẹn mới sau khi bệnh nhân đồng ý đặt lại (Có thể NULL).

---

## 6. Gợi ý chuyên khoa bằng AI

### `AiSuggestionLogs` (Nhật ký tư vấn AI)
Mục đích: Lưu vết toàn bộ luồng tương tác AI hỗ trợ đặt lịch.
*Lưu ý: AI chỉ gợi ý chuyên khoa tham khảo; không chẩn đoán, không kê đơn, không chọn bác sĩ và không quyết định lịch khám.*
* **Id** (int) `PK`: Mã nhật ký.
* **PatientId** (int) `FK` -> `Patients(Id)`
* **InputText** (nvarchar): Mô tả triệu chứng hoặc nhu cầu khám do bệnh nhân nhập.
* **SuggestedSpecialtiesJson** (nvarchar): Chuỗi JSON danh sách các chuyên khoa mà AI phân tích.
* **SelectedSpecialtyId** (int) `FK` -> `Specialties(Id)`: Chuyên khoa mà bệnh nhân cuối cùng lựa chọn (Có thể NULL).
* **Provider** (varchar): Tên Provider (VD: Gemini, OpenAI).
* **PromptVersion** (varchar): Phiên bản prompt để audit chất lượng AI.

---

## 7. Nhà thuốc (Pharmacy)
*Lưu ý: Migration đã tồn tại nhưng các API và UI cho module Nhà thuốc chưa hoàn thiện ở giai đoạn này.*

### `Medicines` (Thuốc)
Mục đích: Danh mục thuốc trong kho phòng khám.
* **Id** (bigint) `PK`: Mã thuốc.
* **Code** (nvarchar): Mã số lô/Mã nội bộ.
* **Name** (nvarchar): Tên thuốc.
* **Unit** (nvarchar): Đơn vị tính (Viên, Lọ).
* **StockQuantity** (int): Số lượng hiện có trong kho.
* **ReorderLevel** (int): Mức cảnh báo sắp hết hàng.
* **IsActive** (bit): Có đang kinh doanh không.

### `Prescriptions` (Đơn thuốc)
Mục đích: Đơn thuốc điện tử do bác sĩ kê.
* **Id** (bigint) `PK`: Mã đơn thuốc.
* **AppointmentId** (bigint) `FK` -> `Appointments(Id)`: Kê cho lịch hẹn nào.
* **PatientId** (bigint) `FK` -> `Patients(Id)`
* **DoctorId** (bigint) `FK` -> `Doctors(Id)`
* **Status** (int): Trạng thái (Pending, Dispensed, Cancelled).
* **Notes** (nvarchar): Lời dặn uống thuốc chung.
* **DispensedAt** (datetime): Giờ phát thuốc tại quầy.
* **DispensedByUserId** (uniqueidentifier) `FK` -> `AspNetUsers(Id)`: Người phát thuốc (Dược sĩ).

### `PrescriptionItems` (Chi tiết đơn thuốc)
Mục đích: Từng loại thuốc trong đơn.
* **Id** (bigint) `PK`: Mã dòng.
* **PrescriptionId** (bigint) `FK` -> `Prescriptions(Id)`
* **MedicineId** (bigint) `FK` -> `Medicines(Id)`
* **Quantity** (int): Số lượng.
* **DosageInstruction** (nvarchar): Hướng dẫn sử dụng (VD: Ngày 2 lần, mỗi lần 1 viên sau ăn).
* **UnitPrice / TotalPrice** (decimal): Đơn giá và Thành tiền.

### `MedicineStockTransactions` (Giao dịch xuất/nhập kho)
Mục đích: Lưu vết toàn bộ biến động kho.
* **Id** (bigint) `PK`: Mã giao dịch.
* **MedicineId** (bigint) `FK` -> `Medicines(Id)`
* **TransactionType** (int): Loại giao dịch (In, Out, Adjust).
* **Quantity** (int): Số lượng biến động.
* **ReferenceId** (nvarchar): Mã tham chiếu (VD: ID Đơn thuốc).
* **CreatedByUserId** (uniqueidentifier): Người thực hiện (Dược sĩ/Quản kho).
