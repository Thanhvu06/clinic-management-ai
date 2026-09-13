using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http.Json;
using System.Threading.Tasks;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.Mpi.DTOs;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Infrastructure.Mpi;
using ClinicManagement.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ClinicManagement.IntegrationTests;

public class HospitalCorePhase1FoundationsTests : IntegrationTestBase
{
    public HospitalCorePhase1FoundationsTests(CustomWebApplicationFactory factory) : base(factory) { }

    [Fact]
    public async Task MigrationSmokeTest_OnPopulatedSqlServerDatabase()
    {
        if (!await SqlServerTestHelper.IsSqlServerAvailableAsync())
        {
            // If SQL Server is not reachable in current host environment, test passes gracefully
            return;
        }

        var dbName = $"ClinicSmoke_{Guid.NewGuid():N}";
        var connStr = SqlServerTestHelper.GetTestDatabaseConnectionString(dbName);

        try
        {
            await SqlServerTestHelper.CreateDatabaseAsync(dbName);
            var options = SqlServerTestHelper.CreateSqlServerOptions(connStr);

            // 1. Migrate up to the last migration before Phase 1
            using (var initialContext = new AppDbContext(options))
            {
                var migrator = initialContext.Database.GetService<IMigrator>();
                await migrator.MigrateAsync("20260908072449_RemoveDiagnosticStatusDefaultValues");
            }

            // 2. Insert pre-Phase 1 user and patient (where Patients had NO FullName, NO MRN, and NOT NULL UserId)
            var preUserId = Guid.NewGuid();
            long prePatientId = 1;

            await using (var seedConn = new SqlConnection(connStr))
            {
                await seedConn.OpenAsync();
                await using var cmd = seedConn.CreateCommand();
                cmd.CommandText = @"
                    INSERT INTO AspNetUsers (
                        Id, UserName, NormalizedUserName, Email, NormalizedEmail, 
                        EmailConfirmed, PhoneNumber, PhoneNumberConfirmed, TwoFactorEnabled, 
                        LockoutEnabled, AccessFailedCount, FullName, IsActive, CreatedAt, UpdatedAt
                    ) VALUES (
                        @UserId, 'prephase1@clinic.local', 'PREPHASE1@CLINIC.LOCAL', 
                        'prephase1@clinic.local', 'PREPHASE1@CLINIC.LOCAL', 1, 
                        '0912345678', 1, 0, 0, 0, N'Nguyễn Văn Tiền Bối', 1, GETUTCDATE(), GETUTCDATE()
                    );

                    INSERT INTO Patients (UserId, DateOfBirth, Gender, Address)
                    VALUES (@UserId, '1985-03-15', 'Male', N'123 Phố Cổ, Hà Nội');

                    SELECT SCOPE_IDENTITY();
                ";
                cmd.Parameters.AddWithValue("@UserId", preUserId);
                var insertedId = await cmd.ExecuteScalarAsync();
                if (insertedId != null && insertedId != DBNull.Value)
                {
                    prePatientId = Convert.ToInt64(insertedId);
                }
            }

            // 3. Migrate forward to latest (AddHospitalOrganizationCoreAndMpi)
            using (var migrateContext = new AppDbContext(options))
            {
                var migrator = migrateContext.Database.GetService<IMigrator>();
                await migrator.MigrateAsync();
            }

            // 4. Verify existing patient data intact and backfilled
            using (var verifyContext = new AppDbContext(options))
            {
                var existingPatient = await verifyContext.Patients
                    .FirstOrDefaultAsync(p => p.Id == prePatientId);

                Assert.NotNull(existingPatient);
                Assert.Equal("Nguyễn Văn Tiền Bối", existingPatient.FullName);
                Assert.Equal("0912345678", existingPatient.PhoneNumber);
                Assert.Equal("prephase1@clinic.local", existingPatient.Email);
                Assert.NotNull(existingPatient.MedicalRecordNumber);
                Assert.Matches(@"^BN-\d{4}-\d{6}$", existingPatient.MedicalRecordNumber);
                Assert.Equal(preUserId, existingPatient.UserId);

                // Check MrnSequences initialized
                var currentYear = DateTime.UtcNow.Year;
                var seq = await verifyContext.MrnSequences.FirstOrDefaultAsync(s => s.Year == currentYear);
                Assert.NotNull(seq);
                Assert.True(seq.LastSequenceNumber >= 1);

                // 5. Test walk-in patient with UserId = null on migrated schema
                var walkIn = new Patient
                {
                    FullName = "Bệnh Nhân Vãng Lai",
                    PhoneNumber = "0987654321",
                    MedicalRecordNumber = $"BN-{currentYear}-000999",
                    Gender = Gender.Female,
                    DateOfBirth = new DateOnly(1995, 6, 15),
                    Address = "Đường mới",
                    UserId = null
                };
                verifyContext.Patients.Add(walkIn);
                await verifyContext.SaveChangesAsync();
                Assert.True(walkIn.Id > 0);
                Assert.Null(walkIn.UserId);

                // 6. Test Organization Facility insertion
                var facility = new Facility
                {
                    Code = "FAC-SMOKE-01",
                    Name = "Bệnh viện Đa khoa Quốc tế",
                    Address = "100 Đường Giải Phóng",
                    City = "Hà Nội",
                    Phone = "02438690000",
                    IsActive = true,
                    CreatedAtUtc = DateTime.UtcNow
                };
                verifyContext.Facilities.Add(facility);
                await verifyContext.SaveChangesAsync();
                Assert.True(facility.Id > 0);

                var fetchedFac = await verifyContext.Facilities.FirstOrDefaultAsync(f => f.Code == "FAC-SMOKE-01");
                Assert.NotNull(fetchedFac);
                Assert.Equal("Bệnh viện Đa khoa Quốc tế", fetchedFac.Name);
            }
        }
        finally
        {
            await SqlServerTestHelper.DropDatabaseAsync(dbName);
        }
    }

