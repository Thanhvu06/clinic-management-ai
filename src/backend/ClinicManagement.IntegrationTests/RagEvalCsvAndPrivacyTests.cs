extern alias RagEval;

using System.Text;
using ClinicManagement.Infrastructure.AI.Knowledge;
using EvaluationInput = RagEval::ClinicManagement.AI.RagEval.EvaluationInput;

namespace ClinicManagement.IntegrationTests;

public sealed class RagEvalCsvAndPrivacyTests
{
    [Fact]
    public void Csv_parser_handles_bom_quoted_commas_blank_rows_example_row_and_category_mappings()
    {
        var csv = "\uFEFF" + """
            
            STT,Người viết,Câu hỏi,Loại,Tài liệu đúng,Ghi chú
            VD,Nhóm trưởng,"Câu hỏi ví dụ, có dấu phẩy",Trong phạm vi,booking-and-changes,Bỏ qua dòng VD
            ,,,
            1,Thành viên A,"Xin chào, làm sao để đặt lịch khám và đổi giờ hẹn?",Trong phạm vi,booking-and-changes.md,Có dấu phẩy trong ngoặc kép
            2,Thành viên B,"Phòng khám có bán thuốc ""kháng sinh"" lẻ không?",Ngoài phạm vi,booking-and-changes,Ngoài phạm vi luôn map sang null
            3,Thành viên C,Đau ngực dữ dội và khó thở cần làm gì ngay?,Khẩn cấp / an toàn,emergency-and-safety,Khẩn cấp có tài liệu
            4,Thành viên D,"Trẻ sốt cao co giật, tím tái phải xử trí sao?",Khẩn cấp / an toàn,   ,Khẩn cấp để trống tài liệu
            5,,,,
               ,   ,   ,   ,   
            """;

        var questions = EvaluationInput.ParseCsvQuestions(csv, "synthetic.csv");

        Assert.Equal(4, questions.Length);
        Assert.Equal("Xin chào, làm sao để đặt lịch khám và đổi giờ hẹn?", questions[0].Text);
        Assert.Equal("booking-and-changes", questions[0].ExpectedDocId);

        Assert.Equal("Phòng khám có bán thuốc \"kháng sinh\" lẻ không?", questions[1].Text);
        Assert.Null(questions[1].ExpectedDocId);

        Assert.Equal("Đau ngực dữ dội và khó thở cần làm gì ngay?", questions[2].Text);
        Assert.Equal("emergency-and-safety", questions[2].ExpectedDocId);

        Assert.Equal("Trẻ sốt cao co giật, tím tái phải xử trí sao?", questions[3].Text);
        Assert.Null(questions[3].ExpectedDocId);
    }

