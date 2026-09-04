namespace ClinicManagement.Application.Appointments.DTOs.Reception;

public class ReceptionStatsDto
{
    public int AppointmentsToday { get; set; }
    public int PendingAppointmentsToday { get; set; }
    public int ConfirmedAppointmentsToday { get; set; }
    public int CompletedAppointmentsToday { get; set; }
    public int PendingChangeRequests { get; set; }
}
