# Tổng hợp kết quả thực nghiệm NCKH — ClinicCare AI

Tài liệu này tổng hợp toàn bộ số liệu thực nghiệm của hệ thống trợ lý AI và mô-đun hỏi đáp tri thức phòng khám (RAG cục bộ) trong dự án **ClinicCare AI**. Mọi số liệu được trích dẫn trực tiếp từ các báo cáo thực nghiệm đã lưu trong kho mã nguồn (`docs/ai/HIDDEN_EVAL.md`, `docs/ai/RAG_EVALUATION.md`) hoặc từ các phép đo trực tiếp trên máy thật trong phiên làm việc này, không ước lượng; quy tắc định dạng hiển thị số của bảng mới được ghi rõ tại bảng.

---

## 1. Cấu hình máy đo thực nghiệm

Các phép đo cục bộ trong phiên làm việc này (mục 4 và mục 5) được thực hiện trên cùng một máy trạm cá nhân (laptop) với thông số phần cứng và phần mềm như sau:

| Thành phần | Thông số chi tiết |
|---|---|
| CPU | AMD Ryzen 9 7945HX with Radeon Graphics |
| GPU rời (xử lý LLM/Embedding) | NVIDIA GeForce RTX 4060 Laptop GPU (`8188 MiB` VRAM, Driver `566.07` / `617.42`) |
| GPU tích hợp | AMD Radeon(TM) 610M (`0,5 GiB`) |
| RAM hệ thống | `31,19 GiB` (`33.487.876.096` byte) |
| Hệ điều hành | Windows (PowerShell / .NET runtime cục bộ) |
| .NET SDK | `10.0.300` (`net10.0`) |
| Ollama runtime | `0.40.15` (`http://127.0.0.1:11434`) |
| Mô hình sinh câu trả lời (Chat LLM) | `qwen2.5:7b` (`4,7 GB` trên đĩa; `4,8 GB` khi nạp `100% GPU`, context `4096`) |
| Mô hình nhúng (Embedding) | `bge-m3:latest` (`1,2 GB` trên đĩa; `689 MB` khi nạp `100% GPU`, context `4096`) |

---

## 2. Kết quả phân loại ý định trên bộ câu hỏi ẩn do người viết (`HIDDEN_EVAL.md`)

- **Nguồn số liệu:** `docs/ai/HIDDEN_EVAL.md`.
- **Thông tin bộ đề ẩn:** Viết tay bởi Nguyễn Thanh Vũ, 2026-10-18, không có AI hỗ trợ; nằm ngoài repo.
- **Ngày đo:** Không ghi trong nguồn.
- **Cấu hình máy:** Không ghi trong nguồn (chạy qua lệnh `ClinicManagement.AI.Training --hidden-eval`).
- **Phân định rõ ràng giữa hai tầng đánh giá:**
  - **T1 (`RoleIntentModel`):** Đánh giá mô hình học máy ML.NET phân loại ý định theo vai trò.
  - **T2 (`HybridIntentRouter`):** Đánh giá toàn bộ đường ống định tuyến nghiệp vụ kết hợp (rule, model ML.NET, ngữ cảnh tài nguyên, fallback). Số liệu T2 là độ chính xác nhãn định tuyến, **không phải là accuracy của model ML.NET**.

### 2.1. Lần chấm 1 (T2 theo cách cũ)

- **Nguồn file:** `docs/ai/HIDDEN_EVAL.md` (dòng 22–24).
- **Ngày viết đề:** 2026-10-18 (Viết tay bởi Nguyễn Thanh Vũ, không có AI hỗ trợ; nằm ngoài repo). Ngày đo: không ghi trong nguồn.
- **Chép nguyên dòng "Tổng" từ bảng accuracy tương ứng (`docs/ai/HIDDEN_EVAL.md:24`):**

| Phạm vi | N | T1 đúng | T1 accuracy | T1 macro-F1 | Coverage | Accepted accuracy | Không chắc | T1 Wilson 95% | T2 đúng | T2 accuracy | T2 macro-F1 | T2 Wilson 95% |
|---|---:|---:|---:|---:|---:|---:|---:|---|---:|---:|---:|---|
| Tổng | 105 | 86 | 81.90% | 82.24% | 58.10% | 91.80% | 41.90% | [73.46%, 88.10%] | 61 | 58.10% | 53.05% | [48.54%, 67.08%] |

*Ghi chú:* Ở Lần chấm 1, model ML.NET T1 đạt accuracy **81.90%** (86/105) và macro-F1 **82.24%**, Wilson 95% là **[73.46%, 88.10%]**. Định tuyến T2 đạt **58.10%** (61/105). Tuyệt đối không nhầm lẫn 58.10% là accuracy của model.

### 2.2. Lần chấm 2 (sau sửa router)

- **Nguồn file:** `docs/ai/HIDDEN_EVAL.md` (dòng 227–229).
- **Ngày viết đề:** 2026-10-18 (Viết tay bởi Nguyễn Thanh Vũ, không có AI hỗ trợ; nằm ngoài repo). Ngày đo: không ghi trong nguồn.
- **Chép nguyên dòng "Tổng" từ bảng accuracy tương ứng (`docs/ai/HIDDEN_EVAL.md:229`):**

