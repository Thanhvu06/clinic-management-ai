namespace ClinicManagement.Infrastructure.Expiry;

public interface IExpiryRule
{
    Task<int> RunAsync(CancellationToken cancellationToken = default);
}
