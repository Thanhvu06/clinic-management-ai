# Gate C — Provider-independent degraded mode

## Kết luận phạm vi

Gate C giữ nguyên model/config hiện có và không gọi Gemini trong kiểm thử. Khi provider hoạt động, luồng là `Gemini planner → deterministic validation → Tool Gateway`. Khi provider lỗi, luồng an toàn là `deterministic planner → server-owned context → Tool Gateway → grounded response`; nếu intent không đủ chắc chắn thì trả clarification/manual handoff. Gemini không bao giờ là authority cho actor, scope, resource ID, quyền hoặc confirmation.

Đây là bảo đảm hành vi fail-closed và tính liên tục của ứng dụng, không phải đánh giá an toàn y khoa.

## Kết quả kiểm toán trước sửa

- `ProviderState` trước đây đã có nhưng chỉ mô tả trạng thái ổn định một phần; raw `ProviderStatus` vẫn bị dùng ở patient booking để suy diễn UI. `NotCalled` có nhánh giữ trạng thái cũ, còn các lỗi transport ở frontend vẫn bị đặt thành `Offline` và dùng thông báo kết nối chung.
- Patient chat và role Copilot dùng cùng `IAiProviderHealth` singleton trong process, nhưng circuit cũ không có `HalfOpen`, probe độc quyền hoặc clock injectable. Patient flow có thể trả degraded manual fallback; role flow dùng deterministic planner cho các lệnh rõ ràng, còn câu mơ hồ không đủ an toàn thì clarification.
- Tổng budget provider đã tồn tại và không reset theo retry, nhưng thiếu HTTP 408, exponential backoff có jitter và metadata Retry-After trả về client.
- Client cancellation đã được phân biệt ở provider nhưng chưa giải phóng probe HalfOpen ở mọi đường đi.
- Confirmation/write không gọi Gemini: prepare/confirm đi qua action gateway, backend revalidate actor/session/facility/resource/version/token/idempotency. Provider state không tự confirm và không xoá draft/preview.

## Cấu hình Development có hiệu lực

Trong checkout này, `ASPNETCORE_ENVIRONMENT=Development` sẽ nạp section `AiProvider` từ `src/backend/ClinicManagement.Api/appsettings.Development.json`:

| Field | Giá trị quan sát được | Nguồn ưu tiên đã kiểm tra |
|---|---:|---|
| `IsEnabled` | `false` | `appsettings.Development.json` |
| `ModelName` | `gemini-1.5-flash` | `appsettings.Development.json` ghi đè default options |
| `TimeoutSeconds` | `10` | `appsettings.Development.json` |

Không thấy override `AiProvider__*` trong environment hiện tại, launch settings hoặc user-secrets metadata. `AiProviderOptions.cs` chỉ là fallback default (`gemini-3.6-flash`) khi section không cung cấp giá trị. `gemini-3.5-flash-lite` chỉ xuất hiện trong fake-provider test factory `GeminiAiProviderResilienceTests.CreateProvider`, không xuất hiện trong appsettings runtime của repo; vì vậy log đó không chứng minh model runtime của Development đang là model này. Không in hoặc ghi nhận API key/secret.

## Contract response

Hai endpoint chat dùng các field mới, vẫn giữ alias cũ:

- `providerState`: `Online`, `Degraded`, `Unavailable`, `Disabled`, `NotCalled`, `SafetyBlocked`.
- `providerFailureCode`: `None`, `RateLimited`, `Timeout`, `NetworkError`, `ServerError`, `AuthenticationFailed`, `ModelUnavailable`, `InvalidResponse`, `SafetyBlocked`, `CircuitOpen`, `ClientCancelled`, `ConfigurationDisabled`, `UnknownProviderFailure`.
- `executionMode`: `ProviderAssisted`, `DeterministicFallback`, `ManualHandoff`.
- `fallbackActive`, `retryable`, `retryAfterUtc`, `retryAfterSeconds`, `correlationId`, `providerWasCalled`.

`NotCalled` không phải lỗi. `ClientCancelled` không hiển thị như mất kết nối. `Disabled` khác lỗi provider; nếu deterministic/manual capability vẫn phục vụ được thì UI hiển thị degraded support và failure code vẫn giữ `ConfigurationDisabled`.

## Error taxonomy và retry thực tế

