using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClinicManagement.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAiRoleConfirmedActionGateway : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "SourceAiActionId",
                table: "DiagnosticOrders",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ActorRole",
                table: "AiPendingToolActions",
                type: "nvarchar(48)",
                maxLength: 48,
                nullable: false,
                defaultValue: "Patient");

            migrationBuilder.AddColumn<string>(
                name: "ConfirmationTokenHash",
                table: "AiPendingToolActions",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ConversationId",
                table: "AiPendingToolActions",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "FacilityId",
                table: "AiPendingToolActions",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ResourceVersion",
                table: "AiPendingToolActions",
                type: "nvarchar(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SourceAiActionId",
                table: "AiPendingToolActions",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_DiagnosticOrders_SourceAiActionId",
                table: "DiagnosticOrders",
                column: "SourceAiActionId",
                unique: true,
                filter: "[SourceAiActionId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_AiPendingToolActions_SourceAiActionId",
                table: "AiPendingToolActions",
                column: "SourceAiActionId",
                unique: true,
                filter: "[SourceAiActionId] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_DiagnosticOrders_SourceAiActionId",
                table: "DiagnosticOrders");

            migrationBuilder.DropIndex(
                name: "IX_AiPendingToolActions_SourceAiActionId",
                table: "AiPendingToolActions");

            migrationBuilder.DropColumn(
                name: "SourceAiActionId",
                table: "DiagnosticOrders");

            migrationBuilder.DropColumn(
                name: "ActorRole",
                table: "AiPendingToolActions");

            migrationBuilder.DropColumn(
                name: "ConfirmationTokenHash",
                table: "AiPendingToolActions");

            migrationBuilder.DropColumn(
                name: "ConversationId",
                table: "AiPendingToolActions");

            migrationBuilder.DropColumn(
                name: "FacilityId",
                table: "AiPendingToolActions");

            migrationBuilder.DropColumn(
                name: "ResourceVersion",
                table: "AiPendingToolActions");

            migrationBuilder.DropColumn(
                name: "SourceAiActionId",
                table: "AiPendingToolActions");
        }
    }
}
