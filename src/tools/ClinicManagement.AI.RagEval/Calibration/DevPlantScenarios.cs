namespace ClinicManagement.AI.RagEval;

internal static class DevPlantScenarios
{
    public static IReadOnlyList<(string From, string To)> Conditions { get; } =
    [
        ("trẻ em", "người trưởng thành"),
        ("người giám hộ", "người không đi cùng"),
        ("người cao tuổi", "người trưởng thành khỏe mạnh"),
        ("người đi cùng", "người đang chờ ở nhà"),
        ("người bệnh", "người không đi khám"),
        ("bệnh nhân", "người đi kèm"),
        ("lễ tân", "nhân viên bảo vệ"),
        ("bác sĩ", "nhân viên thu ngân"),
        ("giấy khai sinh", "giấy phép lái xe"),
        ("CCCD", "thẻ thư viện"),
        ("đơn thuốc", "hóa đơn gửi xe"),
        ("lấy máu", "chụp X-quang"),
        ("cộng hưởng từ", "siêu âm"),
        ("nước lọc", "nước ngọt"),
        ("khi tái khám", "khi khám lần đầu"),
        ("trước khi khám", "sau khi khám"),
        ("buổi sáng", "buổi tối"),
        ("tại quầy", "qua chat"),
        ("máy chung", "máy cá nhân"),
        ("khi có chỉ định", "khi tự quyết định")
    ];

    public static IReadOnlyList<(string Topic, string True, string False)> OutsideSource { get; } =
    [
        ("xét nghiệm", "Kết quả xét nghiệm có thể thay đổi theo thời điểm lấy mẫu.",
            "Mọi chỉ số xét nghiệm đều giữ nguyên suốt cả ngày."),
        ("nước tiểu", "Màu nước tiểu có thể thay đổi theo lượng nước đã uống.",
            "Lượng nước uống không bao giờ ảnh hưởng đến độ cô đặc của nước tiểu."),
        ("đường huyết", "Đường huyết có thể thay đổi sau bữa ăn.",
            "Đường huyết luôn giữ nguyên sau mọi bữa ăn."),
        ("cấp cứu", "Một số triệu chứng cấp cứu có thể xuất hiện đột ngột.",
            "Mọi tình trạng cấp cứu đều khởi phát từ từ trong nhiều ngày."),
        ("thuốc", "Tiền sử dị ứng có thể ảnh hưởng đến lựa chọn thuốc.",
            "Tiền sử dị ứng không liên quan đến phản ứng với thuốc."),
        ("tái khám", "Diễn biến triệu chứng theo thời gian có thể giúp bác sĩ đánh giá bệnh.",
            "Diễn biến triệu chứng không có giá trị trong bất kỳ lần tái khám nào."),
        ("khám", "Mạch có thể tăng tạm thời sau khi vận động.",
            "Vận động không bao giờ làm thay đổi mạch."),
        ("hồ sơ", "Tiền sử phẫu thuật có thể giúp bác sĩ đánh giá tình trạng hiện tại.",
            "Tiền sử phẫu thuật luôn vô ích khi đánh giá sức khỏe."),
        ("", "Chỉ số huyết áp có thể thay đổi trong ngày.",
            "Huyết áp luôn giống nhau ở tất cả mọi người.")
    ];

    public static IEnumerable<DevEdit> ConditionEdits(string original)
    {
        foreach (var (from, to) in Conditions)
        {
            var edit = DevEdits.ReplaceUnique(original, from, to);
            if (edit is not null) yield return edit;

            var capitalized = DevEdits.ReplaceUnique(original, Capitalize(from), Capitalize(to));
            if (capitalized is not null && capitalized != edit) yield return capitalized;
        }
    }

    public static IEnumerable<string> OutsideSentences(string context, bool correct)
    {
        foreach (var (topic, truth, falsehood) in OutsideSource)
            if (context.Contains(topic, StringComparison.OrdinalIgnoreCase))
                yield return correct ? truth : falsehood;
    }

    private static string Capitalize(string text) => char.ToUpperInvariant(text[0]) + text[1..];
}
