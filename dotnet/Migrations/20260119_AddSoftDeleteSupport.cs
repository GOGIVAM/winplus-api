using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace backend.Migrations;

/// <inheritdoc />
public partial class AddSoftDeleteSupport : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<bool>(
            name: "IsDeleted",
            table: "Users",
            type: "boolean",
            nullable: false,
            defaultValue: false);

        migrationBuilder.AddColumn<DateTime>(
            name: "DeletedAt",
            table: "Users",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.AddColumn<bool>(
            name: "IsDeleted",
            table: "Orders",
            type: "boolean",
            nullable: false,
            defaultValue: false);

        migrationBuilder.AddColumn<DateTime>(
            name: "DeletedAt",
            table: "Orders",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.AddColumn<bool>(
            name: "IsDeleted",
            table: "Subjects",
            type: "boolean",
            nullable: false,
            defaultValue: false);

        migrationBuilder.AddColumn<DateTime>(
            name: "DeletedAt",
            table: "Subjects",
            type: "timestamp with time zone",
            nullable: true);

        // Create indexes for soft delete queries
        migrationBuilder.CreateIndex(
            name: "IX_Users_IsDeleted",
            table: "Users",
            column: "IsDeleted");

        migrationBuilder.CreateIndex(
            name: "IX_Orders_IsDeleted",
            table: "Orders",
            column: "IsDeleted");

        migrationBuilder.CreateIndex(
            name: "IX_Subjects_IsDeleted",
            table: "Subjects",
            column: "IsDeleted");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_Users_IsDeleted",
            table: "Users");

        migrationBuilder.DropIndex(
            name: "IX_Orders_IsDeleted",
            table: "Orders");

        migrationBuilder.DropIndex(
            name: "IX_Subjects_IsDeleted",
            table: "Subjects");

        migrationBuilder.DropColumn(
            name: "IsDeleted",
            table: "Users");

        migrationBuilder.DropColumn(
            name: "DeletedAt",
            table: "Users");

        migrationBuilder.DropColumn(
            name: "IsDeleted",
            table: "Orders");

        migrationBuilder.DropColumn(
            name: "DeletedAt",
            table: "Orders");

        migrationBuilder.DropColumn(
            name: "IsDeleted",
            table: "Subjects");

        migrationBuilder.DropColumn(
            name: "DeletedAt",
            table: "Subjects");
    }
}
