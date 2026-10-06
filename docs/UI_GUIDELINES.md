# Hướng dẫn giao diện (UI Guidelines) – ClinicCare AI Frontend

Tài liệu này áp dụng cho mọi thay đổi giao diện trong `src/frontend`. Mục tiêu: một hệ thống thiết kế duy nhất dựa trên **Ant Design (antd)** và **design tokens khóa cứng**, đổi giao diện từng trang mà không làm vỡ E2E.

## 1. Nguồn sự thật: design tokens

- File duy nhất: `src/frontend/src/theme/tokens.ts`.
- Gồm: màu (`colors`), khoảng cách (`spacing` 4/8/12/16/24/32), bo góc (`radius`), kích thước bố cục (`layout`: control 40, dòng bảng 44, sidebar 240, header 56, content padding 24, gap 8), chữ (`typography`), đổ bóng (`shadows`).
- antd đọc token qua `antdTheme` (bọc toàn app bằng `AppThemeProvider` trong `main.tsx`, locale `vi_VN`).
- CSS đọc token qua biến `--cc-*` (do `applyTokenCssVariables()` ghi lên `:root` khi khởi động).
- Các biến cũ trong `index.css` (`--c-primary`, `--space-4`, `--radius-md`, …) **chỉ là alias** trỏ về `--cc-*`. Không xóa, không gán giá trị cứng mới cho chúng.

Quy tắc:

| Được | Không được |
| --- | --- |
| Sửa/thêm token trong `tokens.ts` | Viết mã màu hex / px "tự chế" mới trong trang |
| Dùng `var(--cc-...)` hoặc alias cũ trong CSS Module | Thêm biến `:root` mới có giá trị cứng trong `index.css` |
| Đọc token trong TS: `import { layout } from '../theme/tokens'` | Lặp lại giá trị token bằng số literal |

## 2. Dùng antd, không tự vẽ lại

- **Cấm** tự vẽ lại các control đã có trong antd: `Button`, `Select`, `Modal`, `Table`, `Input`, `DatePicker`, `Tabs`, `Tag`, `Alert`, `Pagination`… Code mới dùng component antd.
- Bảng: dùng `DataTable` (`src/components/common/DataTable.tsx`, bọc antd `Table` có viền, dòng 44px). Không viết `<table>` thủ công mới.
- Các component chung (`src/components/common`): `PageHeader`, `StatCard`, `StatusBadge`, `FilterBar`, `DataTable`, `EmptyState`, `LoadingState`, `InlineError`, `Pagination`. Props công khai của chúng là hợp đồng ổn định – chỉ được **thêm** prop tùy chọn, không đổi/xóa prop cũ.
- Khung nhân viên: `MainLayout` (antd `Layout` + `Sider` + `Header` + `Content` + `Menu`). Thêm mục menu bằng cách thêm vào `NAV_BY_ROLE`, giữ thứ tự và nhãn hiện có.
- Icon: tiếp tục dùng `lucide-react`.

## 3. Style

- **Cấm thêm `style={{...}}` inline mới.** Dùng CSS Module (`*.module.css`) với biến token, hoặc prop của antd (`type`, `size`, `danger`, `align`, `gap`…).
- Ngoại lệ duy nhất: giá trị runtime đến từ dữ liệu/props mà không biểu diễn được bằng class (ví dụ `LoadingState` nhận `height`). Phải có comment giải thích ngay tại chỗ.
- Muốn chỉnh style nội bộ của antd trong một trang: dùng `:global(.ant-...)` lồng dưới class của CSS Module, không ghi đè toàn cục.
- Khoảng 3000 chỗ `style={{}}` cũ **chưa** di trú. Khi sửa một trang, chỉ di trú những dòng mình chạm tới.

## 4. Câu chữ hiển thị

- Viết tiếng Việt tự nhiên, hướng tới bệnh nhân/nhân viên, không lộ thuật ngữ kỹ thuật.
- Không hiển thị: `(Dữ liệu demo)`, `(Dữ liệu minh họa)`, "… chưa được cấu hình trong hệ thống", `SessionId`, `DraftId`, mã lỗi thô, tên tool nội bộ.
- Thiếu dữ liệu cấu hình (ví dụ số điện thoại lễ tân) thì **ẩn cả khối** thay vì hiện câu báo lỗi.

## 5. Quy trình đổi giao diện một trang

1. Mỗi lần chỉ đổi **một trang hoặc một component**. Không viết lại hàng loạt cấu trúc HTML.
2. Trước khi sửa, liệt kê những gì test/E2E đang bám vào:
   - nhãn nút và text hiển thị (`getByRole('button', { name })`, `getByText`),
   - `aria-label`, `title`, `data-testid`, `data-*` (ví dụ `data-suggestion-strip`, `data-role`),
   - route (`/reception/billing`, …).
   Tra trong `src/frontend/src/test/**` và `scripts/e2e/browser-acceptance.mjs`.
3. Đổi sang antd + token, **giữ nguyên** mọi nhãn, `aria-label`, `title`, `data-testid` và route ở bước 2.
4. Chạy: `npm run lint`, `npm run build`, `npm run test` (số test pass không được giảm), rồi `npm run e2e`.
5. Nếu test vỡ vì cấu trúc DOM thay đổi: sửa **code** cho khớp hành vi test, không sửa test để qua.
6. Chụp màn hình trang trước/sau ở desktop (1280×800) và mobile (390×844) và đính vào PR.

## 6. Lưu ý kỹ thuật

- antd v6 dùng `window.matchMedia` (Table/Grid). `src/theme/browserCompat.ts` cung cấp fallback cho môi trường thiếu API (jsdom); `DataTable` đã import sẵn.
- Không bật `scroll` của antd `Table` (cần `ResizeObserver`, jsdom không có). Bảng rộng đã tự cuộn ngang bằng CSS trong `DataTable`.
- Không dùng `breakpoint` của `Layout.Sider`; responsive của khung nhân viên làm bằng CSS media query (≤ 768px: sidebar trượt, header mobile).