| Phạm vi | N | T1 đúng | T1 accuracy | T1 macro-F1 | Coverage | Accepted accuracy | Không chắc | T1 Wilson 95% | Hỏi lại | T2 strict đúng | T2 strict accuracy | T2 strict macro-F1 | T2 strict Wilson 95% | T2 lenient đúng | T2 lenient accuracy | T2 lenient Wilson 95% |
|---|---:|---:|---:|---:|---:|---:|---:|---|---:|---:|---:|---:|---|---:|---:|---|
| Tổng | 105 | 86 | 81.90% | 82.24% | 58.10% | 91.80% | 41.90% | [73.46%, 88.10%] | 23 | 43 | 40.95% | 42.80% | [32.03%, 50.52%] | 66 | 62.86% | [53.31%, 71.49%] |

### 2.3. Lần chấm 3 (model v3, dữ liệu người viết)

- **Nguồn file:** `docs/ai/HIDDEN_EVAL.md` (dòng 411–432).
- **Ngày viết đề:** 2026-10-18 (Viết tay bởi Nguyễn Thanh Vũ, không có AI hỗ trợ; nằm ngoài repo). Ngày đo: không ghi trong nguồn.

#### T1: So sánh model v2 và model v3 (dòng 411–420)

| Model | Vai trò | N | Đúng | Accuracy | Macro-F1 | Accepted | Coverage | Accepted accuracy | Không chắc | Wilson 95% |
|---|---|---:|---:|---:|---:|---:|---:|---:|---:|---|
| v2 | Tổng | 105 | 86 | 81.90% | 82.24% | 61 | 58.10% | 91.80% | 41.90% | [73.46%, 88.10%] |
| v3 | Tổng | 105 | 95 | 90.48% | 90.88% | 101 | 96.19% | 92.08% | 3.81% | [83.35%, 94.74%] |

#### T2: Định tuyến runtime đã chọn (dòng 430–432)

| Vai trò | N | Hỏi lại | Strict đúng | Strict accuracy | Strict macro-F1 | Strict Wilson 95% | Lenient đúng | Lenient accuracy | Lenient Wilson 95% | rule | model | fallback | override |
|---|---:|---:|---:|---:|---:|---|---:|---:|---|---:|---:|---:|---:|
| Tổng | 105 | 24 | 57 | 54.29% | 52.89% | [44.77%, 63.49%] | 81 | 77.14% | [68.24%, 84.13%] | 58 | 31 | 10 | 6 |

---

## 3. Kết quả truy xuất tri thức RAG trên bộ câu hỏi phát triển (`RAG_EVALUATION.md`)

- **Nguồn file:** `docs/ai/RAG_EVALUATION.md`.
- **Ngày đo:** Đo ngày 07/11/2026 (giờ Việt Nam, dòng 5) và cập nhật đo chốt ngày 08/11/2026 (dòng 345, 351–368).
- **Cấu hình máy:** Windows, AMD Ryzen 9 7945HX, RAM khả dụng báo bởi hệ thống 31,2 GiB, NVIDIA GeForce RTX 4060 Laptop GPU 8 GiB, .NET SDK 10.0.300, Ollama 0.40.15 (dòng 5).
- **Tham số đánh giá:** MinScore = 0.45; MinQueryCoverage = 0.50; MinTermIdf = 1.25; MinCosine = 0.20; TopK = 4 (dòng 166, 408, 527).
- **Quy mô kho tri thức (corpus):** 12 tài liệu, 51 đoạn theo heading cấp 2 (thời điểm đo bảng 3.1 là 43 đoạn (tại thời điểm đó)); 68 mục cần nhóm duyệt trong [RAG_CONTENT_REVIEW.md](../ai/RAG_CONTENT_REVIEW.md) (trước đó là 61 mục (tại thời điểm đó)).

### 3.1. Bảng đo chốt sau sửa điều hướng (trích nguyên văn `docs/ai/RAG_EVALUATION.md:355-368`)

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

### 3.2. Bộ câu hỏi ẩn RAG (nhóm viết)

Bảng dưới đây được chừa sẵn để nhóm trưởng tự chạy trên file CSV xuất từ Excel (đặt ngoài repo) và điền số liệu. Công cụ `ClinicManagement.AI.RagEval` ở chế độ `--csv` chỉ in ra số liệu tổng hợp (`N`, `inScope`, `outside`, `hitAt1`, `hitAt3`, `mrr`, `correctRejection`, `averageMs`, `p95Ms`), tuyệt đối không in và không ghi nội dung câu hỏi ra màn hình hay ra file JSON.

- **Nguồn file:** File CSV bộ câu hỏi ẩn RAG do nhóm xuất từ Excel (đặt ngoài repo).
- **Ngày đo:** chưa đo.
- **Cấu hình máy:** chưa đo.

**Lệnh chạy để nhóm trưởng copy và thực thi:**

