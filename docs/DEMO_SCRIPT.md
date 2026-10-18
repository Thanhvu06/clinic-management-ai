# Kịch bản demo theo vai trò

Dùng môi trường Development và dữ liệu thử. Tài khoản/mật khẩu đối chiếu [danh sách demo](DEVELOPMENT_DEMO_ACCOUNTS.md) và [DevelopmentDataSeeder.cs](../src/backend/ClinicManagement.Infrastructure/Persistence/DevelopmentDataSeeder.cs). Mật khẩu chung: `Demo@12345`.

## Chuẩn bị

1. Bật `DemoSeed:Enabled` trong [cấu hình Development](../src/backend/ClinicManagement.Api/appsettings.Development.json). Nếu cần lịch trống mẫu, bật `DemoSeed:GenerateSlots`; muốn lịch hẹn mẫu thì bật thêm `DemoSeed:GenerateAppointments`. Seed cơ bản không bảo đảm có sẵn slot/lịch hẹn cho ngày demo.
2. Chạy `dotnet run --project src/backend/ClinicManagement.Api`; tại `src/frontend` chạy `npm run dev`.
3. Với Admin, kiểm tra cơ sở, phân công nhân viên, lịch làm việc bác sĩ và sinh slot cho một ngày tương lai không phải Chủ nhật. Dùng cùng cơ sở cho tiếp nhận, khám, CLS và thu ngân. Không cố định ID hoặc số lượng dữ liệu mẫu.

## Admin — admin@cliniccare.local

- Mở quản lý người dùng, bác sĩ/chuyên khoa, cơ sở và phân công nhân viên; xác nhận dữ liệu hoạt động.
- Tạo lịch làm việc và sinh slot; xem danh sách nghỉ phép, thử xem/duyệt một yêu cầu trên dữ liệu riêng nếu cần minh họa chặn lịch trống.
- Xem danh mục thuốc, nhóm thuốc, gói khám; mở báo cáo thu tiền và nhật ký kiểm toán.

## Patient — patient@cliniccare.local

- Đăng nhập, kiểm tra hồ sơ; xem thuốc công khai, chuyên khoa/bác sĩ và gói khám.
- Đặt lịch theo chuyên khoa → bác sĩ → ngày → slot trống → lý do. Ghi mã lịch hẹn để lễ tân tra cứu.
- Trong lịch của tôi, gửi yêu cầu đổi/hủy một lịch thử riêng; để một lịch còn hiệu lực cho chuỗi khám.
- Sau khi khám, xem đơn thuốc, hóa đơn và kết quả CLS đã công bố. Nếu bác sĩ đề xuất tái khám, mở đề xuất và chọn slot phù hợp để chấp nhận.

## Receptionist — reception@cliniccare.local

- Chọn đúng cơ sở; tìm lịch vừa đặt, xác nhận rồi check-in. Kiểm tra phiếu khám, MRN, số thứ tự và hàng đợi khoa.
- Minh họa tiếp nhận vãng lai: tìm MPI trước; nếu chưa có thì tạo hồ sơ, chọn khoa/phòng/bác sĩ phù hợp và cấp phiếu. Ghi dị ứng bằng dữ liệu thử để demo kiểm tra kê đơn.
- Duyệt/từ chối yêu cầu đổi/hủy ở lịch thử riêng. Xem đăng ký gói khám nếu có.
- Tại thu ngân, lập hóa đơn từ lượt khám hoặc nguồn tương ứng, thu tiền và xem chi tiết. Sau khi nhà thuốc xác nhận mua, bảo đảm hóa đơn chứa đủ số lượng từng thuốc trước cấp phát.

## Doctor — doctor@cliniccare.local

- Mở hàng đợi tại cơ sở được phân công; chọn bệnh nhân vừa check-in và bắt đầu khám.
- Nhập sinh hiệu, xem BMI/lịch sử/chênh lệch nếu có dữ liệu trước; lưu bệnh án với mã ICD-10 đang hoạt động.
- Tạo chỉ định CLS và in phiếu. Giữ ca khám mở để KTV thực hiện.
- Lập đơn nháp; chạy kiểm tra an toàn thuốc. Nếu tạo được cảnh báo Danger từ dữ liệu dị ứng/tương tác hiện có, minh họa lý do vẫn kê ít nhất 10 ký tự khi phát hành đơn.
- Sau khi CLS hoàn tất và được duyệt nếu cần, xác nhận đã xem kết quả rồi hoàn tất khám. Có thể đề xuất tái khám cho lịch hẹn phù hợp.

## DiagnosticTechnician — technician.02@cliniccare.local

- Mở hàng đợi CLS trong cơ sở được phân công, chọn phiếu mới và bắt đầu thực hiện.
- Với xét nghiệm cần mẫu, ghi lấy/nhận mẫu; nhập đủ kết quả theo tham số. Với dịch vụ phù hợp, thêm tệp kết quả.
- Bấm hoàn tất: phiếu có dịch vụ chưa hủy với `SpecimenType` khác null được gửi sang hàng đợi duyệt. Người nhập không tự duyệt phiếu này.
- Delta check chỉ xuất hiện khi có kết quả trước phù hợp và vượt ngưỡng cấu hình; không giả định seed đã tạo sẵn tình huống đó.

## LabSupervisor — labsupervisor@cliniccare.local

- Mở hàng đợi duyệt tại cơ sở được phân công; xem phiếu do KTV nhập.
- Duyệt hoặc trả lại kèm lý do ít nhất 5 ký tự. Nếu có delta failure, ghi chú kiểm tra ít nhất 5 ký tự khi duyệt.
- Mở báo cáo TAT với khoảng tối đa 31 ngày tính cả hai đầu; đối chiếu dữ liệu sau khi phiếu hoàn tất.
- Quay lại tài khoản bác sĩ xác nhận xem; sau đó kiểm tra bệnh nhân thấy kết quả. Duyệt xét nghiệm riêng chưa đủ để công bố cho bệnh nhân.

## Pharmacist — pharmacist@cliniccare.local

- Xem tổng quan, tồn dùng được và các lô. Chọn thuốc đủ tồn còn hạn cho ca demo.
- Mở đơn chờ, xác nhận mua để giữ thuốc; cùng lễ tân thanh toán đủ từng thuốc.
- Cấp phát, kiểm tra trạng thái đơn và giao dịch kho theo lô. Lô hết hạn không được cấp phát/giữ chỗ; thứ tự lô theo FEFO.

Đường dẫn màn hình, API và test đối chiếu tại [bản đồ tính năng](feature-map.md); điều kiện xử lý tại [luật nghiệp vụ](specifications/business-rules.md). Demo trợ lý AI nằm ngoài phạm vi tài liệu này.
