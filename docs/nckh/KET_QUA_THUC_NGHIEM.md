# Tổng hợp kết quả thực nghiệm NCKH — ClinicCare AI

Tài liệu này tổng hợp toàn bộ số liệu thực nghiệm của hệ thống trợ lý AI và mô-đun hỏi đáp tri thức phòng khám (RAG cục bộ) trong dự án **ClinicCare AI**. Mọi số liệu được trích dẫn trực tiếp từ các báo cáo thực nghiệm đã lưu trong kho mã nguồn (`docs/ai/HIDDEN_EVAL.md`, `docs/ai/RAG_EVALUATION.md`, `docs/ai/ROLE_INTENT_DATASET.md`) hoặc từ các phép đo trực tiếp trên máy thật trong phiên làm việc hiện tại, tuyệt đối không ước lượng và không làm tròn đẹp.

---

## 1. Cấu hình máy đo thực nghiệm

Các phép đo cục bộ trong phiên hiện tại (mục 4 và mục 5) được thực hiện trên cùng một máy trạm cá nhân (laptop) với thông số phần cứng và phần mềm như sau:

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

- **Nguồn số liệu:** [HIDDEN_EVAL.md](../ai/HIDDEN_EVAL.md) và [ROLE_INTENT_DATASET.md](../ai/ROLE_INTENT_DATASET.md).
- **Ngày đo:** Lần 1 và Lần 2 ngày `2026-04-17`; Lần 3 ngày `2026-04-18`.
- **Cấu hình máy:** Máy chạy lệnh `ClinicManagement.AI.Training --hidden-eval` cục bộ (`.NET 10.0`, ML.NET `SdcaMaximumEntropy`).
- **Tập dữ liệu đánh giá:** File `cliniccare-hidden-eval.xlsx` (đặt ngoài repo, độc lập hoàn toàn với tập train/dev), gồm 2 sheet: `T1_Patient_105` (`105` câu hỏi của bệnh nhân) và `T2_RoleIntent_105` (`105` câu hỏi theo vai trò nhân sự/bệnh nhân).

### 2.1. Bảng tổng hợp `T1_Patient_105` (Phân loại ý định bệnh nhân)

- **Nguồn file:** `docs/ai/HIDDEN_EVAL.md` (Mục 1, 2, 3).
- **Ngày đo:** `2026-04-17` và `2026-04-18`.
- **Cấu hình máy:** `.NET 10.0`, ML.NET `SdcaMaximumEntropy`, đối chiếu `v2` (`patient-intent-v2.zip`) và `v3` (`patient-intent-v3.zip`).

| Phiên bản model | Tổng số câu (`N`) | Số câu đúng | Accuracy | Macro-F1 | Weighted-F1 | Emergency Recall (`emergency_medical`) |
|---|---:|---:|---:|---:|---:|---:|
| `v2` (`patient-intent-v2.zip`) | 105 | 63 | 60,00% | 56,12% | 59,64% | 100,00% (`12/12`, `0` bỏ sót) |
| `v3` (`patient-intent-v3.zip`) | 105 | 69 | 65,71% | 59,64% | 65,45% | 100,00% (`12/12`, `0` bỏ sót) |

### 2.2. Bảng tổng hợp `T2_RoleIntent_105` qua 3 lần đo (Phân loại ý định theo vai trò)

- **Nguồn file:** `docs/ai/HIDDEN_EVAL.md` (Mục 1, 2, 3).
- **Ngày đo:** Lần 1 (`2026-04-17`), Lần 2 (`2026-04-17`), Lần 3 (`2026-04-18`).
- **Cấu hình máy:** `.NET 10.0`, bộ phân loại `RoleIntentRouter` + ML.NET `role-intent-v1.zip`.

