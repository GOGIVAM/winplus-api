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
    /// Script manuel scripts/9.7b/setup_test_institution_43.sql sur un vrai
    /// PostgreSQL jetable (jamais la production) : compte 43 sans établissement
    /// (état de production constaté), joué deux fois. La seconde passe ne
    /// change rien et n'écrit aucun journal. La licence obtenue est celle
    /// qu'ouvrirait InstitutionLicenseService.StartTrialAsync.
    /// </summary>
    public class SetupTestInstitution43PostgresTests : IClassFixture<PostgresTestDatabase>
    {
        private readonly PostgresTestDatabase _pg;
        public SetupTestInstitution43PostgresTests(PostgresTestDatabase pg) => _pg = pg;

        internal static string Script()
        {
            for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
            {
                var candidate = Path.Combine(dir.FullName, "scripts", "9.7b", "setup_test_institution_43.sql");
                if (File.Exists(candidate))
                    return Regex.Replace(File.ReadAllText(candidate), @"^\\set .*$", "", RegexOptions.Multiline);
            }
            throw new FileNotFoundException("setup_test_institution_43.sql");
        }

        private async Task<long> CountAsync(string sql)
        {
            await using var c = new NpgsqlConnection(_pg.ConnectionString);
            await c.OpenAsync();
            await using var cmd = new NpgsqlCommand(sql, c);
            return Convert.ToInt64(await cmd.ExecuteScalarAsync());
        }

        private async Task RunAsync(string sql)
        {
            await using var c = new NpgsqlConnection(_pg.ConnectionString);
            await c.OpenAsync();
            await using var cmd = new NpgsqlCommand(sql, c);
            await cmd.ExecuteNonQueryAsync();
        }

        [PostgresFact]
        public async Task Script_OpensTrialLikeTheCode_AndIsIdempotent()
        {
            if (string.IsNullOrEmpty(_pg.ConnectionString)) return;
            await using (var db = _pg.CreateContext())
            {
                User U(int id, string role) => new() { Id = id, Email = $"u{id}@t43.local", Role = role, IsActive = true, FirstName = "U" + id, LastName = "T43" };
                db.Users.AddRange(U(17, "admin"), U(40, "student"), U(43, "institution"), U(41, "teacher"));
                await db.SaveChangesAsync();
                await db.Database.ExecuteSqlRawAsync(@"SELECT setval(pg_get_serial_sequence('""Users""', 'Id'), 1000);");
            }

            var script = Script();
            await RunAsync(script);
            var audit1 = await CountAsync(@"SELECT COUNT(*) FROM ""AdminAuditLogs""");
            Assert.True(audit1 >= 4);

            await using (var db = _pg.CreateContext())
            {
                var u43 = await db.Users.SingleAsync(u => u.Id == 43);
                Assert.NotNull(u43.InstitutionId);
                var inst = await db.Institutions.SingleAsync(i => i.Id == u43.InstitutionId);
                Assert.Equal(43, inst.WalletOwnerUserId);
                var att = await db.InstitutionStudents.SingleAsync(s => s.InstitutionId == inst.Id && s.StudentId == 40);
                Assert.True(att.IsActive);
                Assert.Equal("accepted", att.ConfirmationStatus);

                var lic = await db.InstitutionLicenses.Include(l => l.Seats).SingleAsync(l => l.InstitutionId == inst.Id);
                Assert.Equal(LicenseTiers.Trial, lic.Tier);
                Assert.Equal(LicenseStatuses.Trialing, lic.Status);
                Assert.Equal(LicenseTerms.Quarter, lic.Term);
                Assert.Equal(30, lic.StudentSeats);
                Assert.Equal(3, lic.TeacherSeats);
                Assert.Equal(0m, lic.TotalPriceTtc);
                Assert.Equal(20_000L * 30 * 30 / 330, lic.AiBudgetTokens);
                Assert.Equal(43, lic.CreatedByUserId);
                Assert.InRange((lic.EndDate - lic.StartDate).TotalDays, 29.99, 30.01);
                Assert.Single(lic.Seats, s => s.UserId == 40 && s.Kind == SeatKinds.Student && s.ReleasedAt == null);

                var promo = await db.PromoCodes.SingleAsync(p => p.Code == "ESSAI-ETAB-97B");
                Assert.Equal(lic.PromoCodeId, promo.Id);
                Assert.Equal(1, promo.UsageCount);
                Assert.Single(await db.PromoCodeUsages.Where(u => u.PromoCodeId == promo.Id).ToListAsync(), u => u.UserId == 43 && u.Target == "institution_license");
            }

            // Seconde passe : aucun changement, aucun journal.
            await RunAsync(script);
            Assert.Equal(audit1, await CountAsync(@"SELECT COUNT(*) FROM ""AdminAuditLogs"""));
            Assert.Equal(1, await CountAsync(@"SELECT COUNT(*) FROM ""InstitutionLicenses"""));
            Assert.Equal(1, await CountAsync(@"SELECT COUNT(*) FROM ""InstitutionSeats"""));
            Assert.Equal(1, await CountAsync(@"SELECT COUNT(*) FROM ""PromoCodeUsages"""));
            Assert.Equal(1, await CountAsync(@"SELECT COUNT(*) FROM ""Institutions"""));
            Assert.Equal(1, await CountAsync(@"SELECT ""UsageCount"" FROM ""PromoCodes"" WHERE ""Code"" = 'ESSAI-ETAB-97B'"));
        }
    }
}
