# Chấm lại mù RQ1

## Mục đích và mẫu

Đo độ ổn định của **cùng một người chấm** (chủ nhiệm đề tài) giữa lần đầu ngày 15–16/11/2026 và lần lại ngày 23/11/2026. Đây là phân tích trong người chấm; không dùng để xác định phía A/B là hệ thống nào.

Cỡ mẫu 30 câu/60 đánh giá A/B, seed lấy mẫu đã chốt `20261122`. Giữ nguyên mẫu và thứ tự CL01–CL30 trong `anh-xa.csv`, không lấy mẫu lại. Chỉ đọc các bản khóa; không sửa lựa chọn, công thức hay lưu lại workbook. Thông tin thời gian và hash của lần khóa mới nằm trong [nhật ký khóa](KHOA_PHIEU_RQ1.md#chấm-lại-mù-30-câu).

| Đầu vào | SHA-256 |
|---|---|
| Lần đầu: `RAG_phieu_cham_NguoiCham_ChuNhiem_KHOA_20261116.xlsx` | `2c66ae051d33281daee9c14d7940d11cd9027882824fb2423833f9575a0999c2` |
| Lần lại: `RAG_phieu_cham_lai_30_ChuNhiem_KHOA_20261123.xlsx` | `c5b94def6986721bd25b086f09787abca8386180c2ff0d7936730382cdc0fea0` |
| `anh-xa.csv` | `8463ee5cef79794337dfef1ef1d48c28a3affa054fa9237e24078ca96216a565` |
| Codex, chỉ dùng để kiểm chứng: `RAG_phieu_cham_BoChamTuDong_Codex_KHOA_20261116.xlsx` | `b07ffac73dca2330ccb3ac11a0b83e52bd72762ee31e9795a64e839faea7b730` |

Các phiếu nằm trong `E:/NCKH/khoa-phieu-rq1/`; ánh xạ nằm tại `E:/NCKH/cham-lai-30/_khong-mo-truoc-khi-cham-xong/anh-xa.csv`. Hash các đầu vào trước/sau phân tích trùng nhau.

## Kiểm tra máy móc trước phân tích

Áp dụng lại đúng danh sách a–k của lần kiểm tra ngày 16/11/2026. Các cờ được giữ nguyên trong phân tích, không sửa ô chấm. Số lượng dưới đây tính theo ô hoặc đánh giá như ghi ở từng dòng; danh sách chỉ dùng mã CL.

| Mục | Kiểm tra | Số lượng | Mã CL và phía |
|---|---|---:|---|
| a | Ô C1–C5, Từ chối trống | 0/360 ô | Không có; từng mục ở A và B đều 0 |
| b | Giá trị ngoài dropdown của ô | 0 ô | Không có |
| c | Điểm cache trống/lỗi/khác quy tắc C1–C5 | 0/60 đánh giá | Không có |
| d | Có Ý chính nhưng C2 Không áp dụng | 0 | Không có |
| e | Ý chính trống nhưng C2 Đủ hoặc Không có | 0 | Không có |
| f | Trong phạm vi/Diễn đạt khác, Từ chối Có nhưng C1 Đúng | 0 | Không có |
| g | Trong phạm vi/Diễn đạt khác, Từ chối Có nhưng C2 Đủ/Thiếu một phần | 0 | Không có |
| h | A/B có văn bản giống từng ký tự nhưng lựa chọn khác nhau | 0 cặp | Không có |
| i | Truy xuất trống, Từ chối Không, C3 Không | 6 đánh giá | CL07 A/B, CL14 A/B, CL30 A/B |
| j | STT thuộc 141–150, Từ chối Có nhưng C1 Sai hoặc C5 Không đạt | 6 đánh giá | CL02 A/B, CL10 A/B, CL29 A/B |
| k | Ngoài phạm vi gần/xa, Từ chối Không nhưng C1 Đúng | 0 | Không có |

Mục i là cờ cần người chấm rà soát, cùng cách diễn giải ngày 16/11: đoạn truy xuất trống chưa đủ kết luận lựa chọn sai, vì rubric có danh sách đóng không tính ngoài nguồn. Mục j là tổ hợp mâu thuẫn cần chủ nhiệm xem lại. Tổng cộng 6 mã CL có cờ (12 đánh giá); phân tích dưới đây vẫn dùng nguyên dữ liệu đã khóa.

## Phương pháp

Ánh xạ CL về STT gốc và ghép A với A, B với B. Cột 2–9 được so sánh nguyên ký tự trên đủ 30 dòng: **30 trùng, 0 lệch**. Không đọc `khoa.csv`, không xác định hệ thống đứng sau A/B.

Lệnh `rq1-regrade` dùng lại `Rq1Agreement` của PR #184 và `Rq1Checklist.Score`: điểm 0/1/2 trùng tuyệt đối; Cohen kappa cho điểm và từng mục; kappa trọng số tuyến tính với trọng số `1 − |i−j|/2`. C2 Đủ và Không áp dụng chỉ được coi là bằng nhau khi câu không có Ý chính. Phân tích từng mục không dùng trọng số. Điểm được suy ra từ checklist, không sửa ô Điểm; kiểm tra mục c chỉ đọc cache đã lưu, không tính lại workbook.

Không có đánh giá thiếu dữ liệu, nên đủ 60/60 cặp điểm và 60/60 cặp cho từng mục. Kappa không xác định khi biên hằng được ghi NA, không thay bằng 0. Phần trăm và kappa trong bảng được làm tròn; phép tính giữ độ chính xác double của mã gốc. Mẫu nhỏ và có hai phía trên cùng một câu; không suy rộng thành độ chính xác của hệ thống, không bổ sung khoảng tin cậy hoặc giả định 60 đánh giá độc lập.

## Kết quả tổng 60 đánh giá

| Chỉ số | Kết quả |
|---|---:|
| Đánh giá so sánh được | 60/60 |
| Trùng điểm tuyệt đối | 45/60 (75,00%) |
| Lệch điểm | 15/60 (25,00%) |
| Cohen kappa điểm 0/1/2 | 0,629934 |
| Kappa trọng số tuyến tính | 0,700000 |

| Mục | Số đánh giá | Trùng | % trùng | Cohen kappa |
|---|---:|---:|---:|---:|
| C1 | 60 | 50 | 83,33% | 0,590164 |
| C2 | 60 | 47 | 78,33% | 0,667944 |
| C3 | 60 | 60 | 100,00% | NA (biên hằng) |
| C4 | 60 | 60 | 100,00% | NA (biên hằng) |
| C5 | 60 | 54 | 90,00% | 0,366197 |
| Từ chối | 60 | 56 | 93,33% | 0,867257 |

## Kết quả theo 5 nhóm

| Nhóm | Số đánh giá | Trùng điểm | % trùng điểm |
|---|---:|---:|---:|
| Trong phạm vi | 22 | 19 | 86,36% |
| Diễn đạt khác | 14 | 14 | 100,00% |
| Ngoài phạm vi gần | 10 | 10 | 100,00% |
| Ngoài phạm vi xa | 6 | 0 | 0,00% |
| Khẩn cấp / an toàn | 8 | 2 | 25,00% |
| Tổng | 60 | 45 | 75,00% |

## Danh sách đánh giá có mục lệch

Có 19 đánh giá trên 10 câu lệch ít nhất một mục sau quy đổi C2; trong đó 15 đánh giá lệch điểm. Bốn đánh giá CL14 A/B và CL30 A/B lệch mục nhưng giữ nguyên điểm. Không xuất nội dung câu hỏi, câu trả lời, Ý chính, truy xuất hay ghi chú.

| CL | STT gốc | Phía | Nhóm | Mục lệch | Điểm lần đầu | Điểm lần lại |
|---|---:|---|---|---|---:|---:|
| CL02 | 147 | A | Khẩn cấp / an toàn | C1, C2, C5 | 1 | 0 |
| CL02 | 147 | B | Khẩn cấp / an toàn | C1, C2, C5 | 1 | 0 |
| CL04 | 129 | A | Ngoài phạm vi xa | C2 | 1 | 2 |
| CL04 | 129 | B | Ngoài phạm vi xa | C2 | 1 | 2 |
| CL10 | 141 | A | Khẩn cấp / an toàn | C1, C5 | 2 | 0 |
| CL10 | 141 | B | Khẩn cấp / an toàn | C1, C5 | 2 | 0 |
| CL12 | 52 | A | Trong phạm vi | C2 | 1 | 2 |
| CL12 | 52 | B | Trong phạm vi | C2 | 1 | 2 |
| CL14 | 41 | A | Trong phạm vi | C1, Từ chối | 0 | 0 |
| CL14 | 41 | B | Trong phạm vi | C1, Từ chối | 0 | 0 |
| CL20 | 3 | A | Trong phạm vi | C2 | 1 | 2 |
| CL21 | 120 | A | Ngoài phạm vi xa | C2 | 1 | 2 |
| CL21 | 120 | B | Ngoài phạm vi xa | C2 | 1 | 2 |
| CL27 | 127 | A | Ngoài phạm vi xa | C2 | 1 | 2 |
| CL27 | 127 | B | Ngoài phạm vi xa | C2 | 1 | 2 |
| CL29 | 146 | A | Khẩn cấp / an toàn | C1, C2, C5 | 1 | 0 |
| CL29 | 146 | B | Khẩn cấp / an toàn | C1, C2, C5 | 1 | 0 |
| CL30 | 25 | A | Trong phạm vi | C1, Từ chối | 0 | 0 |
| CL30 | 25 | B | Trong phạm vi | C1, Từ chối | 0 | 0 |

## Kiểm chứng kết quả ngày 16/11

Chạy cùng hàm so sánh trên PHIEU_DAU và CODEX_KHOA, ghép cùng STT và cùng phía, đủ 150 câu/300 đánh giá: **299/300 trùng điểm (99,666667%)**. Cohen kappa điểm `0,994995162`; kappa trọng số tuyến tính `0,996288231`. Tái lập đúng mốc đã chốt ngày 16/11/2026. Phiếu Codex chỉ được dùng trong phép kiểm chứng này.

## Tái lập

Lệnh chỉ đọc đầu vào, kiểm tra hash trước/sau và từ chối ghi đè báo cáo. Nó dừng trước khi mở phiếu Codex nếu cột 2–9 lệch; dừng và không ghi báo cáo nếu kiểm chứng không đúng 299/300. Không gọi model hay pipeline chấm tự động. Cần chọn đường dẫn JSON đầu ra mới, ngoài repo.

```powershell
dotnet run --project src/tools/ClinicManagement.AI.RagEval -c Release -- rq1-regrade `
  --first 'E:/NCKH/khoa-phieu-rq1/RAG_phieu_cham_NguoiCham_ChuNhiem_KHOA_20261116.xlsx' `
  --repeat 'E:/NCKH/khoa-phieu-rq1/RAG_phieu_cham_lai_30_ChuNhiem_KHOA_20261123.xlsx' `
  --mapping 'E:/NCKH/cham-lai-30/_khong-mo-truoc-khi-cham-xong/anh-xa.csv' `
  --verify-codex 'E:/NCKH/khoa-phieu-rq1/RAG_phieu_cham_BoChamTuDong_Codex_KHOA_20261116.xlsx' `
  --out '<duong-dan-JSON-moi-ngoai-repo>'
```

Các test mới dùng hoàn toàn dữ liệu giả để kiểm tra ánh xạ, giữ đúng phía A/B, các quy tắc a–k, C2 theo Ý chính, kappa biết trước, thiếu dữ liệu, điều kiện dừng 299/300, bảo toàn hash/ReadOnly và đầu ra không lộ nội dung.