| Lần đo & Cấu hình đánh giá | Chế độ chấm | Tổng số câu (`N`) | Số câu đúng | Accuracy | Macro-F1 | Weighted-F1 |
|---|---|---:|---:|---:|---:|---:|
| Lần 1 (`2026-04-17`, tập dữ liệu cũ 12 nhãn) | Khớp trực tiếp | 105 | 61 | 58,10% | 56,66% | 56,30% |
| Lần 2 (`2026-04-17`, mở rộng 18 nhãn chuẩn, chưa sửa tiền xử lý) | `strict` (khớp nhãn + không `Clarification`) | 105 | 43 | 40,95% | 38,09% | 39,57% |
| Lần 2 (`2026-04-17`, mở rộng 18 nhãn chuẩn, chưa sửa tiền xử lý) | `lenient` (chấp nhận `Clarification` thiếu mã hồ sơ) | 105 | 66 | 62,86% | 55,00% | 59,56% |
| Lần 3 (`2026-04-18`, thêm chuẩn hóa đầu vào, định tuyến trực tiếp & đối sánh danh mục) | `strict` (khớp nhãn + không `Clarification`) | 105 | 57 | 54,29% | 52,65% | 53,93% |
| Lần 3 (`2026-04-18`, thêm chuẩn hóa đầu vào, định tuyến trực tiếp & đối sánh danh mục) | `lenient` (chấp nhận `Clarification` thiếu mã hồ sơ) | 105 | 81 | 77,14% | 74,31% | 75,38% |

---

## 3. Kết quả truy xuất tri thức RAG trên bộ câu hỏi phát triển (`RAG_EVALUATION.md`)

- **Nguồn file:** [RAG_EVALUATION.md](../ai/RAG_EVALUATION.md) (Mục 3, 5, 7, 8).
- **Ngày đo:** `2026-04-18` (`2026-04-18T11:30:27Z` – `2026-04-18T11:30:56Z`).
- **Cấu hình máy & tham số:** `.NET 10.0`, Ollama cục bộ với `bge-m3:latest` (embedding) và `qwen2.5:7b` (chat); kho tri thức gồm `8` tài liệu Markdown (`36` chunk); `TopK = 3`, `MinScore = 0.45`, `MinQueryCoverage = 0.5`, `MinTermIdf = 1.25`, `MinCosine = 0.20`, `LexicalWeight = 0.35`, `SemanticWeight = 0.65`.

### 3.1. Bảng so sánh 3 tập câu hỏi phát triển (`baseline`, `v2`, `combined`)

- **Nguồn file:** `docs/ai/RAG_EVALUATION.md` (Mục 8.1).
- **Ngày đo:** `2026-04-18`.
- **Cấu hình máy:** Máy cục bộ chạy `ClinicManagement.AI.RagEval` với Ollama `bge-m3:latest`.