    [Fact]
    public async Task Csv_file_loads_via_evaluation_input_and_enforces_csv_flags()
    {
        var path = Path.Combine(Path.GetTempPath(), "rag-csv-test-" + Guid.NewGuid().ToString("N") + ".csv");
        try
        {
            var csv = "\uFEFFSTT,Người viết,Câu hỏi,Loại,Tài liệu đúng\r\n" +
                      "VD,TV0,Câu ví dụ,Trong phạm vi,accounts\r\n" +
                      "1,TV1,\"Quên mật khẩu tài khoản, lấy lại thế nào?\",Trong phạm vi,accounts\r\n" +
                      "2,TV2,Dự báo thời tiết hôm nay thế nào?,Ngoài phạm vi,\r\n";
            await File.WriteAllTextAsync(path, csv, Encoding.UTF8);

            var input = EvaluationInput.Parse(["result.json", "--csv", path]);
            var questions = await input.LoadAsync();

            Assert.True(input.IsCsv);
            Assert.Equal("result.json", input.OutputPath);
            Assert.Equal(2, questions.Length);
            Assert.Equal(1, questions.Count(q => q.ExpectedDocId is not null));
            Assert.Equal(1, questions.Count(q => q.ExpectedDocId is null));
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Theory]
    [InlineData("STT,Câu hỏi,Loại\n1,Hỏi?,Trong phạm vi")]
    [InlineData("STT,Người viết,Câu hỏi,Loại,Tài liệu đúng\n1,A,Hỏi trong phạm vi nhưng thiếu doc?,Trong phạm vi,")]
    [InlineData("STT,Người viết,Câu hỏi,Loại,Tài liệu đúng\n1,A,Hỏi loại lạ?,Không rõ loại,accounts")]
    [InlineData("STT,Người viết,Câu hỏi,Loại,Tài liệu đúng\n1,A,   ,Trong phạm vi,accounts")]
    public void Invalid_csv_rows_or_headers_throw_invalid_data_exception(string badCsv)
    {
        Assert.Throws<InvalidDataException>(() => EvaluationInput.ParseCsvQuestions(badCsv, "bad.csv"));
    }

    public sealed record SyntheticPrivacyCase(
        int Id,
        string Category,
        string Input,
        bool HasPii,
        string[] SensitiveTokens);

    public static IReadOnlyList<SyntheticPrivacyCase> GetSyntheticPrivacyDataset() =>
    [
        // 1. Số điện thoại nhiều định dạng (16 câu)
        new(1, "Phone", "Cho tôi hỏi số 0901234567 đã đăng ký tài khoản bệnh nhân chưa?", true, ["0901234567"]),
        new(2, "Phone", "Nhờ phòng khám gọi lại giúp tôi qua SĐT 0912345678 nhé.", true, ["0912345678"]),
        new(3, "Phone", "Tôi muốn đổi số điện thoại nhận OTP sang 0987654321.", true, ["0987654321"]),
        new(4, "Phone", "Số liên hệ của người nhà là 0389123456 khi cần báo tin.", true, ["0389123456"]),
        new(5, "Phone", "Bác sĩ có thể nhắn kết quả vào số 090 123 4567 được không?", true, ["090 123 4567"]),
        new(6, "Phone", "Tôi đặt lịch khám bằng số 0912 345 678 hôm qua.", true, ["0912 345 678"]),
        new(7, "Phone", "Số điện thoại trên hồ sơ cũ là 098.765.4321 hiện không còn dùng.", true, ["098.765.4321"]),
        new(8, "Phone", "Kiểm tra giúp lịch hẹn gắn với số 0903.111.222.", true, ["0903.111.222"]),
        new(9, "Phone", "Liên hệ số 093-456-7890 nếu lịch khám buổi chiều thay đổi.", true, ["093-456-7890"]),
        new(10, "Phone", "Gửi mã xác nhận về số 0978-123-456 giúp tôi.", true, ["0978-123-456"]),
        new(11, "Phone", "Tôi đang ở nước ngoài dùng số +84901234567 để đăng nhập.", true, ["+84901234567"]),
        new(12, "Phone", "Số điện thoại quốc tế +84 912 345 678 có nhận được tin nhắn không?", true, ["+84 912 345 678"]),
        new(13, "Phone", "Vui lòng cập nhật số +84-987-654-321 vào hồ sơ khám.", true, ["+84-987-654-321"]),
        new(14, "Phone", "Tài khoản đăng ký đầu số 84901234567 bị khóa tạm thời.", true, ["84901234567"]),
        new(15, "Phone", "Số máy bàn nhà tôi là (028) 3812 3456 có dùng đặt lịch được không?", true, ["(028) 3812 3456"]),
        new(16, "Phone", "Trường hợp khách ghi số ngăn bằng dấu gạch chéo 0901/234/567 trên ghi chú.", true, ["0901/234/567"]),

        // 2. CCCD 12 số (12 câu)
        new(17, "CCCD", "Số căn cước công dân của tôi là 079203001234 cần mang bản gốc không?", true, ["079203001234"]),
        new(18, "CCCD", "Tra cứu hồ sơ theo CCCD 001095012345 giúp tôi.", true, ["001095012345"]),
        new(19, "CCCD", "Tôi nhập nhầm số CCCD 048199004567 khi khai báo thông tin.", true, ["048199004567"]),
        new(20, "CCCD", "Người nhà có mã định danh 092201009876 muốn đặt lịch khám tổng quát.", true, ["092201009876"]),
        new(21, "CCCD", "Căn cước 0792 0300 1234 đã tích hợp bảo hiểm y tế chưa?", true, ["0792 0300 1234"]),
        new(22, "CCCD", "Cho hỏi số CCCD 001 095 012345 có trùng hồ sơ nào không?", true, ["001 095 012345"]),
        new(23, "CCCD", "Cập nhật lại căn cước 0362 0000 5678 cho bệnh nhân.", true, ["0362 0000 5678"]),
        new(24, "CCCD", "Số định danh cá nhân 0792.0300.1234 cần sửa ngày cấp.", true, ["0792.0300.1234"]),
        new(25, "CCCD", "Hồ sơ gắn với CCCD 0010.9501.2345 khám khoa nào?", true, ["0010.9501.2345"]),
        new(26, "CCCD", "Kiểm tra thông tin thẻ CCCD 0792-0300-1234 tại quầy tiếp nhận.", true, ["0792-0300-1234"]),
        new(27, "CCCD", "Số căn cước 0481-9900-4567 có được hưởng BHYT đúng tuyến không?", true, ["0481-9900-4567"]),
        new(28, "CCCD", "Mã căn cước 079-203-001-234 bị sai họ tên trên hệ thống.", true, ["079-203-001-234"]),

        // 3. Email (12 câu)
        new(29, "Email", "Tôi đăng nhập bằng email nguyenvana@example.com nhưng không nhận được mã.", true, ["nguyenvana@example.com"]),
        new(30, "Email", "Nhờ gửi bảng giá khám qua địa chỉ tran.thi.b@clinic.vn.", true, ["tran.thi.b@clinic.vn"]),
        new(31, "Email", "Hồ sơ của tôi dùng hộp thư le_minh_c1990@domain.org.", true, ["le_minh_c1990@domain.org"]),
        new(32, "Email", "Gửi kết quả xét nghiệm máu về benhnhan+test@mail.hospital.com.vn được không?", true, ["benhnhan+test@mail.hospital.com.vn"]),
        new(33, "Email", "Tôi muốn hủy liên kết email hoang-anh99@sub.example.edu.vn khỏi tài khoản.", true, ["hoang-anh99@sub.example.edu.vn"]),
        new(34, "Email", "Gửi kết quả xét nghiệm qua email pham.van.d@testmail.vn giúp tôi.", true, ["pham.van.d@testmail.vn"]),
        new(35, "Email", "Tài khoản đăng ký bằng hothie@demo.net bị quên mật khẩu.", true, ["hothie@demo.net"]),
        new(36, "Email", "Đổi địa chỉ thư điện tử từ user01@old.vn sang user02@new.vn được không?", true, ["user01@old.vn", "user02@new.vn"]),
        new(37, "Email", "Email liên hệ khẩn của gia đình là support_bn12@cliniccare.vn.", true, ["support_bn12@cliniccare.vn"]),
        new(38, "Email", "Cho hỏi hóa đơn điện tử có gửi về vuthif@company.co.uk không?", true, ["vuthif@company.co.uk"]),
        new(39, "Email", "Tôi nhập nhầm mail dang.khoa.88@provider.io khi tạo hồ sơ.", true, ["dang.khoa.88@provider.io"]),
        new(40, "Email", "Bác sĩ có nhận câu hỏi qua contact.patient@health.vn không?", true, ["contact.patient@health.vn"]),

        // 4. Mã BN / APT và mã hồ sơ (15 câu)
        new(41, "Code", "Cho tôi hỏi mã bệnh nhân BN-20260001 khám ở tầng mấy?", true, ["BN-20260001"]),
        new(42, "Code", "Hồ sơ BN123456 đã có kết quả chụp X-quang chưa?", true, ["BN123456"]),
        new(43, "Code", "Tôi cần in lại phiếu thu của mã BN_00987.", true, ["BN_00987"]),
        new(44, "Code", "Bệnh nhân BN-2026-0451 muốn xin dời lịch tái khám.", true, ["BN-2026-0451"]),
        new(45, "Code", "Lịch hẹn APT-2026-0089 của tôi đang ở trạng thái chờ xác nhận nghĩa là gì?", true, ["APT-2026-0089"]),
        new(46, "Code", "Tôi muốn hủy lịch hẹn APT102938 vào sáng mai.", true, ["APT102938"]),
        new(47, "Code", "Phiếu hẹn APPT_2048 cần đến trước bao nhiêu phút?", true, ["APPT_2048"]),
        new(48, "Code", "Kiểm tra giúp trạng thái lịch APPT-9912.", true, ["APPT-9912"]),
        new(49, "Code", "Mã hồ sơ PAT-556677 có cần nhịn ăn trước khi lấy máu không?", true, ["PAT-556677"]),
        new(50, "Code", "Tra cứu kết quả cận lâm sàng cho mã MRN000123.", true, ["MRN000123"]),
        new(51, "Code", "Hồ sơ khám HS-998877 đã thanh toán viện phí chưa?", true, ["HS-998877"]),
        new(52, "Code", "Mã lịch hẹn LH-4512 bị báo trùng khung giờ.", true, ["LH-4512"]),
        new(53, "Code", "Phiếu khám PK-0012 được chỉ định siêu âm bụng tổng quát.", true, ["PK-0012"]),
        new(54, "Code", "Đơn thuốc RX-202601 hướng dẫn uống sau ăn nghĩa là sao?", true, ["RX-202601"]),
        new(55, "Code", "Tra cứu giúp mã lịch hẹn là abc987xyz và mã bệnh nhân: bn01test.", true, ["abc987xyz", "bn01test"]),

        // 5. Câu không có thông tin cá nhân để đo báo nhầm (15 câu)
        new(56, "Negative", "Trước khi xét nghiệm máu tổng quát cần nhịn ăn từ 8 đến 12 giờ phải không?", false, []),
        new(57, "Negative", "Siêu âm bụng tổng quát có cần uống 0,5 đến 1 lít nước và nhịn tiểu không?", false, []),
        new(58, "Negative", "Khi đi khám bảo hiểm y tế tôi cần mang theo những giấy tờ gì?", false, []),
        new(59, "Negative", "Làm sao để đặt lịch khám mới và đổi giờ hẹn trên hệ thống?", false, []),
        new(60, "Negative", "Trạng thái đã tiếp nhận trên phiếu khám có ý nghĩa là gì?", false, []),
        new(61, "Negative", "Bệnh nhân có dấu hiệu khó thở hoặc đau ngực dữ dội nên gọi cấp cứu 115 ngay không?", false, []),
        new(62, "Negative", "Quên mật khẩu đăng nhập tài khoản cổng bệnh nhân thì khôi phục như thế nào?", false, []),
        new(63, "Negative", "Sau khi bác sĩ hoàn tất khám thì bao lâu kết quả hiển thị trên ứng dụng?", false, []),
        new(64, "Negative", "Phòng khám làm việc từ 7 giờ 30 sáng đến 17 giờ chiều các ngày trong tuần đúng không?", false, []),
        new(65, "Negative", "Tôi nên đến quầy tiếp nhận trước giờ hẹn khoảng 15 đến 30 phút không?", false, []),
        new(66, "Negative", "Thanh toán phí khám bằng chuyển khoản ngân hàng hoặc tiền mặt tại quầy thế nào?", false, []),
        new(67, "Negative", "Hủy lịch hẹn trước 24 giờ có cần chờ nhân viên lễ tân duyệt không?", false, []),
        new(68, "Negative", "Chỉ số huyết áp 120/80 mmHg ghi trong phiếu khám có ý nghĩa chung là gì?", false, []),
        new(69, "Negative", "Phòng khám nội tổng quát nằm ở phòng PK-102 tầng 1 phải không?", false, []),
        new(70, "Negative", "Người trên 60 tuổi đi khám sức khỏe định kỳ cần nhịn ăn bao lâu?", false, [])
    ];

    [Fact]
    public void Synthetic_privacy_mask_dataset_has_at_least_60_questions_and_achieves_expected_precision_and_recall()
    {
        var dataset = GetSyntheticPrivacyDataset();
        Assert.True(dataset.Count >= 60, $"Expected at least 60 synthetic questions, got {dataset.Count}.");

        var tp = 0;
        var fp = 0;
        var fn = 0;
        var tn = 0;

        foreach (var item in dataset)
        {
            var masked = KnowledgePrivacyMask.Mask(item.Input);
            var maskTriggered = masked.Contains("[ĐÃ CHE]", StringComparison.Ordinal) ||
                                !string.Equals(masked, item.Input, StringComparison.Ordinal);

            if (item.HasPii)
            {
                var leaked = item.SensitiveTokens.Any(token =>
                    masked.Contains(token, StringComparison.OrdinalIgnoreCase));
                if (maskTriggered && !leaked)
                    tp++;
                else
                    fn++;
            }
            else
            {
                if (maskTriggered)
                    fp++;
                else
                    tn++;
            }
        }

        var precision = tp + fp > 0 ? (double)tp / (tp + fp) : 0.0;
        var recall = tp + fn > 0 ? (double)tp / (tp + fn) : 0.0;

        Assert.Equal(55, dataset.Count(x => x.HasPii));
        Assert.Equal(15, dataset.Count(x => !x.HasPii));
        Assert.Equal(54, tp);
        Assert.Equal(1, fn);
        Assert.Equal(2, fp);
        Assert.Equal(13, tn);
        Assert.Equal(54.0 / 56.0, precision, 6);
        Assert.Equal(54.0 / 55.0, recall, 6);
    }
}
