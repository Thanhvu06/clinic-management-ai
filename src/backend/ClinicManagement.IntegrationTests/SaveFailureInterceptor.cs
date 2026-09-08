using System;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace ClinicManagement.IntegrationTests;

/// <summary>
/// A test-only EF Core <see cref="DbCommandInterceptor"/> that throws a controlled
/// <see cref="InvalidOperationException"/> after a configurable number of successful
/// <c>SaveChanges</c> calls. This is used exclusively to prove that the diagnostic-order
/// creation transaction correctly rolls back ALL partial state when the second
/// <c>SaveChangesAsync</c> (audit-log + notifications) fails.
///
/// <para>Reset <see cref="FailOnSaveNumber"/> to <c>0</c> (default) to disable.</para>
/// </summary>
public sealed class SaveFailureInterceptor : SaveChangesInterceptor
{
    private int _saveCallCount;

    /// <summary>
    /// When &gt; 0, the interceptor throws on exactly this save-call number.
    /// Set to 0 to disable the interceptor (no-op mode).
    /// </summary>
    public int FailOnSaveNumber { get; set; }

    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        if (FailOnSaveNumber > 0)
        {
            var current = Interlocked.Increment(ref _saveCallCount);
            if (current == FailOnSaveNumber)
            {
                // Simulate a DB error that occurs after the first INSERT batch committed
                // but before the audit-log + notification INSERT batch.
                throw new InvalidOperationException(
                    "[TEST INTERCEPTOR] Controlled failure injected to verify transaction rollback atomicity.");
            }
        }

        return await base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    /// <summary>Resets the internal call counter so the interceptor can be re-armed.</summary>
    public void Reset() => Interlocked.Exchange(ref _saveCallCount, 0);
}
