using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Backend.Migrations
{
    /// <inheritdoc />
    public partial class AddUnenrollToEnrollment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsDeleted",
                table: "Enrollments",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "UnenrolledAt",
                table: "Enrollments",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UnenrollReason",
                table: "Enrollments",
                type: "text",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Enrollments_IsDeleted",
                table: "Enrollments",
                column: "IsDeleted");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Enrollments_IsDeleted",
                table: "Enrollments");

            migrationBuilder.DropColumn(
                name: "UnenrollReason",
                table: "Enrollments");

            migrationBuilder.DropColumn(
                name: "UnenrolledAt",
                table: "Enrollments");

            migrationBuilder.DropColumn(
                name: "IsDeleted",
                table: "Enrollments");
        }
    }
}
