# Hướng dẫn UI

Áp dụng cho code UI mới và phần được giao sửa. Không tự refactor màn hình ngoài phạm vi. Quy tắc gốc: [AGENTS.md](../AGENTS.md); tìm file theo [bản đồ tính năng](feature-map.md).

- Dùng antd và token `--cc-*` trong [index.css](../src/frontend/src/index.css). Không mã màu hex; không thêm `style={{}}` mới. Đặt CSS trong file của tính năng.
- Code mới không dùng `Row`, `Col`, `Grid`, `Tabs`, `List`, `DatePicker`. Ô ngày dùng `<Input type="date">`; bố cục bằng CSS.
- Nút đang bận dùng `disabled` và đổi chữ theo thao tác; không dùng prop `loading`.
- Tách riêng đang tải, lỗi và dữ liệu rỗng. Lỗi dùng [InlineError](../src/frontend/src/components/common/InlineError.tsx) có nút thử lại; không hiển thị lỗi như danh sách rỗng.
- `Alert` dùng `title`.
- Ngày hiển thị `dd/MM/yyyy` qua `formatDateOnly` trong [formatters.ts](../src/frontend/src/utils/formatters.ts). Giá trị ô ngày vẫn theo định dạng input `yyyy-MM-dd`.
- Lỗi API lấy thông điệp qua `getApiErrorMessage` trong [apiError.ts](../src/frontend/src/utils/apiError.ts).
- Code mới nằm đúng thư mục tính năng; file đã quá 1.000 dòng không nhận thêm logic, phải tách file/lớp riêng. Chỉ comment khi giải thích lý do không hiển nhiên.
