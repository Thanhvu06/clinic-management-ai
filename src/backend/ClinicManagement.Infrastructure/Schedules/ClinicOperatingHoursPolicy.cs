using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Domain.Constants;
using Microsoft.Extensions.Options;

namespace ClinicManagement.Infrastructure.Schedules;

public sealed class ClinicOperatingHoursOptions
{
    public string Mode { get; set; } = "Standard";
}

public sealed class ClinicOperatingHoursPolicy(IOptions<ClinicOperatingHoursOptions> options) : IClinicOperatingHoursPolicy
{
    public static IClinicOperatingHoursPolicy Standard { get; } = new ClinicOperatingHoursPolicy(Options.Create(new ClinicOperatingHoursOptions()));
    public bool IsTwentyFourSeven => options.Value.Mode == "TwentyFourSeven";
    public string DisplayText => IsTwentyFourSeven ? ClinicOperatingHoursText.TwentyFourSeven : ClinicOperatingHoursText.Standard;

    public bool IsOpen(DateOnly date) => IsTwentyFourSeven || date.DayOfWeek != DayOfWeek.Sunday;

    public IReadOnlyList<(TimeOnly Start, TimeOnly End)> GetSeedShifts(DateOnly date)
    {
        if (!IsOpen(date)) return [];
        if (IsTwentyFourSeven) return [(new(0, 0), new(8, 0)), (new(8, 0), new(16, 0)), (new(16, 0), new(23, 30))];
        return date.DayOfWeek == DayOfWeek.Saturday
            ? [(new(8, 0), new(11, 30))]
            : [(new(8, 0), new(11, 30)), (new(13, 30), new(17, 0))];
    }

}