```powershell
# Chế độ 1: BM25 thuần (không cần Ollama)
dotnet run --project src/tools/ClinicManagement.AI.RagEval -- bm25 "D:\ket-qua-rag-an-bm25.json" --csv "D:\bo-cau-hoi-an-rag.csv"

# Chế độ 2: Hybrid BM25 + bge-m3 (cần bật Ollama)
dotnet run --project src/tools/ClinicManagement.AI.RagEval -- hybrid "D:\ket-qua-rag-an-hybrid.json" --csv "D:\bo-cau-hoi-an-rag.csv"

# Chế độ 3: End-to-end Gate + BM25 (không cần Ollama)
dotnet run --project src/tools/ClinicManagement.AI.RagEval -- end-to-end "D:\ket-qua-rag-an-e2e.json" --csv "D:\bo-cau-hoi-an-rag.csv"

# Chế độ 4: End-to-end Gate + Hybrid BM25 + bge-m3 (cần bật Ollama)
dotnet run --project src/tools/ClinicManagement.AI.RagEval -- end-to-end-hybrid "D:\ket-qua-rag-an-e2e-hybrid.json" --csv "D:\bo-cau-hoi-an-rag.csv"

# Phải build Release trước khi chạy hai lệnh dưới; cần Ollama với qwen2.5:7b và bge-m3
dotnet build src/backend/ClinicManagement.sln --configuration Release

# 1. Sinh bản đầy đủ và hai file riêng cho người chấm từ Excel đã điền câu hỏi
dotnet run --no-build --configuration Release --project src/tools/ClinicManagement.AI.RagEval -- blind-answers --xlsx "E:\NCKH\bo-cau-hoi-an\RAG_bo_cau_hoi_an_v1.xlsx" --out-dir "E:\NCKH\bo-cau-hoi-an\ket-qua" --seed 20261110 --retrieval hybrid

# 2. Sau khi chấm: gộp điểm vào file mới; không ghi đè bản master hoặc file người chấm
dotnet run --no-build --configuration Release --project src/tools/ClinicManagement.AI.RagEval -- merge-grades --master "E:\NCKH\bo-cau-hoi-an\ket-qua\RAG_ket_qua_day_du.xlsx" --grader1 "E:\NCKH\bo-cau-hoi-an\ket-qua\RAG_phieu_cham_NguoiCham1.xlsx" --grader2 "E:\NCKH\bo-cau-hoi-an\ket-qua\RAG_phieu_cham_NguoiCham2.xlsx" --out "E:\NCKH\bo-cau-hoi-an\ket-qua\RAG_da_gop_diem.xlsx"
```

CSV chấp nhận năm loại `Trong phạm vi`, `Diễn đạt khác`, `Ngoài phạm vi gần`, `Ngoài phạm vi xa`, `Khẩn cấp / an toàn` và loại cũ `Ngoài phạm vi`. Hai loại đầu bắt buộc có `Tài liệu đúng`; hai loại ngoài phạm vi luôn tính là không có tài liệu; loại khẩn cấp dùng tài liệu nếu được điền. Cột `Ý chính cần có` không tham gia tính retrieval. Công cụ tự tìm dòng tiêu đề sau ghi chú và bỏ dòng `STT=VD`. Báo cáo retrieval bổ sung `byCategory`; chỉ số không có mẫu phù hợp trong từng loại là `null`.

Chế độ `blind-answers` gọi pipeline chat runtime, gồm gate và kiểm tra an toàn đầu vào/đầu ra. **Hai chế độ dùng cùng kết quả truy xuất, chỉ khác bước sinh câu trả lời**: LLM bật chat `qwen2.5:7b`, trích đoạn tắt chat để đi đúng nhánh fallback `KnowledgeExtractive`. `--retrieval hybrid` (mặc định) dùng BM25 + `bge-m3`; `--retrieval bm25` dùng BM25 cho cả hai chế độ và không gọi embedding. Một kết quả truy xuất được chụp lại trong bộ nhớ cho từng cặp trả lời; không truy xuất lần hai cho trích đoạn. Nguồn trích dẫn của đầu ra vẫn tuân theo runtime: LLM có thể trích các đoạn hỗ trợ, trích đoạn chỉ dẫn nguồn đoạn đầu tiên.

Session/audit và dữ liệu nghiệp vụ vẫn được cô lập trong SQLite trống trong bộ nhớ. Seeder hiện có `DevelopmentDataSeeder.SeedMasterDataAsync` gọi seeder tổng `SeedAsync`, vẫn tạo dữ liệu bệnh nhân; các cờ hiện có chỉ tắt lịch làm việc và lịch hẹn, không tách riêng đầy đủ danh mục chuyên khoa, bác sĩ, dịch vụ/giá, cơ sở và thuốc công khai. Vì vậy công cụ không gọi seeder này và không tự viết seeder mới. **Giới hạn CSDL:** câu hỏi nghiệp vụ phụ thuộc dữ liệu danh mục nhận phản hồi runtime ứng với CSDL trống, có thể khác web Development đã seed.

Thời gian mỗi câu gồm lời gọi chat, không gồm dựng chỉ mục và warm-up. LLM chạy trước nên độ trễ LLM gồm lần truy xuất của cặp; trích đoạn dùng lại kết quả trong bộ nhớ. Seed chỉ tái lập phép gán A/B, không bảo đảm LLM sinh cùng văn bản hoặc cùng độ trễ. Bỏ `--seed` để tự sinh và ghi seed trong tổng kết console. Console có tiến độ, tổng số câu, seed, chế độ retrieval và đường dẫn kết quả; hai CSV UTF-8 BOM giữ nguyên thứ tự STT. File `chay-thu.json` lưu `seed`, `retrieval`, `count`, `measuredAtUtc`, `model`, không chứa câu hỏi hay câu trả lời. Chọn thư mục kết quả mới nếu đã có file để tránh ghi đè.

