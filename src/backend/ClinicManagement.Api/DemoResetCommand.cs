using ClinicManagement.Infrastructure.Authentication;
using ClinicManagement.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace ClinicManagement.Api;

public static class DemoResetCommand
{
    public static SqlConnectionStringBuilder Validate(string environment, string connectionString)
    {
        var target = new SqlConnectionStringBuilder(connectionString);
        if (!string.Equals(environment, "Development", StringComparison.Ordinal)
            || !target.InitialCatalog.StartsWith("ClinicCareDemo", StringComparison.Ordinal))
            throw new InvalidOperationException("Từ chối reset: chỉ cho phép Development và tên DB bắt đầu bằng ClinicCareDemo.");
        return target;
    }

    public static async Task RunAsync(WebApplication app, string connectionString)
    {
        var target = Validate(app.Environment.EnvironmentName, connectionString);
        Console.WriteLine($"Reset demo: server={target.DataSource}; database={target.InitialCatalog}");
        using var scope = app.Services.CreateScope();
        var services = scope.ServiceProvider;
        var db = services.GetRequiredService<AppDbContext>();
        await db.Database.EnsureDeletedAsync();
        await db.Database.MigrateAsync();
        await RoleSeeder.SeedRolesAsync(services);
        await DevelopmentDataSeeder.SeedDoctorSchedulesAndSlotsAsync(services, 30, true);
        await MedicineImageSeeder.SeedAsync(db, services.GetRequiredService<MedicineImageStorage>(),
            services.GetRequiredService<IWebHostEnvironment>(), services.GetRequiredService<ILoggerFactory>().CreateLogger(nameof(MedicineImageSeeder)),
            app.Lifetime.ApplicationStopping);
        Console.WriteLine($"Seed complete: patients={await db.Patients.CountAsync()}, doctors={await db.Doctors.CountAsync()}, appointments={await db.Appointments.CountAsync()}, visits={await db.PatientVisits.CountAsync()}, diagnostics={await db.DiagnosticOrders.CountAsync()}, prescriptions={await db.Prescriptions.CountAsync()}, invoices={await db.Invoices.CountAsync()}, payments={await db.Payments.CountAsync()}, registrations={await db.HealthPackageRegistrations.CountAsync()}");
    }
}
