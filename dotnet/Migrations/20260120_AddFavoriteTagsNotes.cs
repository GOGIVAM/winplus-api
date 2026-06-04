using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Backend.Migrations
{
    /// <inheritdoc />
    public partial class AddFavoriteTagsNotes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Tags",
                table: "Favorites",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Notes",
                table: "Favorites",
                type: "text",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Favorites_Tags",
                table: "Favorites",
                column: "Tags");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Favorites_Tags",
                table: "Favorites");

            migrationBuilder.DropColumn(
                name: "Notes",
                table: "Favorites");

            migrationBuilder.DropColumn(
                name: "Tags",
                table: "Favorites");
        }
    }
}
