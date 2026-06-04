using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace backend.Migrations
{
    /// <inheritdoc />
    public partial class AddMissingColumnsToTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Ajouter IsArchived à PricingPlans (idempotent avec SQL brut)
            migrationBuilder.Sql(
                @"DO $$
                BEGIN
                    IF NOT EXISTS(SELECT 1 FROM information_schema.columns WHERE table_name='PricingPlans' AND column_name='IsArchived') 
                    THEN
                        ALTER TABLE ""PricingPlans"" ADD COLUMN ""IsArchived"" boolean NOT NULL DEFAULT false;
                    END IF;
                END $$;");

            // Ajouter Order à Pages (idempotent avec SQL brut)
            migrationBuilder.Sql(
                @"DO $$
                BEGIN
                    IF NOT EXISTS(SELECT 1 FROM information_schema.columns WHERE table_name='Pages' AND column_name='Order') 
                    THEN
                        ALTER TABLE ""Pages"" ADD COLUMN ""Order"" integer NOT NULL DEFAULT 0;
                    END IF;
                END $$;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Pour rollback: supprimer les colonnes
            migrationBuilder.Sql(
                @"DO $$
                BEGIN
                    IF EXISTS(SELECT 1 FROM information_schema.columns WHERE table_name='PricingPlans' AND column_name='IsArchived') 
                    THEN
                        ALTER TABLE ""PricingPlans"" DROP COLUMN ""IsArchived"";
                    END IF;
                END $$;");

            migrationBuilder.Sql(
                @"DO $$
                BEGIN
                    IF EXISTS(SELECT 1 FROM information_schema.columns WHERE table_name='Pages' AND column_name='Order') 
                    THEN
                        ALTER TABLE ""Pages"" DROP COLUMN ""Order"";
                    END IF;
                END $$;");
        }
    }
}
