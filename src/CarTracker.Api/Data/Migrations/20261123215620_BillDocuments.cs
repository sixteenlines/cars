using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CarTracker.Api.Data.Migrations;

/// <inheritdoc />
public partial class _20261123215620_BillDocuments : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "Documents",
            columns: table => new
            {
                Id = table.Column<int>(type: "INTEGER", nullable: false)
                    .Annotation("Sqlite:Autoincrement", true),
                OwnerId = table.Column<string>(type: "TEXT", nullable: false),
                VehicleId = table.Column<int>(type: "INTEGER", nullable: true),
                StorageKey = table.Column<string>(type: "TEXT", nullable: false),
                FileName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                ContentType = table.Column<string>(type: "TEXT", nullable: false),
                SizeBytes = table.Column<long>(type: "INTEGER", nullable: false),
                Sha256 = table.Column<string>(type: "TEXT", nullable: false),
                Status = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                Error = table.Column<string>(type: "TEXT", nullable: true),
                ExtractionJson = table.Column<string>(type: "TEXT", nullable: true),
                Vendor = table.Column<string>(type: "TEXT", nullable: true),
                InvoiceNumber = table.Column<string>(type: "TEXT", nullable: true),
                InvoiceDate = table.Column<DateOnly>(type: "TEXT", nullable: true),
                Total = table.Column<decimal>(type: "TEXT", nullable: true),
                AiCalled = table.Column<bool>(type: "INTEGER", nullable: false),
                AiModel = table.Column<string>(type: "TEXT", nullable: true),
                InputTokens = table.Column<int>(type: "INTEGER", nullable: false),
                OutputTokens = table.Column<int>(type: "INTEGER", nullable: false),
                EstimatedCostUsd = table.Column<decimal>(type: "TEXT", nullable: false),
                CreatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                ImportedAt = table.Column<long>(type: "INTEGER", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Documents", x => x.Id);
                table.ForeignKey(
                    name: "FK_Documents_AspNetUsers_OwnerId",
                    column: x => x.OwnerId,
                    principalTable: "AspNetUsers",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_Documents_Vehicles_VehicleId",
                    column: x => x.VehicleId,
                    principalTable: "Vehicles",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.SetNull);
            });

        migrationBuilder.CreateTable(
            name: "EntryDocuments",
            columns: table => new
            {
                DocumentsId = table.Column<int>(type: "INTEGER", nullable: false),
                EntriesId = table.Column<int>(type: "INTEGER", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_EntryDocuments", x => new { x.DocumentsId, x.EntriesId });
                table.ForeignKey(
                    name: "FK_EntryDocuments_Documents_DocumentsId",
                    column: x => x.DocumentsId,
                    principalTable: "Documents",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_EntryDocuments_Entries_EntriesId",
                    column: x => x.EntriesId,
                    principalTable: "Entries",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_Documents_OwnerId_Sha256",
            table: "Documents",
            columns: new[] { "OwnerId", "Sha256" });

        migrationBuilder.CreateIndex(
            name: "IX_Documents_OwnerId_Status",
            table: "Documents",
            columns: new[] { "OwnerId", "Status" });

        migrationBuilder.CreateIndex(
            name: "IX_Documents_VehicleId",
            table: "Documents",
            column: "VehicleId");

        migrationBuilder.CreateIndex(
            name: "IX_EntryDocuments_EntriesId",
            table: "EntryDocuments",
            column: "EntriesId");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "EntryDocuments");

        migrationBuilder.DropTable(
            name: "Documents");
    }
}
