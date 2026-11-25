using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CarTracker.Api.Data.Migrations;

/// <inheritdoc />
public partial class _20261123213245_Initial : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "AspNetRoles",
            columns: table => new
            {
                Id = table.Column<string>(type: "TEXT", nullable: false),
                Name = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                NormalizedName = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                ConcurrencyStamp = table.Column<string>(type: "TEXT", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_AspNetRoles", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "AspNetUsers",
            columns: table => new
            {
                Id = table.Column<string>(type: "TEXT", nullable: false),
                DisplayName = table.Column<string>(type: "TEXT", nullable: false),
                IsAdmin = table.Column<bool>(type: "INTEGER", nullable: false),
                CreatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                UserName = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                NormalizedUserName = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                Email = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                NormalizedEmail = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                EmailConfirmed = table.Column<bool>(type: "INTEGER", nullable: false),
                PasswordHash = table.Column<string>(type: "TEXT", nullable: true),
                SecurityStamp = table.Column<string>(type: "TEXT", nullable: true),
                ConcurrencyStamp = table.Column<string>(type: "TEXT", nullable: true),
                PhoneNumber = table.Column<string>(type: "TEXT", nullable: true),
                PhoneNumberConfirmed = table.Column<bool>(type: "INTEGER", nullable: false),
                TwoFactorEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                LockoutEnd = table.Column<long>(type: "INTEGER", nullable: true),
                LockoutEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                AccessFailedCount = table.Column<int>(type: "INTEGER", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_AspNetUsers", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "Invites",
            columns: table => new
            {
                Id = table.Column<int>(type: "INTEGER", nullable: false)
                    .Annotation("Sqlite:Autoincrement", true),
                CodeHash = table.Column<string>(type: "TEXT", nullable: false),
                Note = table.Column<string>(type: "TEXT", nullable: true),
                GrantsAdmin = table.Column<bool>(type: "INTEGER", nullable: false),
                CreatedById = table.Column<string>(type: "TEXT", nullable: true),
                CreatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                ExpiresAt = table.Column<long>(type: "INTEGER", nullable: false),
                UsedAt = table.Column<long>(type: "INTEGER", nullable: true),
                UsedById = table.Column<string>(type: "TEXT", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Invites", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "Tags",
            columns: table => new
            {
                Id = table.Column<int>(type: "INTEGER", nullable: false)
                    .Annotation("Sqlite:Autoincrement", true),
                OwnerId = table.Column<string>(type: "TEXT", nullable: false),
                Name = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Tags", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "AspNetRoleClaims",
            columns: table => new
            {
                Id = table.Column<int>(type: "INTEGER", nullable: false)
                    .Annotation("Sqlite:Autoincrement", true),
                RoleId = table.Column<string>(type: "TEXT", nullable: false),
                ClaimType = table.Column<string>(type: "TEXT", nullable: true),
                ClaimValue = table.Column<string>(type: "TEXT", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_AspNetRoleClaims", x => x.Id);
                table.ForeignKey(
                    name: "FK_AspNetRoleClaims_AspNetRoles_RoleId",
                    column: x => x.RoleId,
                    principalTable: "AspNetRoles",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "AspNetUserClaims",
            columns: table => new
            {
                Id = table.Column<int>(type: "INTEGER", nullable: false)
                    .Annotation("Sqlite:Autoincrement", true),
                UserId = table.Column<string>(type: "TEXT", nullable: false),
                ClaimType = table.Column<string>(type: "TEXT", nullable: true),
                ClaimValue = table.Column<string>(type: "TEXT", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_AspNetUserClaims", x => x.Id);
                table.ForeignKey(
                    name: "FK_AspNetUserClaims_AspNetUsers_UserId",
                    column: x => x.UserId,
                    principalTable: "AspNetUsers",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "AspNetUserLogins",
            columns: table => new
            {
                LoginProvider = table.Column<string>(type: "TEXT", nullable: false),
                ProviderKey = table.Column<string>(type: "TEXT", nullable: false),
                ProviderDisplayName = table.Column<string>(type: "TEXT", nullable: true),
                UserId = table.Column<string>(type: "TEXT", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_AspNetUserLogins", x => new { x.LoginProvider, x.ProviderKey });
                table.ForeignKey(
                    name: "FK_AspNetUserLogins_AspNetUsers_UserId",
                    column: x => x.UserId,
                    principalTable: "AspNetUsers",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "AspNetUserRoles",
            columns: table => new
            {
                UserId = table.Column<string>(type: "TEXT", nullable: false),
                RoleId = table.Column<string>(type: "TEXT", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_AspNetUserRoles", x => new { x.UserId, x.RoleId });
                table.ForeignKey(
                    name: "FK_AspNetUserRoles_AspNetRoles_RoleId",
                    column: x => x.RoleId,
                    principalTable: "AspNetRoles",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_AspNetUserRoles_AspNetUsers_UserId",
                    column: x => x.UserId,
                    principalTable: "AspNetUsers",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "AspNetUserTokens",
            columns: table => new
            {
                UserId = table.Column<string>(type: "TEXT", nullable: false),
                LoginProvider = table.Column<string>(type: "TEXT", nullable: false),
                Name = table.Column<string>(type: "TEXT", nullable: false),
                Value = table.Column<string>(type: "TEXT", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_AspNetUserTokens", x => new { x.UserId, x.LoginProvider, x.Name });
                table.ForeignKey(
                    name: "FK_AspNetUserTokens_AspNetUsers_UserId",
                    column: x => x.UserId,
                    principalTable: "AspNetUsers",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "Vehicles",
            columns: table => new
            {
                Id = table.Column<int>(type: "INTEGER", nullable: false)
                    .Annotation("Sqlite:Autoincrement", true),
                OwnerId = table.Column<string>(type: "TEXT", nullable: false),
                Name = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                Make = table.Column<string>(type: "TEXT", nullable: true),
                Model = table.Column<string>(type: "TEXT", nullable: true),
                Year = table.Column<int>(type: "INTEGER", nullable: true),
                Badge = table.Column<string>(type: "TEXT", nullable: true),
                ColorExterior = table.Column<string>(type: "TEXT", nullable: true),
                ColorInterior = table.Column<string>(type: "TEXT", nullable: true),
                Plate = table.Column<string>(type: "TEXT", nullable: true),
                Vin = table.Column<string>(type: "TEXT", nullable: true),
                PurchasePrice = table.Column<decimal>(type: "TEXT", nullable: true),
                PurchaseDate = table.Column<DateOnly>(type: "TEXT", nullable: true),
                PurchaseOdometer = table.Column<int>(type: "INTEGER", nullable: true),
                CurrentOdometer = table.Column<int>(type: "INTEGER", nullable: true),
                IncludePurchaseInTotals = table.Column<bool>(type: "INTEGER", nullable: false),
                Notes = table.Column<string>(type: "TEXT", nullable: true),
                CoverPhotoId = table.Column<int>(type: "INTEGER", nullable: true),
                CreatedAt = table.Column<long>(type: "INTEGER", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Vehicles", x => x.Id);
                table.ForeignKey(
                    name: "FK_Vehicles_AspNetUsers_OwnerId",
                    column: x => x.OwnerId,
                    principalTable: "AspNetUsers",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "Entries",
            columns: table => new
            {
                Id = table.Column<int>(type: "INTEGER", nullable: false)
                    .Annotation("Sqlite:Autoincrement", true),
                VehicleId = table.Column<int>(type: "INTEGER", nullable: false),
                Kind = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                Status = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                Title = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                Date = table.Column<DateOnly>(type: "TEXT", nullable: true),
                EndDate = table.Column<DateOnly>(type: "TEXT", nullable: true),
                Odometer = table.Column<int>(type: "INTEGER", nullable: true),
                Vendor = table.Column<string>(type: "TEXT", nullable: true),
                Notes = table.Column<string>(type: "TEXT", nullable: true),
                Currency = table.Column<string>(type: "TEXT", nullable: false),
                ManualTotal = table.Column<decimal>(type: "TEXT", nullable: true),
                RelatedEntryId = table.Column<int>(type: "INTEGER", nullable: true),
                CreatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                UpdatedAt = table.Column<long>(type: "INTEGER", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Entries", x => x.Id);
                table.ForeignKey(
                    name: "FK_Entries_Entries_RelatedEntryId",
                    column: x => x.RelatedEntryId,
                    principalTable: "Entries",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.SetNull);
                table.ForeignKey(
                    name: "FK_Entries_Vehicles_VehicleId",
                    column: x => x.VehicleId,
                    principalTable: "Vehicles",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "EntryTags",
            columns: table => new
            {
                EntriesId = table.Column<int>(type: "INTEGER", nullable: false),
                TagsId = table.Column<int>(type: "INTEGER", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_EntryTags", x => new { x.EntriesId, x.TagsId });
                table.ForeignKey(
                    name: "FK_EntryTags_Entries_EntriesId",
                    column: x => x.EntriesId,
                    principalTable: "Entries",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_EntryTags_Tags_TagsId",
                    column: x => x.TagsId,
                    principalTable: "Tags",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "LineItems",
            columns: table => new
            {
                Id = table.Column<int>(type: "INTEGER", nullable: false)
                    .Annotation("Sqlite:Autoincrement", true),
                EntryId = table.Column<int>(type: "INTEGER", nullable: false),
                SortOrder = table.Column<int>(type: "INTEGER", nullable: false),
                Name = table.Column<string>(type: "TEXT", nullable: false),
                Brand = table.Column<string>(type: "TEXT", nullable: true),
                PartNumber = table.Column<string>(type: "TEXT", nullable: true),
                Quantity = table.Column<decimal>(type: "TEXT", nullable: false),
                UnitPrice = table.Column<decimal>(type: "TEXT", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_LineItems", x => x.Id);
                table.ForeignKey(
                    name: "FK_LineItems_Entries_EntryId",
                    column: x => x.EntryId,
                    principalTable: "Entries",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "Photos",
            columns: table => new
            {
                Id = table.Column<int>(type: "INTEGER", nullable: false)
                    .Annotation("Sqlite:Autoincrement", true),
                OwnerId = table.Column<string>(type: "TEXT", nullable: false),
                VehicleId = table.Column<int>(type: "INTEGER", nullable: false),
                EntryId = table.Column<int>(type: "INTEGER", nullable: true),
                StorageKey = table.Column<string>(type: "TEXT", nullable: false),
                OriginalFileName = table.Column<string>(type: "TEXT", nullable: true),
                Width = table.Column<int>(type: "INTEGER", nullable: false),
                Height = table.Column<int>(type: "INTEGER", nullable: false),
                SizeBytes = table.Column<long>(type: "INTEGER", nullable: false),
                TakenAt = table.Column<long>(type: "INTEGER", nullable: true),
                Caption = table.Column<string>(type: "TEXT", nullable: true),
                UploadedAt = table.Column<long>(type: "INTEGER", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Photos", x => x.Id);
                table.ForeignKey(
                    name: "FK_Photos_Entries_EntryId",
                    column: x => x.EntryId,
                    principalTable: "Entries",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.SetNull);
                table.ForeignKey(
                    name: "FK_Photos_Vehicles_VehicleId",
                    column: x => x.VehicleId,
                    principalTable: "Vehicles",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_AspNetRoleClaims_RoleId",
            table: "AspNetRoleClaims",
            column: "RoleId");

        migrationBuilder.CreateIndex(
            name: "RoleNameIndex",
            table: "AspNetRoles",
            column: "NormalizedName",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_AspNetUserClaims_UserId",
            table: "AspNetUserClaims",
            column: "UserId");

        migrationBuilder.CreateIndex(
            name: "IX_AspNetUserLogins_UserId",
            table: "AspNetUserLogins",
            column: "UserId");

        migrationBuilder.CreateIndex(
            name: "IX_AspNetUserRoles_RoleId",
            table: "AspNetUserRoles",
            column: "RoleId");

        migrationBuilder.CreateIndex(
            name: "EmailIndex",
            table: "AspNetUsers",
            column: "NormalizedEmail");

        migrationBuilder.CreateIndex(
            name: "UserNameIndex",
            table: "AspNetUsers",
            column: "NormalizedUserName",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_Entries_RelatedEntryId",
            table: "Entries",
            column: "RelatedEntryId");

        migrationBuilder.CreateIndex(
            name: "IX_Entries_VehicleId_Date",
            table: "Entries",
            columns: new[] { "VehicleId", "Date" });

        migrationBuilder.CreateIndex(
            name: "IX_EntryTags_TagsId",
            table: "EntryTags",
            column: "TagsId");

        migrationBuilder.CreateIndex(
            name: "IX_Invites_CodeHash",
            table: "Invites",
            column: "CodeHash",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_LineItems_EntryId",
            table: "LineItems",
            column: "EntryId");

        migrationBuilder.CreateIndex(
            name: "IX_Photos_EntryId",
            table: "Photos",
            column: "EntryId");

        migrationBuilder.CreateIndex(
            name: "IX_Photos_VehicleId",
            table: "Photos",
            column: "VehicleId");

        migrationBuilder.CreateIndex(
            name: "IX_Tags_OwnerId_Name",
            table: "Tags",
            columns: new[] { "OwnerId", "Name" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_Vehicles_OwnerId",
            table: "Vehicles",
            column: "OwnerId");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "AspNetRoleClaims");

        migrationBuilder.DropTable(
            name: "AspNetUserClaims");

        migrationBuilder.DropTable(
            name: "AspNetUserLogins");

        migrationBuilder.DropTable(
            name: "AspNetUserRoles");

        migrationBuilder.DropTable(
            name: "AspNetUserTokens");

        migrationBuilder.DropTable(
            name: "EntryTags");

        migrationBuilder.DropTable(
            name: "Invites");

        migrationBuilder.DropTable(
            name: "LineItems");

        migrationBuilder.DropTable(
            name: "Photos");

        migrationBuilder.DropTable(
            name: "AspNetRoles");

        migrationBuilder.DropTable(
            name: "Tags");

        migrationBuilder.DropTable(
            name: "Entries");

        migrationBuilder.DropTable(
            name: "Vehicles");

        migrationBuilder.DropTable(
            name: "AspNetUsers");
    }
}
