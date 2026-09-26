using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace ClinicManagement.IntegrationTests;

/// <summary>
/// A test-only EF Core <see cref="SaveChangesInterceptor"/> that throws a controlled
/// exception after a configurable number of <c>SaveChanges</c> calls. This is used
/// exclusively to prove that the diagnostic-order creation transaction correctly
/// rolls back ALL partial state when a mid-transaction failure occurs, and that
/// non-OrderCode <see cref="Microsoft.EntityFrameworkCore.DbUpdateException"/> instances
/// are rethrown rather than converted to ORDER_CODE_COLLISION.
/// </summary>
public sealed class SaveFailureInterceptor : SaveChangesInterceptor
{
    private int _saveCallCount;
    private int _wasTriggered;

    /// <summary>
    /// The number of times SavingChangesAsync has been invoked since the last reset.
    /// </summary>
    public int SaveCallCount => _saveCallCount;

    /// <summary>
    /// Indicates whether the controlled failure was triggered.
    /// </summary>
    public bool WasTriggered => Volatile.Read(ref _wasTriggered) == 1;

    /// <summary>
    /// When &gt; 0, the interceptor throws on exactly this save-call number.
    /// Set to 0 to disable the interceptor (no-op mode).
    /// </summary>
    public int FailOnSaveNumber { get; set; }

    /// <summary>
    /// Optional custom exception factory. If null, a controlled <see cref="InvalidOperationException"/> is thrown.
    /// </summary>
    public Func<Exception>? ExceptionFactory { get; set; }

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
                Interlocked.Exchange(ref _wasTriggered, 1);
                // Simulate a DB error that occurs after the first SaveChanges executed/flushed inside the still-uncommitted transaction
                // but before the audit-log + notification SaveChangesAsync.
                if (ExceptionFactory != null)
                {
                    throw ExceptionFactory();
                }

                throw new InvalidOperationException(
                    "[TEST INTERCEPTOR] Controlled failure injected to verify transaction rollback atomicity.");
            }
        }

        return await base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    /// <summary>Resets the internal call counter and state so the interceptor can be re-armed.</summary>
    public void Reset()
    {
        Interlocked.Exchange(ref _saveCallCount, 0);
        Interlocked.Exchange(ref _wasTriggered, 0);
        FailOnSaveNumber = 0;
        ExceptionFactory = null;
    }
}
