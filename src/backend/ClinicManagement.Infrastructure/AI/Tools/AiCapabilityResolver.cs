using ClinicManagement.Application.AI.Tools;
using ClinicManagement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ClinicManagement.Infrastructure.AI.Tools;

public sealed class AiCapabilityResolver : IAiCapabilityResolver
{
    private readonly AppDbContext _db;

    public AiCapabilityResolver(AppDbContext db) => _db = db;

    public async Task<IReadOnlySet<AiActorCapability>> ResolveAsync(AiToolExecutionContext context, CancellationToken cancellationToken = default)
    {
        var capabilities = new HashSet<AiActorCapability> { AiActorCapability.ReadClinicCatalog };
        if (!context.IsAuthenticated || !context.ActorId.HasValue)
            return capabilities;

        if (context.Roles.Contains(AiActorRole.Patient) &&
            await _db.Patients.AsNoTracking().AnyAsync(x => x.UserId == context.ActorId.Value, cancellationToken))
        {
            capabilities.Add(AiActorCapability.ReadOwnAppointments);
            capabilities.Add(AiActorCapability.PrepareBooking);
            capabilities.Add(AiActorCapability.PrepareAppointmentChange);
            capabilities.Add(AiActorCapability.ExecuteConfirmedPatientAction);
        }

        if (context.Roles.Contains(AiActorRole.Receptionist) &&
            await HasFacilityAssignmentAsync(context, nameof(AiActorRole.Receptionist), cancellationToken))
            capabilities.Add(AiActorCapability.ReadReceptionWorkspace);

        if (context.Roles.Contains(AiActorRole.Doctor) &&
            await _db.Doctors.AsNoTracking().AnyAsync(x => x.UserId == context.ActorId.Value && x.IsActive, cancellationToken))
            capabilities.Add(AiActorCapability.ReadDoctorWorkspace);

        if (context.Roles.Contains(AiActorRole.DiagnosticTechnician) &&
            await HasFacilityAssignmentAsync(context, nameof(AiActorRole.DiagnosticTechnician), cancellationToken))
            capabilities.Add(AiActorCapability.ReadDiagnosticWorkspace);

        if (context.Roles.Contains(AiActorRole.Pharmacist) &&
            await HasFacilityAssignmentAsync(context, nameof(AiActorRole.Pharmacist), cancellationToken))
            capabilities.Add(AiActorCapability.ReadPharmacyWorkspace);

        if (context.Roles.Contains(AiActorRole.Admin))
            capabilities.Add(AiActorCapability.ReadAdminMetrics);

        return capabilities;
    }

    private async Task<bool> HasFacilityAssignmentAsync(AiToolExecutionContext context, string role, CancellationToken cancellationToken)
    {
        if (!context.ActorId.HasValue || context.ActorId == Guid.Empty) return false;
        var query = _db.StaffFacilityAssignments.AsNoTracking()
            .Where(x => x.UserId == context.ActorId.Value && x.IsActive && x.Role == role);
        if (context.FacilityId.HasValue)
            query = query.Where(x => x.FacilityId == context.FacilityId.Value);
        return await query.AnyAsync(cancellationToken);
    }
}
