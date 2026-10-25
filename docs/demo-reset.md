Chỉ dùng môi trường Development và DB có tên bắt đầu bằng `ClinicCareDemo`; lệnh xóa DB hiện tại rồi migrate và seed theo ngày Việt Nam lúc chạy.
Đặt `ConnectionStrings__ClinicManagementDb` trỏ tới DB demo cần reset, rồi chạy: `dotnet run --project src/backend/ClinicManagement.Api -- --reset-demo`.
Lệnh in server, tên DB và số bản ghi chính sau seed, rồi thoát; cấu hình kết nối và thông tin bí mật chỉ đặt trong môi trường local.