Mẫu trống trong repo: `docs/nckh/templates/RAG_bo_cau_hoi_an_mau.xlsx`. Điền câu hỏi từ dòng **4** của sheet **Câu hỏi**, cột **A–F** lần lượt là STT, Người viết, Câu hỏi, Loại, Tài liệu đúng, Ý chính cần có; dòng ví dụ `VD` được bỏ qua. `--xlsx` thay cho `--csv`, hai tùy chọn loại trừ nhau; chế độ CSV vẫn được giữ. File Excel gốc không bị sửa.

Với `--xlsx`, thư mục kết quả có thêm ba workbook bên cạnh hai CSV và JSON hiện có. `RAG_ket_qua_day_du.xlsx` giữ đủ sáu sheet và tự điền **Phiếu chấm!D–E**, **Khóa!B–D** theo STT ở cột A từng sheet. Chỉ gửi `RAG_phieu_cham_NguoiCham1.xlsx` cho người chấm 1 và `RAG_phieu_cham_NguoiCham2.xlsx` cho người chấm 2: ba sheet **Khóa**, **Tính**, **Tổng hợp** đã bị xóa hẳn trong hai file này. Người chấm 1 điền **F–J**, người chấm 2 điền **K–O** trên sheet **Phiếu chấm**. Giữ bản đầy đủ và khóa riêng đến khi hoàn tất chấm mù.

Lệnh `merge-grades` ghép theo STT, không theo vị trí dòng. Cột **K–O** lấy từ `--grader2`; cột **F–J** lấy từ `--grader1` nếu có, hoặc giữ điểm trong `--master` nếu không truyền. Tổng kết chỉ báo số lượt dòng có điểm/ghi chú đã chép và số STT có điểm không khớp (cộng cả hai người chấm), không in giá trị ô. Mọi file đầu ra phải là file mới, không ghi đè để tránh mất điểm đã chấm. Công thức, validation, conditional formatting, định dạng và freeze panes được giữ; workbook được đặt chế độ tự tính lại khi mở bằng Excel. Công cụ dùng ClosedXML 0.105.1, giấy phép MIT.

| Chế độ | Tổng số câu (`N`) | Trong phạm vi | Ngoài phạm vi | Hit@1 | Hit@3 | MRR | Từ chối đúng | TB (ms) | p95 (ms) |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| `bm25` | chưa đo | chưa đo | chưa đo | chưa đo | chưa đo | chưa đo | chưa đo | chưa đo | chưa đo |
| `hybrid` | chưa đo | chưa đo | chưa đo | chưa đo | chưa đo | chưa đo | chưa đo | chưa đo | chưa đo |
| `end-to-end` | chưa đo | chưa đo | chưa đo | chưa đo | chưa đo | chưa đo | chưa đo | chưa đo | chưa đo |
| `end-to-end-hybrid` | chưa đo | chưa đo | chưa đo | chưa đo | chưa đo | chưa đo | chưa đo | chưa đo | chưa đo |

---

### 3.3. So sánh BM25, dense và hybrid — bộ phát triển, mức tài liệu

Đo thật ngày 10/11/2026 bằng `compare-retrieval --questions combined`, Ollama `bge-m3` bật, trên cùng **262 câu** (192 câu có tài liệu đúng, 70 câu ngoài phạm vi), **12 tài liệu / 51 đoạn**. Dense tính cosine trên toàn bộ đoạn, dùng lại index/cache embedding do runtime xây dựng, không lọc ứng viên bằng BM25. Giữ cấu hình hiện có: top-k = 4 đoạn, MinCosine = 0,20; hành vi BM25/hybrid và pipeline trả lời runtime không thay đổi. Đây là retrieval thuần, chưa qua gate/safety của chế độ end-to-end.

| Chế độ | N | Có tài liệu đúng | Ngoài phạm vi | Hit@1 | Hit@3 | MRR | Từ chối đúng |
|---|---:|---:|---:|---:|---:|---:|---:|
| BM25 | 262 | 192 | 70 | 0.8958333333 | 0.984375 | 0.9375 | 0.9428571429 (66/70) |
| Dense | 262 | 192 | 70 | 0.8385416667 | 0.953125 | 0.8932291667 | 0 (0/70) |
| Hybrid | 262 | 192 | 70 | 0.9375 | 0.984375 | 0.9592013889 | 0.9428571429 (66/70) |

Hit@1, Hit@3 và MRR tính trên 192 câu có tài liệu đúng, sau khi gộp ID tài liệu trùng nhau trong top-4 đoạn; không phải đánh giá đúng đoạn hoặc độ đúng câu trả lời. Từ chối đúng là tỷ lệ không trả về đoạn nào trên 70 câu không có tài liệu đúng. Bảng hiển thị 10 chữ số có nghĩa như console; JSON giữ độ chính xác số thực của phép tính.

Bộ JSON dev chưa có cột `Loại`: báo cáo xếp 192 câu có tài liệu vào `Trong phạm vi`, 70 câu còn lại vào loại cũ `Ngoài phạm vi`. Không suy diễn nhãn `Diễn đạt khác`, `Ngoài phạm vi gần`, `Ngoài phạm vi xa` hay `Khẩn cấp / an toàn`; bốn nhóm này có N=0, chỉ số không áp dụng là `null` trong JSON / `NA` trên console. Với CSV/Excel có nhãn, báo cáo tách đúng từng loại đầu vào.

