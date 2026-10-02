namespace ClinicManagement.IntegrationTests;

/// <summary>
/// Process-wide unique queue numbers for PatientVisit fixtures. Fixtures share
/// a test database and the unique index (VisitDate, FacilityId, DepartmentId,
/// QueueNumber), so random numbers could collide. The range starts well above
/// the fixed fixture values (100+i, 555-999, 10000+n, 20000+n) and the
/// service-issued DailyQueueSequence numbers, which start at 1.
/// </summary>
internal static class TestQueueNumbers
{
    private const int Base = 500_000;
    private static int _last = Base;

    public static int Next() => Interlocked.Increment(ref _last);
}
