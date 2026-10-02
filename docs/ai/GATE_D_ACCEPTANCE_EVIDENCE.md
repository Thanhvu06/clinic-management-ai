# Gate D acceptance evidence

## Trạng thái hiện tại

Gate D **chưa đạt** tiêu chí nghiệm thu live Gemini ban đầu. Các lần chạy live được ghi bên dưới vẫn là kết quả lịch sử, không được diễn giải lại thành PASS. Trong lượt cập nhật tài liệu này không dispatch workflow và không gọi Gemini thật (`live attempts = 0`).

Kết quả kiểm thử offline tại HEAD `8925a471cb970d4a63537f27a49dba185a481d5b` xác nhận các đường kiểm thử hiện có chạy được với SQLite tạm và fake provider. Kết quả đó không thay thế bằng chứng live provider và không phải bằng chứng về chất lượng AI hoặc chất lượng y khoa.

## Live canary Gemini sau bản vá contract

| Workflow run | Commit | Model / ngân sách | Kết quả được ghi nhận |
| --- | --- | --- | --- |
| `36686702159` | `a0c8e26` | `gemini-3.5-flash-lite`, 12 lần gọi | `FAIL`. Đây là kết quả lịch sử đã ghi: 5/7 ca đạt và 2/7 ca không đạt; không được đổi thành PASS. |
| `36698632751` | `68cd8b9` | `gemini-3.5-flash-lite`, `max_calls=1` | Ca `patient-http-read` nhận HTTP 400; report phân loại là `ModelUnavailable`. |
| `36699257171` | `68cd8b9` | `gemini-3.8-flash`, `max_calls=1` | Ca `patient-http-read` nhận HTTP 400; report phân loại là `ModelUnavailable`. |
| `36703211367` | `b8a5772` | `gemini-3.8-flash`, `max_calls=1` | Sau bản vá chẩn đoán, report ghi `ProviderHttpStatus=400`, `ProviderErrorStatus=InvalidArgument`, `ProviderRejectedRequestPart=ResponseFormatSchema`. |
| `36704909142` | `b8a5772` | `gemini-3.8-flash`, `max_calls=12` | Có 7 ca và 6 lần gọi Gemini. Cả 6 ca role Copilot, với 2, 3, 4, 5 và 12 công cụ, đều trả HTTP 400 `InvalidArgument`. Ca Patient legacy không gọi Gemini đạt. `AcceptanceStatus=FAIL`, `DatabaseUnchanged=true`; report không ghi lỗi auth, quota hoặc timeout. |
| `36708986538` | `ca1c642` | `gemini-3.8-flash`, `max_calls=1` | Report ghi `ProviderRejectionKind=InvalidValue`, `ProviderRejectedFieldPath=generation_config.response_format.text.mime_type`, `RequestSchemaSizeBytes=6386`, `RequestSchemaToolBranches=12`, `RequestSchemaMaxDepth=10`. |

### Kết luận có bằng chứng

- Gemini từ chối giá trị tại `generationConfig.responseFormat.text.mimeType` trong request của role planner.
- Việc từ chối đã xuất hiện với các số lượng công cụ khác nhau và với cả hai model đã thử: `gemini-3.5-flash-lite` và `gemini-3.8-flash`.
- Không có ca role Copilot nào đạt sau bản vá `f93dbda`. Trước bản vá, run tại `a0c8e26` có 5/7 ca đạt.
- Run `36704909142` vẫn bảo toàn fingerprint dữ liệu (`DatabaseUnchanged=true`), nhưng điều đó không làm cho acceptance chuyển thành PASS.

### Chưa biết / chưa thử

- Chưa xác minh cách gửi structured output nào được Google chấp nhận cho request này.
- Hướng dùng `generationConfig.responseMimeType` cùng `responseJsonSchema` chưa được thử.
- Không có live run mới sau các kết quả trên trong lượt cập nhật tài liệu này.

## Browser E2E

Browser E2E được chạy tại HEAD `8925a471cb970d4a63537f27a49dba185a481d5b` bằng lệnh `npm.cmd run e2e` trong `src/frontend`.

- `PASS`: 29.
- `NOT_COVERED`: 2.
- `FAIL`: 0.
- Provider: `FakeGeminiHttpHandler`; không có network call đến Gemini.
- Dữ liệu: SQLite tạm do `SyntheticCanaryFactory` tạo; không dùng database thật hoặc dữ liệu bệnh nhân thật.

Hai scenario `NOT_COVERED` không được tính là PASS:

1. `5 write preview/confirm` — đường cancel được phủ; confirm cố ý không có trong browser fixture không thay đổi dữ liệu.
2. `6 unpublished/published diagnostic result` — acceptance harness chưa có fixture publish an toàn.

## Kiểm thử offline trong lượt cập nhật này

| Phạm vi | Lệnh | Kết quả |
| --- | --- | --- |
| Backend integration, Release | `dotnet test src/backend/ClinicManagement.IntegrationTests/ClinicManagement.IntegrationTests.csproj -c Release --no-build --no-restore --logger "console;verbosity=minimal"` | 819 passed, 0 failed, 0 skipped |
| Frontend unit/component | `npm.cmd run test` tại `src/frontend` | 210 tests passed trong 25 test files; 0 failed |
| Browser E2E | `npm.cmd run e2e` tại `src/frontend` | 29 PASS, 2 NOT_COVERED, 0 FAIL |

Lần chạy backend đầu tiên có build nhưng hết thời gian chờ trước khi in summary, vì vậy không được dùng làm số liệu nghiệm thu. Bảng trên chỉ dùng summary của lần chạy hoàn tất.

## Chờ quyết định

Dự án hiện không theo đuổi hướng Gemini live. Gate D chưa được chốt theo tiêu chí ban đầu. Việc có định nghĩa lại tiêu chí Gate D hay không thuộc quyết định của chủ đề tài; tài liệu này không tự đặt tiêu chí thay thế.

## Giới hạn của bằng chứng

- Không có Gemini live attempt trong lượt này.
- Chưa kiểm chứng contract structured output thay thế với Google.
- Hai browser scenario vẫn là `NOT_COVERED` như nêu trên.
- Test offline và canary kỹ thuật không chứng minh độ đúng của nội dung AI, an toàn lâm sàng hay chất lượng y khoa.
