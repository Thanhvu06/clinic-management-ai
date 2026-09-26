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

        return capabilities;
    }
}
