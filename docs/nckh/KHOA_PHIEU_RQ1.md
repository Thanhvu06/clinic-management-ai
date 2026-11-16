# Khóa phiếu chấm RQ1

## Tệp

| Tên gốc | Tên bản khóa | Vai trò | Kích thước (byte) | Giờ sửa (LastWriteTime) | SHA-256 |
|---|---|---|---:|---|---|
| `RAG_phieu_cham_ChuNhiem.xlsx` | `RAG_phieu_cham_NguoiCham_ChuNhiem_KHOA_20261116.xlsx` | Người chấm (chủ nhiệm đề tài) – kết quả chính | 55034 | 08:27:28 ngày 16/11/2026 (UTC+7) | `2c66ae051d33281daee9c14d7940d11cd9027882824fb2423833f9575a0999c2` |
| `RAG_phieu_cham_NguoiCham2_v2_20261114_141233_2621757.xlsx` | `RAG_phieu_cham_BoChamTuDong_Codex_KHOA_20261116.xlsx` | Bộ chấm tự động Codex (AI). Tên tệp gốc có chữ NguoiCham2 do mẫu đặt tên, không phải người chấm. | 71917 | 16:01:14 ngày 14/11/2026 (UTC+7) | `b07ffac73dca2330ccb3ac11a0b83e52bd72762ee31e9795a64e839faea7b730` |
| `RAG_phieu_cham_NguoiCham2_v2_20261114_132048_8669423.xlsx` | `RAG_phieu_cham_TrongGoc_KHOA_20261116.xlsx` | Phiếu trống gốc, dùng cho chấm lại mù 30 câu | 53997 | 13:20:49 ngày 14/11/2026 (UTC+7) | `b6fb8efcab1391ec8ff95c54b5903ce0dc0cc67db0dfb225a9346c28edf5c033` |

## Giờ khóa và rubric

- Giờ khóa 08:51:07 ngày 16/11/2026 (UTC+7)
- [Rubric RQ1](RUBRIC_RQ1.md) phiên bản 1.0.2, commit `680d93437cd9700d501d56e72216a82763da2820`.

## Nhật ký chấm

- 14/11/2026: tạo phiếu trống gốc (GOC) và phiếu bộ chấm tự động Codex (CODEX); Codex đã đọc nội dung bộ ẩn khi chấm.
- 15/11/2026 → 16/11/2026: chủ nhiệm chấm tay 150 cặp trên bản sao của phiếu trống gốc; tệp lưu lúc 18:29:11 ngày 15/11/2026 (SHA-256 f16532a9cae7db864f8987eceaf842e2fe856ec6c0a085a3ee63559c2ae62914).
- 16/11/2026: kiểm tra lỗi máy móc lần 1 trên bản 15/11 (mục a–k): 0 lỗi; mục i (đoạn truy xuất trống, Từ chối = Không, C3 = Không) bật ở STT 2, 76, 87, 131, 132, 133, 135, 138 (cả A và B).
- 16/11/2026 08:27:28: chủ nhiệm sửa một dấu chấm, không đổi lựa chọn checklist; kiểm tra lần 2 xác nhận cột 1–9, 28, 29 trùng phiếu gốc.
- 16/11/2026: kiểm tra lần 2 (mục l–q, chạy lại a–k): 0 lỗi; mục i như lần 1. Chủ nhiệm xem lại 8 STT ở mục i, giữ nguyên lựa chọn (câu trả lời chỉ chứa nội dung thuộc danh sách đóng).
- Thứ tự xem dữ liệu: lần kiểm tra 1 đã đọc phiếu người TRƯỚC khi tính hash phiếu Codex. Giờ sửa (LastWriteTime) của CODEX là 16:01:14 ngày 14/11/2026 (UTC+7).
- Giờ khóa: 08:51:07 ngày 16/11/2026 (UTC+7). Chưa mở khóa A/B (khoa.csv) tại thời điểm khóa.
