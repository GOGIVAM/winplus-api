using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace backend.Migrations
{
    public partial class AddDiscountAmountToOrders : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                @"DO $$
                BEGIN
                    IF NOT EXISTS(SELECT 1 FROM information_schema.columns WHERE table_name='Orders' AND column_name='DiscountAmount') THEN
                        ALTER TABLE ""Orders"" ADD COLUMN ""DiscountAmount"" numeric(18,2) NOT NULL DEFAULT 0;
                    END IF;
                END $$;");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                @"DO $$
                BEGIN
                    IF EXISTS(SELECT 1 FROM information_schema.columns WHERE table_name='Orders' AND column_name='DiscountAmount') THEN
                        ALTER TABLE ""Orders"" DROP COLUMN ""DiscountAmount"";
                    END IF;
                END $$;");
        }
    }
}
