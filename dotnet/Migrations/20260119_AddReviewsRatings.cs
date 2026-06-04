using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace backend.Migrations;

/// <inheritdoc />
public partial class AddReviewsRatings : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "Reviews",
            columns: table => new
            {
                Id = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                UserId = table.Column<int>(type: "integer", nullable: false),
                SubjectId = table.Column<int>(type: "integer", nullable: false),
                Rating = table.Column<int>(type: "integer", nullable: false),
                Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                Comment = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                IsVerifiedPurchase = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                HelpfulCount = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                IsDeleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Reviews", x => x.Id);
                table.ForeignKey(
                    name: "FK_Reviews_Users_UserId",
                    column: x => x.UserId,
                    principalTable: "Users",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_Reviews_Subjects_SubjectId",
                    column: x => x.SubjectId,
                    principalTable: "Subjects",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        // Indexes
        migrationBuilder.CreateIndex(
            name: "IX_Reviews_UserId",
            table: "Reviews",
            column: "UserId");

        migrationBuilder.CreateIndex(
            name: "IX_Reviews_SubjectId",
            table: "Reviews",
            column: "SubjectId");

        migrationBuilder.CreateIndex(
            name: "IX_Reviews_UserId_SubjectId",
            table: "Reviews",
            columns: new[] { "UserId", "SubjectId" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_Reviews_Rating",
            table: "Reviews",
            column: "Rating");

        migrationBuilder.CreateIndex(
            name: "IX_Reviews_CreatedAt",
            table: "Reviews",
            column: "CreatedAt");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "Reviews");
    }
}
