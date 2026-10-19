using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ClinicManagement.Application.AI;
using ClinicManagement.Application.AI.DTOs;
using ClinicManagement.Application.AI.Interfaces;
using ClinicManagement.Application.Common.Constants;
using ClinicManagement.Application.AI.Tools;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Infrastructure.Identity;
using ClinicManagement.Infrastructure.AI;
using ClinicManagement.Infrastructure.Persistence;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;

namespace ClinicManagement.AI.LiveCanary;

internal sealed class SyntheticCanaryFactory : WebApplicationFactory<global::Program>
{
    private readonly string _dbFilePath = Path.Combine(Path.GetTempPath(), $"clinic_gate_d_canary_{Guid.NewGuid():N}.db");
    private readonly IAiProviderAttemptBudget _attemptBudget;
    private readonly HttpMessageHandler? _providerHandler;
    private readonly string? _serverUrl;
    private readonly bool _providerEnabled;
    private readonly bool _roleIntentEnabled;

    public SyntheticCanaryFactory(
        IAiProviderAttemptBudget attemptBudget,
        HttpMessageHandler? providerHandler = null,
        string? serverUrl = null,
        bool providerEnabled = true,
        bool roleIntentEnabled = true)
    {
        _attemptBudget = attemptBudget;
        _providerHandler = providerHandler;
        _serverUrl = serverUrl;
        _providerEnabled = providerEnabled;
        _roleIntentEnabled = roleIntentEnabled;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        if (!_roleIntentEnabled)
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
                new Dictionary<string, string?> { ["RoleIntentModel:Enabled"] = "false" }));
        if (!string.IsNullOrWhiteSpace(_serverUrl))
            builder.UseKestrel().UseUrls(_serverUrl);
        builder.ConfigureLogging(logging =>
        {
            logging.ClearProviders();
            logging.AddDebug();
        });
        builder.ConfigureServices(services =>
        {
            services.AddDataProtection().UseEphemeralDataProtectionProvider();
            var descriptors = services.Where(d =>
                d.ServiceType.Name.Contains("DbContextOptions", StringComparison.Ordinal) ||
                d.ServiceType == typeof(System.Data.Common.DbConnection) ||
                d.ServiceType == typeof(AppDbContext)).ToList();
            foreach (var descriptor in descriptors) services.Remove(descriptor);

            var connectionString = new SqliteConnectionStringBuilder
            {
                DataSource = _dbFilePath,
                Mode = SqliteOpenMode.ReadWriteCreate,
                DefaultTimeout = 15
            }.ToString();
            using (var initConnection = new SqliteConnection(connectionString))
            {
                initConnection.Open();
                using var command = initConnection.CreateCommand();
                command.CommandText = "PRAGMA journal_mode = WAL; PRAGMA busy_timeout = 15000;";
                command.ExecuteNonQuery();
            }

            using (var initDb = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connectionString).Options))
                initDb.Database.EnsureCreated();

            services.AddDbContext<AppDbContext>(options => options.UseSqlite(connectionString));
            services.AddSingleton(_attemptBudget);

            if (_providerHandler is not null)
            {
                foreach (var descriptor in services
                    .Where(d => d.ServiceType == typeof(IAiSpecialtySuggestionProvider))
                    .ToList())
                    services.Remove(descriptor);

                services.PostConfigure<AiProviderOptions>(options =>
                {
                    options.IsEnabled = _providerEnabled;
                    options.ApiKey = "synthetic-e2e-key";
                    options.ProviderUrl = "https://synthetic-gemini.invalid";
                    options.ModelName = "synthetic-e2e-model";
                    options.TimeoutSeconds = 2;
                    options.MaxAttempts = 2;
                    options.RetryBaseDelayMilliseconds = 10;
                    options.CircuitFailureThreshold = 3;
                    options.CircuitCooldownSeconds = 1;
                });
                services.AddHttpClient<IAiSpecialtySuggestionProvider, GeminiAiProvider>()
                    .ConfigurePrimaryHttpMessageHandler(() => _providerHandler);
            }
        });
    }

    public async Task SeedAsync()
    {
        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var userManager = scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Identity.UserManager<ApplicationUser>>();
        var roleManager = scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Identity.RoleManager<Microsoft.AspNetCore.Identity.IdentityRole<Guid>>>();

        foreach (var role in new[] { RoleNames.Patient, RoleNames.Receptionist, RoleNames.Doctor, RoleNames.Admin, RoleNames.Pharmacist, RoleNames.DiagnosticTechnician })
            if (!await roleManager.RoleExistsAsync(role))
                (await roleManager.CreateAsync(new Microsoft.AspNetCore.Identity.IdentityRole<Guid>(role))).Succeeded.ShouldBeTrue(role);

        var facility = new Facility
        {
            Code = "CANARY-ALPHA",
            Name = "Synthetic Gate D Facility",
            Address = "Synthetic only",
            City = "Synthetic",
            Phone = "0000001000",
            IsActive = true
        };
        var secondFacility = new Facility
        {
            Code = "CANARY-BETA",
            Name = "Synthetic Gate D Secondary Facility",
            Address = "Synthetic only",
            City = "Synthetic",
            Phone = "0000001007",
            IsActive = true
        };
        db.Facilities.AddRange(facility, secondFacility);
        var specialty = new Specialty
        {
            SpecialtyCode = "CANARY-SP",
            Name = "Synthetic General Medicine",
            Description = "Synthetic canary catalog entry",
            IsActive = true,
            AiEnabled = true,
            ConsultationFee = 1m
        };
        db.Specialties.Add(specialty);
        await db.SaveChangesAsync();
        var department = new Department
        {
            FacilityId = facility.Id,
            SpecialtyId = specialty.Id,
            Code = "CANARY-DEPT",
            Name = "Synthetic Canary Department",
            DepartmentType = DepartmentType.Clinical,
            IsActive = true
        };
        var secondDepartment = new Department
        {
            FacilityId = secondFacility.Id,
            SpecialtyId = specialty.Id,
            Code = "CANARY-DEPT-BETA",
            Name = "Synthetic Secondary Department",
            DepartmentType = DepartmentType.Clinical,
            IsActive = true
        };
        db.Departments.AddRange(department, secondDepartment);
        await db.SaveChangesAsync();

        var accounts = new[]
        {
            (Email: "canary.patient@synthetic.invalid", Name: "Synthetic Patient", Phone: "0000001001", Role: RoleNames.Patient),
            (Email: "canary.reception@synthetic.invalid", Name: "Synthetic Reception", Phone: "0000001002", Role: RoleNames.Receptionist),
            (Email: "canary.doctor@synthetic.invalid", Name: "Synthetic Doctor", Phone: "0000001003", Role: RoleNames.Doctor),
            (Email: "canary.technician@synthetic.invalid", Name: "Synthetic Technician", Phone: "0000001004", Role: RoleNames.DiagnosticTechnician),
            (Email: "canary.pharmacist@synthetic.invalid", Name: "Synthetic Pharmacist", Phone: "0000001005", Role: RoleNames.Pharmacist),
            (Email: "canary.admin@synthetic.invalid", Name: "Synthetic Admin", Phone: "0000001006", Role: RoleNames.Admin)
        };
        var users = new Dictionary<string, ApplicationUser>(StringComparer.OrdinalIgnoreCase);
        foreach (var account in accounts)
        {
            var user = new ApplicationUser
            {
                UserName = account.Email,
                Email = account.Email,
                EmailConfirmed = true,
                PhoneNumber = account.Phone,
                FullName = account.Name,
                IsActive = true,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            var result = await userManager.CreateAsync(user, "Canary@12345");
            result.Succeeded.ShouldBeTrue(account.Email);
            (await userManager.AddToRoleAsync(user, account.Role)).Succeeded.ShouldBeTrue(account.Role);
            users[account.Role] = user;
        }

        var secondPatientUser = new ApplicationUser
        {
            UserName = "canary.patient-b@synthetic.invalid",
            Email = "canary.patient-b@synthetic.invalid",
            EmailConfirmed = true,
            PhoneNumber = "0000001008",
            FullName = "Synthetic Patient B",
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        (await userManager.CreateAsync(secondPatientUser, "Canary@12345")).Succeeded.ShouldBeTrue(secondPatientUser.Email!);
        (await userManager.AddToRoleAsync(secondPatientUser, RoleNames.Patient)).Succeeded.ShouldBeTrue(RoleNames.Patient);
        users["PatientB"] = secondPatientUser;

        var secondDoctorUser = new ApplicationUser
        {
            UserName = "canary.doctor-b@synthetic.invalid",
            Email = "canary.doctor-b@synthetic.invalid",
            EmailConfirmed = true,
            PhoneNumber = "0000001009",
            FullName = "Synthetic Doctor B",
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        (await userManager.CreateAsync(secondDoctorUser, "Canary@12345")).Succeeded.ShouldBeTrue(secondDoctorUser.Email!);
        (await userManager.AddToRoleAsync(secondDoctorUser, RoleNames.Doctor)).Succeeded.ShouldBeTrue(RoleNames.Doctor);
        users["DoctorB"] = secondDoctorUser;

        var doctor = new Doctor
        {
            UserId = users[RoleNames.Doctor].Id,
            AcademicTitle = "Synthetic MD",
            ExperienceYears = 1,
            Description = "Synthetic canary doctor",
            IsActive = true
        };
        var secondDoctor = new Doctor
        {
            UserId = users["DoctorB"].Id,
            AcademicTitle = "Synthetic MD B",
            ExperienceYears = 1,
            Description = "Synthetic secondary facility doctor",
            IsActive = true
        };
        db.Doctors.AddRange(doctor, secondDoctor);
        var patient = new Patient
        {
            UserId = users[RoleNames.Patient].Id,
            MedicalRecordNumber = "CANARY-MRN-001",
            FullName = "Synthetic Patient",
            PhoneNumber = "0000001001",
            Email = accounts[0].Email,
            Gender = Gender.Other,
            PrimaryFacilityId = facility.Id
        };
        db.Patients.Add(patient);
        var secondPatient = new Patient
        {
            UserId = users["PatientB"].Id,
            MedicalRecordNumber = "CANARY-MRN-002",
            FullName = "Synthetic Patient B",
            PhoneNumber = "0000001008",
            Email = "canary.patient-b@synthetic.invalid",
            Gender = Gender.Other,
            PrimaryFacilityId = secondFacility.Id
        };
        db.Patients.Add(secondPatient);
        await db.SaveChangesAsync();
        db.DoctorSpecialties.AddRange(
            new DoctorSpecialty { DoctorId = doctor.Id, SpecialtyId = specialty.Id, IsPrimary = true },
            new DoctorSpecialty { DoctorId = secondDoctor.Id, SpecialtyId = specialty.Id, IsPrimary = true });
        foreach (var role in new[] { RoleNames.Receptionist, RoleNames.Doctor, RoleNames.DiagnosticTechnician, RoleNames.Pharmacist })
            db.StaffFacilityAssignments.Add(new StaffFacilityAssignment
            {
                UserId = users[role].Id,
                FacilityId = facility.Id,
                DepartmentId = department.Id,
                Role = role,
                IsPrimary = true,
                IsActive = true
            });
        db.StaffFacilityAssignments.Add(new StaffFacilityAssignment
        {
            UserId = users["DoctorB"].Id,
            FacilityId = secondFacility.Id,
            DepartmentId = secondDepartment.Id,
            Role = RoleNames.Doctor,
            IsPrimary = true,
            IsActive = true
        });
        await db.SaveChangesAsync();

        // Keep every actor case grounded in the same synthetic encounter. The
        // rows are read-only canary fixtures and are fingerprinted before and
        // after the HTTP run; no real patient or production database is used.
        var today = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(7));
        var now = DateTime.UtcNow;
        // Disabled-provider browser acceptance needs bookable synthetic slots.
        // Include a Sunday fixture so the browser can verify it is filtered out.
        if (_providerHandler is not null && !_providerEnabled)
        {
            // The existing booking endpoint requires a complete synthetic patient profile.
            patient.DateOfBirth = new DateOnly(1990, 1, 1);
            for (var offset = 1; offset <= 13; offset++)
            {
                var day = today.AddDays(offset);
                db.DoctorWorkSchedules.Add(new DoctorWorkSchedule
                {
                    DoctorId = doctor.Id, WorkDate = day,
                    StartTime = new TimeOnly(14, 0), EndTime = new TimeOnly(15, 0), IsActive = true
                });
                db.AppointmentSlots.Add(new AppointmentSlot
                {
                    DoctorId = doctor.Id, SlotDate = day,
                    StartTime = new TimeOnly(14, 0), EndTime = new TimeOnly(14, 30), IsBooked = false
                });
            }
        }
        var slot = new AppointmentSlot
        {
            DoctorId = doctor.Id,
            SlotDate = today,
            StartTime = new TimeOnly(9, 0),
            EndTime = new TimeOnly(9, 30),
            IsBooked = true
        };
        db.AppointmentSlots.Add(slot);
        var diagnosticService = new DiagnosticService
        {
            Code = "CANARY-LAB-001",
            Name = "Synthetic blood test",
            Category = DiagnosticCategory.Laboratory,
            PreparationInstructions = "Synthetic canary only",
            Price = 1m,
            IsActive = true
        };
        db.DiagnosticServices.Add(diagnosticService);
        var medicine = new Medicine
        {
            Code = "CANARY-MED-001",
            Name = "Synthetic medicine",
            Unit = "tablet",
            StockQuantity = 100,
            ReorderLevel = 10,
            UnitPrice = 1m,
            IsActive = true
        };
        db.Medicines.Add(medicine);
        await db.SaveChangesAsync();

        var appointment = new Appointment
        {
            AppointmentCode = "CANARY-APT-001",
            PatientId = patient.Id,
            DoctorId = doctor.Id,
            SpecialtyId = specialty.Id,
            FacilityId = facility.Id,
            AppointmentSlotId = slot.Id,
            AppointmentDate = today,
            StartTime = slot.StartTime,
            EndTime = slot.EndTime,
            Reason = "Synthetic canary read fixture",
            Status = AppointmentStatus.Confirmed
        };
        db.Appointments.Add(appointment);
        await db.SaveChangesAsync();

        var secondSlot = new AppointmentSlot
        {
            DoctorId = secondDoctor.Id,
            SlotDate = today,
            StartTime = new TimeOnly(10, 0),
            EndTime = new TimeOnly(10, 30),
            IsBooked = true
        };
        db.AppointmentSlots.Add(secondSlot);
        await db.SaveChangesAsync();
        db.Appointments.Add(new Appointment
        {
            AppointmentCode = "CANARY-APT-002",
            PatientId = secondPatient.Id,
            DoctorId = secondDoctor.Id,
            SpecialtyId = specialty.Id,
            FacilityId = secondFacility.Id,
            AppointmentSlotId = secondSlot.Id,
            AppointmentDate = today,
            StartTime = secondSlot.StartTime,
            EndTime = secondSlot.EndTime,
            Reason = "Synthetic secondary facility isolation fixture",
            Status = AppointmentStatus.Confirmed
        });
        await db.SaveChangesAsync();

        var visit = new PatientVisit
        {
            VisitCode = "CANARY-VIS-001",
            PatientId = patient.Id,
            AppointmentId = appointment.Id,
            FacilityId = facility.Id,
            DepartmentId = department.Id,
            AssignedDoctorId = doctor.Id,
            VisitDate = today,
            ArrivalType = VisitArrivalType.Scheduled,
            Priority = VisitPriority.Normal,
            ChiefComplaint = "Synthetic canary read fixture",
            QueueNumber = 1,
            Status = VisitStatus.WaitingForDoctor,
            CreatedByUserId = users[RoleNames.Receptionist].Id,
            CheckedInAtUtc = now,
            CreatedAtUtc = now
        };
        db.PatientVisits.Add(visit);
        await db.SaveChangesAsync();

        var diagnosticOrder = new DiagnosticOrder
        {
            OrderCode = "CANARY-ORD-001",
            AppointmentId = appointment.Id,
            PatientVisitId = visit.Id,
            PatientId = patient.Id,
            OrderingDoctorId = doctor.Id,
            FacilityId = facility.Id,
            PerformingDepartmentId = department.Id,
            ClinicalIndication = "Synthetic canary read fixture",
            Status = DiagnosticOrderStatus.Ordered,
            OrderedAtUtc = now
        };
        diagnosticOrder.Items.Add(new DiagnosticOrderItem
        {
            DiagnosticServiceId = diagnosticService.Id,
            Status = DiagnosticItemStatus.Ordered,
            IsPackageCovered = false
        });
        db.DiagnosticOrders.Add(diagnosticOrder);

        var prescription = new Prescription
        {
            AppointmentId = appointment.Id,
            PatientVisitId = visit.Id,
            PatientId = patient.Id,
            DoctorId = doctor.Id,
            Status = PrescriptionStatus.Issued,
            Notes = "Synthetic canary read fixture",
            CreatedAt = now
        };
        prescription.Items.Add(new PrescriptionItem
        {
            MedicineId = medicine.Id,
            Quantity = 1,
            Dosage = "1 tablet",
            Frequency = "once daily",
            DurationDays = 1,
            Instructions = "Synthetic canary only"
        });
        db.Prescriptions.Add(prescription);
        await db.SaveChangesAsync();
    }

    public async Task<string> BusinessFingerprintAsync()
    {
        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var snapshot = new
        {
            appointments = await db.Appointments.AsNoTracking().OrderBy(x => x.Id).Select(x => new { x.Id, x.Status, x.FacilityId }).ToListAsync(),
            visits = await db.PatientVisits.AsNoTracking().OrderBy(x => x.Id).Select(x => new { x.Id, x.Status, x.FacilityId }).ToListAsync(),
            diagnosticOrders = await db.DiagnosticOrders.AsNoTracking().OrderBy(x => x.Id).Select(x => new { x.Id, x.Status, x.FacilityId }).ToListAsync(),
            diagnosticResults = await db.DiagnosticResults.AsNoTracking().OrderBy(x => x.Id).Select(x => new { x.Id, x.DiagnosticOrderItemId }).ToListAsync(),
            prescriptions = await db.Prescriptions.AsNoTracking().OrderBy(x => x.Id).Select(x => new { x.Id, x.Status, x.PatientId }).ToListAsync(),
            invoices = await db.Invoices.AsNoTracking().OrderBy(x => x.Id).Select(x => new { x.Id, x.Status, x.PatientVisitId }).ToListAsync(),
            payments = await db.Payments.AsNoTracking().OrderBy(x => x.Id).Select(x => new { x.Id, x.InvoiceId }).ToListAsync(),
            medicines = await db.Medicines.AsNoTracking().OrderBy(x => x.Id).Select(x => new { x.Id, x.StockQuantity }).ToListAsync(),
            stockTransactions = await db.MedicineStockTransactions.AsNoTracking().OrderBy(x => x.Id).Select(x => new { x.Id, x.MedicineId, x.QuantityChange }).ToListAsync(),
            pendingActions = await db.AiPendingToolActions.AsNoTracking().OrderBy(x => x.ActionId).Select(x => new { x.ActionId, x.State, x.ResourceType, x.ResourceId }).ToListAsync()
        };
        var canonical = JsonSerializer.Serialize(snapshot);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (!disposing) return;
        try
        {
            SqliteConnection.ClearAllPools();
            foreach (var path in new[] { _dbFilePath, $"{_dbFilePath}-shm", $"{_dbFilePath}-wal" })
                if (File.Exists(path)) File.Delete(path);
        }
        catch
        {
            // Best effort cleanup of synthetic canary state only.
        }
    }
}