McNemar ghép cặp Hit@1 trên **192 câu có tài liệu đúng**; b = chế độ trái đúng/phải sai, c = trái sai/phải đúng:

| Cặp trái–phải | b | c | p hai phía | Phương pháp |
|---|---:|---:|---:|---|
| BM25–Dense | 23 | 12 | 0.09096894798 | Chi-square hiệu chỉnh liên tục |
| BM25–Hybrid | 3 | 11 | 0.05737304688 | Exact binomial |
| Dense–Hybrid | 2 | 21 | 6.604194641E-05 | Exact binomial |

Dùng exact binomial khi b+c < 25; còn lại dùng chi-square với hiệu chỉnh liên tục: χ² = max(0, |b−c|−1)²/(b+c), 1 bậc tự do. Đây là p-value thô, chưa hiệu chỉnh cho ba phép so sánh. Không kết luận chất lượng trên bộ ẩn từ kết quả bộ phát triển.

**Lệnh người dùng tự chạy trên bộ ẩn** (phải build Release trước; các đường dẫn dưới đây chỉ là hướng dẫn, không được dùng trong lần đo dev này). JSON chỉ chứa số tổng hợp; `--output` phải trỏ đến file mới, không ghi đè:

```powershell
dotnet build "C:\Projects\clinic-management-ai\src\backend\ClinicManagement.sln" -c Release

dotnet run --no-build -c Release --project "C:\Projects\clinic-management-ai\src\tools\ClinicManagement.AI.RagEval" -- compare-retrieval --xlsx "E:\NCKH\bo-cau-hoi-an\RAG_bo_cau_hoi_an_v1.xlsx" --output "E:\NCKH\bo-cau-hoi-an\compare-retrieval.json"

dotnet run --no-build -c Release --project "C:\Projects\clinic-management-ai\src\tools\ClinicManagement.AI.RagEval" -- rq1-stats --xlsx "E:\NCKH\bo-cau-hoi-an\ket-qua\RAG_da_gop_diem.xlsx" --output "E:\NCKH\bo-cau-hoi-an\ket-qua\rq1-stats.json"
```

`compare-retrieval` cũng nhận `--questions baseline|v2|combined|path.json` hoặc `--csv path.csv`; chỉ chọn một nguồn. Lệnh `dense` nhận cùng tùy chọn để đo riêng dense. Mặc định nguồn câu hỏi là baseline. Công cụ dừng nếu không dựng/đọc được index bge-m3 hoặc hybrid bị rơi về BM25 do lỗi embedding, tránh gắn nhãn sai phép đo.

**RQ1 sau khi gộp điểm:** nối các sheet theo STT từ dòng 4, bỏ `VD`; lấy Loại ở `Câu hỏi!D`, khóa A/B ở `Khóa!B`, độ trễ LLM/trích đoạn ở `Khóa!C–D`. Một câu đủ dữ liệu phải có bốn điểm nguyên 0/1/2 (`Phiếu chấm!F,G,K,L`) và bốn dấu an toàn `Đạt`/`Không đạt` (`H,I,M,N`); câu thiếu được đếm riêng và loại khỏi các thống kê. Điểm mỗi chế độ là trung bình hai người sau giải khóa. Không đọc nội dung câu hỏi, câu trả lời hay ghi chú để tính thống kê; không xuất chúng ra console/JSON.

Wilcoxon bỏ cặp chênh lệch bằng 0, dùng hạng trung bình cho ties; n không bằng 0 ≤ 25 dùng phân phối exact của mọi phép đổi dấu trên midranks, còn lại xấp xỉ chuẩn có hiệu chỉnh ties, không hiệu chỉnh liên tục. Tất cả cặp bằng nhau cho p=1, r=0. Effect size r = |z|/√n, z lấy từ phương sai midranks kể cả khi p tính exact. Báo cả tổng và từng Loại; nhóm rỗng cho chỉ số không áp dụng là null/NA.

Cờ không an toàn của một chế độ bật khi ít nhất một người chấm `Không đạt`; McNemar trên hai cờ dùng cùng quy tắc trên. Cohen's kappa và kappa trọng số tuyến tính (trọng số 1−|i−j|/2) gộp hai lượt A/B của mỗi câu, tức 2N cặp điểm hai người; kappa không xác định do biên hằng là null/NA. Độ trễ dùng các câu đủ điểm: trung vị lấy trung bình hai vị trí giữa, p95 dùng nearest rank. Mọi p-value hai phía, chưa hiệu chỉnh đa kiểm định. Công cụ dùng MathNet.Numerics 5.0.0 (MIT) cho hàm erfc; thuật toán thống kê được kiểm bằng dữ liệu giả có đáp án biết trước. Tham khảo định nghĩa [McNemar của statsmodels](https://www.statsmodels.org/stable/generated/statsmodels.stats.contingency_tables.mcnemar.html) và [Wilcoxon của SciPy](https://docs.scipy.org/doc/scipy/reference/generated/scipy.stats.wilcoxon.html); ngưỡng chọn exact theo yêu cầu thí nghiệm ở đây.

## 4. Kết quả đo thời gian trả lời thực tế trên laptop (`POST /api/v1/ai/chat`)

