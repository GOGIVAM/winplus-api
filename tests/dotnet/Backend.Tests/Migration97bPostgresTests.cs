using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Backend.Models.Entities;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace Backend.Tests
{
    /// <summary>
    /// Lot 9.7b, étape 5 : grille SQL_Subscriptions97bGrid.sql et script manuel
    /// scripts/9.7b/migrate_subscriptions.sql, sur un vrai PostgreSQL jetable
    /// (jamais la production). Le script est joué deux fois : la seconde ne
    /// change rien et n'ajoute aucune ligne de journal.
    /// </summary>
    public class Migration97bPostgresTests : IClassFixture<PostgresTestDatabase>
    {
        private readonly PostgresTestDatabase _pg;
        public Migration97bPostgresTests(PostgresTestDatabase pg) => _pg = pg;

        private static string MigrationScript()
        {
            for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
            {
                var candidate = Path.Combine(dir.FullName, "scripts", "9.7b", "migrate_subscriptions.sql");
                if (File.Exists(candidate))
                    // Méta-commande psql sans objet hors psql.
                    return Regex.Replace(File.ReadAllText(candidate), @"^\\set .*$", "", RegexOptions.Multiline);
            }
            throw new FileNotFoundException("migrate_subscriptions.sql");
        }

        private async Task<T> ScalarAsync<T>(string sql)
        {
            await using var c = new NpgsqlConnection(_pg.ConnectionString);
            await c.OpenAsync();
            await using var cmd = new NpgsqlCommand(sql, c);
            var r = await cmd.ExecuteScalarAsync();
            return r is null or DBNull ? default! : (T)Convert.ChangeType(r, typeof(T));
        }

        private Task<string?> InForceAsync(int user, string column = "p.\"PlanKey\" || '/' || s.\"BillingPeriod\" || '/' || s.\"AutoRenew\"") =>
            ScalarAsync<string?>($@"SELECT string_agg({column}, ',') FROM ""Subscriptions"" s JOIN ""PricingPlans"" p ON p.""Id"" = s.""PricingPlanId""
                                    WHERE s.""UserId"" = {user} AND s.""IsActive"" AND NOT s.""IsDeleted"" AND LOWER(s.""Status"") IN ('active','trialing','past_due')");

        [PostgresFact]
        public async Task GridThenMigration_ReachesReferenceStates_AndIsIdempotent()
        {
            if (string.IsNullOrEmpty(_pg.ConnectionString)) return;
            await using (var db = _pg.CreateContext())
            {
                // Grille posée par la fixture (SQL_Subscriptions97bGrid.sql, joué deux fois).
                Assert.Equal(12, await db.PricingPlans.CountAsync(p => p.PlanKey != null && !p.IsDeleted));
                Assert.Equal(3, await db.PlanPrices.CountAsync(p => p.PlanVersion!.PricingPlan!.PlanKey == "parent-famille-plus"));
                Assert.Equal("75", await db.PlanEntitlements.Where(e => e.PlanVersion!.PricingPlan!.PlanKey == "teacher-pro" && e.Key == "teacher.revenue_share").Select(e => e.Value).SingleAsync());
                Assert.Equal(24000m, await db.PlanPrices.Where(p => p.PlanVersion!.PricingPlan!.PlanKey == "student-standard" && p.Period == "yearly").Select(p => p.PriceTtc).SingleAsync());

                var inst = new Institution { Name = "Établissement 43", Country = "CM" };
                db.Institutions.Add(inst);
                await db.SaveChangesAsync();
                User U(int id, string role, int? institution = null) => new()
                {
                    Id = id, Email = $"u{id}@mig97b.local", Role = role, IsActive = true, FirstName = "U" + id, LastName = "IT", InstitutionId = institution,
                };
                db.Users.AddRange(U(17, "admin"), U(19, "student"), U(21, "organization"), U(26, "parent"), U(30, "student"),
                    U(40, "student"), U(41, "teacher"), U(42, "parent"), U(43, "institution", inst.Id), U(50, "teacher"),
                    U(60, "teacher"), U(61, "student"), U(62, "student"));
                await db.SaveChangesAsync();
                await db.Database.ExecuteSqlRawAsync(@"SELECT setval(pg_get_serial_sequence('""Users""', 'Id'), 1000);");

                // Grille A (sans clé), comme en production avant migration.
                PricingPlan Old(string name, string cat, decimal price) => new() { Name = name, Category = cat, Price = price, Currency = "XAF" };
                var starter = Old("Starter", "students", 0);
                var standard = Old("Standard", "students", 5900);
                var premium = Old("Premium", "students", 11900);
                db.PricingPlans.AddRange(starter, standard, premium);
                await db.SaveChangesAsync();

                // Production : plusieurs abonnements en vigueur possibles, l'index n'existe pas encore.
                await db.Database.ExecuteSqlRawAsync(@"DROP INDEX IF EXISTS ""UX_Subscriptions_OneInForcePerUser"";");
                Subscription Sub(int user, PricingPlan plan, int endDays) => new()
                {
                    UserId = user, PricingPlanId = plan.Id, Status = "Active", IsActive = true,
                    StartDate = DateTime.UtcNow.AddDays(-10), EndDate = DateTime.UtcNow.AddDays(endDays),
                };
                db.Subscriptions.AddRange(
                    Sub(30, premium, 20), Sub(60, premium, 20), Sub(61, starter, 20),
                    Sub(62, standard, 10), Sub(62, standard, 25), Sub(41, premium, 5));
                await db.SaveChangesAsync();
            }

            var script = MigrationScript();
            await using (var db = _pg.CreateContext())
                await db.Database.ExecuteSqlRawAsync(script);
            var auditAfterFirst = await ScalarAsync<long>(@"SELECT COUNT(*) FROM ""AdminAuditLogs"" WHERE ""Reason"" = 'migration 9.7b'");
            Assert.True(auditAfterFirst > 0);

            await using (var db = _pg.CreateContext())
                await db.Database.ExecuteSqlRawAsync(script);
            Assert.Equal(auditAfterFirst, await ScalarAsync<long>(@"SELECT COUNT(*) FROM ""AdminAuditLogs"" WHERE ""Reason"" = 'migration 9.7b'"));

            Assert.Equal("student-premium/yearly/true", await InForceAsync(30));
            Assert.Equal("parent-famille/monthly/true", await InForceAsync(26));
            Assert.Equal("teacher-pro/quarterly/false", await InForceAsync(50));
            Assert.Equal("student-standard/monthly/true", await InForceAsync(19));
            var end19 = await ScalarAsync<DateTime>(@"SELECT s.""EndDate"" FROM ""Subscriptions"" s WHERE s.""UserId"" = 19 AND s.""IsActive""");
            Assert.InRange((end19.ToUniversalTime() - DateTime.UtcNow).TotalHours, 46, 49);
            Assert.Null(await InForceAsync(40));
            Assert.Null(await InForceAsync(41));
            Assert.Null(await InForceAsync(42));
            Assert.Null(await InForceAsync(60)); // plan élève sur un professeur : résilié
            Assert.Null(await InForceAsync(61)); // Starter : compte en gratuit
            Assert.Equal("student-standard/monthly/false", await InForceAsync(62)); // doublon : un seul reste

            Assert.Equal("institution", await ScalarAsync<string>(@"SELECT ""Role"" FROM ""Users"" WHERE ""Id"" = 21"));
            Assert.Equal(1L, await ScalarAsync<long>(@"SELECT COUNT(*) FROM ""ParentStudentLinks"" WHERE ""ParentId"" = 26 AND ""StudentId"" = 30 AND ""Status"" = 'accepted'"));
            Assert.Equal(1L, await ScalarAsync<long>(@"SELECT COUNT(*) FROM ""ParentStudentLinks"" WHERE ""ParentId"" = 42 AND ""Status"" = 'accepted'"));
            Assert.Equal(1L, await ScalarAsync<long>(@"SELECT COUNT(*) FROM ""InstitutionStudents"" WHERE ""StudentId"" = 40 AND ""ConfirmationStatus"" = 'accepted' AND ""IsActive"""));
            Assert.Equal("trialing/trial/30/3/1", await ScalarAsync<string>(
                @"SELECT l.""Status"" || '/' || l.""Tier"" || '/' || l.""StudentSeats"" || '/' || l.""TeacherSeats"" || '/' || (SELECT COUNT(*) FROM ""InstitutionSeats"" s WHERE s.""LicenseId"" = l.""Id"" AND s.""UserId"" = 40)
                    FROM ""InstitutionLicenses"" l WHERE l.""PromoCodeId"" IS NOT NULL"));
            Assert.Equal(1L, await ScalarAsync<long>(@"SELECT COUNT(*) FROM ""InstitutionLicenses"""));
            Assert.Equal(1L, await ScalarAsync<long>(@"SELECT COUNT(*) FROM pg_indexes WHERE indexname = 'UX_Subscriptions_OneInForcePerUser'"));
        }
    }
}
