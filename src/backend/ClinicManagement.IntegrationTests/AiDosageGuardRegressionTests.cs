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
}