- **Nguồn dữ liệu câu hỏi:** 20 câu hỏi trong phạm vi thuộc bộ phát triển `src/backend/ClinicManagement.Infrastructure/AI/Knowledge/Eval/rag-dev-questions-v2.json` (tuyệt đối không dùng bộ câu hỏi ẩn), mỗi câu gọi 3 lượt liên tiếp (`20 câu × 3 lượt = 60 request` cho mỗi chế độ) qua endpoint thật `POST http://localhost:5258/api/v1/ai/chat`.
- **Thời điểm đo:** Phiên làm việc ngày 09/11/2026 (`2026-11-09T06:45:52.4498364+00:00`).
- **Cấu hình máy:** AMD Ryzen 9 7945HX, RAM `31,19 GiB`, GPU NVIDIA GeForce RTX 4060 Laptop GPU (`8188 MiB` VRAM), Ollama `0.40.15` (`qwen2.5:7b` + `bge-m3:latest`), .NET SDK `10.0.300`.
- **Phương pháp tách Cold / Warm:** Trước lượt gọi đầu tiên của chế độ bật Ollama, mô hình `qwen2.5:7b` được giải phóng khỏi bộ nhớ (`keep_alive: 0`) để đo chính xác độ trễ câu đầu tiên khi phải nạp mô hình (`Cold`, `N = 1`) tách biệt với `59` lượt gọi tiếp theo khi mô hình đã sẵn sàng trên GPU (`Warm`, `N = 59`). Tương tự ở chế độ tắt Ollama (`Rag:IsEnabled = false`), câu đầu tiên sau khi khởi động tiến trình backend được ghi riêng (`Cold`, `N = 1`) và `59` lượt tiếp theo được tổng hợp vào nhóm `Warm` (`N = 59`).

### 4.1. Bảng độ trễ End-to-End có Ollama (`qwen2.5:7b`) và khi tắt Ollama (Fallback trích đoạn)

| Chế độ vận hành | Nhóm lượt gọi | Số lượt (`N`) | Cold / Lượt đầu (ms) | TB / Mean (ms) | Min (ms) | p50 (ms) | p95 (ms) | Max (ms) | Phân bố `dialogueOutcome` |
|---|---|---:|---:|---:|---:|---:|---:|---:|---|
| Bật Ollama (`qwen2.5:7b` + `bge-m3:latest`) | Câu đầu sau khởi động (`Cold`) | 1 | 4803,73 | 4803,73 | 4803,73 | 4803,73 | 4803,73 | 4803,73 | `KnowledgeAnswer: 1` |
| Bật Ollama (`qwen2.5:7b` + `bge-m3:latest`) | Các câu sau (`Warm`) | 59 | — | 660,68 | 4,54 | 623,63 | 1399,13 | 1522,93 | `KnowledgeAnswer: 55`, `OutOfScopeMedicalRequest: 3`, `MEDICAL_PRESCRIPTION_OUT_OF_SCOPE: 1` |
| Bật Ollama (`qwen2.5:7b` + `bge-m3:latest`) | Toàn bộ (`All`) | 60 | 4803,73 | 729,73 | 4,54 | 623,63 | 1399,13 | 4803,73 | `KnowledgeAnswer: 56`, `OutOfScopeMedicalRequest: 3`, `MEDICAL_PRESCRIPTION_OUT_OF_SCOPE: 1` |
| Tắt Ollama (Fallback trích đoạn `KnowledgeExtractive`) | Câu đầu sau khởi động (`Cold`) | 1 | 373,11 | 373,11 | 373,11 | 373,11 | 373,11 | 373,11 | `KnowledgeExtractive: 1` |
| Tắt Ollama (Fallback trích đoạn `KnowledgeExtractive`) | Các câu sau (`Warm`) | 59 | — | 15,85 | 3,02 | 13,39 | 39,12 | 64,62 | `KnowledgeExtractive: 56`, `OutOfScopeMedicalRequest: 3` |
| Tắt Ollama (Fallback trích đoạn `KnowledgeExtractive`) | Toàn bộ (`All`) | 60 | 373,11 | 21,81 | 3,02 | 13,39 | 39,12 | 373,11 | `KnowledgeExtractive: 57`, `OutOfScopeMedicalRequest: 3` |

### 4.2. Bảng mức tiêu thụ bộ nhớ RAM và VRAM khi chạy thực nghiệm

- **Nguồn đo:** Đọc trực tiếp từ tiến trình hệ điều hành (`WorkingSet64`, `PrivateMemorySize64`), lệnh `ollama ps` và `nvidia-smi` ngay sau khi hoàn tất 60 lượt gọi ở mỗi chế độ trong phiên đo ngày 09/11/2026.
- **Thời điểm đo:** `2026-11-09T06:45:52.4498364+00:00`.
- **Cấu hình máy:** AMD Ryzen 9 7945HX, RAM `31,19 GiB`, NVIDIA GeForce RTX 4060 Laptop GPU (`8188 MiB` VRAM).

| Chế độ vận hành | Tiến trình / Thành phần | RAM Working Set (MiB) | RAM Private Memory (MiB) | VRAM GPU sử dụng | Ghi chú từ `ollama ps` / `nvidia-smi` |
|---|---|---:|---:|---|---|
| Bật Ollama (`qwen2.5:7b` + `bge-m3:latest`) | Backend (`ClinicManagement.Api`) | 259,52 | 134,52 | — | Đã nạp chỉ mục chunk trong bộ nhớ |
| Bật Ollama (`qwen2.5:7b` + `bge-m3:latest`) | Ollama (`ollama` + `llama-server`) | 2271,72 | 8132,28 | `5426 MiB / 8188 MiB` (toàn GPU) | `qwen2.5:7b`: `4,8 GB` (`100% GPU`, context `4096`); `bge-m3:latest`: `689 MB` (`100% GPU`, context `4096`) |
| Tắt Ollama (Fallback trích đoạn) | Backend (`ClinicManagement.Api`) | 245,23 | 145,19 | — | Chỉ dùng BM25 + ghép đoạn trích định sẵn (`DeterministicFallback`) |

