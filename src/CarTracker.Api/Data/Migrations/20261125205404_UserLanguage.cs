using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CarTracker.Api.Data.Migrations;

/// <inheritdoc />
public partial class _20261125205404_UserLanguage : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "Language",
            table: "AspNetUsers",
            type: "TEXT",
            nullable: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "Language",
            table: "AspNetUsers");
    }
}