    [Fact]
    public async Task MrnGenerator_HighConcurrency_ShouldProduceUniqueSequences()
    {
        var isSqlServer = await SqlServerTestHelper.IsSqlServerAvailableAsync();
        string dbName = $"ClinicMrnConc_{Guid.NewGuid():N}";
        string? connStr = isSqlServer ? SqlServerTestHelper.GetTestDatabaseConnectionString(dbName) : null;

        try
        {
            DbContextOptions<AppDbContext> options;
            if (isSqlServer)
            {
                await SqlServerTestHelper.CreateDatabaseAsync(dbName);
                options = SqlServerTestHelper.CreateSqlServerOptions(connStr!);
                using var initCtx = new AppDbContext(options);
                await initCtx.Database.MigrateAsync();
            }
            else
            {
                var sqliteConn = new Microsoft.Data.Sqlite.SqliteConnection("DataSource=:memory:");
                await sqliteConn.OpenAsync();
                options = new DbContextOptionsBuilder<AppDbContext>()
                    .UseSqlite(sqliteConn)
                    .Options;
                using var initCtx = new AppDbContext(options);
                initCtx.Database.EnsureCreated();
            }

            const int concurrency = 20;
            var startBarrier = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            var tasks = Enumerable.Range(0, concurrency).Select(async _ =>
            {
                await startBarrier.Task;
                using var context = new AppDbContext(options);
                var generator = new MrnGenerator(context);
                return await generator.GenerateNextMrnAsync();
            }).ToList();

            startBarrier.SetResult(true);

            var generatedMrns = await Task.WhenAll(tasks);

            Assert.Equal(concurrency, generatedMrns.Length);
            var distinctMrns = generatedMrns.Distinct().ToList();
            Assert.Equal(concurrency, distinctMrns.Count);

            var currentYear = DateTime.UtcNow.Year;
            foreach (var mrn in generatedMrns)
            {
                Assert.Matches($@"^BN-{currentYear}-\d{{6}}$", mrn);
            }

            using (var verifyCtx = new AppDbContext(options))
            {
                var seq = await verifyCtx.MrnSequences.FirstOrDefaultAsync(s => s.Year == currentYear);
                Assert.NotNull(seq);
                Assert.Equal(concurrency, seq.LastSequenceNumber);
            }
        }
        finally
        {
            if (isSqlServer)
            {
                await SqlServerTestHelper.DropDatabaseAsync(dbName);
            }
        }
    }