---

## 5. Kết quả đo che thông tin cá nhân (`KnowledgePrivacyMask`)

- **Nguồn file test:** `src/backend/ClinicManagement.IntegrationTests/RagEvalCsvAndPrivacyTests.cs` (`GetSyntheticPrivacyDataset`, `Synthetic_privacy_mask_dataset_has_at_least_60_questions_and_achieves_expected_precision_and_recall`).
- **Đối tượng được kiểm tra:** Lớp `src/backend/ClinicManagement.Infrastructure/AI/Knowledge/KnowledgePrivacyMask.cs` (giữ nguyên toàn bộ biểu thức chính quy hiện có, không chỉnh sửa logic).
- **Ngày đo:** Phiên làm việc ngày 09/11/2026 (chạy trực tiếp trong bộ kiểm thử tích hợp xUnit trên `.NET 10.0`).
- **Cấu hình máy:** AMD Ryzen 9 7945HX, RAM `31,19 GiB`, .NET SDK `10.0.300`.
- **Cấu trúc bộ dữ liệu giả (`70` câu tổng hợp):**
  - `55` câu có chứa thông tin định danh cá nhân (PII) gồm 4 nhóm: số điện thoại nhiều định dạng (`16` câu), CCCD 12 số (`12` câu), địa chỉ email (`12` câu), mã bệnh nhân / lịch hẹn / hồ sơ `BN`, `APT`, `APPT`, `PAT`, `MRN`, `HS`, `LH`, `PK`, `RX` và ngữ cảnh mã (`15` câu).
  - `15` câu hỏi y tế/hành chính tổng quát không chứa thông tin cá nhân (chứa các con số thông thường như số giờ nhịn ăn `8 đến 12 giờ`, lượng nước `0,5 đến 1 lít`, số cấp cứu `115`, huyết áp `120/80 mmHg`, số phòng khám `PK-102`, nhóm tuổi chung `60 tuổi`) để đo tỷ lệ báo nhầm (False Positive).

| Nhóm câu hỏi thử nghiệm | Số câu (`N`) | Có PII thực tế | Che đúng (`TP`) | Lọt thông tin (`FN`) | Báo nhầm (`FP`) | Giữ nguyên đúng (`TN`) | Precision | Recall |
|---|---:|---:|---:|---:|---:|---:|---:|---:|
| Số điện thoại nhiều định dạng (`Phone`) | 16 | 16 | 15 | 1 | 0 | 0 | 100,00% | 93,75% |
| Căn cước công dân 12 số (`CCCD`) | 12 | 12 | 12 | 0 | 0 | 0 | 100,00% | 100,00% |
| Địa chỉ thư điện tử (`Email`) | 12 | 12 | 12 | 0 | 0 | 0 | 100,00% | 100,00% |
| Mã bệnh nhân / lịch hẹn / hồ sơ (`BN`/`APT`/...) | 15 | 15 | 15 | 0 | 0 | 0 | 100,00% | 100,00% |
| Câu hỏi không có PII (`Negative`) | 15 | 0 | 0 | 0 | 2 | 13 | — | — |
| **Tổng hợp toàn bộ bộ test** | **70** | **55** | **54** | **1** | **2** | **13** | **96,43%** (`54/56`) | **98,18%** (`54/55`) |

- **Chi tiết 1 trường hợp lọt (`FN = 1`):** Câu số 16 (`"Trường hợp khách ghi số ngăn bằng dấu gạch chéo 0901/234/567 trên ghi chú."`) dùng ký tự `/` làm dấu phân cách giữa các cụm số điện thoại; biểu thức chính quy `Phone` và `LongNumber` hiện chỉ cho phép khoảng trắng, dấu chấm, dấu gạch ngang và dấu ngoặc đơn (`[\s().-]*`) nên không nhận diện được chuỗi `0901/234/567`.
- **Chi tiết 2 trường hợp báo nhầm (`FP = 2`):**
  1. Câu số 69 (`"Phòng khám nội tổng quát nằm ở phòng PK-102 tầng 1 phải không?"`): ký hiệu số phòng `PK-102` trùng tiền tố `PK` của mã phiếu khám trong biểu thức `RecordCode` nên bị thay bằng `[ĐÃ CHE]`.
  2. Câu số 70 (`"Người trên 60 tuổi đi khám sức khỏe định kỳ cần nhịn ăn bao lâu?"`): cụm từ chỉ nhóm tuổi chung `60 tuổi` khớp biểu thức `Age` (`\b\d{1,3}\s*(?:tuổi|tuoi)\b`) nên bị che dù không gắn với cá nhân cụ thể.

---

## 6. Hạn chế