| Tập câu hỏi | Chế độ | Tổng (`N`) | Trong phạm vi | Ngoài phạm vi | Hit@1 | Hit@3 | MRR | Từ chối đúng | TB (ms) | p95 (ms) |
|---|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| `baseline` (`rag-dev-questions.json`) | `bm25` | 216 | 156 | 60 | 76,28% | 92,31% | 0,8355 | 90,00% | 0,24 | 0,43 |
| `baseline` (`rag-dev-questions.json`) | `hybrid` | 216 | 156 | 60 | 91,03% | 96,79% | 0,9370 | 90,00% | 16,65 | 19,21 |
| `baseline` (`rag-dev-questions.json`) | `end-to-end` | 216 | 156 | 60 | 76,28% | 92,31% | 0,8355 | 96,67% | 2,13 | 0,27 |
| `baseline` (`rag-dev-questions.json`) | `end-to-end-hybrid` | 216 | 156 | 60 | 91,03% | 96,79% | 0,9370 | 96,67% | 11,45 | 17,91 |
| `v2` (`rag-dev-questions-v2.json`) | `bm25` | 46 | 36 | 10 | 77,78% | 88,89% | 0,8333 | 90,00% | 0,40 | 0,32 |
| `v2` (`rag-dev-questions-v2.json`) | `hybrid` | 46 | 36 | 10 | 83,33% | 97,22% | 0,8981 | 90,00% | 16,32 | 18,35 |
| `v2` (`rag-dev-questions-v2.json`) | `end-to-end` | 46 | 36 | 10 | 69,44% | 77,78% | 0,7361 | 90,00% | 9,99 | 0,58 |
| `v2` (`rag-dev-questions-v2.json`) | `end-to-end-hybrid` | 46 | 36 | 10 | 75,00% | 86,11% | 0,8009 | 90,00% | 22,52 | 16,52 |
| `combined` (`baseline + v2`) | `bm25` | 262 | 192 | 70 | 76,56% | 91,67% | 0,8351 | 90,00% | 0,18 | 0,25 |
| `combined` (`baseline + v2`) | `hybrid` | 262 | 192 | 70 | 89,58% | 96,88% | 0,9297 | 90,00% | 16,74 | 19,20 |
| `combined` (`baseline + v2`) | `end-to-end` | 262 | 192 | 70 | 75,00% | 89,58% | 0,8168 | 95,71% | 1,79 | 0,24 |
| `combined` (`baseline + v2`) | `end-to-end-hybrid` | 262 | 192 | 70 | 88,02% | 94,79% | 0,9115 | 95,71% | 10,83 | 17,67 |

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
```

| Chế độ | Tổng số câu (`N`) | Trong phạm vi | Ngoài phạm vi | Hit@1 | Hit@3 | MRR | Từ chối đúng | TB (ms) | p95 (ms) |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| `bm25` | chưa đo | chưa đo | chưa đo | chưa đo | chưa đo | chưa đo | chưa đo | chưa đo | chưa đo |
| `hybrid` | chưa đo | chưa đo | chưa đo | chưa đo | chưa đo | chưa đo | chưa đo | chưa đo | chưa đo |
| `end-to-end` | chưa đo | chưa đo | chưa đo | chưa đo | chưa đo | chưa đo | chưa đo | chưa đo | chưa đo |
| `end-to-end-hybrid` | chưa đo | chưa đo | chưa đo | chưa đo | chưa đo | chưa đo | chưa đo | chưa đo | chưa đo |

---

## 4. Kết quả đo thời gian trả lời thực tế trên laptop (`POST /api/v1/ai/chat`)

- **Nguồn dữ liệu câu hỏi:** 20 câu hỏi trong phạm vi thuộc bộ phát triển `src/backend/ClinicManagement.Infrastructure/AI/Knowledge/Eval/rag-dev-questions-v2.json` (tuyệt đối không dùng bộ câu hỏi ẩn), mỗi câu gọi 3 lượt liên tiếp (`20 câu × 3 lượt = 60 request` cho mỗi chế độ) qua endpoint thật `POST http://localhost:5258/api/v1/ai/chat`.
- **Thời điểm đo:** `2026-11-09T06:45:52.4498364+00:00` (theo đồng hồ hệ thống khi chạy thực nghiệm).
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

- **Nguồn đo:** Đọc trực tiếp từ tiến trình hệ điều hành (`WorkingSet64`, `PrivateMemorySize64`), lệnh `ollama ps` và `nvidia-smi` ngay sau khi hoàn tất 60 lượt gọi ở mỗi chế độ.
- **Thời điểm đo:** `2026-11-09T06:45:52.4498364+00:00`.
- **Cấu hình máy:** AMD Ryzen 9 7945HX, RAM `31,19 GiB`, NVIDIA GeForce RTX 4060 Laptop GPU (`8188 MiB` VRAM).

