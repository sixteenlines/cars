using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CarTracker.Api.Data.Migrations;

/// <inheritdoc />
public partial class _20261125184418_Fuel : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "AiUsages",
            columns: table => new
            {
                Id = table.Column<int>(type: "INTEGER", nullable: false)
                    .Annotation("Sqlite:Autoincrement", true),
                OwnerId = table.Column<string>(type: "TEXT", nullable: false),
                Purpose = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                Model = table.Column<string>(type: "TEXT", nullable: true),
                InputTokens = table.Column<int>(type: "INTEGER", nullable: false),
                OutputTokens = table.Column<int>(type: "INTEGER", nullable: false),
                EstimatedCostUsd = table.Column<decimal>(type: "TEXT", nullable: false),
                CreatedAt = table.Column<long>(type: "INTEGER", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_AiUsages", x => x.Id);
                table.ForeignKey(
                    name: "FK_AiUsages_AspNetUsers_OwnerId",
                    column: x => x.OwnerId,
                    principalTable: "AspNetUsers",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "FuelUps",
            columns: table => new
            {
                Id = table.Column<int>(type: "INTEGER", nullable: false)
                    .Annotation("Sqlite:Autoincrement", true),
                VehicleId = table.Column<int>(type: "INTEGER", nullable: false),
                Date = table.Column<DateOnly>(type: "TEXT", nullable: false),
                Odometer = table.Column<int>(type: "INTEGER", nullable: true),
                Grade = table.Column<string>(type: "TEXT", maxLength: 10, nullable: true),
                Liters = table.Column<decimal>(type: "TEXT", nullable: true),
                PricePerLiter = table.Column<decimal>(type: "TEXT", nullable: true),
                Total = table.Column<decimal>(type: "TEXT", nullable: false),
                FullTank = table.Column<bool>(type: "INTEGER", nullable: false),
                Station = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                Notes = table.Column<string>(type: "TEXT", nullable: true),
                CreatedAt = table.Column<long>(type: "INTEGER", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_FuelUps", x => x.Id);
                table.ForeignKey(
                    name: "FK_FuelUps_Vehicles_VehicleId",
                    column: x => x.VehicleId,
                    principalTable: "Vehicles",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_AiUsages_OwnerId_Purpose_CreatedAt",
            table: "AiUsages",
            columns: new[] { "OwnerId", "Purpose", "CreatedAt" });

        migrationBuilder.CreateIndex(
            name: "IX_FuelUps_VehicleId_Date",
            table: "FuelUps",
            columns: new[] { "VehicleId", "Date" });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "AiUsages");

        migrationBuilder.DropTable(
            name: "FuelUps");
    }
}
