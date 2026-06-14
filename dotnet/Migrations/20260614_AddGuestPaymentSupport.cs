using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace backend.Migrations
{
    public partial class AddGuestPaymentSupport : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Rendre UserId nullable sur Payments
            migrationBuilder.Sql(
                @"DO $$
                BEGIN
                    IF EXISTS (
                        SELECT 1 FROM information_schema.columns
                        WHERE table_name='Payments' AND column_name='UserId' AND is_nullable='NO'
                    ) THEN
                        ALTER TABLE ""Payments"" ALTER COLUMN ""UserId"" DROP NOT NULL;
                    END IF;
                END $$;");

            // Ajouter GuestEmail sur Payments
            migrationBuilder.Sql(
                @"DO $$
                BEGIN
                    IF NOT EXISTS(SELECT 1 FROM information_schema.columns WHERE table_name='Payments' AND column_name='GuestEmail') THEN
                        ALTER TABLE ""Payments"" ADD COLUMN ""GuestEmail"" character varying(255);
                    END IF;
                END $$;");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                @"DO $$
                BEGIN
                    IF EXISTS(SELECT 1 FROM information_schema.columns WHERE table_name='Payments' AND column_name='GuestEmail') THEN
                        ALTER TABLE ""Payments"" DROP COLUMN ""GuestEmail"";
                    END IF;
                END $$;");
        }
    }
}
