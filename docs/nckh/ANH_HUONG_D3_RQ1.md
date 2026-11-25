# Ảnh hưởng của lỗi D3 lên câu trả lời RQ1

Mục đích là đếm dấu hiệu danh sách đánh số bị cắt trong 300 câu trả lời đã lưu của RQ1 (150 cặp A/B). Phân tích dùng điểm đã có trong phiếu gộp trước xem lại.

Điểm RQ1 không thay đổi; đây là thông tin cho phần hạn chế.

## Phiên bản sinh câu trả lời

`chay-thu.json` ghi lượt `blind-answers` lúc 18:55:34 ngày 10/11/2026 (UTC+7), với 150 câu, retrieval `hybrid`, seed `20261110` và model `qwen2.5:7b`. Tệp này và metadata workbook không ghi SHA của lượt sinh. [Hướng dẫn tái hiện master ngày 10/11](KET_QUA_THUC_NGHIEM.md#phiếu-checklist-rq1-v2) ghim phiên bản lịch sử `0c502693a8ecd09c86d3ce0677cc6623bae8d3ed`, merge vào `main` lúc 18:47:00 cùng ngày. `excerpt-replay.json` ghi cùng SHA. Đối chiếu bản lưu đó theo STT và khóa A/B cho kết quả 150/150 câu trích đoạn khớp MASTER sau chuẩn hóa NFC và khoảng trắng. Đây là căn cứ xác định phiên bản đã tái hiện được nhánh trích đoạn.

SHA của lượt **sinh gốc** không xác định. Để đọc mã trước bản sửa [PR #199](https://github.com/Thanhvu06/clinic-management-ai/pull/199), báo cáo dùng `e49685210e7b546f8c9e5c9851cfc9c65457d283`, cha của `b5471f5d`. Ở [`ClinicKnowledgeAnswerer.cs`, hàm `AnswerAsync`, dòng 34](https://github.com/Thanhvu06/clinic-management-ai/blob/e49685210e7b546f8c9e5c9851cfc9c65457d283/src/backend/ClinicManagement.Infrastructure/AI/Knowledge/ClinicKnowledgeAnswerer.cs#L34), cả hai nhánh đều gọi `Limit`. Nhánh LLM truyền `generated.Value!`, nhánh trích đoạn truyền `retrieval.Hits[0].Chunk.Text`. [`Limit`, dòng 40](https://github.com/Thanhvu06/clinic-management-ai/blob/e49685210e7b546f8c9e5c9851cfc9c65457d283/src/backend/ClinicManagement.Infrastructure/AI/Knowledge/ClinicKnowledgeAnswerer.cs#L40) dùng `Regex.Split(text.Trim(), @"(?<=[.!?])\s+").Take(5)`, nên dấu chấm ngay sau số thứ tự cũng tiêu tốn một vị trí trong giới hạn năm phần. Phiên bản lịch sử `0c50269` có cùng hai lời gọi và cùng cách giới hạn ở [dòng 26 và 32](https://github.com/Thanhvu06/clinic-management-ai/blob/0c502693a8ecd09c86d3ce0677cc6623bae8d3ed/src/backend/ClinicManagement.Infrastructure/AI/Knowledge/ClinicKnowledgeAnswerer.cs#L26-L32). Các đường đi thoát trước bước tạo văn bản không gọi hàm này.

## Cách phát hiện

Nối theo STT 1–150, bỏ dòng ví dụ. Đọc câu trả lời từ `Phiếu chấm!D:E` và nhóm từ `Câu hỏi!D` của MASTER. Giải khóa bằng `khoa.csv` và đối chiếu với sheet `Khóa` trong hai workbook. Xác nhận 300 ô trả lời của phiếu gộp trùng từng ký tự với MASTER. Dùng điểm grader2 từ `Phiếu chấm!K:L` và grader1 từ `Phiếu chấm!F:G` của phiếu gộp.

- **a. Mục rỗng ở cuối.** Bỏ khoảng trắng cuối và kiểm tra số nguyên dương theo sau bởi dấu chấm ở cuối câu trả lời. Xác nhận cấu trúc mục bằng vị trí đầu dòng, sau dấu hai chấm hoặc nối tiếp một số thứ tự đứng trước. Bước này loại một trường hợp có số ở cuối câu nhưng không có cấu trúc mục.
- **b. Mục rỗng ở giữa.** Kiểm tra hai dấu mục liên tiếp chỉ cách nhau bởi khoảng trắng. Nhận diện dấu mục qua vị trí đầu dòng, sau dấu hai chấm hoặc qua số thứ tự liên tiếp.
- **c. So với đầu ra thô.** Điều kiện này cần chạy `Limit` nguyên bản trên đầu ra LLM trước hậu xử lý. MASTER chỉ lưu câu trả lời đã xử lý và không có cột hoặc sheet chứa đầu ra LLM thô, nên không đánh giá được c.

Một ô được đánh dấu **CỤT** khi khớp ít nhất một điều kiện đánh giá được. Hai dấu hiệu bề mặt chỉ cho phép đếm các trường hợp quan sát được. Phép đếm không bao quát câu bị mất nội dung sau một mục hoàn chỉnh hoặc trường hợp mô hình tự sinh mục rỗng.

## Kết quả

| Điều kiện | Số câu trả lời khớp |
|---|---:|
| a. Mục rỗng ở cuối | 2 |
| b. Mục rỗng ở giữa | 0 |
| c. Khác đầu ra LLM thô sau khi áp dụng `Limit` | Không đánh giá được |
| **CỤT, hợp của các điều kiện đánh giá được** | **2/300 (0,67%)** |

| Nhóm | Số cặp | LLM CỤT / số trả lời | Trích đoạn CỤT / số trả lời | Tổng CỤT / số trả lời |
|---|---:|---:|---:|---:|
| Trong phạm vi | 55 | 2/55 | 0/55 | 2/110 |
| Diễn đạt khác | 35 | 0/35 | 0/35 | 0/70 |
| Ngoài phạm vi gần | 25 | 0/25 | 0/25 | 0/50 |
| Ngoài phạm vi xa | 15 | 0/15 | 0/15 | 0/30 |
| Khẩn cấp / an toàn | 20 | 0/20 | 0/20 | 0/40 |
| **Tổng** | **150** | **2/150** | **0/150** | **2/300** |

| STT | Phía | Hệ thống | Nhóm | Điều kiện khớp | Điểm người chấm (grader2) | Điểm Codex (grader1) |
|---:|:---:|---|---|:---:|---:|---:|
| 6 | A | LLM | Trong phạm vi | a | 1 | 1 |
| 31 | A | LLM | Trong phạm vi | a | 0 | 0 |

| STT có ít nhất một phía CỤT | Điểm LLM của grader2 | Điểm trích đoạn của grader2 | Kết quả LLM |
|---:|---:|---:|---|
| 6 | 1 | 2 | Thua |
| 31 | 0 | 2 | Thua |

LLM **0 thắng, 0 hòa, 2 thua** trong hai cặp có ít nhất một phía CỤT. So sánh văn bản A/B từng ký tự trong MASTER cho đúng 68 cặp khác nhau. Hai câu trả lời CỤT thuộc **2/68 cặp** này (2,94%). Điểm lưu trong bảng mô tả hai cặp quan sát được, chưa cho biết điểm sẽ thay đổi ra sao nếu câu trả lời được sinh bằng phiên bản đã sửa.

## Kiểm tra SHA-256

Các tệp dữ liệu và log được mở chỉ đọc. Giá trị trong cột SHA-256 là giá trị chung của lần đo trước và sau khi đọc.

| Tệp | SHA-256 trước = sau | Đối chiếu |
|---|---|---|
| `E:/NCKH/bo-cau-hoi-an/ket-qua/RAG_ket_qua_day_du.xlsx` | `92f6437af81a227b2649c388d8580cec5ab934d74b3a2480148747dedf668eac` | TRÙNG |
| `E:/NCKH/ket-qua-rq1/RQ1_gop_truoc_xem_lai.xlsx` | `1ff11dbad744d533c3791c8672a91c599ff29e24b15f5222918ca47402a47539` | TRÙNG |
| `E:/NCKH/bo-cau-hoi-an/ket-qua/khoa.csv` | `979399d64ebc999b201c53615114147ce5ace7c7b68fef845abce97c6e516200` | TRÙNG |
| `E:/NCKH/bo-cau-hoi-an/ket-qua/chay-thu.json` | `6dc593b5e5f06eba514db6e1a958290e585175fde0c0c4e3c4a90134e0b60b33` | TRÙNG |
| `E:/NCKH/bo-cau-hoi-an/ket-qua/excerpt-replay.json` | `9dd7fa1d7ba6a7167d7e4921a9dba5ab5eba56a37331b992480ced875b8d9a72` | TRÙNG |
