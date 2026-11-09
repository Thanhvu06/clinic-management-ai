namespace ClinicManagement.Infrastructure.Persistence;

public sealed class DemoTopUpOptions
{
    public bool Enabled { get; set; }
    public int TopUpIntervalMinutes { get; set; } = 60;
    public int MinUpcomingConfirmed { get; set; } = 12;
    public int MinPendingConfirmations { get; set; } = 3;
    public int StaleAfterHours { get; set; } = 48;
    public int DemoPatientDayStartHour { get; set; } = 7;
    public int DemoPatientDayEndHour { get; set; } = 20;
}
