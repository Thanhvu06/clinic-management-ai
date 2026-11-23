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

## Chấm lại mù 30 câu

- Người chấm: chủ nhiệm đề tài, cùng người chấm lần đầu ngày 15–16/11/2026; lần chấm lại ngày 23/11/2026.
- Phiếu gốc của lần chấm lại: `E:/NCKH/cham-lai-30/RAG_phieu_cham_lai_30.xlsx`, 24.539 byte, LastWriteTime `2026-11-23T14:01:33.9364494+07:00`.
- SHA-256 phiếu gốc của lần chấm lại: `c5b94def6986721bd25b086f09787abca8386180c2ff0d7936730382cdc0fea0`.
- Giờ khóa: `2026-11-23T14:03:52.8632608+07:00`. Sao chép và đặt ReadOnly trước khi đọc nội dung; từ đó chỉ phân tích bản khóa.
- Bản khóa: `E:/NCKH/khoa-phieu-rq1/RAG_phieu_cham_lai_30_ChuNhiem_KHOA_20261123.xlsx`, SHA-256 `c5b94def6986721bd25b086f09787abca8386180c2ff0d7936730382cdc0fea0`, trùng phiếu gốc.
- `SHA256SUMS.txt` ngày 16/11 đang ReadOnly nên không được sửa hay gỡ thuộc tính. Theo xác nhận của chủ nhiệm, dòng hash mới được ghi riêng vào `E:/NCKH/khoa-phieu-rq1/SHA256SUMS_20261123.txt` và đặt ReadOnly lúc `2026-11-23T14:09:11.7306622+07:00`; SHA-256 tệp bổ sung: `c62aa1f425ce4f9ce003d381dc1d4477275942ba6e32abc2a704df08d744cc50`.
- 18/11 tạo phiếu, file lỗi định dạng x14ac, 23/11 tạo lại cùng mẫu và thứ tự (hash c198494b…5e38) trước khi chấm.
- Hash đầy đủ của phiếu trống được tạo lại trước khi chấm: `c198494bb001f84c0ebac0a3c898a13dfa88065602fc40d73ea8bb69e3245e38`.
- Ánh xạ giữ nguyên 30 câu, thứ tự CL01–CL30, seed lấy mẫu đã chốt `20261122`; SHA-256 `anh-xa.csv`: `8463ee5cef79794337dfef1ef1d48c28a3affa054fa9237e24078ca96216a565`.
- Kết quả kiểm tra máy móc và độ ổn định được ghi trong [Chấm lại RQ1](CHAM_LAI_RQ1.md). Không sửa ô chấm, không mở khóa A/B.
