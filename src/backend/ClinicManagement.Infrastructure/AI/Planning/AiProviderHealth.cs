using ClinicManagement.Application.AI.Planning;

namespace ClinicManagement.Infrastructure.AI.Planning;

/// <summary>Small process-wide circuit breaker; it stores no request or patient data.</summary>
public sealed class AiProviderHealth : IAiProviderHealth
{
    private readonly object _gate = new();
    private int _consecutiveFailures;
    private DateTimeOffset? _openUntil;

    public string State
    {
        get
        {
            lock (_gate) return _openUntil > DateTimeOffset.UtcNow ? "Open" : _consecutiveFailures == 0 ? "Healthy" : "Degraded";
        }
    }

    public bool CanAttempt()
    {
        lock (_gate)
        {
            if (!_openUntil.HasValue || _openUntil <= DateTimeOffset.UtcNow)
            {
                _openUntil = null;
                return true;
            }
            return false;
        }
    }

    public void RecordSuccess()
    {
        lock (_gate)
        {
            _consecutiveFailures = 0;
            _openUntil = null;
        }
    }

    public void RecordFailure()
    {
        lock (_gate)
        {
            _consecutiveFailures++;
            if (_consecutiveFailures >= 3)
                _openUntil = DateTimeOffset.UtcNow.AddSeconds(30);
        }
    }
}
