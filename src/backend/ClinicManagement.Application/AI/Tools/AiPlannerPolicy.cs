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
        "reception.get_pending_change_requests", "reception.get_unpaid_invoices", "technician.get_service_guidance",
        "pharmacist.get_medicine_stock", "pharmacist.get_medicine_batches", "admin.get_revenue_report",
        "admin.get_active_doctors", "admin.get_pending_leave_requests",
        "clinic.search_knowledge",
        "admin.get_revenue_summary",
        "reception.get_pending_payments",
        "doctor.get_my_appointments_today",
        "technician.get_completed_today",
        "pharmacist.get_low_stock",
        "clinic.search_specialties",
        "clinic.search_doctors",
        "clinic.get_available_slots",
        "clinic.get_facilities",
        "clinic.get_pricing",
        "patient.get_my_appointments",
        "patient.get_appointment_detail",
        "patient.get_my_visits",
        "patient.get_my_diagnostic_results",
        "patient.get_my_prescriptions",
        "patient.get_my_bills",
        "reception.get_today_appointments",
        "reception.get_upcoming_appointments",
        "reception.get_queue",
        "reception.lookup_appointment",
        "doctor.get_my_queue",
        "doctor.get_patient_summary",
        "doctor.get_diagnostic_orders",
        "doctor.get_prescription_status",
        "technician.get_worklist",
        "pharmacist.get_prescription_queue",
        "pharmacist.get_prescription_payment_status",
        "pharmacist.get_inventory_status",
        "admin.get_dashboard_metrics",
        "admin.get_ai_health"
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    public static bool IsAllowed(string? name) =>
        !string.IsNullOrWhiteSpace(name) && AllowedToolNames.Contains(name.Trim());
}
