# Kết quả đánh giá RAG phát triển

## Phạm vi và máy đo

Đo ngày 07/11/2026 (giờ Việt Nam), trên Windows, AMD Ryzen 9 7945HX, RAM khả dụng báo bởi hệ thống 31,2 GiB, NVIDIA GeForce RTX 4060 Laptop GPU 8 GiB, .NET SDK 10.0.300, Ollama 0.40.15. Model chat `qwen2.5:7b` (id `845dbda0ea48`), embedding `bge-m3:latest` (id `790764642607`, 1024 chiều). Không sửa, huấn luyện hoặc thay artifact ML.NET hiện có.

Corpus: 12 tài liệu tiếng Việt, 36 đoạn. Bộ phát triển mới: 156 câu có expectedDocId và 20 câu ngoài phạm vi, tổng 176; gồm câu không dấu, viết tắt và lỗi gõ nhẹ. Không mở hoặc sử dụng bộ đề ẩn. Đây là số liệu trên bộ phát triển do cùng thay đổi biên soạn, không phải đánh giá độc lập hay bảo đảm chất lượng trên dữ liệu thật.

Cấu hình: MinScore=0,35; TopK=4; BM25 k1=1,2, b=0,75; điểm lexical `raw/(raw+5)`; RRF k=60; dense cosine≥0,55 trong tập ứng viên lexical. Cả hai chế độ dùng cùng bộ lọc chủ đề công khai. Vì vậy số liệu từ chối đo cả bộ lọc phạm vi và retrieval, không phải BM25 trần. Rank tính trên docId khác nhau trong tối đa bốn đoạn trả về. MRR của tài liệu không có trong kết quả bằng 0.

## Kết quả retrieval thực tế — trước sửa (PR #145)

Độ trễ chỉ tính truy xuất, bao gồm câu bị từ chối; không gồm dựng index, warm-up hoặc sinh câu trả lời. Một truy vấn warm-up được loại trước đo. Hybrid chỉ được ghi khi index hoàn chỉnh và tất cả lần gọi embedding truy vấn trong phép đo thành công. Có 149 truy vấn dùng embedding; các câu không vượt lexical gate không gọi model.

| Chế độ | Hit@1 | Hit@3 | MRR | Từ chối đúng ngoài phạm vi | Trung bình (ms) | p95 (ms) |
|---|---:|---:|---:|---:|---:|---:|
| bm25 | 89.74% | 94.87% | 0.9209 | 95.00% | 0.178 | 0.249 |
| hybrid | 84.62% | 91.67% | 0.8793 | 95.00% | 62.341 | 31.253 |

BM25 đạt ngưỡng test Hit@3≥85%. Hybrid thấp hơn BM25 trên bộ này; không có bằng chứng hybrid cải thiện chất lượng trong cấu hình hiện tại. Câu hỏi embedding đã được khái quát hóa để tránh dữ liệu bệnh nhân, có thể mất chi tiết so với câu gốc. Chưa điều chỉnh trọng số theo lỗi của bộ đề ẩn. p95 hybrid thấp hơn trung bình do một số request chậm ở đuôi phân phối; các số được ghi nguyên theo phép đo.

Các lần dựng index trước đó thất bại vì health check `localhost` hết timeout hai giây do kết nối IPv6 trên Windows. Sau khi ánh xạ localhost sang IPv4 và tắt proxy, đo hybrid hoàn tất. Các lần thất bại không được báo thành số hybrid. File cache chỉ chứa vector/tài liệu công khai, không có tài khoản hay câu hỏi cá nhân.

## Chạy thử API thực tế — trước sửa (PR #145)

Backend Development và frontend Vite được bật trên máy, Gemini vẫn tắt. Mỗi câu được gửi cả khách và bệnh nhân demo; mật khẩu và access token chỉ giữ trong RAM, không in hoặc lưu trong kết quả. Bảng dưới ghi lần thành công của bệnh nhân và HTTP của khách. Bốn câu cuối từng nhận HTTP 429 vì giới hạn tốc độ; thử lại sau khi cửa sổ hết hạn, không thay tài khoản hay cấu hình limiter. Độ trễ dưới đây không tính thời gian chờ cửa sổ này. Câu triệu chứng được kiểm tra thêm mã chuyên khoa, vẫn SP08 và provider Disabled.

| # | Câu hỏi | Tuyến bệnh nhân (HTTP 200) | Gọi Ollama | Nguồn docId trong prompt | Độ trễ (ms) | Khách |
|---:|---|---|---|---|---:|---|
| 1 | Có cần nhịn ăn trước xét nghiệm không? | KnowledgeAnswer | Có | prepare-visit, prepare-tests, supported-visitors | 44391.5 | HTTP 401 |
| 2 | Đi khám cần mang theo giấy tờ gì? | KnowledgeAnswer | Có | bring-documents, prepare-visit | 1729.8 | HTTP 401 |
| 3 | Quy trình một lượt khám thế nào? | KnowledgeAnswer | Có | visit-workflow, prescriptions | 1872.0 | HTTP 401 |
| 4 | Đăng ký tài khoản trên website thế nào? | KnowledgeAnswer | Có | accounts, diagnostic-results | 2018.7 | HTTP 401 |
| 5 | Trẻ em đi khám cần chuẩn bị gì? | KnowledgeAnswer | Có | supported-visitors, bring-documents | 2131.4 | HTTP 401 |
| 6 | Khi nào kết quả được công bố cho bệnh nhân? | KnowledgeAnswer | Có | diagnostic-results, bring-documents | 1899.5 | HTTP 401 |
| 7 | Bitcoin hôm nay tăng hay giảm? | UnclearInput | Không | — | 41.0 | HTTP 401 |
| 8 | Viết giúp bài thơ về mùa thu | UnclearInput | Không | — | 7.0 | HTTP 401 |
| 9 | đau ngực khó thở | SafetyEmergency | Không | — | 54.8 | HTTP 401 |
| 10 | Khám nội tổng quát giá bao nhiêu? | PricingInquiryResolved | Không | — | 52.6 | HTTP 401 |
| 11 | Tôi bị đau đầu chóng mặt | Gợi ý chuyên khoa SP08; outcome=null | Không | — | 42.3 | HTTP 401 |
| 12 | Tôi muốn đặt lịch khám nội tổng quát | NamedSpecialtyResolved | Không | — | 32.3 | HTTP 401 |

Câu cấp cứu bệnh nhân có cảnh báo 115, không gọi Ollama. Câu đặt lịch chỉ giải quyết chuyên khoa/chuẩn bị bước tiếp theo; không xác nhận tạo lịch hay thực hiện thay đổi lịch trong smoke test. Sáu câu hướng dẫn dùng qwen2.5:7b; lần đầu có chi phí nạp model nên gần 45 giây, các lượt sau khoảng hai giây. Nguồn trong bảng là tập tài liệu đã cấp cho model, không phải kiểm định riêng từng câu sinh ra. Tên và heading nguồn được trả trong Sources và được widget hiển thị.

Khách hiện không có chat công khai: `/api/v1/ai/chat` giữ `[Authorize(Roles="Patient")]`, trả 401 cho cả 12 câu. Widget khách giữ hướng dẫn đăng nhập. Đây là phần chưa đáp ứng mong muốn hỏi đáp trực tiếp cho khách vì giới hạn sửa widget chỉ cho hiển thị trích dẫn và yêu cầu không thay đổi test/quyền cũ. Không hạ quyền endpoint để hoàn thành smoke test.

### Sau khi tắt Ollama

Dừng cả server và ứng dụng tray do tác vụ cài/bật, sau đó hỏi lại bằng cùng tài khoản demo:

