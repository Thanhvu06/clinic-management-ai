namespace ClinicManagement.Api;

public sealed class AiRateLimitOptions
{
    public const string SectionName = "AiRateLimiting";

    public int WindowSeconds { get; set; } = 60;
    public int ChatPermitLimit { get; set; } = 8;
    public int EndpointPermitLimit { get; set; } = 10;
    public int TestingPermitLimit { get; set; } = 1000;
}
