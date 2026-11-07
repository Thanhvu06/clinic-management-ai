# Hỏi đáp tài liệu phòng khám với Ollama cục bộ

## Luồng xử lý

```mermaid
flowchart LR
    A[Câu hỏi] --> S[Safety: cấp cứu, injection, PII, phạm vi thuốc]
    S --> I[Intent và thao tác bệnh nhân hiện có]
    I --> DB[Giá, cơ sở, lịch và hồ sơ: CSDL]
    I --> Q[Câu hỏi hướng dẫn công khai]
    Q --> R[BM25 + embedding bge-m3 + RRF]
    R --> G[Ollama qwen2.5:7b hoặc trích xuất]
    G --> O[AiProviderOutputGuard]
    O --> C[Trả lời, Sources, giữ nguyên nháp]
```

`AiSpecialtyService.cs` chỉ nối hai thời điểm gọi RAG; phần phát hiện nằm trong `AiSpecialtyService.Knowledge.cs` và `AI/Knowledge`. Diff caller là ba dòng thêm/ba dòng thay, không tăng số dòng. Safety, phạm vi thuốc và bộ kiểm tra dữ liệu cá nhân vẫn chạy trước.

RAG sớm chỉ thử câu hướng dẫn chung có “hướng dẫn”, “cách”, “như thế nào”, “thế nào”, “quy trình”, “thủ tục” hoặc “phải làm gì”; không có từ sở hữu dữ liệu cá nhân, “mã” hay “số”; không có chuyên khoa được nhận diện, bác sĩ, ngày/giờ hoặc chỉ số lựa chọn do classifier trích xuất. Vì vậy “hướng dẫn đổi lịch hẹn” được hỏi đáp tài liệu, còn “hủy lịch hẹn của tôi”, “đổi lịch hẹn ngày mai”, kết quả/đơn thuốc của chính bệnh nhân vẫn qua handler đọc/hủy lịch trước. Không sửa `AiSpecialtyService.PatientReadFilters.cs`. Những câu khác giữ thứ tự đọc dữ liệu trước, RAG sau; cùng một câu không truy xuất RAG hai lần.

Cổng `IsCandidate` nhận câu hỏi chưa rõ intent, hoặc hướng dẫn chung về thao tác khi không có lựa chọn cụ thể. Intent rõ về thao tác không trở thành RAG chỉ vì có “được không”. Nhận cả dấu hỏi, cách hỏi không dấu và viết tắt như “ko”, “k”, “sao”, “ở đâu”. Từ không dấu “ho”, “dau”, “non” chỉ coi là triệu chứng trong cụm rõ, tránh nhầm “hồ sơ” và “ở đâu”; dấu hiệu cấp cứu vẫn thắng mọi tuyến. Giá/giờ làm việc/địa chỉ/điện thoại của cơ sở tiếp tục lấy từ CSDL; các câu “ở đâu” về quy trình không bị ép thành tra cứu cơ sở. “huy lich” ngắn và yêu cầu trộn đặt/xem lịch vẫn theo luồng hỏi lại cũ.

## Truy xuất và dữ liệu gửi mô hình

- Tài liệu Markdown có front matter `id`, `title`, `category`, `updated`; chia theo heading, tối đa 550 ký tự, overlap khoảng 50 ký tự ở phần dài. Đoạn cuối có thể ngắn hơn 300 ký tự. Mỗi đoạn giữ nguồn và mã riêng.
- BM25 chạy cục bộ trong mọi cấu hình: chữ thường, bỏ dấu câu, token có dấu và bỏ dấu, thêm bigram. Điểm chuẩn hóa `raw / (raw + 5)`, ngưỡng mặc định 0,35. Không có Topics hay danh sách chủ đề bắt buộc ở cổng hoặc retriever; MinScore quyết định có đủ bằng chứng lexical để trả lời. Giữ MinScore=0,35 để so sánh cùng cấu hình; đánh đổi về từ chối ngoài phạm vi được ghi rõ trong RAG_EVALUATION.md.
- Khi RAG bật, worker dựng embedding bge-m3 ở background theo lô hai đoạn, công bố nguyên index sau khi kiểm tra đủ vector hữu hạn, cùng chiều, khác vector không. CSDL không tham gia dựng index. Lexical retrieval dùng được trong lúc chờ.
- `KnowledgePrivacyMask` giữ nội dung câu hỏi thật, che email, số điện thoại, dãy từ chín chữ số (kể cả cách bằng dấu cách/chấm/gạch), mã lịch/hồ sơ theo prefix hoặc ngữ cảnh, phần tên sau lời giới thiệu và tuổi đi kèm. Phép che lặp lại không làm thay đổi câu đã che. Regex hết thời gian thì trả rỗng và không gọi mô hình. Ví dụ phần “nhịn ăn bao lâu trước khi lấy máu?” được giữ nguyên thay vì đổi thành nhãn chủ đề.
- Cùng một câu đã che được dùng cho BM25, query embedding và `/api/chat`. Không truyền lịch sử hội thoại, nháp, hồ sơ, dữ liệu tool hay thông tin tài khoản. Hàm PublicQuestion và cơ chế thay câu hỏi bằng nhãn chủ đề đã bị loại khỏi đường gửi mô hình. Bộ che là quy tắc định dạng, không phải hệ thống nhận diện mọi loại PII tự do; màn hình chat vẫn giữ cảnh báo không nhập thông tin riêng tư.
- RRF dùng `sum(1 / (60 + rank))`, rank bắt đầu từ 1, top-k mặc định 4. Dense ranking với cosine ≥ 0,55 chỉ xếp lại các đoạn đã vượt ngưỡng lexical. Đây là lựa chọn thận trọng: embedding không tự mở rộng sang tài liệu không có bằng chứng lexical. Điểm RRF dùng xếp hạng; ngưỡng được áp trên bằng chứng trước fusion.
- Cache tại `App_Data/rag/<SHA256>.json`, đã được `.gitignore` của repo loại trừ. Hash gồm phiên bản index, tên embedding model và nội dung có thứ tự. Cache hỏng sẽ dựng lại; không ghi được cache vẫn dùng index RAM. Tắt ứng dụng rồi thay model/tag bằng nội dung khác thì nên xóa cache tương ứng vì tag Ollama có thể đổi nội dung.

