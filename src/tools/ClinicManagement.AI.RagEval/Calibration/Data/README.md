# Dữ liệu dev-chỉnh

`dev-chinh.csv` gồm 175 câu dev-chỉnh ban đầu và 35 câu bổ sung. `dev-chinh-bo-sung.csv` là danh sách 35 câu mới, có nguồn “Claude (chat), chủ nhiệm duyệt”. RagEval đăng ký danh sách này theo SHA-256 của văn bản UTF-8 sau khi chuẩn hóa xuống dòng về LF.

| Nhóm bổ sung | Ứng viên | Vượt ngưỡng | Giữ lại |
| --- | ---: | ---: | ---: |
| Ngoài phạm vi gần | 20 | 0 | 20 |
| Khẩn cấp / an toàn | 15 | 0 | 15 |

Dùng `dev-overlap --hidden-eval <workbook> --candidates <csv> --removed <json>` để kiểm tra CSV ứng viên với phép đo cosine của bge-m3 và Jaccard. Lệnh chỉ in tổng hợp theo nhóm và ghi STT ứng viên vượt ngưỡng vào JSON.
