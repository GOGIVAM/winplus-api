using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace backend.Migrations
{
    /// <inheritdoc />
    public partial class AddPerformanceIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Indexes pour LearningHistories (filtrage et tri par date)
            migrationBuilder.CreateIndex(
                name: "IX_LearningHistories_UserId_ActivityAt",
                table: "LearningHistories",
                columns: new[] { "UserId", "ActivityAt" });

            // Indexes pour Orders (filtrage et tri par date)
            migrationBuilder.CreateIndex(
                name: "IX_Orders_UserId_OrderDate",
                table: "Orders",
                columns: new[] { "UserId", "OrderDate" });

            // Indexes pour AnalyticsEvents (time-series queries)
            migrationBuilder.CreateIndex(
                name: "IX_AnalyticsEvents_UserId_CreatedAt",
                table: "AnalyticsEvents",
                columns: new[] { "UserId", "CreatedAt" });

            // Indexes pour Subjects (filtrage + tri)
            migrationBuilder.CreateIndex(
                name: "IX_Subjects_Category_Price",
                table: "Subjects",
                columns: new[] { "Category", "Price" });

            // Index supplémentaire pour Subjects par IsPublished (pour les queries publishées)
            migrationBuilder.CreateIndex(
                name: "IX_Subjects_IsPublished",
                table: "Subjects",
                column: "IsPublished");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_LearningHistories_UserId_ActivityAt",
                table: "LearningHistories");

            migrationBuilder.DropIndex(
                name: "IX_Orders_UserId_OrderDate",
                table: "Orders");

            migrationBuilder.DropIndex(
                name: "IX_AnalyticsEvents_UserId_CreatedAt",
                table: "AnalyticsEvents");

            migrationBuilder.DropIndex(
                name: "IX_Subjects_Category_Price",
                table: "Subjects");

            migrationBuilder.DropIndex(
                name: "IX_Subjects_IsPublished",
                table: "Subjects");
        }
    }
}