| Chế độ vận hành | Tiến trình / Thành phần | RAM Working Set (MiB) | RAM Private Memory (MiB) | VRAM GPU sử dụng | Ghi chú từ `ollama ps` / `nvidia-smi` |
|---|---|---:|---:|---|---|
| Bật Ollama (`qwen2.5:7b` + `bge-m3:latest`) | Backend (`ClinicManagement.Api`) | 259,52 | 134,52 | — | Đã nạp chỉ mục `36` chunk trong bộ nhớ |
| Bật Ollama (`qwen2.5:7b` + `bge-m3:latest`) | Ollama (`ollama` + `llama-server`) | 2271,72 | 8132,28 | `5426 MiB / 8188 MiB` (toàn GPU) | `qwen2.5:7b`: `4,8 GB` (`100% GPU`, context `4096`); `bge-m3:latest`: `689 MB` (`100% GPU`, context `4096`) |
| Tắt Ollama (Fallback trích đoạn) | Backend (`ClinicManagement.Api`) | 245,23 | 145,19 | — | Chỉ dùng BM25 + ghép đoạn trích định sẵn (`DeterministicFallback`) |

---

## 5. Kết quả đo che thông tin cá nhân (`KnowledgePrivacyMask`)

- **Nguồn file test:** `src/backend/ClinicManagement.IntegrationTests/RagEvalCsvAndPrivacyTests.cs` (`GetSyntheticPrivacyDataset`, `Synthetic_privacy_mask_dataset_has_at_least_60_questions_and_achieves_expected_precision_and_recall`).
- **Đối tượng được kiểm tra:** Lớp `src/backend/ClinicManagement.Infrastructure/AI/Knowledge/KnowledgePrivacyMask.cs` (giữ nguyên toàn bộ biểu thức chính quy hiện có, không chỉnh sửa logic).
- **Ngày đo:** `2026-11-09` (chạy trực tiếp trong bộ kiểm thử tích hợp xUnit trên `.NET 10.0`).
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

1. **Độ chính xác phân loại ý định theo vai trò (`T2_RoleIntent_105`) ở chế độ nghiêm ngặt còn thấp:**
   - Ở lần đo đầu tiên (`2026-04-17`), accuracy của `T2_RoleIntent_105` chỉ đạt `58,10%` (`61/105` câu đúng).
   - Sau khi chuẩn hóa sang 18 nhãn chuẩn và bổ sung tiền xử lý ở lần đo thứ ba (`2026-04-18`), dù chế độ `lenient` đạt `77,14%` (`81/105`), chế độ `strict` mới đạt `54,29%` (`57/105`). Nguyên nhân chính là các câu hỏi tác vụ nghiệp vụ trong bộ đề ẩn không truyền kèm mã hồ sơ/lịch hẹn trong ngữ cảnh màn hình (`CurrentRoute` / `ResourceContext`), khiến hệ thống phải trả về câu hỏi làm rõ (`Clarification`, `24` câu) thay vì gọi công cụ ngay, cùng với `16` câu rơi vào nhánh `Fallback` khi câu hỏi vượt ngoài các mẫu câu huấn luyện.
2. **Hiện tượng thời gian trung bình (TB) lớn hơn phân vị thứ 95 (`p95`) trong các phép đo truy xuất RAG nhanh:**
   - Trong bảng kết quả `RAG_EVALUATION.md`, ở nhiều cấu hình không gọi mạng (như `end-to-end` trên bộ `baseline`: `TB = 2,13 ms > p95 = 0,27 ms`; trên bộ `v2`: `TB = 9,99 ms > p95 = 0,58 ms`; trên bộ `combined`: `TB = 1,79 ms > p95 = 0,24 ms`, và cả `end-to-end-hybrid` trên bộ `v2`: `TB = 22,52 ms > p95 = 16,52 ms`), giá trị trung bình cộng cao hơn hẳn `p95`.
   - Nguyên nhân là trên các tập mẫu nhỏ (`46` đến `262` câu), trên 95% truy vấn hoàn tất rất nhanh dưới mức mili-giây, nhưng câu truy vấn đầu tiên kích hoạt biên dịch JIT (`.NET Lazy<T>` khởi tạo `VietnameseIntentClassifier`, nạp model ML.NET `patient-intent-v3.zip` từ đĩa vào bộ nhớ) tốn vài trăm mili-giây, tạo ra ngoại lai đuôi phải (right-tail outlier) kéo trung bình cộng vượt lên trên phân vị thứ 95.
