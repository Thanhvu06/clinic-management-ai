# Quy tắc hết hạn

`ExpiryWorker` chạy ngay khi backend khởi động, sau đó chờ `ExpiryRules:IntervalMinutes` (mặc định 10 phút) giữa các lượt. Mỗi quy tắc cập nhật tối đa `BatchSize` (100) bản ghi trong một giao dịch Serializable, lặp đến khi hết dữ liệu. Mỗi quy tắc có scope riêng; lỗi một quy tắc được ghi log và không chặn quy tắc sau. Reset demo yêu cầu tất cả quy tắc chạy thành công.

Mọi mốc nghiệp vụ dùng `IDateTimeProvider` theo giờ Việt Nam. Mốc bằng hạn đã được tính là đến hạn. Quy tắc theo ngày được áp dụng lúc 00:00 ngày kế tiếp; tái khám đến hạn lúc 00:00 của ngày đề xuất cộng 7 ngày.

| Quy tắc | Mốc | Kết quả / nhãn |
| --- | --- | --- |
| Lịch Pending | Giờ bắt đầu | Cancelled; Quá hạn xác nhận |
| Lịch Confirmed chưa có lượt khám | Giờ bắt đầu + 30 phút | NoShow; Vắng mặt |
| Khóa online | 3 NoShow trong 90 ngày | Khóa 30 ngày từ lần đạt ngưỡng; vẫn tiếp nhận tại lễ tân |
| Lượt Registered / CheckedIn / WaitingForDoctor | Hết ngày tiếp nhận | NoShow; Bỏ về |
| Lượt InConsultation | Qua ngày | Giữ trạng thái; Quá hạn hoàn tất |
| CLS Ordered chưa bắt đầu/lấy mẫu/có kết quả | 24 giờ | Cancelled; Quá hạn thực hiện |
| Đơn Issued | 5 ngày từ ngày kê | Expired; Hết hạn lĩnh thuốc / Hết hạn, cần kê lại |
| Hóa đơn Unpaid | 3 ngày | Giữ trạng thái; Quá hạn |
| Đăng ký gói Pending | Qua ngày mong muốn | Cancelled; Quá hạn xác nhận |
| Yêu cầu đổi/hủy Pending | Giờ bắt đầu lịch gốc | Expired; Hết hạn xử lý; khôi phục lịch gốc rồi áp quy tắc lịch hẹn |
| Tái khám PendingPatientResponse | Ngày đề xuất + 7 ngày | Expired; Hết hạn |
| Nghỉ phép Pending | Qua ngày bắt đầu nghỉ | Expired; Hết hạn |

Các mốc cấu hình nằm trong `ClinicManagement.Api/appsettings.json`, mục `ExpiryRules`: `AppointmentGraceMinutes`, `DiagnosticHours`, `PrescriptionDays`, `InvoiceOverdueDays`, `RevisitDays`, `NoShowThreshold`, `NoShowWindowDays`, `OnlineBookingBlockDays`. Hệ thống kiểm tra giá trị hợp lệ khi khởi động.

Khóa online được tính từ lịch sử NoShow; dữ liệu cũ chưa có lịch sử dùng giờ hẹn cộng thời gian chờ. NoShow tiếp theo trong thời gian khóa không kéo dài thời hạn của lần khóa đó. Sau khi khóa kết thúc, một lần NoShow mới đạt ngưỡng có thể bắt đầu đợt khóa mới. API đặt lịch và chấp nhận tái khám cùng kiểm tra điều kiện này. API lĩnh/giữ thuốc kiểm tra hạn ngay trong giao dịch, không cần chờ worker và không thay đổi tồn kho khi từ chối.

Trạng thái, lịch sử, audit và thông báo được lưu chung giao dịch. Audit hệ thống có `UserId = null`, mô tả tiếng Việt chứa mã nghiệp vụ. Migration duy nhất `AllowSystemExpiryActors` cho phép hai trường người thực hiện hiện có nhận null: `SystemAuditLogs.UserId` và `AppointmentHistory.PerformedByUserId`; không thêm cột hạn. Khôi phục lịch gốc và hết hạn lịch gốc là hai chuyển trạng thái, có hai lịch sử/audit tương ứng.

`ExpiryDemoSeed` bổ sung ví dụ quá hạn cho từng quy tắc và dùng marker để không tạo trùng. `--reset-demo` chạy worker sau seed với đồng hồ thực; quy ước demo Chủ nhật neo dữ liệu hôm nay sang thứ Hai được giữ nguyên.
