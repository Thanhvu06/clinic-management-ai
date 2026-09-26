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
        "patient.get_appointment_detail",
        "reception.get_today_appointments",
        "reception.get_queue",
        "reception.lookup_appointment",
        "doctor.get_my_queue",
        "doctor.get_patient_summary",
        "doctor.get_diagnostic_orders",
        "technician.get_worklist",
        "pharmacist.get_prescription_queue",
        "pharmacist.get_inventory_status",
        "admin.get_dashboard_metrics",
        "admin.get_ai_health"
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    public static bool IsAllowed(string? name) =>
        !string.IsNullOrWhiteSpace(name) && AllowedToolNames.Contains(name.Trim());
}
