using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace backend.Migrations
{
    public partial class AddGuestOrderSupport : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Rendre UserId nullable pour les commandes anonymes
            migrationBuilder.Sql(
                @"DO $$
                BEGIN
                    IF EXISTS (
                        SELECT 1 FROM information_schema.columns
                        WHERE table_name='Orders' AND column_name='UserId' AND is_nullable='NO'
                    ) THEN
                        ALTER TABLE ""Orders"" ALTER COLUMN ""UserId"" DROP NOT NULL;
                    END IF;
                END $$;");

            // Ajouter GuestEmail
            migrationBuilder.Sql(
                @"DO $$
                BEGIN
                    IF NOT EXISTS(SELECT 1 FROM information_schema.columns WHERE table_name='Orders' AND column_name='GuestEmail') THEN
                        ALTER TABLE ""Orders"" ADD COLUMN ""GuestEmail"" text;
                    END IF;
                END $$;");

            // Ajouter GuestName
            migrationBuilder.Sql(
                @"DO $$
                BEGIN
                    IF NOT EXISTS(SELECT 1 FROM information_schema.columns WHERE table_name='Orders' AND column_name='GuestName') THEN
                        ALTER TABLE ""Orders"" ADD COLUMN ""GuestName"" text;
                    END IF;
                END $$;");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                @"DO $$
                BEGIN
                    IF EXISTS(SELECT 1 FROM information_schema.columns WHERE table_name='Orders' AND column_name='GuestEmail') THEN
                        ALTER TABLE ""Orders"" DROP COLUMN ""GuestEmail"";
                    END IF;
                END $$;");

            migrationBuilder.Sql(
                @"DO $$
                BEGIN
                    IF EXISTS(SELECT 1 FROM information_schema.columns WHERE table_name='Orders' AND column_name='GuestName') THEN
                        ALTER TABLE ""Orders"" DROP COLUMN ""GuestName"";
                    END IF;
                END $$;");
        }
    }
}