## Sinh câu trả lời và khi Ollama lỗi

Gọi `/api/chat` với `stream=false`, temperature 0,1, giới hạn token và tối đa năm câu ở đầu ra. System prompt yêu cầu chỉ dựa tài liệu, không chẩn đoán/kê thuốc, thiếu thông tin thì hướng dẫn liên hệ lễ tân. Prompt chỉ chứa câu hỏi thật đã che và các đoạn có nguồn được chọn: điểm ≥60% điểm hit đầu, tối đa hai nguồn khác nhau theo docId + heading. Chỉ các đoạn này được cấp cho chat model; Sources không liệt kê những đoạn đã loại. Trích xuất vẫn chỉ dùng nguồn đoạn tốt nhất. Mọi câu trả lời, kể cả trích xuất, đều qua `AiProviderOutputGuard`; bị chặn dùng thông báo, mã lỗi và hành động cấp cứu giống nhánh provider hiện tại.

Khi RAG bật, sau khi index embedding sẵn sàng, background worker gọi `/api/chat` với messages rỗng để nạp chat model, `keep_alive="30m"`. Warm-up không chặn API khởi động, không chứa câu hỏi/hồ sơ bệnh nhân; thất bại chỉ ghi warning và giữ khả năng trích xuất. Chat và embed đều dùng keep_alive 30 phút; warm-up và dựng index nền không tính vào ProviderWasCalled của lượt chat.

RAG tắt, HTTP lỗi, nội dung JSON sai, model chưa có, timeout hoặc circuit mở: dùng đoạn tốt nhất, cắt gọn và trích đúng nguồn đoạn đó. Circuit độc lập với Gemini, dùng cơ chế `AiProviderHealth`: mở sau một lỗi, nghỉ 30 giây, chỉ một probe khi half-open. Health check `/api/tags` giới hạn hai giây; worker không chặn khởi động. Request HTTP không dùng proxy và chỉ cho phép HTTP loopback. Với host `localhost`, dùng IPv4 `127.0.0.1` để tránh độ trễ IPv6 khi Ollama Windows chỉ nghe IPv4.

`KnowledgeAnswer` nghĩa là dùng đầu ra chat Ollama; `KnowledgeExtractive` là trích xuất. `ProviderWasCalled` tính cả request embedding truy vấn/chat thực sự gửi đi; khi circuit chặn hoặc RAG tắt thì false. `ExecutionMode` là `ProviderAssisted` khi sinh, `DeterministicFallback` khi trích xuất, `ManualHandoff` khi output guard chặn. Embedding dựng index nền không được tính vào từng lượt chat. Nguồn của đầu ra sinh là các đoạn cung cấp cho mô hình, không phải bằng chứng kiểm định từng mệnh đề do mô hình sinh.

API thêm `Sources: [{docId,title,heading}]`. Vì hook `useAiBookingFlow.ts` thuộc vùng chưa được mở khóa, backend đồng thời chuyển cùng metadata công khai qua `toolResults/resultType=knowledge_sources` mà hook đã vận chuyển sẵn. `MedicalChatWidgetSources` hiển thị bằng `Typography.Text type="secondary"`, hỗ trợ cả trường Sources trực tiếp và metadata này. Không đổi logic hay quyền đăng nhập của widget.

## Thêm tài liệu

