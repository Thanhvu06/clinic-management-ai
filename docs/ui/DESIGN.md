# Quy tắc thiết kế giao diện

Bàn làm việc theo phong cách dashboard của Stripe, Linear, GitHub: chữ và số là chính, ít trang trí, viền mảnh thay bóng, một màu nhấn. Quy tắc kỹ thuật chung (antd, token `--cc-*`, trạng thái tải/lỗi/rỗng) xem [AGENTS.md](../../AGENTS.md) và [UI_GUIDELINES.md](../UI_GUIDELINES.md).

## Ngôn ngữ và số liệu

- Số thập phân dùng dấu phẩy, ví dụ 37,2 và 22,5.
- Dùng “Sinh hiệu” và “trợ lý”; không dùng “Copilot” ở chữ hiển thị mới.
- Không chèn từ tiếng Anh trong ngoặc sau nhãn tiếng Việt.
- Không dùng tagline hoặc câu quảng cáo trong giao diện nội bộ và bản in.

## Chữ

- Viết hoa chữ đầu câu cho mọi tiêu đề, nhãn, nút. Danh từ chung (bác sĩ, dược sĩ, khung giờ) viết thường.
- Không in hoa toàn bộ, kể cả bằng CSS `text-transform: uppercase`.
- Không emoji, không icon trang trí cạnh tiêu đề.
- Không lẫn tiếng Anh khi đã có từ tiếng Việt thông dụng.

| Đúng | Sai |
| --- | --- |
| `Đơn chờ cấp` | `ĐƠN THUỐC CHỜ CẤP` |
| `Bàn làm việc bác sĩ` | `Bàn Làm Việc Bác Sĩ`, `🩺 Bàn làm việc` |
| `Lịch trực & khung giờ` | `Lịch trực & Slots` |

## Câu mô tả

Không đặt câu mô tả dưới tiêu đề, trừ khi nó mang thông tin không có ở chỗ khác.

- Đúng: `Ca sáng 07:00–11:30 · Phòng 204` dưới tiêu đề bàn làm việc bác sĩ.
- Sai: `Trung tâm kiểm soát toàn bộ phòng khám` dưới `Tổng quan`.

## Card

- Viền 1px `--cc-color-border`, bo góc 8px, không `box-shadow`, không hiệu ứng nhấc khi hover.
- Không gradient, không viền màu một cạnh (`border-left: 4px solid ...`), không nền màu cho cả khối.

```css
/* Đúng */
.panel { border: 1px solid var(--cc-color-border); border-radius: var(--cc-radius-md); }

/* Sai */
.panel { box-shadow: var(--cc-shadow-md); border-left: 4px solid var(--cc-color-info); }
.panel:hover { transform: translateY(-2px); }
```

## Màu

- Một màu nhấn (`--cc-color-primary`) cho nút chính và đường dẫn chính.
- Màu cảnh báo/nguy hiểm chỉ dùng khi dữ liệu thật sự cần xử lý (ví dụ số đơn chờ cấp > 0).
- Không nền pastel cho icon.

## Số liệu

- Hiển thị số liệu tổng hợp bằng `MetricStrip` (`components/common/MetricStrip.tsx`); số dùng `font-variant-numeric: tabular-nums`.
- Ô cần xử lý dùng `tone="attention"`: chữ giá trị chuyển màu cảnh báo chỉ khi giá trị > 0.
- Ô dẫn tới trang chi tiết dùng `to`, không bọc thêm thẻ.

```tsx
// Đúng
<MetricStrip items={[
    { label: 'Đơn chờ cấp', value: 3, tone: 'attention', to: '/pharmacy/prescriptions' },
    { label: 'Đã cấp hôm nay', value: 12 },
]} />

// Sai: icon nền pastel, nhãn in hoa, màu theo từng ô
<StatCard title="ĐƠN THUỐC CHỜ CẤP" value={3} icon={<Clock />} color="warning" />
```

## Icon

Icon chỉ dùng trong nút, menu và trạng thái. Không dùng icon để trang trí ô số liệu hay tiêu đề khối.

## Điều hướng

Không lặp lại menu sidebar dưới dạng lưới thẻ trên trang. Hành động chính của trang đặt ở `actions` của `PageHeader`.

- Đúng: nút `Tiếp nhận người bệnh`, `Thu ngân` trong phần actions.
- Sai: 8 thẻ "Quản lý dữ liệu & Danh mục" trỏ tới đúng 8 mục đã có ở sidebar.

## Danh sách

Ưu tiên bảng dày thông tin (`DataTable`, hàng 44px) thay cho lưới thẻ. Khối xem nhanh trên bàn làm việc giới hạn 5–8 dòng và có đường dẫn tới trang đầy đủ.