    [Fact]
    public async Task WalkInRegistration_DecoupledFromAccount_ShouldNotCreateApplicationUser()
    {
        await AuthenticateAsync("rec@test.com");

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var initialUserCount = await db.Users.CountAsync();

        var uniquePhone = $"09{Random.Shared.Next(10000000, 99999999)}";
        var uniqueNid = $"0790{Random.Shared.Next(10000000, 99999999)}";

        var req = new RegisterWalkInPatientRequest
        {
            FullName = "Bệnh Nhân Vãng Lai Tách Tài Khoản",
            PhoneNumber = uniquePhone,
            NationalId = uniqueNid,
            Gender = Gender.Male,
            DateOfBirth = new DateOnly(1993, 4, 12),
            Address = "123 Đường Trần Phú"
        };

        var res = await Client.PostAsJsonAsync("/api/v1/mpi/patients/walk-in", req);
        var contentStr = await res.Content.ReadAsStringAsync();
        Assert.True(res.StatusCode == HttpStatusCode.Created, $"Expected Created but got {res.StatusCode}: {contentStr}");

        var created = await res.Content.ReadFromJsonAsync<ApiResponse<MpiPatientDto>>();
        Assert.NotNull(created?.Data);
        Assert.Null(created.Data.UserId);
        Assert.Equal(req.FullName, created.Data.FullName);
        Assert.Equal(req.PhoneNumber, created.Data.PhoneNumber);
        Assert.Matches(@"^BN-\d{4}-\d{6}$", created.Data.MedicalRecordNumber);

        using var verifyScope = Factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var finalUserCount = await verifyDb.Users.CountAsync();
        Assert.Equal(initialUserCount, finalUserCount);

        var patientInDb = await verifyDb.Patients.FindAsync(created.Data.Id);
        Assert.NotNull(patientInDb);
        Assert.Null(patientInDb.UserId);
    }

    [Fact]
    public async Task WalkInRegistration_MidTransactionFailureAfterMrnSequenceUpdate_RollsBackAllEntitiesAndSequence()
    {
        await using var atomicFactory = new AtomicityTestWebApplicationFactory();

        int currentYear = DateTime.UtcNow.Year;
        long initialSequence;
        int initialPatientCount;
        int initialAllergyCount;
        int initialEmergencyCount;

        using (var scope = atomicFactory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var seq = await db.MrnSequences.FirstOrDefaultAsync(s => s.Year == currentYear);
            initialSequence = seq?.LastSequenceNumber ?? 0;
            initialPatientCount = await db.Patients.CountAsync();
            initialAllergyCount = await db.PatientAllergies.CountAsync();
            initialEmergencyCount = await db.EmergencyContacts.CountAsync();
        }

        // Arm interceptor to fail on Save #2 (after MrnGenerator has saved sequence update on Save #1)
        atomicFactory.Interceptor.Reset();
        atomicFactory.Interceptor.FailOnSaveNumber = 2;
        atomicFactory.Interceptor.ExceptionFactory = () => new DbUpdateException(
            "[TEST] Injected failure on Save #2 (after MRN sequence update in transaction)",
            new Exception("Simulated DB disk failure during patient insert"));

        var req = new RegisterWalkInPatientRequest
        {
            FullName = "Bệnh Nhân Thất Bại Giữa Chừng",
            PhoneNumber = "0988777666",
            NationalId = "079011223344",
            Allergies = new List<CreatePatientAllergyRequest>
            {
                new() { AllergenType = AllergenType.Drug, AllergenName = "Penicillin", Severity = AllergySeverity.Severe }
            },
            EmergencyContact = new EmergencyContactDto
            {
                FullName = "Người Thân",
                Relationship = "Mẹ",
                PhoneNumber = "0988111222"
            }
        };

        using (var scope = atomicFactory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var mrnGen = new MrnGenerator(db);
            var mpiService = new MpiPatientService(db, mrnGen);

            await Assert.ThrowsAsync<DbUpdateException>(() => mpiService.RegisterWalkInPatientAsync(req));
        }

        Assert.True(atomicFactory.Interceptor.WasTriggered, "Interceptor must have been triggered on save #2.");

        // 3. Verify that the transaction completely rolled back:
        // - MrnSequences was rolled back to its initialSequence!
        // - Patients count was not incremented!
        // - PatientAllergies count was not incremented!
        // - EmergencyContacts count was not incremented!
        using (var verifyScope = atomicFactory.Services.CreateScope())
        {
            var verifyDb = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
            var finalSeq = await verifyDb.MrnSequences.FirstOrDefaultAsync(s => s.Year == currentYear);
            var finalSeqNumber = finalSeq?.LastSequenceNumber ?? 0;

            Assert.Equal(initialSequence, finalSeqNumber);
            Assert.Equal(initialPatientCount, await verifyDb.Patients.CountAsync());
            Assert.Equal(initialAllergyCount, await verifyDb.PatientAllergies.CountAsync());
            Assert.Equal(initialEmergencyCount, await verifyDb.EmergencyContacts.CountAsync());
        }
    }