1. Thêm `.md` trong `src/backend/ClinicManagement.Infrastructure/AI/Knowledge/Documents`, đặt id duy nhất, cập nhật ngày và heading có nghĩa. Tài liệu được copy cả khi build và publish.
2. Đối chiếu hành vi với service thật. Không viết giá, địa chỉ, điện thoại, giờ làm việc, nội dung chẩn đoán hoặc liều thuốc. Giá/cơ sở chỉ dẫn người dùng tới trang tương ứng.
3. Không cần đăng ký Topics. Chạy bộ phát triển để kiểm tra điểm BM25 và bảo đảm câu hỏi thật được che định danh trước khi gửi mô hình; không thêm dữ liệu bệnh nhân vào corpus.
4. Thêm câu hỏi phát triển và test mới; chạy đánh giá và test output guard trên toàn bộ đoạn. Build và khởi động lại để nạp corpus mới. Hash mới tự tạo cache embedding mới.

Đối chiếu nghiệp vụ: `ConfirmedAppointmentExpiryRule` và `docs/expiry-rules.md` (30 phút, tác vụ nền, hạn yêu cầu và vắng mặt); `ChangeRequestService` (đổi/hủy chờ xử lý); `DiagnosticResultPublicationPolicy`/`DiagnosticWorkflowService` (công bố sau duyệt); `PharmacyService`/`PrescriptionPaymentEligibilityService` (điều kiện lĩnh thuốc); `BillingService` và trang `PatientInvoices` (thu tại quầy); `Login`/`ForgotPasswordModal` và `AuthenticationService` (khôi phục mật khẩu).

Các đoạn chuẩn bị xét nghiệm chỉ hướng dẫn hỏi nơi thực hiện, không ấn định thời gian nhịn ăn. Tham chiếu thông tin chuẩn bị chung: [NHS: blood tests](https://www.nhs.uk/tests-and-treatments/blood-tests/) và [NHS: X-ray](https://www.nhs.uk/tests-and-treatments/x-ray/). API Ollama: [tài liệu chính thức](https://github.com/ollama/ollama/blob/main/docs/api.md).

## Bật/tắt và đánh giá

```powershell
ollama serve
ollama pull qwen2.5:7b
ollama pull bge-m3
dotnet run --project src/backend/ClinicManagement.Api --launch-profile http
dotnet run --project src/tools/ClinicManagement.AI.RagEval -- bm25
dotnet run --project src/tools/ClinicManagement.AI.RagEval -- hybrid
dotnet run --project src/tools/ClinicManagement.AI.RagEval -- end-to-end
dotnet run --project src/tools/ClinicManagement.AI.RagEval -- end-to-end-hybrid
```

`Rag:IsEnabled=false` trong appsettings mặc định và Testing; Development bật. Các tùy chọn: `BaseUrl`, `ChatModel`, `EmbeddingModel`, `TimeoutSeconds` (45, giới hạn 1–120), `MinScore` (0,35), `TopK` (4). Có thể ghi đè bằng biến môi trường `Rag__IsEnabled=false`. Gemini/provider/planner hiện có không bị sửa và vẫn tắt theo cấu hình Development. Tool đánh giá không gọi chat, không cần tài khoản bệnh nhân và không chạy trong CI. Hybrid đòi hỏi đủ index và embedding truy vấn thành công; lỗi sẽ dừng thay vì báo số BM25 thành hybrid.

Giới hạn quyền hiện tại: endpoint `/api/v1/ai/chat` yêu cầu Patient, khách nhận HTTP 401; widget khách chỉ có thông báo đăng nhập. Phạm vi sửa widget của yêu cầu này chỉ mở hiển thị nguồn, nên chưa mở chat công khai cho khách. Việc mở quyền khách cần một thay đổi được phép riêng, kèm kiểm thử hợp đồng bảo mật hiện có.


Chế độ `bm25`/`hybrid` giữ phép đo retrieval thuần. `end-to-end`/`end-to-end-hybrid` đo safety, classifier, IsCandidate và retrieval; mọi câu trong phạm vi bị gate loại đều tính trượt trong mẫu số 156. Báo riêng tỷ lệ qua gate trên 156/176 câu và tỷ lệ từ chối 20 câu ngoài phạm vi. Tool không khởi tạo CSDL, không thực thi các handler cá nhân trước RAG và không sinh câu trả lời; kiểm thử API/luồng chat xác minh riêng thứ tự đọc/hủy lịch. Số end-to-end của tool không được trình bày như độ chính xác toàn bộ endpoint có CSDL.

`App_Data/` đã có sẵn trong `.gitignore` (không cần thêm dòng trùng), bao phủ cả thư mục gốc và thư mục con như `src/backend/ClinicManagement.Api/App_Data/rag`. Kiểm tra bằng `git check-ignore -v App_Data/rag-smoke.json src/backend/ClinicManagement.Api/App_Data/rag/probe.json`. File thử cũ `App_Data/rag-smoke.json` đã xóa cục bộ; các kết quả mới dùng tên `rag-review-*` trong thư mục được ignore.
