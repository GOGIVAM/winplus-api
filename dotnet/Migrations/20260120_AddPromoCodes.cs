using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace backend.Migrations;

/// <inheritdoc />
public partial class AddPromoCodes : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // PromoCode table
        migrationBuilder.CreateTable(
            name: "PromoCodes",
            columns: table => new
            {
                Id = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                Code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                DiscountType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                DiscountValue = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                MinimumPurchase = table.Column<decimal>(type: "numeric(18,2)", nullable: true),
                MaximumDiscount = table.Column<decimal>(type: "numeric(18,2)", nullable: true),
                UsageLimit = table.Column<int>(type: "integer", nullable: true),
                UsageCount = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                PerUserLimit = table.Column<int>(type: "integer", nullable: true, defaultValue: 1),
                ValidFrom = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                ValidUntil = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                IsActive = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                ApplicableSubjectIds = table.Column<string>(type: "text", nullable: true),
                CreatedBy = table.Column<int>(type: "integer", nullable: false),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_PromoCodes", x => x.Id);
                table.ForeignKey(
                    name: "FK_PromoCodes_Users_CreatedBy",
                    column: x => x.CreatedBy,
                    principalTable: "Users",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        // PromoCodeUsage table
        migrationBuilder.CreateTable(
            name: "PromoCodeUsages",
            columns: table => new
            {
                Id = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                PromoCodeId = table.Column<int>(type: "integer", nullable: false),
                UserId = table.Column<int>(type: "integer", nullable: false),
                OrderId = table.Column<int>(type: "integer", nullable: false),
                DiscountAmount = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                UsedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_PromoCodeUsages", x => x.Id);
                table.ForeignKey(
                    name: "FK_PromoCodeUsages_PromoCodes_PromoCodeId",
                    column: x => x.PromoCodeId,
                    principalTable: "PromoCodes",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_PromoCodeUsages_Users_UserId",
                    column: x => x.UserId,
                    principalTable: "Users",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_PromoCodeUsages_Orders_OrderId",
                    column: x => x.OrderId,
                    principalTable: "Orders",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        // Indexes
        migrationBuilder.CreateIndex(
            name: "IX_PromoCodes_Code",
            table: "PromoCodes",
            column: "Code",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_PromoCodes_IsActive",
            table: "PromoCodes",
            column: "IsActive");

        migrationBuilder.CreateIndex(
            name: "IX_PromoCodes_ValidFrom_ValidUntil",
            table: "PromoCodes",
            columns: new[] { "ValidFrom", "ValidUntil" });

        migrationBuilder.CreateIndex(
            name: "IX_PromoCodeUsages_PromoCodeId",
            table: "PromoCodeUsages",
            column: "PromoCodeId");

        migrationBuilder.CreateIndex(
            name: "IX_PromoCodeUsages_UserId",
            table: "PromoCodeUsages",
            column: "UserId");

        migrationBuilder.CreateIndex(
            name: "IX_PromoCodeUsages_OrderId",
            table: "PromoCodeUsages",
            column: "OrderId");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "PromoCodeUsages");

        migrationBuilder.DropTable(
            name: "PromoCodes");
    }
}
