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
        const int maxRetries = 3;

        for (var attempt = 0; attempt < maxRetries; attempt++)
        {
            try
            {
                var sequence = await _dbContext.MrnSequences
                    .FirstOrDefaultAsync(s => s.Year == currentYear, cancellationToken);

                if (sequence == null)
                {
                    sequence = new MrnSequence
                    {
                        Year = currentYear,
                        LastSequenceNumber = 1
                    };
                    _dbContext.MrnSequences.Add(sequence);
                }
                else
                {
                    sequence.LastSequenceNumber++;
                }

                await _dbContext.SaveChangesAsync(cancellationToken);

                return $"MRN-{currentYear}-{sequence.LastSequenceNumber:D6}";
            }
            catch (DbUpdateConcurrencyException) when (attempt < maxRetries - 1)
            {
                // Retry on concurrency collision
                await Task.Delay(50 * (attempt + 1), cancellationToken);
            }
        }

        // Fallback in case of persistent collision
        return $"MRN-{currentYear}-{Guid.NewGuid().ToString("N")[..8].ToUpperInvariant()}";
    }
}
