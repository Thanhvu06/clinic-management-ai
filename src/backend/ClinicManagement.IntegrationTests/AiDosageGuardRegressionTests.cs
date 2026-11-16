using ClinicManagement.Infrastructure.AI;

namespace ClinicManagement.IntegrationTests;

public sealed class AiDosageGuardRegressionTests
{
    [Theory]
    [InlineData("Paracetamol nên uống mấy lần một ngày?", true)]
    [InlineData("Bé 10 kg nên cho uống bao nhiêu siro hạ sốt?", true)]
    [InlineData("Ibuprofen có nên uống gấp đôi không?", true)]
    [InlineData("Nên nhỏ mấy giọt thuốc nhỏ mắt?", true)]
    [InlineData("Cho con uống mấy gói oresol là đủ?", true)]
    [InlineData("Uống bao nhiêu viên hạ sốt một lần?", true)]
    [InlineData("Đi khám nên đến sớm bao nhiêu phút?", false)]
    [InlineData("Nên đến trước giờ hẹn mấy phút?", false)]
    [InlineData("Nên dùng bao nhiêu một lần?", true)]
    [InlineData("Nên chờ bao nhiêu giờ mới dùng siro?", true)]
    [InlineData("Nên chờ bao nhiêu tiếng?", false)]
    public void Dose_suggestions_are_refused_unless_the_clause_only_asks_about_time(
        string message, bool refused)
    {
        Assert.Equal(refused, AiMedicalScopeGuard.IsPrescriptionRequest(message));
    }

    [Theory]
    [InlineData("Liệu tôi có nên đi khám không?", false)]
    [InlineData("Ho tăng lên thì có nên đi khám không?", false)]
    [InlineData("Phòng khám ở tầng mấy, tôi nên lên tầng nào?", false)]
    [InlineData("Nên đặt lịch trước bao nhiêu ngày?", false)]
    [InlineData("Phí khám bao nhiêu tiền, có nên mua bảo hiểm không?", false)]
    [InlineData("Nên mang theo mấy loại giấy tờ?", false)]
    [InlineData("Nên gọi điện trước mấy tiếng để hủy lịch?", false)]
    [InlineData("Nên đưa ông đến sớm mấy phút?", false)]
    [InlineData("Nên uống mấy ống thuốc?", true)]
    [InlineData("Cho bé uống 2 gói có được không, nên tăng liều không?", true)]
    [InlineData("Liều paracetamol nên là bao nhiêu?", true)]
    [InlineData("Nên dùng bao nhiêu một lần?", true)]
    public void Dose_guard_distinguishes_medication_amounts_from_other_questions(
        string message, bool refused)
    {
        Assert.Equal(refused, AiMedicalScopeGuard.IsPrescriptionRequest(message));
    }

    [Theory]
    [InlineData("siêu âm bụng uống bao nhiêu nước")]
    [InlineData("Nên uống bao nhiêu nước trước khi siêu âm?")]
    [InlineData("Truoc khi sieu am nen uong nuoc bao nhieu?")]
    public void Dose_guard_allows_water_intake_questions_for_test_preparation(string message)
    {
        Assert.False(AiMedicalScopeGuard.IsPrescriptionRequest(message));
    }

    [Theory]
    [InlineData("Gói khám tổng quát có mấy gói, nên chọn gói nào?", false)]
    [InlineData("Nên dùng gói khám nào, giá bao nhiêu?", false)]
    [InlineData("Có nên dùng bảo hiểm không, được giảm bao nhiêu?", false)]
    [InlineData("Nên đặt cọc bao nhiêu?", false)]
    [InlineData("Nên uống mấy lít nước?", false)]
    [InlineData("Nên uống bao nhiêu cốc nước trước khi siêu âm?", false)]
    [InlineData("Nên uống bao nhiêu ml nước trước khi siêu âm?", false)]
    [InlineData("Nên uống mấy gói thuốc?", true)]
    [InlineData("Cho bé uống bao nhiêu ml siro?", true)]
    [InlineData("Nên dùng thuốc này bao nhiêu viên?", true)]
    [InlineData("Nên tiêm mấy ống?", true)]
    public void Dose_guard_distinguishes_service_and_water_amounts_from_medication_amounts(
        string message, bool refused)
    {
        Assert.Equal(refused, AiMedicalScopeGuard.IsPrescriptionRequest(message));
    }
}
