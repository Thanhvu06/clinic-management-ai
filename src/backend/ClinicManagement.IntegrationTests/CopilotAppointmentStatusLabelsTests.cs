using ClinicManagement.Domain.Enums;
using ClinicManagement.Infrastructure.AI.Tools;

namespace ClinicManagement.IntegrationTests;

public class CopilotAppointmentStatusLabelsTests
{
    [Theory]
    [InlineData(AppointmentStatus.Pending, "Chờ xác nhận")]
    [InlineData(AppointmentStatus.Confirmed, "Đã xác nhận")]
    [InlineData(AppointmentStatus.CheckedIn, "Đã tiếp nhận")]
    [InlineData(AppointmentStatus.InConsultation, "Đang khám")]
    [InlineData(AppointmentStatus.Completed, "Đã khám xong")]
    [InlineData(AppointmentStatus.Cancelled, "Đã hủy")]
    [InlineData(AppointmentStatus.NoShow, "Vắng mặt")]
    [InlineData(AppointmentStatus.PendingReschedule, "Chờ duyệt đổi lịch")]
    [InlineData(AppointmentStatus.PendingCancellation, "Chờ duyệt hủy lịch")]
    public void DoctorCopilotLocalizesAppointmentStatus(AppointmentStatus status, string label)
    {
        Assert.Equal(label, CopilotDisplayText.DoctorStatus(status.ToString()));
    }
}