| Câu hỏi | Tuyến | ExecutionMode | Gọi Ollama | Nguồn docId – heading | Độ trễ (ms) |
|---|---|---|---|---|---:|
| Có cần nhịn ăn trước xét nghiệm không? | KnowledgeExtractive | DeterministicFallback | Có, embedding không kết nối được | prepare-tests – Nhịn ăn trước xét nghiệm | 2024.6 |
| Đi khám cần mang theo giấy tờ gì? | KnowledgeExtractive | DeterministicFallback | Không, circuit đang mở | bring-documents – Giấy tờ nhận diện | 21.1 |

Cả hai trả HTTP 200 và nội dung trích xuất kèm nguồn; không lộ lỗi Ollama cho người dùng. Khi RAG tắt hẳn, cả embedding và chat đều không được gọi, còn BM25/trích xuất vẫn hoạt động (được kiểm tra bằng HTTP handler giả trong xUnit).

## Tái lập

```powershell
dotnet run --project src/tools/ClinicManagement.AI.RagEval -- bm25
dotnet run --project src/tools/ClinicManagement.AI.RagEval -- hybrid
dotnet run --project src/tools/ClinicManagement.AI.RagEval -- end-to-end
dotnet run --project src/tools/ClinicManagement.AI.RagEval -- end-to-end-hybrid
dotnet run --project src/tools/ClinicManagement.AI.RagEval -- sweep
```

Có thể thêm đường dẫn JSON báo cáo làm tham số thứ hai. Tool từ chối ghi kết quả hybrid nếu index hoặc query embedding lỗi. Không có tùy chọn đọc bộ đề ẩn. CI build project nhưng không chạy tool đánh giá hybrid. Các phép đo hiện tại là một lượt mỗi chế độ, không phải trung bình nhiều lần; số đo độ trễ thay đổi theo tải máy và trạng thái model/cache.

## Sau sửa review L1–L7

Đo lại ngày 07/11/2026 trên cùng máy, cùng qwen2.5:7b và bge-m3; không đổi/huấn luyện model hoặc sửa bộ câu hỏi. Corpus vẫn 12 tài liệu/36 đoạn; đoạn quyền riêng tư được sửa để mô tả đúng việc che câu hỏi thật. Các bảng trước sửa ở trên được giữ nguyên.

### Retrieval và gate + retrieval

Giữ MinScore=0,35, TopK=4, cùng công thức BM25/RRF. Không còn Topics. Truy vấn embedding/chat giữ câu hỏi thật sau khi che định danh. Hit@1/Hit@3/MRR có mẫu số là **toàn bộ 156 câu trong phạm vi**; câu bị gate loại tính trượt, không loại khỏi mẫu số. Từ chối ngoài phạm vi đo 20 câu riêng. Đây là phép đo phát triển một lượt, không phải kiểm định độc lập.

“end-to-end” của tool là safety + classifier + IsCandidate + retrieval; không sinh câu trả lời, không khởi tạo CSDL hay thực thi handler cá nhân. Thứ tự handler và nhánh RAG sớm được xác minh riêng qua test chat và sáu câu API phía dưới. Không dùng số của tool như độ chính xác toàn endpoint có CSDL.

| Chế độ sau sửa | Qua gate / 156 câu | Qua gate / 176 câu | Hit@1 | Hit@3 | MRR | Từ chối đúng / 20 câu | TB ms | p95 ms |
|---|---:|---:|---:|---:|---:|---:|---:|---:|
| bm25 | Không áp gate | Không áp gate | 93.59% | 100.00% | 0.9658 | 5.00% | 0.522 | 0.240 |
| hybrid | Không áp gate | Không áp gate | 94.87% | 100.00% | 0.9744 | 5.00% | 16.710 | 20.034 |
| end-to-end | 89.10% | 84.66% | 83.33% | 89.10% | 0.8600 | 55.00% | 4.664 | 7.683 |
| end-to-end-hybrid | 89.10% | 84.66% | 84.62% | 89.10% | 0.8686 | 55.00% | 23.360 | 34.150 |

Gate + retrieval BM25 đạt Hit@3 89,10% ≥85%; hybrid cùng Hit@3, không thấp hơn BM25 quá hai điểm phần trăm. Có 139/156 câu trong phạm vi qua gate; trên toàn bộ 176 câu có 149 câu qua gate và 148 câu có hit vượt ngưỡng. So với số 61/156 câu qua cổng được nêu trong review đầu vào, cổng đã rộng hơn; không giả định số đó là một phép đo toàn endpoint.

**Đánh đổi còn lại:** khi bỏ Topics theo yêu cầu, từ chối đúng giảm từ 95% trước sửa xuống 5% ở retrieval thuần và 55% với gate + retrieval tại cùng MinScore. Không che giấu mức giảm này. Các kết quả Hit@3 cao không đồng nghĩa từ chối ngoài phạm vi tốt. Câu Bitcoin trong smoke test vẫn vượt ngưỡng, gọi Ollama và nhận Sources không liên quan; đó là false positive ở retrieval. Chưa hiệu chỉnh MinScore/config ngoài phạm vi file được mở khóa; cần cân nhắc riêng đánh đổi recall/từ chối trên tập kiểm chứng mới, không dựa bộ đề ẩn.

17 câu trong phạm vi bị gate loại gồm tình huống cấp cứu phải đi safety và các câu classifier trích nhầm “được ko”, “không”, “duyệt à” thành tên bác sĩ. Không bỏ qua entity đã trích để làm đẹp điểm. Trung bình có thể lớn hơn p95 do vài request chậm; số liệu giữ nguyên từ phép đo, có tải test khác trên máy. Index/warm-up không tính trong latency của tool.

### Chạy thử thật sau sửa

Backend Development, frontend Vite và ollama serve được bật; đăng nhập tài khoản bệnh nhân demo, không in/lưu mật khẩu hay token. Model đã warm-up ở background trước câu thử đầu; chat/embed dùng keep_alive 30m. Câu đầu sau khởi động được đánh dấu riêng; so sánh với 44.391,5 ms của lần trước là so sánh một lần đo có warm-up, không phải benchmark cold GPU nhiều lượt. Không thay limiter; script chờ cửa sổ rate limit khi cần.

