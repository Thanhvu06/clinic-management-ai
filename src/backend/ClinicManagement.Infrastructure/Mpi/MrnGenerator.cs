using System;
using System.Threading;
using System.Threading.Tasks;
using ClinicManagement.Application.Mpi.Interfaces;
using ClinicManagement.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace ClinicManagement.Infrastructure.Mpi;

public class MrnGenerator : IMrnGenerator
{
    private readonly AppDbContext _dbContext;

    public MrnGenerator(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<string> GenerateNextMrnAsync(CancellationToken cancellationToken = default)
    {
        var currentYear = DateTime.UtcNow.Year;
        const int maxAttempts = 3;
        await using var ownedTransaction = _dbContext.Database.CurrentTransaction == null
            ? await _dbContext.Database.BeginTransactionAsync(cancellationToken)
            : null;

        for (var attempt = 0; attempt < maxAttempts; attempt++)
        {
            await EnsureSequenceAsync(currentYear, cancellationToken);

            var affected = await _dbContext.MrnSequences
                .Where(s => s.Year == currentYear)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.LastSequenceNumber,
                    x => x.LastSequenceNumber + 1), cancellationToken);

            if (affected == 0)
            {
                continue;
            }

            // The UPDATE's write lock is held until the caller (or this method)
            // commits, so no other allocator can change the value before this read.
            var sequenceNumber = await _dbContext.MrnSequences.AsNoTracking()
                .Where(s => s.Year == currentYear)
                .Select(s => s.LastSequenceNumber)
                .SingleAsync(cancellationToken);

            if (ownedTransaction != null)
            {
                await ownedTransaction.CommitAsync(cancellationToken);
            }

            return $"BN-{currentYear}-{sequenceNumber:D6}";
        }

        throw new InvalidOperationException($"Không thể phát sinh mã bệnh án duy nhất cho năm {currentYear} sau {maxAttempts} lần thử.");
    }

    private async Task EnsureSequenceAsync(int year, CancellationToken cancellationToken)
    {
        // Avoid shared-to-exclusive lock conversion deadlocks, including when the
        // caller uses Serializable. HOLDLOCK also protects the missing year's gap.
        var sequences = _dbContext.Database.IsSqlServer()
            ? _dbContext.MrnSequences.FromSqlInterpolated(
                $"SELECT * FROM [MrnSequences] WITH (UPDLOCK, HOLDLOCK) WHERE [Year] = {year}")
            : _dbContext.MrnSequences.Where(s => s.Year == year);

        if (await sequences.AsNoTracking().AnyAsync(cancellationToken))
        {
            return;
        }

        var prefix = $"BN-{year}-";
        var maxExistingMrn = await _dbContext.Patients.AsNoTracking()
            .Where(p => p.MedicalRecordNumber.StartsWith(prefix))
            .Select(p => p.MedicalRecordNumber)
            .OrderByDescending(m => m)
            .FirstOrDefaultAsync(cancellationToken);

        long initialSequence = 0;
        if (maxExistingMrn != null && maxExistingMrn.Length >= prefix.Length + 6
            && long.TryParse(maxExistingMrn.Substring(prefix.Length), out var parsed))
        {
            initialSequence = parsed;
        }

        try
        {
            // Direct SQL must not flush or detach any pending entities of the caller.
            await _dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"INSERT INTO [MrnSequences] ([Year], [LastSequenceNumber]) VALUES ({year}, {initialSequence})",
                cancellationToken);
        }
        catch (SqlException ex) when (ex.Number is 2601 or 2627)
        {
            // Another request initialized this year's unique sequence row.
        }
        catch (SqliteException ex) when (ex.SqliteExtendedErrorCode == 2067)
        {
            // SQLite's SQLITE_CONSTRAINT_UNIQUE for the Year index.
        }
    }
}
