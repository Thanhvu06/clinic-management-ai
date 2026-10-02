using System;
using System.Threading;
using System.Threading.Tasks;
using ClinicManagement.Application.Mpi.Interfaces;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Infrastructure.Persistence;
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
        const int maxRetries = 10;

        for (var attempt = 0; attempt < maxRetries; attempt++)
        {
            try
            {
                var sequence = await _dbContext.MrnSequences
                    .FirstOrDefaultAsync(s => s.Year == currentYear, cancellationToken);

                if (sequence == null)
                {
                    var prefix = $"BN-{currentYear}-";
                    var maxExistingMrn = await _dbContext.Patients
                        .Where(p => p.MedicalRecordNumber.StartsWith(prefix))
                        .Select(p => p.MedicalRecordNumber)
                        .OrderByDescending(m => m)
                        .FirstOrDefaultAsync(cancellationToken);

                    long initialSeq = 0;
                    if (maxExistingMrn != null && maxExistingMrn.Length >= prefix.Length + 6)
                    {
                        var seqPart = maxExistingMrn.Substring(prefix.Length);
                        if (long.TryParse(seqPart, out var parsed))
                        {
                            initialSeq = parsed;
                        }
                    }

                    sequence = new MrnSequence
                    {
                        Year = currentYear,
                        LastSequenceNumber = initialSeq + 1
                    };
                    _dbContext.MrnSequences.Add(sequence);
                }
                else
                {
                    sequence.LastSequenceNumber++;
                }

                await _dbContext.SaveChangesAsync(cancellationToken);

                return $"BN-{currentYear}-{sequence.LastSequenceNumber:D6}";
            }
            catch (Exception ex) when (ex is DbUpdateConcurrencyException or DbUpdateException)
            {
                // Always clear change tracker to purge the failed entity state before retry
                _dbContext.ChangeTracker.Clear();

                if (attempt == maxRetries - 1)
                {
                    throw new InvalidOperationException(
                        $"Không thể phát sinh mã bệnh án duy nhất sau {maxRetries} lần thử do xung đột dữ liệu đồng thời.", ex);
                }

                // Exponential backoff with random jitter to prevent lock-step retry collisions
                var delayMs = Random.Shared.Next(25, 75) * (attempt + 1);
                await Task.Delay(delayMs, cancellationToken);
            }
        }

        throw new InvalidOperationException($"Không thể phát sinh mã bệnh án duy nhất cho năm {currentYear}.");
    }
}