| # | Câu hỏi | Tuyến thực tế | Gọi Ollama | Nguồn docId – heading | ms |
|---:|---|---|---|---|---:|
| 1 | Có cần nhịn ăn trước xét nghiệm không? **(đầu sau khởi động)** | KnowledgeAnswer | Có | prepare-tests – Nhịn ăn trước xét nghiệm; prepare-visit – Ăn uống và trang phục | 1581.2 |
| 2 | Đi khám cần mang theo giấy tờ gì? | KnowledgeAnswer | Có | bring-documents – Giấy tờ nhận diện; bring-documents – Giấy hẹn và người đi cùng | 1865.8 |
| 3 | Quy trình một lượt khám thế nào? | KnowledgeAnswer | Có | visit-workflow – Từ đặt lịch tới khám; visit-workflow – Hoàn tất lượt khám | 1722.4 |
| 4 | Đăng ký tài khoản trên website thế nào? | KnowledgeAnswer | Có | accounts – Đăng ký và đăng nhập; accounts – Dùng tài khoản an toàn | 1906.6 |
| 5 | Trẻ em đi khám cần chuẩn bị gì? | KnowledgeAnswer | Có | supported-visitors – Trẻ em đi khám; bring-documents – Giấy tờ nhận diện | 1840.2 |
| 6 | Khi nào kết quả được công bố cho bệnh nhân? | KnowledgeAnswer | Có | diagnostic-results – Khi nào có thể xem kết quả; diagnostic-results – Xem kết quả trên website | 1901.9 |
| 7 | Bitcoin hôm nay tăng hay giảm? | KnowledgeAnswer | Có | payments – Phạm vi dịch vụ và thắc mắc chi phí; bring-documents – Giấy tờ nhận diện | 1248.7 |
| 8 | Viết giúp bài thơ về mùa thu | UnclearInput | Không | — | 71.3 |
| 9 | đau ngực khó thở | SafetyEmergency | Không | — | 56.1 |
| 10 | Khám nội tổng quát giá bao nhiêu? | PricingInquiryResolved | Không | — | 48.4 |
| 11 | Tôi bị đau đầu chóng mặt | Gợi ý SP08; outcome=null | Không | — | 140.6 |
| 12 | Tôi muốn đặt lịch khám nội tổng quát | NamedSpecialtyResolved | Không | — | 29.4 |
| 13 | trễ hẹn 20p có sao k | KnowledgeAnswer | Có | appointments – Đến trễ và vắng mặt | 1202.5 |
| 14 | nhịn ăn bao lâu trước khi lấy máu | KnowledgeAnswer | Có | prepare-tests – Nhịn ăn trước xét nghiệm; prepare-tests – Uống nước và lấy mẫu | 1098.2 |
| 15 | cho tôi đặt lịch khám tim được không | Luồng hiện có; outcome=null | Không | — | 47.9 |
| 16 | hủy lịch hẹn ngày mai được không | NothingToCancel | Không | — | 50.3 |
| 17 | mang ho so cu di kham duoc khong | KnowledgeAnswer | Có | bring-documents – Hồ sơ và kết quả cũ; supported-visitors – Người cao tuổi và người cần hỗ trợ | 873.4 |

Cả 17 lượt HTTP 200. Cấp cứu vẫn có cảnh báo 115; câu giá lấy từ CSDL; triệu chứng vẫn gợi ý SP08. Ba câu thêm về trễ hẹn/nhịn ăn/hồ sơ không dấu được trả lời RAG; hai câu thao tác đặt/hủy không gọi Ollama. Sources tối đa hai, không trùng docId + heading. “NothingToCancel” phản ánh trạng thái demo tại lúc đo; không xác nhận hủy một lịch cụ thể hay tạo lịch mới qua smoke test.

### Sáu câu kiểm tra theo phạm vi mở khóa bổ sung

Chỉ câu hướng dẫn chung không có sở hữu, mã/số hay entity lựa chọn được thử RAG trước handler đọc/hủy; không hoán đổi toàn bộ hai nhánh, không sửa PatientReadFilters.

| Câu hỏi | Tuyến thực tế | Gọi Ollama | ms |
|---|---|---|---:|
| hướng dẫn đổi lịch hẹn | KnowledgeAnswer | Có | 2094.2 |
| cách hủy lịch hẹn như thế nào | KnowledgeAnswer | Có | 1174.3 |
| hủy lịch hẹn của tôi | NothingToCancel | Không | 31.5 |
| đổi lịch hẹn ngày mai | NothingToCancel | Không | 26.5 |
| xem kết quả xét nghiệm của tôi | PatientReadResolved | Không | 322.6 |
| đơn thuốc của tôi | PatientReadResolved | Không | 59.2 |

Bốn câu cá nhân/thay đổi lịch không có Sources và không gọi Ollama. Sau khi thử, backend, frontend và Ollama đều được tắt. `App_Data/` vốn đã có trong .gitignore, bao phủ cả root lẫn API; đã xóa file local cũ `App_Data/rag-smoke.json`, không thêm dòng ignore trùng. Kết quả mới lưu dưới tên `rag-review-*` ở thư mục đã ignore, không đưa file nháp vào commit.

## Sau sửa từ chối câu ngoài phạm vi (query coverage, cosine floor và hiệu chỉnh ngưỡng)

Mở rộng bộ phát triển từ 176 lên **216 câu** (giữ nguyên 156 câu trong phạm vi + 20 câu ngoài phạm vi cũ, bổ sung 40 câu ngoài phạm vi mới có `expectedDocId: null` gồm tiền ảo, chứng khoán, thời tiết, bóng đá, lập trình, nấu ăn, du lịch, làm thơ, toán, tin tức, hỏi cách chữa/uống thuốc cho bệnh cụ thể, hỏi bác sĩ giỏi nhất, hỏi phòng khám khác, có câu chứa `?` và `k/ko/gì`).

Cơ chế kiểm soát mới tại retrieval và answerer:
1. **Độ phủ truy vấn và từ IDF cao (`Bm25KnowledgeIndex.MeetsCoverage`, `KnowledgeText.ContentTerms`)**: hit đứng đầu chỉ được chấp nhận khi khớp $\ge 50\%$ số từ nội dung (không phải stopword) của câu hỏi và có ít nhất 1 từ khớp đạt $\text{IDF} \ge \text{MinTermIdf}$. Bổ sung stopword chung (`hay, tang, giam, nhat, gio, hom, nay, ngay, nam, thang, roi, luon, rat, qua, lam, biet, the, vay, a, ha, nhe, oi, ko, k, luc, hien, phut`) và đối chiếu từ có dấu trước khi gập dấu để tránh trùng âm khác nghĩa (`thơ/thở`, `lục/lúc`, `bát/bất`, `cờ/có`, `đá/đã`, `quả/quá`, `dược/được`, `chờ/cho`).
2. **Sàn cosine (`MinCosine`)**: khi index embedding sẵn sàng, hit đứng đầu phải đạt cosine với câu hỏi $\ge \text{MinCosine}$; nếu không đạt thì từ chối ngay cả khi BM25 đã qua.
3. **Từ chối câu trả lời LLM báo không có thông tin (`ClinicKnowledgeAnswerer`)**: khi câu trả lời sinh ra chứa mẫu `"chưa có thông tin"`, `"không có thông tin"`, `"tài liệu không đề cập"`..., trả về `null` để rơi về `UnclearInput` và không đính kèm `Sources`.

### Bảng quét ngưỡng trên bộ dev 216 câu (156 trong phạm vi, 60 ngoài phạm vi)

Quét bằng `dotnet run --project src/tools/ClinicManagement.AI.RagEval -- sweep` với `MinQueryCoverage = 0.50`:

