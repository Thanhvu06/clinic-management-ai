using Xunit;

namespace ClinicManagement.IntegrationTests;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class AiPhase12AcceptanceCollection
{
    public const string Name = "AI Phase 1.2 acceptance";
}
