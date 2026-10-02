using ClinicManagement.Application.AI;
using ClinicManagement.Application.AI.Planning;
using ClinicManagement.Infrastructure.AI;
using Microsoft.Extensions.Options;

namespace ClinicManagement.Infrastructure.AI.Planning;

/// <summary>
/// Process-wide provider circuit. It contains only sanitized counters/timestamps;
/// no request, prompt, patient or secret data is retained.
/// </summary>
public sealed class AiProviderHealth : IAiProviderHealth
{
    public const int DefaultFailureThreshold = 3;
    public static readonly TimeSpan DefaultOpenDuration = TimeSpan.FromSeconds(30);

    private readonly int _failureThreshold;
    private readonly TimeSpan _openDuration;
    private readonly TimeProvider _timeProvider;
    private readonly object _gate = new();
    private int _consecutiveFailures;
    private DateTimeOffset? _openUntil;
    private bool _probeInFlight;
    private string _state = "Closed";
    private string? _lastFailureCode;
    private DateTimeOffset? _lastSuccessAtUtc;
    private long _attemptCount;
    private long _successCount;
    private readonly Dictionary<string, long> _failureCounts = new(StringComparer.Ordinal);

    public AiProviderHealth(
        TimeSpan? openDuration = null,
        int failureThreshold = DefaultFailureThreshold,
        TimeProvider? timeProvider = null,
        IOptions<AiProviderOptions>? options = null)
    {
        var configured = options?.Value;
        _openDuration = openDuration ?? TimeSpan.FromSeconds(Math.Max(1, configured?.CircuitCooldownSeconds ?? (int)DefaultOpenDuration.TotalSeconds));
        _failureThreshold = Math.Max(1, configured?.CircuitFailureThreshold ?? failureThreshold);
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public string State
    {
        get
        {
            lock (_gate)
            {
                RefreshCooldown();
                return _state;
            }
        }
    }

    public DateTimeOffset? NextProbeAtUtc
    {
        get
        {
            lock (_gate)
            {
                RefreshCooldown();
                return _openUntil;
            }
        }
    }

    public int ConsecutiveFailures
    {
        get { lock (_gate) return _consecutiveFailures; }
    }

    public string? LastFailureCode
    {
        get { lock (_gate) return _lastFailureCode; }
    }

    public DateTimeOffset? LastSuccessAtUtc
    {
        get { lock (_gate) return _lastSuccessAtUtc; }
    }

    public long AttemptCount
    {
        get { lock (_gate) return _attemptCount; }
    }

    public long SuccessCount
    {
        get { lock (_gate) return _successCount; }
    }

    public long FailureCount
    {
        get
        {
            lock (_gate)
                return _failureCounts.Values.Sum();
        }
    }

    public IReadOnlyDictionary<string, long> FailureCounts
    {
        get { lock (_gate) return new Dictionary<string, long>(_failureCounts, StringComparer.Ordinal); }
    }

    public bool CanAttempt()
    {
        lock (_gate)
        {
            RefreshCooldown();
            if (_state == "Closed")
            {
                _attemptCount++;
                return true;
            }
            if (_state == "Open") return false;
            if (_probeInFlight) return false;

            // Exactly one request owns the HalfOpen probe. Other callers remain
            // on deterministic fallback until that probe reports an outcome.
            _probeInFlight = true;
            _attemptCount++;
            return true;
        }
    }

    public void RecordSuccess()
    {
        lock (_gate)
        {
            _consecutiveFailures = 0;
            _openUntil = null;
            _probeInFlight = false;
            _state = "Closed";
            _lastSuccessAtUtc = _timeProvider.GetUtcNow();
            _successCount++;
        }
    }

    public void RecordFailure(string? failureCode = null)
    {
        lock (_gate)
        {
            var normalized = AiProviderStatusContract.FailureCodeFromProviderStatus(failureCode);
            if (normalized != AiProviderStatusContract.FailureNone)
                _failureCounts[normalized] = _failureCounts.TryGetValue(normalized, out var count) ? count + 1 : 1;
            if (!AiProviderStatusContract.IsCircuitFailure(failureCode))
            {
                // Non-transient failures and client cancellation release a
                // HalfOpen probe but never make healthy traffic unavailable.
                _probeInFlight = false;
                _lastFailureCode = normalized == AiProviderStatusContract.FailureNone ? null : normalized;
                return;
            }

            _probeInFlight = false;
            _lastFailureCode = normalized;
            _consecutiveFailures++;
            if (_consecutiveFailures >= _failureThreshold)
            {
                _openUntil = _timeProvider.GetUtcNow().Add(_openDuration);
                _state = "Open";
            }
            else
            {
                _state = "Closed";
            }
        }
    }

    private void RefreshCooldown()
    {
        if (_state == "Open" && _openUntil is { } until && until <= _timeProvider.GetUtcNow())
        {
            _state = "HalfOpen";
            _probeInFlight = false;
        }
    }
}
