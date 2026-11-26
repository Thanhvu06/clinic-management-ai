using ClinicManagement.Infrastructure.Common;

namespace ClinicManagement.Infrastructure.Appointments;

public partial class DoctorAppointmentService
{
    private void RecordConsultationStarted(Guid userId, string entityName, long entityId, string reference) =>
        UserActionAudit.Record(_dbContext, userId, UserActionAudit.StartConsultation, entityName, entityId,
            $"Bác sĩ bắt đầu khám {reference}", _dateTimeProvider.UtcNow);
}