    [Fact]
    public async Task WalkInRegistration_HighConcurrency_AllSucceedWithUniqueMrns()
    {
        await AuthenticateAsync("rec@test.com");

        const int concurrency = 15;
        var startBarrier = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        var tasks = Enumerable.Range(0, concurrency).Select(async i =>
        {
            await startBarrier.Task;
            var req = new RegisterWalkInPatientRequest
            {
                FullName = $"Bệnh Nhân Đồng Thời {i:D2}",
                PhoneNumber = $"09{Random.Shared.Next(10000000, 99999999)}",
                NationalId = $"079{Random.Shared.Next(100000000, 999999999)}",
                Gender = Gender.Male,
                DateOfBirth = new DateOnly(1990, 1, 1),
                Address = $"Địa chỉ test {i}"
            };

            var res = await Client.PostAsJsonAsync("/api/v1/mpi/patients/walk-in", req);
            var content = await res.Content.ReadAsStringAsync();
            Assert.True(res.StatusCode == HttpStatusCode.Created, $"Expected Created but got {res.StatusCode}: {content}");
            var dto = await res.Content.ReadFromJsonAsync<ApiResponse<MpiPatientDto>>();
            return dto!.Data!;
        }).ToList();

        startBarrier.SetResult(true);
        var results = await Task.WhenAll(tasks);

        Assert.Equal(concurrency, results.Length);
        var distinctMrns = results.Select(r => r.MedicalRecordNumber).Distinct().ToList();
        Assert.Equal(concurrency, distinctMrns.Count);

        var currentYear = DateTime.UtcNow.Year;
        foreach (var p in results)
        {
            Assert.Matches($@"^BN-{currentYear}-\d{{6}}$", p.MedicalRecordNumber);
            Assert.Null(p.UserId);
        }
    }

