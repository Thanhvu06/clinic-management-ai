using Microsoft.EntityFrameworkCore.Migrations;

namespace ClinicManagement.Infrastructure.Persistence.Migrations;

public partial class AddShortDisplayCodes : Migration
{
    private static readonly (string Table, string Prefix, string CreationOrder)[] Codes =
    [
        ("Appointments", "LH", """
            COALESCE(
                (SELECT MIN(h.[CreatedAt]) FROM [dbo].[AppointmentHistory] h
                    WHERE h.[AppointmentId] = r.[Id] AND h.[Action] = ''Created''),
                CAST(r.[AppointmentDate] AS datetime2)), r.[Id]
            """),
        ("Patients", "BN", "r.[Id]"),
        ("PatientVisits", "LK", "r.[CreatedAtUtc], r.[Id]"),
        ("Invoices", "HD", "r.[CreatedAtUtc], r.[Id]"),
        ("Payments", "TT", "r.[ReceivedAtUtc], r.[Id]"),
        ("Prescriptions", "DT", "r.[CreatedAt], r.[Id]"),
        ("DiagnosticOrders", "CL", "r.[OrderedAtUtc], r.[Id]"),
        ("HealthPackageRegistrations", "DK", "r.[CreatedAt], r.[Id]")
    ];

    protected override void Up(MigrationBuilder migrationBuilder)
    {
        foreach (var (table, prefix, order) in Codes)
        {
            var sequence = prefix + "DisplayCodeSequence";
            migrationBuilder.CreateSequence<long>(sequence, "dbo");
            migrationBuilder.AddColumn<string>("ShortCode", table, type: "nvarchar(21)", maxLength: 21, nullable: true);

            migrationBuilder.Sql($"""
                EXEC(N'
                    WITH Numbered AS (
                        SELECT r.[Id], ROW_NUMBER() OVER (ORDER BY {order}) AS [Number]
                        FROM [dbo].[{table}] r WITH (TABLOCKX)
                    )
                    UPDATE r SET [ShortCode] = CONCAT(N''{prefix}'', FORMAT(n.[Number], ''0000'', ''en-US''))
                    FROM [dbo].[{table}] r INNER JOIN Numbered n ON n.[Id] = r.[Id]
                    WHERE r.[ShortCode] IS NULL;

                    DECLARE @next bigint = (SELECT COUNT_BIG(*) + 1 FROM [dbo].[{table}]);
                    DECLARE @restart nvarchar(200) = N''ALTER SEQUENCE [dbo].[{sequence}] RESTART WITH ''
                        + CONVERT(nvarchar(20), @next);
                    EXEC(@restart);
                ');
                """);

            migrationBuilder.AlterColumn<string>("ShortCode", table,
                type: "nvarchar(21)", maxLength: 21, nullable: false,
                defaultValueSql: $"CONCAT(N'{prefix}', FORMAT(NEXT VALUE FOR [dbo].[{sequence}], '0000', 'en-US'))",
                oldClrType: typeof(string), oldType: "nvarchar(21)", oldMaxLength: 21, oldNullable: true);
            migrationBuilder.CreateIndex($"IX_{table}_ShortCode", table, "ShortCode", unique: true);
        }
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        foreach (var (table, prefix, _) in Codes)
        {
            migrationBuilder.DropIndex($"IX_{table}_ShortCode", table);
            migrationBuilder.DropColumn("ShortCode", table);
            migrationBuilder.DropSequence(prefix + "DisplayCodeSequence", "dbo");
        }
    }
}
