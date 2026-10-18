# Quy tắc làm việc trong repo

## Phạm vi

- Chỉ làm đúng việc được giao; không refactor ngoài phạm vi, không tự thêm tính năng.
- Trước khi sửa code, đọc [bản đồ tính năng](docs/feature-map.md) để xác định đúng file.

## Vùng cấm

Chỉ sửa khi prompt mở khóa rõ ràng. Đường dẫn dưới đây tính từ gốc repo; dấu `*`/`**` là mẫu phạm vi:

- `src/backend/ClinicManagement.Infrastructure/AI/**`, `src/backend/ClinicManagement.Application/AI/**`.
- `src/frontend/src/components/copilot/*`, `src/frontend/src/components/MedicalChatWidget*`, `src/frontend/src/contexts/ChatContext.tsx`, `src/frontend/src/hooks/useAiBookingFlow.ts`, `src/frontend/src/api/aiCopilotApi.ts`.
- `src/tools/**`, `docs/ai/**`; mọi model/dataset, kể cả trong các thư mục trên.
- `.github/`, `scripts/e2e/`, `src/frontend/package.json`, `src/frontend/package-lock.json`, cấu hình Vitest (hiện trong `src/frontend/vite.config.ts`).

## Test

- Không sửa, xóa hoặc skip test cũ trừ khi được cho phép đích danh.
- Test cũ fail thì sửa code cho tương thích; không sửa được thì dừng và báo cáo.
- Việc mới phải có test mới.

## UI

- Dùng antd và token `--cc-*`; không mã màu hex, không thêm `style={{}}` mới.
- Code mới không dùng `Row`, `Col`, `Grid`, `Tabs`, `List`, `DatePicker`; ô ngày dùng `<Input type="date">`.
- Nút bận dùng `disabled` và đổi chữ, không dùng `loading`.
- Tách riêng trạng thái đang tải, lỗi có nút thử lại bằng `InlineError`, và rỗng.
- `Alert` dùng `title`. Ngày hiển thị `dd/MM/yyyy` qua `formatDateOnly`; lỗi API qua `getApiErrorMessage`.
- Nguồn dùng chung: `src/frontend/src/components/common/InlineError.tsx`, `src/frontend/src/utils/formatters.ts`, `src/frontend/src/utils/apiError.ts`, `src/frontend/src/index.css`.

## Backend

- Đổi schema phải có migration; `dotnet ef migrations has-pending-model-changes` phải sạch.
- Thời gian nghiệp vụ dùng giờ Việt Nam qua `IDateTimeProvider` tại `src/backend/ClinicManagement.Application/Common/Interfaces/IDateTimeProvider.cs`.

## Cấu trúc code

- Không thêm comment giải thích code đang làm gì; chỉ comment lý do không hiển nhiên.
- Code mới đặt trong thư mục của đúng tính năng.
- Không thêm logic mới vào file đã quá 1.000 dòng; tách thành file/lớp riêng.

## Git và CI

- Đúng 1 commit, không `Co-Authored-By`, push không force. Không commit file nháp.
- Tự chạy `gh pr create` → `gh pr checks --watch` → `gh pr merge --merge --delete-branch`.
- Trước khi merge: fetch; nếu main đổi thì merge `origin/main`, push và chờ CI xanh lại.
- Chỉ rerun CI 1 lần khi lỗi thuộc danh sách chập chờn: `checkinDateAndDepartment.test.tsx:55` timeout; cascade `receptionAppointmentsCopilot`; `adminPagesBatch3` “30-day range” timeout; E2E “Unexpected end of JSON input”/timeout. Lỗi khác phải xử lý hoặc dừng báo cáo.
- Báo cáo cuối 1 lần, theo thứ tự: branch/commit/PR/merge SHA → thay đổi theo mục → test mới → build/test/lint → CI → rủi ro.
