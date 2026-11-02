namespace ClinicManagement.Application.Common.Interfaces;

public interface IClinicOperatingHoursPolicy
{
    bool IsTwentyFourSeven { get; }
    string DisplayText { get; }
    bool IsOpen(DateOnly date);
    IReadOnlyList<(TimeOnly Start, TimeOnly End)> GetSeedShifts(DateOnly date);
}