1. **Phân biệt rạch ròi giữa độ chính xác mô hình học máy ML.NET (T1) và độ chính xác định tuyến toàn hệ thống (T2):**
   - Độ chính xác của riêng model ML.NET (`RoleIntentModel`) ở Lần chấm 1 đạt **81.90%** (86/105) và Lần chấm 3 đạt **90.48%** (95/105).
   - Tỷ lệ **58.10%** (61/105) ở Lần chấm 1 và **54.29%** (57/105 strict) ở Lần chấm 3 là độ chính xác nhãn định tuyến cuối cùng của toàn hệ thống `HybridIntentRouter` (khi kết hợp bộ quy tắc, kiểm tra ngữ cảnh tài nguyên, bộ lọc an toàn và fallback khi thiếu context). Tuyệt đối không được gọi 58.10% hay 54.29% là accuracy của model ML.NET.
   - Nguyên nhân định tuyến T2 strict thấp hơn T1 model là do câu hỏi trong đề ẩn không truyền kèm mã hồ sơ/lịch hẹn trong ngữ cảnh màn hình (`CurrentRoute` / `ResourceContext`), buộc router phải trả về câu hỏi làm rõ (`Clarification`, 24 câu ở Lần 3) thay vì tự ý gọi công cụ.
2. **Hiện tượng thời gian trung bình (TB) lớn hơn phân vị thứ 95 (`p95`) trong các phép đo truy xuất RAG:**
   - Trong bảng kết quả `RAG_EVALUATION.md:20` và `docs/ai/RAG_EVALUATION.md:355-368` (ví dụ Mốc `bm25`: TB 0.553 ms > p95 0.326 ms; V2 `bm25`: TB 1.055 ms > p95 0.678 ms; Gộp `bm25`: TB 0.505 ms > p95 0.346 ms), giá trị trung bình cộng cao hơn p95.
   - Nguyên nhân đã được ghi rõ tại `docs/ai/RAG_EVALUATION.md:20`: *"p95 hybrid thấp hơn trung bình do một số request chậm ở đuôi phân phối; các số được ghi nguyên theo phép đo."* Trên các tập mẫu, tuyệt đại đa số truy vấn hoàn tất cực nhanh dưới mili-giây, nhưng một vài câu đầu tiên kích hoạt nạp đối tượng hoặc ngoại lai đuôi kéo trung bình cộng lên.
3. **Độ trễ khởi động lạnh (`Cold Start`) khi nạp mô hình ngôn ngữ lớn cục bộ:**
   - Khi mô hình `qwen2.5:7b` chưa nằm trong VRAM của GPU, lượt gọi đầu tiên qua `POST /api/v1/ai/chat` mất `4803,73 ms` (~4,8 giây), cao gấp ~`7,7` lần so với trung vị các lượt gọi ấm (`p50 = 623,63 ms`, `p95 = 1399,13 ms`) và tiêu tốn `5426 MiB` VRAM cùng `2271,72 MiB` RAM Working Set của tiến trình Ollama/llama-server. Trên các máy không có GPU rời đủ VRAM (dưới 6 GB), độ trễ sinh câu trả lời sẽ tăng mạnh hoặc phải chuyển sang chế độ fallback trích đoạn (`p50 = 13,39 ms`).
4. **Hạn chế của cổng lọc ứng viên (`KnowledgeQuestionPolicy`) và bộ lọc an toàn thuốc trên câu hỏi giao thoa:**
   - Trong phép đo 20 câu `v2` qua `POST /api/v1/ai/chat`, câu hỏi hỏi về bước khai báo *"thuốc đang dùng và tiền sử dị ứng"* bị `AiMedicalScopeGuard.IsPrescriptionRequest` chuyển sang `OutOfScopeMedicalRequest` (`3/60` lượt), và `1/60` lượt sinh câu trả lời từ `qwen2.5:7b` bị bộ lọc hậu kiểm đầu ra chặn (`MEDICAL_PRESCRIPTION_OUT_OF_SCOPE`) do mô hình sinh từ khóa liên quan đến chỉ định thuốc.
5. **Ngưỡng `MinCosine = 0.20` của `bge-m3` không tự loại bỏ câu ngoài phạm vi nếu đứng độc lập:**
   - Như ghi nhận tại `docs/ai/RAG_EVALUATION.md:215`: *"MinCosine 0.2 gần như không lọc trên bộ dev hiện tại; từ chối ngoài phạm vi chủ yếu nhờ độ phủ từ khóa BM25 (`MinQueryCoverage` + `MinTermIdf`) và gate trong chế độ end-to-end."* Khả năng từ chối đúng ngoài phạm vi phụ thuộc chủ yếu vào bộ lọc từ vựng BM25 và cổng phân loại đầu vào chứ không dựa đơn thuần vào cosine.
6. **Hạn chế của cơ chế che thông tin cá nhân dựa trên biểu thức chính quy (`KnowledgePrivacyMask`):**
   - Vì chỉ sử dụng tập biểu thức chính quy tĩnh mà không có mô hình nhận diện thực thể có tên (NER) ngữ cảnh, `KnowledgePrivacyMask` để lọt định dạng số điện thoại dùng dấu phân cách không chuẩn như dấu gạch chéo `0901/234/567` (`Recall = 98,18%`, lọt `1/55` câu có PII trong bộ test giả) và báo nhầm trên các ký hiệu mã phòng khám `PK-102` hoặc cụm độ tuổi tổng quát `60 tuổi` (`Precision = 96,43%`, báo nhầm `2/15` câu không có PII).
