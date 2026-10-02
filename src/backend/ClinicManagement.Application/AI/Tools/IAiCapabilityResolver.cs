namespace ClinicManagement.Application.AI.Tools;

public interface IAiCapabilityResolver
{
    Task<IReadOnlySet<AiActorCapability>> ResolveAsync(AiToolExecutionContext context, CancellationToken cancellationToken = default);
}