    [Fact]
    public async Task WalkInRegistration_SqlServerConcurrency_SameCccd_ExactlyOneSucceedsOthersGet409()
    {
        if (!await SqlServerTestHelper.IsSqlServerAvailableAsync())
        {
            return;
        }

        var dbName = $"ClinicCccdConc_{Guid.NewGuid():N}";
        var connStr = SqlServerTestHelper.GetTestDatabaseConnectionString(dbName);

        try
        {
            await SqlServerTestHelper.CreateDatabaseAsync(dbName);
            var options = SqlServerTestHelper.CreateSqlServerOptions(connStr);

            using (var initContext = new AppDbContext(options))
            {
                await initContext.Database.MigrateAsync();
            }

            const string duplicateCccd = "079199887766";
            const int concurrency = 5;
            var startBarrier = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            var tasks = Enumerable.Range(0, concurrency).Select(async i =>
            {
                await startBarrier.Task;
                using var context = new AppDbContext(options);
                var mrnGen = new MrnGenerator(context);
                var mpiService = new MpiPatientService(context, mrnGen);

                var req = new RegisterWalkInPatientRequest
                {
                    FullName = $"Bệnh Nhân CCCD Trùng {i}",
                    NationalId = duplicateCccd,
                    PhoneNumber = $"09{Random.Shared.Next(10000000, 99999999)}"
                };

                try
                {
                    var result = await mpiService.RegisterWalkInPatientAsync(req);
                    return (Success: true, Error: (string?)null);
                }
                catch (ConflictException ex)
                {
                    return (Success: false, Error: ex.Message);
                }
            }).ToList();

            startBarrier.SetResult(true);
            var results = await Task.WhenAll(tasks);

            var successCount = results.Count(r => r.Success);
            var conflictCount = results.Count(r => !r.Success && r.Error != null && r.Error.Contains(duplicateCccd));

            Assert.Equal(1, successCount);
            Assert.Equal(concurrency - 1, conflictCount);

            using (var verifyCtx = new AppDbContext(options))
            {
                var patientsWithCccd = await verifyCtx.Patients.Where(p => p.NationalId == duplicateCccd).ToListAsync();
                Assert.Single(patientsWithCccd);
            }
        }
        finally
        {
            await SqlServerTestHelper.DropDatabaseAsync(dbName);
        }
    }

    [Theory]
    [InlineData(3)]
    [InlineData(99)]
    [InlineData(-1)]
    public async Task GenderContract_UndefinedNumericEnum_MustBeRejectedWith400(int invalidGenderValue)
    {
        await AuthenticateAsync("rec@test.com");

        var rawJson = $@"{{
            ""fullName"": ""Bệnh Nhân Giới Tính Sai"",
            ""phoneNumber"": ""0912345678"",
            ""gender"": {invalidGenderValue}
        }}";

        var content = new System.Net.Http.StringContent(rawJson, System.Text.Encoding.UTF8, "application/json");
        var res = await Client.PostAsync("/api/v1/mpi/patients/walk-in", content);
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Theory]
    [InlineData(Gender.Male, "Male")]
    [InlineData(Gender.Female, "Female")]
    [InlineData(Gender.Other, "Other")]
    public async Task GenderContract_RoundTrip_SupportsAllGenders(Gender gender, string expectedGenderStr)
    {
        await AuthenticateAsync("rec@test.com");

        var req = new RegisterWalkInPatientRequest
        {
            FullName = $"Bệnh Nhân Giới Tính {expectedGenderStr}",
            PhoneNumber = $"09{Random.Shared.Next(10000000, 99999999)}",
            Gender = gender,
            DateOfBirth = new DateOnly(1990, 1, 1),
            Address = "Test Address"
        };

        var res = await Client.PostAsJsonAsync("/api/v1/mpi/patients/walk-in", req);
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);

        var created = await res.Content.ReadFromJsonAsync<ApiResponse<MpiPatientDto>>();
        Assert.NotNull(created?.Data);
        Assert.Equal(gender, created.Data.Gender);
        Assert.Equal(expectedGenderStr, created.Data.Gender.ToString());

        var getRes = await Client.GetAsync($"/api/v1/mpi/patients/by-mrn/{created.Data.MedicalRecordNumber}");
        Assert.Equal(HttpStatusCode.OK, getRes.StatusCode);
        var fetched = await getRes.Content.ReadFromJsonAsync<ApiResponse<MpiPatientDto>>();
        Assert.NotNull(fetched?.Data);
        Assert.Equal(gender, fetched.Data.Gender);
    }
}
