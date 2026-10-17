using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClinicManagement.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMedicineBatches : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "MedicineBatchId",
                table: "MedicineStockTransactions",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "MedicineBatches",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    MedicineId = table.Column<long>(type: "bigint", nullable: false),
                    BatchNumber = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    ExpiryDate = table.Column<DateOnly>(type: "date", nullable: false),
                    QuantityReceived = table.Column<int>(type: "int", nullable: false),
                    QuantityRemaining = table.Column<int>(type: "int", nullable: false),
                    UnitCost = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    ReceivedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    DisposedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DisposeReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MedicineBatches", x => x.Id);
                    table.CheckConstraint("CK_MedicineBatch_QuantityReceived", "[QuantityReceived] >= 0");
                    table.CheckConstraint("CK_MedicineBatch_QuantityRemaining", "[QuantityRemaining] >= 0");
                    table.ForeignKey(
                        name: "FK_MedicineBatches_Medicines_MedicineId",
                        column: x => x.MedicineId,
                        principalTable: "Medicines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MedicineStockTransactions_MedicineBatchId",
                table: "MedicineStockTransactions",
                column: "MedicineBatchId");

            migrationBuilder.CreateIndex(
                name: "IX_MedicineBatches_MedicineId_BatchNumber_ExpiryDate",
                table: "MedicineBatches",
                columns: new[] { "MedicineId", "BatchNumber", "ExpiryDate" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_MedicineStockTransactions_MedicineBatches_MedicineBatchId",
                table: "MedicineStockTransactions",
                column: "MedicineBatchId",
                principalTable: "MedicineBatches",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_MedicineStockTransactions_MedicineBatches_MedicineBatchId",
                table: "MedicineStockTransactions");

            migrationBuilder.DropTable(
                name: "MedicineBatches");

            migrationBuilder.DropIndex(
                name: "IX_MedicineStockTransactions_MedicineBatchId",
                table: "MedicineStockTransactions");

            migrationBuilder.DropColumn(
                name: "MedicineBatchId",
                table: "MedicineStockTransactions");
        }
    }
}