| Tình huống | Retry provider | Circuit | UI/fallback |
|---|---:|---:|---|
| HTTP 408, timeout tổng budget | Có, tối đa `MaxAttempts` và còn budget | Có | `Degraded` + `Timeout` |
| HTTP 429 | Có nếu còn budget; tôn trọng `Retry-After` delta/date | Có theo policy | `Degraded` + retry-after nếu có |
| HTTP 500/502/503/504 | Có | Có | `Degraded` + `ServerError` |
| network exception tạm thời | Có | Có | `Degraded` + `NetworkError` |
| 400, 401/403, 404 model/endpoint | Không | Không | `Unavailable`/manual hoặc degraded capability |
| invalid JSON/schema | Không, không dùng dữ liệu | Không mở circuit | `InvalidResponse` + fallback/clarification |
| safety/tool authorization/resource validation | Không | Không | `SafetyBlocked` hoặc clarification |
| caller cancellation | Không, dừng ngay | Không tăng count; giải phóng probe | không thêm lỗi giả |
| circuit Open | Không gọi provider | chờ cooldown | deterministic fallback nếu đủ chắc chắn, nếu không manual |
| backend/database/tool failure | Không retry provider | không tính provider | typed tool error, không bịa dữ liệu |

Mỗi lần gọi dùng một tổng budget gồm request, delay và retry kế tiếp. Backoff là exponential, jitter bounded, delay không vượt phần budget còn lại. Write/prepare/confirm không dùng retry provider.

## Circuit state machine

`Closed → Open` sau threshold reliability failures (mặc định 3). `Open` không gọi provider. Sau cooldown, state thành `HalfOpen`; đúng một request giữ probe, các request khác nhận fallback. Probe thành công về `Closed`; probe thất bại mở lại. `TimeProvider` injectable cho test. Circuit là in-memory process-wide singleton nên mỗi instance/restart có state riêng; chưa phải distributed circuit.

## Capability matrix khi Gemini lỗi

| Actor | Deterministic reads | Write/confirmation |
|---|---|---|
| Patient | catalog chuyên khoa/bác sĩ/cơ sở/giá/slot thật; lịch hẹn, visit, kết quả đã công bố, đơn thuốc, hóa đơn của chính mình | giữ session/draft/snapshot/version/preview; chỉ prepare khi rõ và đủ context; không tự chọn doctor/slot, không auto-confirm |
| Receptionist | lịch trong facility, lookup appointment, queue/check-in context | prepare qua action gateway với facility/department/version; confirmation riêng |
| Doctor | queue, case hiện tại được phân công, vitals, orders, prescription status | prepared action có resource/version mới được confirm; free-text mơ hồ không tạo write |
| DiagnosticTechnician | worklist và order/item được giao | start/complete/record-result chỉ qua gateway khi order/item/version đầy đủ; không suy đoán result |
| Pharmacist | prescription queue, payment eligibility, inventory theo scope | reserve/dispense chỉ khi payment/quantity/version hợp lệ; không tự chọn prescription |
| Admin | dashboard và health metrics đã sanitize | không có quyền arbitrary SQL, đổi role/giá/khóa user/hủy hóa đơn từ chat |

Mọi actor vẫn dùng authentication, actor resolver, scope, `AiToolBindingRegistry`, Tool Gateway, redaction và audit hiện có. `not_found`/capability unavailable được trả trung thực; không lấy top record làm fallback.

## Grounding, draft và confirmation

Cards chỉ được dựng từ typed tool result, có source và `retrievedAtUtc`; không render raw JSON, secret, token hoặc internal ID ngoài scope. Provider outage không xoá history/session/draft/action preview. Pending action đã prepare trước outage vẫn confirm độc lập với Gemini sau khi backend revalidate toàn bộ binding và exactly-once. Response muộn sau cancel/đổi route/account bị bỏ ở frontend.

## Frontend và observability

Patient booking và Unified Copilot đều dùng `providerState` ổn định cho badge; raw status chỉ là compatibility detail. Lỗi 429/timeout/server/auth-model/circuit/cancel có message riêng; `NotCalled` là xử lý nội bộ, không phải Offline. Abort không thêm bubble lỗi. HTTP provider failures tiếp tục cho phép chat/fallback và giữ draft; chỉ lỗi transport đến backend mới dùng Offline.

Admin health tool trả state/circuit, fallback flag, request/success count, failure counts đã sanitize, last failure/success và next probe. Các metric này in-memory, không chứa prompt/response/PHI/secret và mất khi restart; chưa tuyên bố distributed observability.

## Kiểm thử và giới hạn

Focused fake-HTTP tests bao phủ 200, 429/Retry-After, 503 recovery, exhausted budget, timeout, invalid JSON, client cancel, invalid response không mở circuit, Closed/Open/HalfOpen, single probe và recovery. Frontend tests bao phủ message theo failure code, retry-after, client cancel và Disabled badge. Các test authorization/read-scope/confirmation/Gate A/Gate B hiện có được giữ nguyên.

Chưa gọi Gemini thật, chưa chạm database thật/OneDrive, chưa apply migration, chưa đổi ML.NET khỏi Shadow/Off, chưa commit/push; remote CI chưa thể xác minh trước khi push. Live provider/model availability và behavior multi-instance vẫn là việc cần kiểm tra ở phase tiếp theo.
