# Kết quả đánh giá RAG phát triển

## Phạm vi và máy đo

Đo ngày 07/11/2026 (giờ Việt Nam), trên Windows, AMD Ryzen 9 7945HX, RAM khả dụng báo bởi hệ thống 31,2 GiB, NVIDIA GeForce RTX 4060 Laptop GPU 8 GiB, .NET SDK 10.0.300, Ollama 0.40.15. Model chat `qwen2.5:7b` (id `845dbda0ea48`), embedding `bge-m3:latest` (id `790764642607`, 1024 chiều). Không sửa, huấn luyện hoặc thay artifact ML.NET hiện có.

Corpus: 12 tài liệu tiếng Việt, 36 đoạn. Bộ phát triển mới: 156 câu có expectedDocId và 20 câu ngoài phạm vi, tổng 176; gồm câu không dấu, viết tắt và lỗi gõ nhẹ. Không mở hoặc sử dụng bộ đề ẩn. Đây là số liệu trên bộ phát triển do cùng thay đổi biên soạn, không phải đánh giá độc lập hay bảo đảm chất lượng trên dữ liệu thật.

Cấu hình: MinScore=0,35; TopK=4; BM25 k1=1,2, b=0,75; điểm lexical `raw/(raw+5)`; RRF k=60; dense cosine≥0,55 trong tập ứng viên lexical. Cả hai chế độ dùng cùng bộ lọc chủ đề công khai. Vì vậy số liệu từ chối đo cả bộ lọc phạm vi và retrieval, không phải BM25 trần. Rank tính trên docId khác nhau trong tối đa bốn đoạn trả về. MRR của tài liệu không có trong kết quả bằng 0.

## Kết quả retrieval thực tế

Độ trễ chỉ tính truy xuất, bao gồm câu bị từ chối; không gồm dựng index, warm-up hoặc sinh câu trả lời. Một truy vấn warm-up được loại trước đo. Hybrid chỉ được ghi khi index hoàn chỉnh và tất cả lần gọi embedding truy vấn trong phép đo thành công. Có 149 truy vấn dùng embedding; các câu không vượt lexical gate không gọi model.

| Chế độ | Hit@1 | Hit@3 | MRR | Từ chối đúng ngoài phạm vi | Trung bình (ms) | p95 (ms) |
|---|---:|---:|---:|---:|---:|---:|
| bm25 | 89.74% | 94.87% | 0.9209 | 95.00% | 0.178 | 0.249 |
| hybrid | 84.62% | 91.67% | 0.8793 | 95.00% | 62.341 | 31.253 |

BM25 đạt ngưỡng test Hit@3≥85%. Hybrid thấp hơn BM25 trên bộ này; không có bằng chứng hybrid cải thiện chất lượng trong cấu hình hiện tại. Câu hỏi embedding đã được khái quát hóa để tránh dữ liệu bệnh nhân, có thể mất chi tiết so với câu gốc. Chưa điều chỉnh trọng số theo lỗi của bộ đề ẩn. p95 hybrid thấp hơn trung bình do một số request chậm ở đuôi phân phối; các số được ghi nguyên theo phép đo.

Các lần dựng index trước đó thất bại vì health check `localhost` hết timeout hai giây do kết nối IPv6 trên Windows. Sau khi ánh xạ localhost sang IPv4 và tắt proxy, đo hybrid hoàn tất. Các lần thất bại không được báo thành số hybrid. File cache chỉ chứa vector/tài liệu công khai, không có tài khoản hay câu hỏi cá nhân.

## Chạy thử API thực tế

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
```

Có thể thêm đường dẫn JSON báo cáo làm tham số thứ hai. Tool từ chối ghi kết quả hybrid nếu index hoặc query embedding lỗi. Không có tùy chọn đọc bộ đề ẩn. CI build project nhưng không chạy tool đánh giá hybrid. Các phép đo hiện tại là một lượt mỗi chế độ, không phải trung bình nhiều lần; số đo độ trễ thay đổi theo tải máy và trạng thái model/cache.
