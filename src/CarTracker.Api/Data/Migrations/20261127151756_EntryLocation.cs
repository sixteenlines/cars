using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CarTracker.Api.Data.Migrations;

/// <inheritdoc />
public partial class _20261127151756_EntryLocation : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "Location",
            table: "Entries",
            type: "TEXT",
            nullable: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "Location",
            table: "Entries");
    }
}
