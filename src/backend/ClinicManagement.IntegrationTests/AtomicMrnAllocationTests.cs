using System.Data;
using System.Net;
using System.Net.Http.Json;
using ClinicManagement.Application.Authentication.DTOs;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.Visits.DTOs;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Infrastructure.Mpi;
using ClinicManagement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit.Abstractions;

namespace ClinicManagement.IntegrationTests;

public class AtomicMrnAllocationTests : IntegrationTestBase
{
    private readonly ITestOutputHelper _output;

    public AtomicMrnAllocationTests(CustomWebApplicationFactory factory, ITestOutputHelper output) : base(factory)
    {
        _output = output;
    }

    [Fact]
    public async Task Register_FortyConcurrentPatients_AllSucceedWithUniqueMrns()
    {
        var runId = Guid.NewGuid().ToString("N");
        var emails = Enumerable.Range(0, 40).Select(i => $"mrn-{runId}-{i}@test.com").ToArray();
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var tasks = emails.Select(async (email, i) =>
        {
            await start.Task;
            using var response = await Client.PostAsJsonAsync("/api/v1/auth/register", new RegisterRequest
            {
                FullName = $"Concurrent account patient {i}",
                Email = email,
                PhoneNumber = $"087654{i:D4}",
                Password = "Pass@123"
            });
            Assert.True(response.StatusCode == HttpStatusCode.Created,
                $"Expected Created, got {response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        }).ToArray();

        start.SetResult();
        await Task.WhenAll(tasks);

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var patients = await db.Patients.AsNoTracking().Where(p => emails.Contains(p.Email!)).ToListAsync();
        Assert.Equal(40, patients.Count);
        Assert.All(patients, p => Assert.NotNull(p.UserId));
        AssertUniqueMrns(patients.Select(p => p.MedicalRecordNumber), 40);
    }

    [Fact]
    public async Task Intake_TwentyConcurrentNewPatients_AllSucceedWithUniqueMrns()
    {
        using var client = await CreateAuthenticatedClientAsync("rec@test.com");
        long facilityId;
        long departmentId;
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var department = await db.Departments.AsNoTracking().FirstAsync(d => d.IsActive);
            facilityId = department.FacilityId;
            departmentId = department.Id;
        }

        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var tasks = Enumerable.Range(0, 20).Select(async i =>
        {
            await start.Task;
            var request = new ReceptionIntakeRequest
            {
                IdempotencyKey = $"atomic-mrn-{Guid.NewGuid():N}",
                FacilityId = facilityId,
                DepartmentId = departmentId,
                ChiefComplaint = "Concurrent new patient intake",
                NewPatient = new NewPatientProfileDto
                {
                    FullName = $"Concurrent intake patient {i}",
                    PhoneNumber = $"086543{i:D4}"
                }
            };
            using var response = await client.PostAsJsonAsync("/api/v1/patient-visits/intake", request);
            Assert.True(response.StatusCode == HttpStatusCode.OK,
                $"Expected OK, got {response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
            var body = await response.Content.ReadFromJsonAsync<ApiResponse<CheckInTicketDto>>();
            Assert.NotNull(body?.Data);
            return body.Data.MedicalRecordNumber;
        }).ToArray();

        start.SetResult();
        AssertUniqueMrns(await Task.WhenAll(tasks), 20);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GenerateNextMrn_PreservesUnrelatedAddedEntity(bool sequenceExists)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        if (!sequenceExists)
        {
            await db.MrnSequences.Where(s => s.Year == DateTime.UtcNow.Year).ExecuteDeleteAsync();
        }
        else
        {
            await new MrnGenerator(db).GenerateNextMrnAsync();
        }

        var initialSequence = await db.MrnSequences.AsNoTracking()
            .Where(s => s.Year == DateTime.UtcNow.Year)
            .Select(s => (long?)s.LastSequenceNumber).SingleOrDefaultAsync();

        var pending = new Facility { Code = $"PENDING-{Guid.NewGuid():N}", Name = "Pending facility" };
        db.Facilities.Add(pending);
        var entry = db.Entry(pending);

        await using var transaction = await db.Database.BeginTransactionAsync();
        var mrn = await new MrnGenerator(db).GenerateNextMrnAsync();

        Assert.Equal(EntityState.Added, entry.State);
        Assert.Contains(db.ChangeTracker.Entries<Facility>(), e => ReferenceEquals(e.Entity, pending));
        Assert.Empty(db.ChangeTracker.Entries<MrnSequence>());
        Assert.False(await db.Facilities.AsNoTracking().AnyAsync(f => f.Code == pending.Code));
        Assert.Matches($@"^BN-{DateTime.UtcNow.Year}-\d{{6}}$", mrn);
        Assert.Same(transaction, db.Database.CurrentTransaction);
        await transaction.RollbackAsync();
        Assert.Equal(initialSequence, await db.MrnSequences.AsNoTracking()
            .Where(s => s.Year == DateTime.UtcNow.Year)
            .Select(s => (long?)s.LastSequenceNumber).SingleOrDefaultAsync());
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task MissingYear_TenConcurrentAllocations_StartAfterLargestExistingMrn(bool callerTransaction, bool preferSqlServer)
    {
        // Match the existing SQL Server test helper's SQLite fallback on machines
        // without SQL Server. CI and LocalDB exercise both relational providers.
        var useSqlServer = preferSqlServer && await SqlServerTestHelper.IsSqlServerAvailableAsync();
        var databaseName = $"AtomicMrn_{Guid.NewGuid():N}";
        var path = Path.Combine(Path.GetTempPath(), databaseName + ".db");
        var options = useSqlServer
            ? SqlServerTestHelper.CreateSqlServerOptions(SqlServerTestHelper.GetTestDatabaseConnectionString(databaseName))
            : new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite($"Data Source={path};Pooling=False;Default Timeout=30").Options;
        var year = DateTime.UtcNow.Year;
        try
        {
            await using (var db = new AppDbContext(options))
            {
                _output.WriteLine($"Provider: {db.Database.ProviderName}");
                await db.Database.EnsureCreatedAsync();
                if (!useSqlServer)
                {
                    await db.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;");
                }
                db.Patients.AddRange(
                    new Patient { FullName = "Existing maximum", MedicalRecordNumber = $"BN-{year}-004200" },
                    new Patient { FullName = "Existing lower", MedicalRecordNumber = $"BN-{year}-000100" },
                    new Patient { FullName = "Previous year", MedicalRecordNumber = $"BN-{year - 1}-999999" });
                await db.SaveChangesAsync();
                Assert.Empty(await db.MrnSequences.ToListAsync());
            }

            var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var tasks = Enumerable.Range(0, 10).Select(_ => Task.Run(async () =>
            {
                await start.Task;
                await using var db = new AppDbContext(options);
                await using var transaction = callerTransaction
                    ? await db.Database.BeginTransactionAsync(IsolationLevel.Serializable)
                    : null;
                var mrn = await new MrnGenerator(db).GenerateNextMrnAsync();
                if (transaction != null)
                {
                    Assert.Same(transaction, db.Database.CurrentTransaction);
                    await transaction.CommitAsync();
                }
                else
                {
                    Assert.Null(db.Database.CurrentTransaction);
                }
                return mrn;
            })).ToArray();

            start.SetResult();
            var mrns = await Task.WhenAll(tasks);
            Assert.Equal(Enumerable.Range(4201, 10).Select(n => $"BN-{year}-{n:D6}"), mrns.OrderBy(m => m));
            await using var verify = new AppDbContext(options);
            var sequence = Assert.Single(await verify.MrnSequences.AsNoTracking().ToListAsync());
            Assert.Equal(year, sequence.Year);
            Assert.Equal(4210, sequence.LastSequenceNumber);
        }
        finally
        {
            if (useSqlServer)
            {
                await SqlServerTestHelper.DropDatabaseAsync(databaseName);
            }
            else
            {
                foreach (var suffix in new[] { "", "-wal", "-shm" })
                {
                    File.Delete(path + suffix);
                }
            }
        }
    }

    private static void AssertUniqueMrns(IEnumerable<string> values, int expectedCount)
    {
        var mrns = values.ToArray();
        Assert.Equal(expectedCount, mrns.Length);
        Assert.Equal(expectedCount, mrns.Distinct().Count());
        Assert.All(mrns, mrn => Assert.Matches($@"^BN-{DateTime.UtcNow.Year}-\d{{6}}$", mrn));
    }
}
