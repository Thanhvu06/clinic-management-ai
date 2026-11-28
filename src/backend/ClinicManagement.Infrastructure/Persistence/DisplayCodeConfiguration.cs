using System.Linq.Expressions;
using ClinicManagement.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace ClinicManagement.Infrastructure.Persistence;

internal static class DisplayCodeConfiguration
{
    public static void Configure(ModelBuilder model, bool sqlServer)
    {
        Configure(model, sqlServer, "LH", (Appointment row) => row.ShortCode);
        Configure(model, sqlServer, "BN", (Patient row) => row.ShortCode);
        Configure(model, sqlServer, "LK", (PatientVisit row) => row.ShortCode);
        Configure(model, sqlServer, "HD", (Invoice row) => row.ShortCode);
        Configure(model, sqlServer, "TT", (Payment row) => row.ShortCode);
        Configure(model, sqlServer, "DT", (Prescription row) => row.ShortCode);
        Configure(model, sqlServer, "CL", (DiagnosticOrder row) => row.ShortCode);
        Configure(model, sqlServer, "DK", (HealthPackageRegistration row) => row.ShortCode);
    }

    private static void Configure<TEntity>(ModelBuilder model, bool sqlServer, string prefix,
        Expression<Func<TEntity, string?>> code) where TEntity : class
    {
        var entity = model.Entity<TEntity>();
        var property = entity.Property(code).HasMaxLength(21);
        entity.HasIndex(property.Metadata.Name).IsUnique();
        if (sqlServer)
        {
            var sequence = prefix + "DisplayCodeSequence";
            model.HasSequence<long>(sequence, "dbo");
            property.IsRequired().HasDefaultValueSql($"CONCAT(N'{prefix}', FORMAT(NEXT VALUE FOR [dbo].[{sequence}], '0000', 'en-US'))");
        }
    }
}
