using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Backend.Migrations
{
    /// <inheritdoc />
    public partial class AddFavoriteCollections : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Create FavoriteCollections table
            migrationBuilder.CreateTable(
                name: "FavoriteCollections",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Color = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    Icon = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    Order = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FavoriteCollections", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FavoriteCollections_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            // Add CollectionId to Favorites
            migrationBuilder.AddColumn<int>(
                name: "CollectionId",
                table: "Favorites",
                type: "integer",
                nullable: true);

            // Create indexes
            migrationBuilder.CreateIndex(
                name: "IX_FavoriteCollections_UserId",
                table: "FavoriteCollections",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_FavoriteCollections_Name_UserId",
                table: "FavoriteCollections",
                columns: new[] { "Name", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Favorites_CollectionId",
                table: "Favorites",
                column: "CollectionId");

            // Add foreign key constraint
            migrationBuilder.AddForeignKey(
                name: "FK_Favorites_FavoriteCollections_CollectionId",
                table: "Favorites",
                column: "CollectionId",
                principalTable: "FavoriteCollections",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Favorites_FavoriteCollections_CollectionId",
                table: "Favorites");

            migrationBuilder.DropIndex(
                name: "IX_Favorites_CollectionId",
                table: "Favorites");

            migrationBuilder.DropIndex(
                name: "IX_FavoriteCollections_Name_UserId",
                table: "FavoriteCollections");

            migrationBuilder.DropIndex(
                name: "IX_FavoriteCollections_UserId",
                table: "FavoriteCollections");

            migrationBuilder.DropForeignKey(
                name: "FK_FavoriteCollections_Users_UserId",
                table: "FavoriteCollections");

            migrationBuilder.DropTable(
                name: "FavoriteCollections");

            migrationBuilder.DropColumn(
                name: "CollectionId",
                table: "Favorites");
        }
    }
}
