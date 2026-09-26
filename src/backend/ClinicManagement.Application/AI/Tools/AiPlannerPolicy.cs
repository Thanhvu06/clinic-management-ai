using System.Collections.Frozen;

namespace ClinicManagement.Application.AI.Tools;

/// <summary>
/// The only tool names a model planner may emit. Keep this policy server-owned;
/// the provider prompt is documentation for the model, never an authorization boundary.
/// </summary>
public static class AiPlannerPolicy
{
    public static IReadOnlySet<string> AllowedToolNames { get; } = new[]
    {
        "clinic.search_specialties",
        "clinic.search_doctors",
        "clinic.get_available_slots",
        "clinic.get_facilities",
        "clinic.get_pricing",
        "patient.get_my_appointments",
        "patient.get_appointment_detail"
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    public static bool IsAllowed(string? name) =>
        !string.IsNullOrWhiteSpace(name) && AllowedToolNames.Contains(name.Trim());
}