| MinScore | MinQueryCoverage | MinTermIdf | MinCosine | BM25 Hit@3 | BM25 Từ chối | Hybrid Hit@3 | Hybrid Từ chối | E2E BM25 Hit@1 | E2E BM25 Hit@3 | E2E BM25 Từ chối | E2E Hybrid Hit@1 | E2E Hybrid Hit@3 | E2E Hybrid Từ chối |
|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| 0.35 | 0.50 | 1.15 | 0.55 | 98.72% | 93.33% | 82.69% | 100.00% | 82.69% | 87.82% | 96.67% | 70.51% | 73.08% | 100.00% |
| 0.35 | 0.50 | 1.25 | 0.55 | 98.08% | 93.33% | 82.05% | 100.00% | 82.69% | 87.18% | 96.67% | 69.87% | 72.44% | 100.00% |
| 0.40 | 0.50 | 1.25 | 0.50 | 98.08% | 93.33% | 83.97% | 100.00% | 82.69% | 87.18% | 96.67% | 71.15% | 73.72% | 100.00% |
| 0.40 | 0.50 | 1.25 | 0.40 | 98.08% | 93.33% | 87.18% | 98.33% | 82.69% | 87.18% | 96.67% | 73.72% | 76.28% | 100.00% |
| 0.45 | 0.50 | 1.15 | 0.35 | 98.72% | 93.33% | 91.67% | 93.33% | 82.69% | 87.82% | 96.67% | 77.56% | 80.77% | 96.67% |
| 0.45 | 0.50 | 1.25 | 0.55 | 98.08% | 93.33% | 82.05% | 100.00% | 82.69% | 87.18% | 96.67% | 69.87% | 72.44% | 100.00% |
| 0.45 | 0.50 | 1.25 | 0.45 | 98.08% | 93.33% | 86.54% | 98.33% | 82.69% | 87.18% | 96.67% | 73.08% | 75.64% | 100.00% |
| 0.45 | 0.50 | 1.25 | 0.35 | 98.08% | 93.33% | 91.03% | 93.33% | 82.69% | 87.18% | 96.67% | 76.92% | 80.13% | 96.67% |
| 0.45 | 0.50 | 1.25 | 0.25 | 98.08% | 93.33% | 98.08% | 93.33% | 82.69% | 87.18% | 96.67% | 83.33% | 87.18% | 96.67% |
| **0.45** | **0.50** | **1.25** | **0.20** | **98.08%** | **93.33%** | **98.72%** | **93.33%** | **82.69%** | **87.18%** | **96.67%** | **83.33%** | **87.82%** | **96.67%** |
| 0.45 | 0.50 | 1.35 | 0.20 | 97.44% | 93.33% | 98.08% | 93.33% | 82.05% | 86.54% | 96.67% | 82.69% | 87.18% | 96.67% |
| 0.45 | 0.50 | 1.50 | 0.20 | 92.31% | 93.33% | 92.95% | 93.33% | 79.49% | 81.41% | 96.67% | 78.85% | 82.05% | 96.67% |
| 0.50 | 0.50 | 1.25 | 0.20 | 98.08% | 93.33% | 98.08% | 93.33% | 82.69% | 87.18% | 96.67% | 82.69% | 87.18% | 96.67% |
| 0.50 | 0.50 | 1.50 | 0.55 | 92.31% | 93.33% | 77.56% | 100.00% | 79.49% | 81.41% | 96.67% | 65.38% | 67.95% | 100.00% |

Nhận xét chọn ngưỡng:
- Tại `MinCosine = 0.55`, các câu hỏi tiếng Việt không dấu/viết tắt (`"mang ho so cu di kham duoc khong"`, `"nhin an truoc xet nghiem bao lau"`) có cosine `bge-m3` khoảng `0.209–0.484` so với đoạn văn có dấu nên bị loại ở chế độ hybrid (kéo E2E Hybrid Hit@3 xuống 72,44%).
- Chọn bộ ngưỡng **`MinScore = 0.45`, `MinQueryCoverage = 0.50`, `MinTermIdf = 1.25`, `MinCosine = 0.20`** (giữ ngưỡng hợp nhất RRF semantic ở `max(0.55, MinCosine)`): đạt đồng thời từ chối ngoài phạm vi **96.67% ($\ge 90\%$)** và Hit@3 trong phạm vi **87.18% (BM25) / 87.82% (Hybrid) ($\ge 85\%$)**.

### Bảng so sánh trước và sau sửa trên bộ dev 216 câu (156 trong phạm vi, 60 ngoài phạm vi)

