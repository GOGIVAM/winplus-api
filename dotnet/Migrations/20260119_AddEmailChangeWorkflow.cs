using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace backend.Migrations;

/// <inheritdoc />
public partial class AddEmailChangeWorkflow : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "PendingEmail",
            table: "Users",
            type: "character varying(256)",
            maxLength: 256,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "EmailChangeToken",
            table: "Users",
            type: "text",
            nullable: true);

        migrationBuilder.AddColumn<DateTime>(
            name: "EmailChangeTokenExpiry",
            table: "Users",
            type: "timestamp with time zone",
            nullable: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "PendingEmail",
            table: "Users");

        migrationBuilder.DropColumn(
            name: "EmailChangeToken",
            table: "Users");

        migrationBuilder.DropColumn(
            name: "EmailChangeTokenExpiry",
            table: "Users");
    }
}