3. **Độ trễ khởi động lạnh (`Cold Start`) khi nạp mô hình ngôn ngữ lớn cục bộ:**
   - Khi mô hình `qwen2.5:7b` chưa nằm trong VRAM của GPU, lượt gọi đầu tiên qua `POST /api/v1/ai/chat` mất `4803,73 ms` (~4,8 giây), cao gấp ~`7,7` lần so với trung vị các lượt gọi ấm (`p50 = 623,63 ms`, `p95 = 1399,13 ms`) và tiêu tốn `5426 MiB` VRAM cùng `2271,72 MiB` RAM Working Set của tiến trình Ollama/llama-server. Trên các máy không có GPU rời đủ VRAM (dưới 6 GB), độ trễ sinh câu trả lời sẽ tăng mạnh hoặc phải chuyển sang chế độ fallback trích đoạn (`p50 = 13,39 ms`).
4. **Hạn chế của cổng lọc ứng viên (`KnowledgeQuestionPolicy`) và bộ lọc an toàn thuốc trên câu hỏi giao thoa:**
   - Trên bộ phát triển `v2` (`46` câu), chế độ `end-to-end-hybrid` có `Hit@3 = 86,11%`, thấp hơn `hybrid` thuần (`97,22%`) do `4/36` câu hỏi trong phạm vi bị cổng `KnowledgeQuestionPolicy.IsCandidate` từ chối trước khi truy xuất (vì câu ngắn chứa từ khóa trùng với luồng đặt lịch hoặc thiếu dấu hiệu câu hỏi rõ ràng).
   - Trong phép đo 20 câu `v2` qua `POST /api/v1/ai/chat`, câu hỏi hỏi về bước khai báo *"thuốc đang dùng và tiền sử dị ứng"* bị `AiMedicalScopeGuard.IsPrescriptionRequest` chặn nhầm sang `OutOfScopeMedicalRequest` (`3/60` lượt), và `1/60` lượt sinh câu trả lời từ `qwen2.5:7b` bị bộ lọc hậu kiểm đầu ra chặn (`MEDICAL_PRESCRIPTION_OUT_OF_SCOPE`) do mô hình sinh từ khóa liên quan đến chỉ định thuốc.
5. **Ngưỡng `MinCosine = 0.20` của `bge-m3` không tự loại bỏ câu ngoài phạm vi nếu đứng độc lập:**
   - Với mô hình nhúng đa ngữ `bge-m3`, điểm tương đồng cosine giữa câu hỏi bất kỳ và các đoạn tài liệu tiếng Việt thường nằm trên `0,20`. Do đó, khả năng từ chối đúng câu ngoài phạm vi (`90,00%` ở chế độ truy xuất thuần và `95,71%`–`96,67%` ở chế độ `end-to-end`) phụ thuộc chủ yếu vào cổng lọc từ vựng BM25 (`MinScore = 0.45`, `MinQueryCoverage = 0.5`, `MinTermIdf = 1.25`) và cổng phân loại đầu vào thay vì ngưỡng cosine đơn thuần.
6. **Hạn chế của cơ chế che thông tin cá nhân dựa trên biểu thức chính quy (`KnowledgePrivacyMask`):**
   - Vì chỉ sử dụng tập biểu thức chính quy tĩnh mà không có mô hình nhận diện thực thể có tên (NER) ngữ cảnh, `KnowledgePrivacyMask` vẫn để lọt định dạng số điện thoại dùng dấu phân cách không chuẩn như dấu gạch chéo `0901/234/567` (`Recall = 98,18%`, lọt `1/55` câu có PII trong bộ test giả) và báo nhầm trên các ký hiệu mã phòng khám `PK-102` hoặc cụm độ tuổi tổng quát `60 tuổi` (`Precision = 96,43%`, báo nhầm `2/15` câu không có PII).
