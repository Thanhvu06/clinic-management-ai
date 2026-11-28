using ClinicManagement.Infrastructure.Common;

namespace ClinicManagement.Api.Controllers;

public sealed class AppointmentLookupAttempts(TimeProvider clock)
{
    private const int FailureLimit = 5;
    private const int PartitionLimit = 50_000;
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan Lockout = TimeSpan.FromMinutes(15);
    private readonly object _gate = new();
    private readonly Dictionary<string, Partition> _partitions = new();
    private DateTimeOffset _nextCleanup;

    public async Task<List<T>?> RunAsync<T>(string ipAddress, string query, Func<Task<List<T>>> lookup)
    {
        var code = query.Length <= 64 ? DisplayCodeSearch.Canonical(query) : "";
        var ipKey = "ip:" + ipAddress;
        var codeKey = "code:" + code;
        Partition ip;
        Partition? byCode;
        lock (_gate)
        {
            var now = clock.GetUtcNow();
            if (now >= _nextCleanup)
            {
                foreach (var key in _partitions.Where(pair => pair.Value.IsExpired(now)).Select(pair => pair.Key).ToArray())
                    _partitions.Remove(key);
                _nextCleanup = now.AddMinutes(1);
            }

            if ((_partitions.Count >= PartitionLimit - 1) ||
                !TryGetPartition(ipKey, now, out ip) ||
                (code.Length > 0 && !TryGetPartition(codeKey, now, out _)))
                return null;

            byCode = code.Length > 0 ? _partitions[codeKey] : null;
            ip.Pending++;
            if (byCode is not null) byCode.Pending++;
        }

        var failed = false;
        try
        {
            var matches = await lookup();
            failed = matches.Count == 0;
            return matches;
        }
        finally
        {
            lock (_gate)
            {
                var now = clock.GetUtcNow();
                ip.Complete(failed, now);
                byCode?.Complete(failed, now);
            }
        }
    }

    private bool TryGetPartition(string key, DateTimeOffset now, out Partition partition)
    {
        if (!_partitions.TryGetValue(key, out var existing) || existing.IsExpired(now))
            _partitions[key] = existing = new Partition(now + Window);
        partition = existing;
        return partition.BlockedUntil <= now && partition.Failures + partition.Pending < FailureLimit;
    }

    private sealed class Partition(DateTimeOffset expiresAt)
    {
        public int Pending { get; set; }
        public int Failures { get; private set; }
        public DateTimeOffset BlockedUntil { get; private set; }

        public bool IsExpired(DateTimeOffset now) =>
            Pending == 0 && expiresAt <= now && BlockedUntil <= now;

        public void Complete(bool failed, DateTimeOffset now)
        {
            Pending--;
            if (failed && ++Failures == FailureLimit)
                BlockedUntil = now + Lockout;
        }
    }
}
