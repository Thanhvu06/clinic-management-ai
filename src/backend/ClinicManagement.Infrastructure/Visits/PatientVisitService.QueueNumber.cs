using System;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using ClinicManagement.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace ClinicManagement.Infrastructure.Visits;

public partial class PatientVisitService
{
    private async Task<int> GetNextQueueNumberAsync(long facilityId, long departmentId, DateOnly date, CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 20; attempt++)
        {
            var sequence = await _dbContext.DailyQueueSequences
                .FirstOrDefaultAsync(s => s.FacilityId == facilityId && s.DepartmentId == departmentId && s.Date == date, cancellationToken);

            var floor = await _dbContext.PatientVisits
                .Where(v => v.FacilityId == facilityId && v.DepartmentId == departmentId && v.VisitDate == date)
                .MaxAsync(v => (int?)v.QueueNumber, cancellationToken) ?? 0;

            if (sequence == null)
            {
                sequence = new DailyQueueSequence
                {
                    FacilityId = facilityId,
                    DepartmentId = departmentId,
                    Date = date,
                    LastNumber = floor + 1
                };
                _dbContext.DailyQueueSequences.Add(sequence);
                try
                {
                    await _dbContext.SaveChangesAsync(cancellationToken);
                    return sequence.LastNumber;
                }
                catch (DbUpdateException)
                {
                    _dbContext.Entry(sequence).State = EntityState.Detached;
                    await Task.Delay(RandomNumberGenerator.GetInt32(10, 50), cancellationToken);
                    continue;
                }
            }
            else
            {
                sequence.LastNumber = Math.Max(sequence.LastNumber, floor) + 1;
                try
                {
                    await _dbContext.SaveChangesAsync(cancellationToken);
                    return sequence.LastNumber;
                }
                catch (DbUpdateConcurrencyException)
                {
                    try { await _dbContext.Entry(sequence).ReloadAsync(cancellationToken); } catch { _dbContext.Entry(sequence).State = EntityState.Detached; }
                    await Task.Delay(RandomNumberGenerator.GetInt32(10, 50), cancellationToken);
                    continue;
                }
                catch (DbUpdateException)
                {
                    try { await _dbContext.Entry(sequence).ReloadAsync(cancellationToken); } catch { _dbContext.Entry(sequence).State = EntityState.Detached; }
                    await Task.Delay(RandomNumberGenerator.GetInt32(10, 50), cancellationToken);
                    continue;
                }
            }
        }

        throw new InvalidOperationException("Failed to allocate queue number after multiple attempts due to high concurrency (QUEUE_NUMBER_COLLISION).");
    }

}
