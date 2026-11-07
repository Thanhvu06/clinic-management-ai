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
