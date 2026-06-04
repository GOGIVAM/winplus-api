using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace backend.Migrations;

/// <inheritdoc />
public partial class FixUserDeletedAtField : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(@"
            ALTER TABLE ""Users"" DROP COLUMN IF EXISTS ""DeletedAt"";
            ALTER TABLE ""Users"" ADD COLUMN IF NOT EXISTS ""DeletedBy"" text NULL;
            ALTER TABLE ""Users"" ADD COLUMN IF NOT EXISTS ""DeletedByUserId"" integer NULL;
        ");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "DeletedBy",
            table: "Users");

        migrationBuilder.DropColumn(
            name: "DeletedByUserId",
            table: "Users");

        migrationBuilder.AddColumn<DateTime>(
            name: "DeletedAt",
            table: "Users",
            type: "timestamp with time zone",
            nullable: true);
    }
}