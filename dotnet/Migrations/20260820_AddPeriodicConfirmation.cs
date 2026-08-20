using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace backend.Migrations
{
    /// <inheritdoc />
    public partial class AddPeriodicConfirmation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Ajouter LastPeriodicConfirmAt à Users
            migrationBuilder.Sql(
                @"DO $$
                BEGIN
                    IF NOT EXISTS(SELECT 1 FROM information_schema.columns WHERE table_name='Users' AND column_name='LastPeriodicConfirmAt')
                    THEN
                        ALTER TABLE ""Users"" ADD COLUMN ""LastPeriodicConfirmAt"" timestamp with time zone NULL;
                    END IF;
                END $$;");

            // Ajouter Purpose à EmailVerificationTokens (défaut : 'verify_email')
            migrationBuilder.Sql(
                @"DO $$
                BEGIN
                    IF NOT EXISTS(SELECT 1 FROM information_schema.columns WHERE table_name='EmailVerificationTokens' AND column_name='Purpose')
                    THEN
                        ALTER TABLE ""EmailVerificationTokens"" ADD COLUMN ""Purpose"" character varying(30) NOT NULL DEFAULT 'verify_email';
                    END IF;
                END $$;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                @"DO $$
                BEGIN
                    IF EXISTS(SELECT 1 FROM information_schema.columns WHERE table_name='Users' AND column_name='LastPeriodicConfirmAt')
                    THEN
                        ALTER TABLE ""Users"" DROP COLUMN ""LastPeriodicConfirmAt"";
                    END IF;
                END $$;");

            migrationBuilder.Sql(
                @"DO $$
                BEGIN
                    IF EXISTS(SELECT 1 FROM information_schema.columns WHERE table_name='EmailVerificationTokens' AND column_name='Purpose')
                    THEN
                        ALTER TABLE ""EmailVerificationTokens"" DROP COLUMN ""Purpose"";
                    END IF;
                END $$;");
        }
    }
}