| Chế độ | Giai đoạn | Qua gate / 156 | Qua gate / 216 | Hit@1 | Hit@3 | MRR | Từ chối đúng / 60 câu | TB ms | p95 ms |
|---|---|---:|---:|---:|---:|---:|---:|---:|---:|
| bm25 | Trước sửa (PR #146) | Không áp gate | Không áp gate | 93.59% | 100.00% | 0.9658 | 3.33% (2/60) | 0.441 | 0.223 |
| bm25 | **Sau sửa** | Không áp gate | Không áp gate | **92.95%** | **98.08%** | **0.9519** | **93.33% (56/60)** | **0.482** | **0.264** |
| hybrid | Trước sửa (PR #146) | Không áp gate | Không áp gate | 94.87% | 100.00% | 0.9744 | 3.33% (2/60) | 16.995 | 26.674 |
| hybrid | **Sau sửa** | Không áp gate | Không áp gate | **93.59%** | **98.72%** | **0.9605** | **93.33% (56/60)** | **13.696** | **22.070** |
| end-to-end | Trước sửa (PR #146) | 89.10% | 84.72% | 83.33% | 89.10% | 0.8600 | 26.67% (16/60) | 3.889 | 6.151 |
| end-to-end | **Sau sửa** | **89.10%** | **85.65%** | **82.69%** | **87.18%** | **0.8462** | **96.67% (58/60)** | **4.039** | **6.198** |
| end-to-end-hybrid | Trước sửa (PR #146) | 89.10% | 84.72% | 84.62% | 89.10% | 0.8686 | 26.67% (16/60) | 22.169 | 28.299 |
| end-to-end-hybrid | **Sau sửa** | **89.10%** | **85.65%** | **83.33%** | **87.82%** | **0.8547** | **96.67% (58/60)** | **18.816** | **28.383** |

### Chạy thử thật 21 câu (17 câu trước + 4 câu ngoài phạm vi mục 3)

Bật `ollama serve` và backend `Development`, đăng nhập bệnh nhân demo (mật khẩu và token chỉ giữ trong RAM), hỏi đủ 21 câu rồi tắt hoàn toàn backend và Ollama:

| # | Câu hỏi | Tuyến thực tế | Gọi Ollama | Nguồn docId – heading | ms |
|---:|---|---|---|---|---:|
| 1 | Có cần nhịn ăn trước xét nghiệm không? | KnowledgeAnswer | Có | prepare-tests – Nhịn ăn trước xét nghiệm; prepare-tests – Uống nước và lấy mẫu | 1785.7 |
| 2 | Đi khám cần mang theo giấy tờ gì? | KnowledgeAnswer | Có | bring-documents – Giấy tờ nhận diện; bring-documents – Giấy hẹn và người đi cùng | 1631.1 |
| 3 | Quy trình một lượt khám thế nào? | KnowledgeAnswer | Có | visit-workflow – Từ đặt lịch tới khám; visit-workflow – Hoàn tất lượt khám | 1473.8 |
| 4 | Đăng ký tài khoản trên website thế nào? | KnowledgeAnswer | Có | accounts – Đăng ký và đăng nhập; accounts – Dùng tài khoản an toàn | 1592.6 |
| 5 | Trẻ em đi khám cần chuẩn bị gì? | KnowledgeAnswer | Có | bring-documents – Giấy tờ nhận diện; supported-visitors – Trẻ em đi khám | 1607.2 |
| 6 | Khi nào kết quả được công bố cho bệnh nhân? | KnowledgeAnswer | Có | diagnostic-results – Khi nào có thể xem kết quả; diagnostic-results – Xem kết quả trên website | 1565.8 |
| 7 | Bitcoin hôm nay tăng hay giảm? | UnclearInput | Không | — | 44.9 |
| 8 | Viết giúp bài thơ về mùa thu | UnclearInput | Không | — | 6.8 |
| 9 | đau ngực khó thở | SafetyEmergency | Không | — | 32.0 |
| 10 | Khám nội tổng quát giá bao nhiêu? | PricingInquiryResolved | Không | — | 16.6 |
| 11 | Tôi bị đau đầu chóng mặt | Gợi ý SP08; outcome=null | Không | — | 140.8 |
| 12 | Tôi muốn đặt lịch khám nội tổng quát | NamedSpecialtyResolved | Không | — | 8.3 |
| 13 | trễ hẹn 20p có sao k | KnowledgeAnswer | Có | appointments – Đến trễ và vắng mặt | 922.9 |
| 14 | nhịn ăn bao lâu trước khi lấy máu | KnowledgeAnswer | Có | prepare-tests – Nhịn ăn trước xét nghiệm; prepare-tests – Uống nước và lấy mẫu | 885.6 |
| 15 | cho tôi đặt lịch khám tim được không | Luồng hiện có; outcome=null | Không | — | 24.6 |
| 16 | hủy lịch hẹn ngày mai được không | NothingToCancel | Không | — | 35.8 |
| 17 | mang ho so cu di kham duoc khong | KnowledgeAnswer | Có | bring-documents – Hồ sơ và kết quả cũ; supported-visitors – Người cao tuổi và người cần hỗ trợ | 748.6 |
| 18 | Bitcoin tăng hay giảm? | UnclearInput | Không | — | 5.8 |
| 19 | thời tiết mai thế nào? | UnclearInput | Không | — | 6.3 |
| 20 | viết code python sắp xếp mảng được k | UnclearInput | Không | — | 6.1 |
| 21 | đội nào vô địch world cup? | UnclearInput | Không | — | 6.9 |

## Nâng cấp nội dung v2 — 08/11/2026

Giữ nguyên bộ mốc `rag-dev-questions.json`: **216 câu = 156 trong phạm vi + 60 ngoài phạm vi**. Bộ mới `rag-dev-questions-v2.json`: **46 câu = 36 trong phạm vi + 10 ngoài phạm vi**, gồm câu không dấu/viết tắt. Bộ gộp: **262 câu = 192 trong phạm vi + 70 ngoài phạm vi**. Không sửa/xóa/skip test cũ, không mở đề ẩn, không đổi model hoặc dataset ML.NET. Bộ v2 được cố định trước khi đo trước/sau; sau đó chỉ chỉnh nội dung corpus để vượt ngưỡng, không đổi câu hỏi hoặc ngưỡng.

Đo trên cùng máy Windows/.NET 10, chat `qwen2.5:7b`, embedding `bge-m3`, hoàn toàn cục bộ. Cấu hình cả trước/sau: `MinScore=0.45`, `MinQueryCoverage=0.5`, `MinTermIdf=1.25`, `MinCosine=0.2`, `TopK=4`. Testing được đồng bộ bốn ngưỡng, vẫn tắt RAG; Development vẫn bật. Corpus trước 12 tài liệu/36 đoạn; sau 12 tài liệu/43 đoạn, mỗi đoạn không quá 550 ký tự.

**MinCosine 0.2 gần như không lọc trên bộ dev hiện tại**; từ chối ngoài phạm vi chủ yếu nhờ độ phủ từ khóa BM25 (`MinQueryCoverage` + `MinTermIdf`) và gate trong chế độ end-to-end. Sàn này khác ngưỡng dense ranking `max(0.55, MinCosine)` dùng RRF. Không thay MinCosine và không quy việc tăng từ chối cho cosine.

### Đo trước/sau riêng từng bộ

Mỗi dòng là một lần đo thật; dựng index/warm-up không tính vào độ trễ. Hit@1/Hit@3/MRR dùng toàn bộ câu trong phạm vi của bộ tương ứng, kể cả câu bị gate loại. Từ chối dùng toàn bộ câu ngoài phạm vi. `bm25`/`hybrid` đo retrieval; `end-to-end*` đo safety + classifier + gate + retrieval, **không sinh câu trả lời, không chạy handler CSDL**. Các bảng lịch sử phía trên giữ nguyên số của lần đo cũ.

| Bộ | Chế độ | Giai đoạn | Trong/ngoài | Hit@1 | Hit@3 | MRR | Từ chối đúng | TB ms | p95 ms |
|---|---|---|---:|---:|---:|---:|---:|---:|---:|
| Mốc | bm25 | Trước | 156/60 | 92.95% | 98.08% | 0.9519 | 93.33% | 0.448 | 0.246 |
| Mốc | bm25 | Sau | 156/60 | 89.10% | 97.44% | 0.9300 | 95.00% | 0.518 | 0.312 |
| Mốc | hybrid | Trước | 156/60 | 93.59% | 98.72% | 0.9605 | 93.33% | 38.276 | 31.346 |
| Mốc | hybrid | Sau | 156/60 | 91.67% | 97.44% | 0.9434 | 95.00% | 11.185 | 20.876 |
| Mốc | end-to-end | Trước | 156/60 | 82.69% | 87.18% | 0.8462 | 96.67% | 3.858 | 6.231 |
| Mốc | end-to-end | Sau | 156/60 | 80.13% | 86.54% | 0.8317 | 98.33% | 3.849 | 5.815 |
| Mốc | end-to-end-hybrid | Trước | 156/60 | 83.33% | 87.82% | 0.8547 | 96.67% | 16.012 | 26.330 |
| Mốc | end-to-end-hybrid | Sau | 156/60 | 81.41% | 86.54% | 0.8387 | 98.33% | 17.249 | 28.424 |
| V2 | bm25 | Trước | 36/10 | 66.67% | 75.00% | 0.7083 | 90.00% | 1.040 | 0.523 |
| V2 | bm25 | Sau | 36/10 | 97.22% | 100.00% | 0.9861 | 90.00% | 1.150 | 0.626 |
| V2 | hybrid | Trước | 36/10 | 72.22% | 75.00% | 0.7361 | 90.00% | 15.751 | 29.904 |
| V2 | hybrid | Sau | 36/10 | 97.22% | 100.00% | 0.9861 | 90.00% | 18.846 | 33.571 |
| V2 | end-to-end | Trước | 36/10 | 61.11% | 63.89% | 0.6250 | 90.00% | 7.038 | 11.122 |
| V2 | end-to-end | Sau | 36/10 | 86.11% | 88.89% | 0.8750 | 90.00% | 6.914 | 14.960 |
| V2 | end-to-end-hybrid | Trước | 36/10 | 61.11% | 63.89% | 0.6250 | 90.00% | 28.285 | 77.832 |
| V2 | end-to-end-hybrid | Sau | 36/10 | 86.11% | 88.89% | 0.8750 | 90.00% | 25.205 | 42.864 |
| Gộp | bm25 | Trước | 192/70 | 88.02% | 93.75% | 0.9063 | 92.86% | 0.438 | 0.258 |
| Gộp | bm25 | Sau | 192/70 | 90.62% | 97.92% | 0.9405 | 94.29% | 0.469 | 0.322 |
| Gộp | hybrid | Trước | 192/70 | 89.58% | 94.27% | 0.9184 | 92.86% | 13.854 | 25.000 |
| Gộp | hybrid | Sau | 192/70 | 92.71% | 97.92% | 0.9514 | 94.29% | 12.448 | 21.356 |
| Gộp | end-to-end | Trước | 192/70 | 78.65% | 82.81% | 0.8047 | 95.71% | 3.751 | 4.940 |
| Gộp | end-to-end | Sau | 192/70 | 81.25% | 86.98% | 0.8398 | 97.14% | 3.908 | 4.385 |
| Gộp | end-to-end-hybrid | Trước | 192/70 | 79.17% | 83.33% | 0.8116 | 95.71% | 17.877 | 32.764 |
| Gộp | end-to-end-hybrid | Sau | 192/70 | 82.29% | 86.98% | 0.8455 | 97.14% | 18.961 | 32.960 |

Cả ba bộ sau sửa đều đạt E2E Hit@3 ≥85% và từ chối ≥90%, ở cả BM25 và hybrid. Bộ mốc Hit@3 giảm từ 87,18% xuống 86,54% (BM25), từ 87,82% xuống 86,54% (hybrid); không che giấu giảm recall khi thay corpus. Bộ v2 tăng từ 63,89% lên 88,89%; bộ gộp từ 82,81%/83,33% lên 86,98%. Retrieval Hit@3 không chứng minh trả lời có đủ từng chi tiết hoặc đúng mọi mệnh đề.

Bộ v2 còn bốn câu bị gate loại: hỏi đến sớm “bao nhiêu phút” bị phạm vi thuốc bắt nhầm, hỏi nghĩa trạng thái hủy bị classifier coi là thao tác, câu có “bác sĩ” bị trích nhầm tên, câu đăng nhập có “số điện thoại” vào tuyến cơ sở. Không sửa safety/classifier trong tác vụ này, không bỏ các câu này khỏi mẫu số. Câu ngoài phạm vi “Hướng dẫn bảo dưỡng xe máy định kỳ?” còn false positive ở retrieval E2E (1/10); giữ nguyên câu và ghi nhận giới hạn. Bộ mốc từ chối 59/60, bộ gộp 68/70. Không coi mức 90% trên 10 câu là kiểm định độc lập.

### Tái lập chọn file và bộ gộp

```powershell
dotnet run --project src/tools/ClinicManagement.AI.RagEval -- end-to-end App_Data/v2-baseline.json --questions baseline
dotnet run --project src/tools/ClinicManagement.AI.RagEval -- end-to-end App_Data/v2-only.json --questions v2
dotnet run --project src/tools/ClinicManagement.AI.RagEval -- end-to-end App_Data/v2-combined.json --questions combined
dotnet run --project src/tools/ClinicManagement.AI.RagEval -- end-to-end App_Data/v2-paths.json --questions src/backend/ClinicManagement.Infrastructure/AI/Knowledge/Eval/rag-dev-questions.json --questions src/backend/ClinicManagement.Infrastructure/AI/Knowledge/Eval/rag-dev-questions-v2.json
```

Thay `end-to-end` bằng `bm25`, `hybrid`, `end-to-end-hybrid` cho các chế độ khác. Bỏ `--questions` giữ cú pháp cũ và bộ mốc. Báo cáo mới có `questionFiles`, mẫu số động, `gateRejections`, `retrievalMisses`, `falsePositives`; không còn mô tả mẫu số 156 cố định khi chọn v2/gộp. File câu hỏi thiếu, rỗng/sai định dạng, thiếu một trong hai nhóm hoặc chọn trùng file bị từ chối; output phải dùng tên khác file câu hỏi, bảo vệ cả bản nguồn khi alias đọc bản copy build. Wildcard Eval hiện có copy v2 tới output/publish; test mới đọc chính file đã copy.

### Chạy thử API thực tế sau sửa

Backend Development và `ollama serve`, đăng nhập bệnh nhân demo; mật khẩu/token chỉ trong RAM, không ghi vào bảng/log kết quả. Hỏi lại đúng 21 câu trước và thêm 8 lượt; câu nhịn ăn được lặp theo yêu cầu. Giữ nguyên limiter và chờ cửa sổ khi nhận 429, không tính thời gian chờ vào latency thành công. Nguồn là những đoạn được cung cấp cho model, không phải kiểm định từng mệnh đề. Model chat được warm-up nền trước phép thử.

| # | Câu hỏi | Tuyến thực tế | Gọi Ollama | Nguồn docId – heading | ms |
|---:|---|---|---|---|---:|
| 1 | Có cần nhịn ăn trước xét nghiệm không? | KnowledgeAnswer (HTTP 200) | Có | prepare-tests – Nhịn ăn trước xét nghiệm; prepare-visit – Ăn uống và trang phục | 2481.6 |
| 2 | Đi khám cần mang theo giấy tờ gì? | KnowledgeAnswer (HTTP 200) | Có | bring-documents – Giấy tờ nhận diện; bring-documents – Giấy hẹn và người đi cùng | 1666.1 |
| 3 | Quy trình một lượt khám thế nào? | KnowledgeAnswer (HTTP 200) | Có | visit-workflow – Từ đặt lịch tới khám; visit-workflow – Hoàn tất lượt khám | 2273.9 |
| 4 | Đăng ký tài khoản trên website thế nào? | KnowledgeAnswer (HTTP 200) | Có | accounts – Đăng ký và đăng nhập; accounts – Dùng tài khoản an toàn | 2206.6 |
| 5 | Trẻ em đi khám cần chuẩn bị gì? | KnowledgeAnswer (HTTP 200) | Có | supported-visitors – Trẻ em đi khám; prepare-visit – Trước buổi khám | 2058.3 |
| 6 | Khi nào kết quả được công bố cho bệnh nhân? | KnowledgeAnswer (HTTP 200) | Có | diagnostic-results – Khi nào có thể xem kết quả; diagnostic-results – Xem kết quả trên website | 1629.0 |
| 7 | Bitcoin hôm nay tăng hay giảm? | UnclearInput (HTTP 200) | Không | — | 42.9 |
| 8 | Viết giúp bài thơ về mùa thu | UnclearInput (HTTP 200) | Không | — | 24.3 |
| 9 | đau ngực khó thở | SafetyEmergency (HTTP 200) | Không | — | 63.1 |
| 10 | Khám nội tổng quát giá bao nhiêu? | PricingInquiryResolved (HTTP 200) | Không | — | 33.4 |
| 11 | Tôi bị đau đầu chóng mặt | Gợi ý SP08 (HTTP 200) | Không | — | 134.2 |
| 12 | Tôi muốn đặt lịch khám nội tổng quát | NamedSpecialtyResolved (HTTP 200) | Không | — | 26.3 |
| 13 | trễ hẹn 20p có sao k | KnowledgeAnswer (HTTP 200) | Có | appointments – Đến trễ và vắng mặt | 1312.7 |
| 14 | nhịn ăn bao lâu trước khi lấy máu | KnowledgeAnswer (HTTP 200) | Có | prepare-tests – Nhịn ăn trước xét nghiệm; prepare-tests – Uống nước và lấy mẫu | 1139.6 |
| 15 | cho tôi đặt lịch khám tim được không | Luồng hiện có; outcome=null (HTTP 200) | Không | — | 42.4 |
| 16 | hủy lịch hẹn ngày mai được không | NothingToCancel (HTTP 200) | Không | — | 62.3 |
| 17 | mang ho so cu di kham duoc khong | KnowledgeAnswer (HTTP 200) | Có | bring-documents – Hồ sơ và kết quả cũ; supported-visitors – Phụ nữ mang thai đi khám | 1441.7 |
| 18 | Bitcoin tăng hay giảm? | UnclearInput (HTTP 200) | Không | — | 19.9 |
| 19 | thời tiết mai thế nào? | UnclearInput (HTTP 200) | Không | — | 30.2 |
| 20 | viết code python sắp xếp mảng được k | UnclearInput (HTTP 200) | Không | — | 19.9 |
| 21 | đội nào vô địch world cup? | UnclearInput (HTTP 200) | Không | — | 32.1 |
| 22 | nhịn ăn bao lâu trước khi lấy máu | KnowledgeAnswer (HTTP 200) | Có | prepare-tests – Nhịn ăn trước xét nghiệm; prepare-tests – Uống nước và lấy mẫu | 972.7 |
| 23 | siêu âm bụng có cần nhịn ăn không | KnowledgeAnswer (HTTP 200) | Có | prepare-tests – Siêu âm ổ bụng; prepare-tests – Nhịn ăn trước xét nghiệm | 1201.0 |
| 24 | siêu âm bàng quang chuẩn bị sao | KnowledgeAnswer (HTTP 200) | Có | prepare-tests – Siêu âm bàng quang và vùng chậu; prepare-tests – Siêu âm, chụp phim và an toàn | 1680.7 |
| 25 | chụp MRI cần lưu ý gì | KnowledgeAnswer (HTTP 200) | Có | prepare-tests – Cộng hưởng từ MRI và kim loại; prepare-tests – Siêu âm, chụp phim và an toàn | 1937.0 |
| 26 | dẫn bé đi khám mang giấy gì | KnowledgeAnswer (HTTP 200) | Có | bring-documents – Giấy tờ nhận diện; bring-documents – Giấy hẹn và người đi cùng | 1594.5 |
| 27 | dấu hiệu nào phải đi cấp cứu ngay | KnowledgeAnswer (HTTP 200) | Có | emergency – Dấu hiệu cần hỗ trợ khẩn cấp; emergency – Sốt kèm dấu hiệu nguy hiểm | 1902.7 |
| 28 | đi khám nên đến sớm bao lâu | KnowledgeAnswer (HTTP 200) | Có | prepare-visit – Trước buổi khám; appointments – Đến trễ và vắng mặt | 753.5 |
| 29 | xem kết quả xét nghiệm ở mục nào trên web | PatientReadResolved (HTTP 200) | Không | — | 86.4 |

### Nguyên văn câu trả lời của 8 lượt mới

**1. nhịn ăn bao lâu trước khi lấy máu**

> Nhịn ăn 8-12 giờ trước khi lấy máu để chuẩn bị cho xét nghiệm đường huyết lúc đói và mỡ máu. Nên lấy máu vào buổi sáng.

**2. siêu âm bụng có cần nhịn ăn không**

> Siêu âm bụng thường cần nhịn ăn khoảng 6-8 giờ, đặc biệt khi khảo sát gan mật. Hãy làm theo chỉ định cụ thể của bác sĩ hoặc nhân viên thực hiện xét nghiệm.

**3. siêu âm bàng quang chuẩn bị sao**

> Để chuẩn bị cho siêu âm bàng quang, bạn nên uống khoảng 0,5-1 lít nước lọc và nhịn tiểu trong khoảng 1 giờ trước khi thực hiện. Tuy nhiên, nếu bạn cảm thấy quá khó chịu, không nên cố nhịn tiểu. Hãy hỏi bác sĩ nếu có thắc mắc.

**4. chụp MRI cần lưu ý gì**

> Trước khi chụp MRI, cần tháo toàn bộ vật kim loại mang trên người theo hướng dẫn. Báo trước nếu có máy tạo nhịp, thiết bị cấy ghép hoặc mảnh kim loại trong người để nhân viên kiểm tra an toàn. Báo nếu có thai hoặc nghi có thai. Làm theo chỉ định cụ thể của bác sĩ hoặc nhân viên thực hiện.

**5. dẫn bé đi khám mang giấy gì**

> Khi dẫn bé đi khám, bạn nên mang theo giấy khai sinh hoặc CCCD của cha/mẹ/người giám hộ, cùng với sổ tiêm chủng nếu có. Ngoài ra, nên mang theo thông tin xác nhận lịch hẹn hoặc giấy hẹn tái khám.

**6. dấu hiệu nào phải đi cấp cứu ngay**

> Nếu có đau ngực, khó thở, yếu liệt nửa người, méo miệng, nói khó, co giật, bất tỉnh hoặc chảy máu không cầm, hãy gọi 115 hoặc đến cấp cứu ngay. Nếu sốt cao kèm cứng cổ hoặc lơ mơ, hoặc trẻ sốt co giật, cũng cần gọi 115 hoặc đến cơ sở cấp cứu ngay.

**7. đi khám nên đến sớm bao lâu**

> Nên đến sớm khoảng 15 phút để làm thủ tục tiếp nhận.

**8. xem kết quả xét nghiệm ở mục nào trên web**

> Có 0 chỉ định cận lâm sàng cho dịch vụ o muc nao tren web.

### Giới hạn thử thật và kiểm tra

Đủ 29/29 lượt bệnh nhân nhận HTTP 200; đây là thành công HTTP, không phải tất cả câu trả lời đúng mục đích. Trong 8 lượt mới, 7 lượt `KnowledgeAnswer`; câu xem kết quả ở mục nào trên web bị handler `DiagnosticLookup`/`PatientReadResolved` bắt trước RAG và trả nội dung không hướng dẫn menu. Giữ nguyên hành vi C# ngoài phạm vi được mở khóa, không thay câu hỏi để che lỗi. Câu triệu chứng cấp cứu dùng `SafetyEmergency`, không gọi Ollama; câu giá dùng CSDL, triệu chứng đau đầu/chóng mặt vẫn gợi ý SP08. Khách hỏi hướng dẫn nhận HTTP 401. Sau phép thử đã tắt backend và Ollama, không còn listener tại 5258/11434.

Các câu sinh có thể bỏ câu nhắc ưu tiên chỉ định riêng dù corpus có đủ; ví dụ câu nhịn ăn và đến sớm chỉ trả phần chính. Chưa có kiểm định độc lập về độ đầy đủ hoặc mọi mệnh đề do LLM sinh; 61 mục trong [RAG_CONTENT_REVIEW.md](RAG_CONTENT_REVIEW.md) vẫn chờ nhóm duyệt.

Test mới: 9 ca nội dung/v2 (copy bộ v2, nguồn hợp lệ, uniqueness, E2E BM25 Hit@3/từ chối và 6 tình huống bằng chứng chuẩn bị) cùng 14 ca chọn file/bảo vệ output/dữ liệu không hợp lệ. Tổng **155 test RAG cũ + mới đạt**, không skip, chạy từ output riêng `App_Data/rag-v2-final-artifacts` để không ảnh hưởng lượt toàn bộ chạy song song. Project test chỉ thêm reference có alias tới RagEval, tránh trùng lớp Program toàn cục với API; không sửa test cũ. CLI chọn hai đường dẫn tường minh cho số liệu chất lượng/mẫu số giống alias `combined`; các trường hợp thiếu giá trị, chọn trùng, output trùng tên input/bản nguồn và file không tồn tại đều bị từ chối. Bộ mốc được kiểm tra không có diff; SHA256 bộ v2 là `e052bbdd7a50acc5eb7680e5c732f41d59d2bbc9482c230cb23b056962df661e`, trùng bản copy đã dùng đo trước sửa.

Build toàn solution cuối cùng đạt, 0 lỗi (28 cảnh báo NU1902/NU1903 từ dependency hiện có; không đổi phiên bản package). `dotnet ef migrations has-pending-model-changes` báo không có thay đổi so với migration cuối. `git diff --check` sạch; `verify-ui` không áp dụng vì không sửa frontend.

Lượt toàn bộ `dotnet test src/backend/ClinicManagement.sln --no-build` kết thúc sau 13 phút 20 giây: **2.685 đạt, 1 lỗi, 0 skip / 2.686 test** (trước khi thêm 14 ca parser; các ca này đã được xác minh trong lượt RAG cuối). Test cũ lỗi là `AiSessionCleanupWorkerTests.ContinuesWithNextCleanupCycleAfterCycleFailure`, tại dòng 110 (`PurgeExpiredAsync` expected once, lúc Verify ghi nhận 0). Log ở `App_Data/rag-v2-all-tests.log`, TRX ở `App_Data/rag-v2-test-results/rag-v2-all.trx`; kết quả 155 test RAG ở `App_Data/rag-v2-final-test-results/rag-v2-focused-final.trx`. Không sửa/skip test, không sửa worker AI chưa mở khóa và không coi chạy riêng RAG xanh là toàn bộ xanh. Dừng trước commit/push/PR/CI/merge theo quy tắc test cũ của repo.

## Bổ sung điều hướng giao diện sau review — 08/11/2026

Theo mở khóa bổ sung, `KnowledgeQuestionPolicy.IsEarlyGuidance` nhận thêm chín cụm sau chuẩn hóa: `o muc nao`, `muc nao`, `o dau tren web`, `tren web`, `tren website`, `menu`, `trang nao`, `bam vao dau`, `vao dau de`. `IsCandidate` dùng cùng nhận diện cho câu không có dấu hỏi và intent đọc dữ liệu. Giữ nguyên loại trừ sở hữu, mã/số và entity lựa chọn; không sửa `AiSpecialtyService.PatientReadFilters.cs`, caller hoặc test/worker cleanup cũ.

28 ca test mới trong `RagNavigationRoutingTests.cs`: 3 câu điều hướng qua HTTP thực, 4 câu đọc cá nhân qua HTTP (gồm hai câu yêu cầu và biến thể có “trên web”/“trang nào”), 9 cụm nhận diện, 11 câu có sở hữu/mã/chuyên khoa và 1 ca kiểm tra 5 loại entity lựa chọn. Kết hợp các test trước: **183 test RAG đạt, 0 lỗi, 0 skip**. Nguồn trích xuất offline có thể là `visit-workflow` vì cũng chứa đúng tên menu; test kiểm tra tuyến RAG, có nguồn và có đúng nhãn menu, không ép chỉ một docId khi các tài liệu cùng hướng dẫn hợp lệ.

### Đo chốt sau sửa điều hướng

Giữ nguyên 216 câu mốc, 46 câu v2, corpus 12 tài liệu/43 đoạn, model và bốn ngưỡng. Chạy lại đủ bốn chế độ trên ba bộ. Bảng trước/sau nội dung và nguyên văn 8 câu phía trên giữ lịch sử lần đo trước mở khóa điều hướng; bảng dưới là chất lượng chốt. Latency một lượt tham khảo, máy có tải test khác, không suy thành benchmark độc lập.

| Bộ | Chế độ | Trong/ngoài | Hit@1 | Hit@3 | MRR | Từ chối đúng | TB ms | p95 ms |
|---|---|---:|---:|---:|---:|---:|---:|---:|
| Mốc | bm25 | 156/60 | 89.10% | 97.44% | 0.9300 | 95.00% | 0.553 | 0.326 |
| Mốc | hybrid | 156/60 | 91.67% | 97.44% | 0.9434 | 95.00% | 15.889 | 46.762 |
| Mốc | end-to-end | 156/60 | 80.13% | 86.54% | 0.8317 | 98.33% | 4.294 | 7.805 |
| Mốc | end-to-end-hybrid | 156/60 | 81.41% | 86.54% | 0.8387 | 98.33% | 16.956 | 26.059 |
| V2 | bm25 | 36/10 | 97.22% | 100.00% | 0.9861 | 90.00% | 1.055 | 0.678 |
| V2 | hybrid | 36/10 | 97.22% | 100.00% | 0.9861 | 90.00% | 21.721 | 30.616 |
| V2 | end-to-end | 36/10 | 86.11% | 88.89% | 0.8750 | 90.00% | 7.101 | 15.767 |
| V2 | end-to-end-hybrid | 36/10 | 86.11% | 88.89% | 0.8750 | 90.00% | 26.098 | 43.157 |
| Gộp | bm25 | 192/70 | 90.62% | 97.92% | 0.9405 | 94.29% | 0.505 | 0.346 |
| Gộp | hybrid | 192/70 | 92.71% | 97.92% | 0.9514 | 94.29% | 12.880 | 21.503 |
| Gộp | end-to-end | 192/70 | 81.25% | 86.98% | 0.8398 | 97.14% | 4.590 | 5.534 |
| Gộp | end-to-end-hybrid | 192/70 | 82.29% | 86.98% | 0.8455 | 97.14% | 19.216 | 30.189 |

### Thử thật lại câu 8 và hai câu điều hướng mới

Bật backend Development và Ollama cục bộ, đăng nhập bệnh nhân demo; không in/lưu mật khẩu hoặc token. Cả ba lượt trả `KnowledgeAnswer`, gọi Ollama, có nguồn đúng và HTTP 200. Câu khách thử lại vẫn HTTP 401. Sau đo đã tắt backend/Ollama, xác minh không còn listener 5258/11434. Không đổi dữ liệu câu hỏi để thay câu bị định tuyến sai; chính câu 8 cũ được thử lại nguyên văn.

| # | Câu hỏi | Tuyến | Ollama | Nguồn docId – heading | ms |
|---:|---|---|---|---|---:|
| 1 | xem kết quả xét nghiệm ở mục nào trên web | KnowledgeAnswer (HTTP 200) | Có | diagnostic-results – Xem kết quả trên website; diagnostic-results – Khi nào có thể xem kết quả | 3550.4 |
| 2 | xem đơn thuốc ở trang nào | KnowledgeAnswer (HTTP 200) | Có | prescriptions – Xem đơn thuốc; prescriptions – Thanh toán và lĩnh thuốc | 1689.1 |
| 3 | hóa đơn xem ở đâu trên website | KnowledgeAnswer (HTTP 200) | Có | payments – Thanh toán tại quầy; payments – Kiểm tra hóa đơn | 1245.8 |

Nguyên văn câu trả lời:

**1. xem kết quả xét nghiệm ở mục nào trên web**

> Bạn có thể xem kết quả xét nghiệm trên website bằng cách đăng nhập tài khoản, mở menu tài khoản, chọn "Kết quả cận lâm sàng" và sau đó chọn phiếu cận lâm sàng theo mã chỉ định, lượt khám và ngày.

**2. xem đơn thuốc ở trang nào**

> Bạn có thể xem đơn thuốc ở trang Đơn thuốc của tôi, sau khi đăng nhập và mở menu tài khoản. Tìm theo mã đơn, bác sĩ hoặc tên thuốc trong ô tìm kiếm, kiểm tra ngày khám và lời dặn; bấm In đơn thuốc để in đơn cần xem.

**3. hóa đơn xem ở đâu trên website**

> Bạn có thể xem hóa đơn bằng cách đăng nhập vào tài khoản, mở menu tài khoản, chọn "Hóa đơn của tôi", sau đó lọc và xem chi tiết từng hóa đơn.

### Xác minh test race và toàn bộ test

Theo ngoại lệ được người dùng cho phép, giữ nguyên `AiSessionCleanupWorkerTests.ContinuesWithNextCleanupCycleAfterCycleFailure` và worker. Chạy riêng đúng ba lần, cả ba đạt (205/207/215 ms); TRX ở `App_Data/rag-v2-race-results/`. Test race này được phép rerun CI một lần nếu chính nó fail; lần thứ hai fail thì dừng. Không mở rộng ngoại lệ sang lỗi khác.

Lượt toàn bộ dùng `--artifacts-path` đã dừng vì các test `IntentTrainingOutputDirectoryTests` giả định độ sâu cố định của output và tìm sai repository root. Đây là cấu hình chạy không phù hợp, không sửa/skip test để xử lý; lượt toàn bộ hợp lệ được chạy lại trong `bin/Debug/net10.0` chuẩn, log `App_Data/rag-v2-all-standard.log`, TRX `App_Data/rag-v2-all-standard-results/rag-v2-all-standard.trx`.

Kết quả lượt toàn bộ chuẩn: `Passed!  - Failed:     0, Passed:  2728, Skipped:     0, Total:  2728, Duration: 13 m 25 s - ClinicManagement.IntegrationTests.dll (net10.0)`.
