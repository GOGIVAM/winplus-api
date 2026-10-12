using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Backend.Data;
using Backend.Models.DTOs;
using Backend.Models.Entities;
using Backend.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Npgsql;
using Xunit;

namespace Backend.Tests
{
    /// <summary>
    /// Test exécuté seulement si une base PostgreSQL de test est fournie par la
    /// variable d'environnement <c>WINPLUS_TEST_PG</c> (chaîne de connexion vers
    /// un serveur local : localhost ou 127.0.0.1 uniquement). Sans elle, le test
    /// est signalé « ignoré », jamais réussi en silence.
    /// </summary>
    public sealed class PostgresFactAttribute : FactAttribute
    {
        public PostgresFactAttribute()
        {
            if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(PostgresTestDatabase.EnvVar)))
                Skip = $"PostgreSQL de test non configuré ({PostgresTestDatabase.EnvVar}).";
        }
    }

    /// <summary>
    /// Base PostgreSQL jetable : créée au début de la classe de tests, schéma
    /// du vrai modèle EF (EnsureCreated) complété par les scripts SQL manuels du
    /// lot 9.3 et du 17.14 (contraintes CHECK du registre, rejouées pour vérifier
    /// aussi leur idempotence), puis supprimée. Refuse tout serveur distant :
    /// la base de production n'est jamais utilisable ici.
    /// </summary>
    public sealed class PostgresTestDatabase : IAsyncLifetime
    {
        public const string EnvVar = "WINPLUS_TEST_PG";
        public string ConnectionString { get; private set; } = "";
        private string? _adminConnection;
        private string? _dbName;

        public async Task InitializeAsync()
        {
            var raw = Environment.GetEnvironmentVariable(EnvVar);
            if (string.IsNullOrWhiteSpace(raw)) return;
            var builder = new NpgsqlConnectionStringBuilder(raw);
            if (builder.Host is not ("localhost" or "127.0.0.1" or "::1"))
                throw new InvalidOperationException($"{EnvVar} doit viser un serveur PostgreSQL local (localhost), jamais la production.");

            _dbName = $"winplus_it_{Guid.NewGuid():N}";
            builder.Database = "postgres";
            _adminConnection = builder.ConnectionString;
            await using (var admin = new NpgsqlConnection(_adminConnection))
            {
                await admin.OpenAsync();
                await using var create = new NpgsqlCommand($"CREATE DATABASE \"{_dbName}\"", admin);
                await create.ExecuteNonQueryAsync();
            }

            builder.Database = _dbName;
            builder.IncludeErrorDetail = true;
            ConnectionString = builder.ConnectionString;

            await using var db = CreateContext();
            await db.Database.EnsureCreatedAsync();
            await db.Database.ExecuteSqlRawAsync(
                "CREATE TABLE IF NOT EXISTS \"__EFMigrationsHistory\" (\"MigrationId\" varchar(150) PRIMARY KEY, \"ProductVersion\" varchar(32) NOT NULL);");
            // Contraintes du registre posées par les scripts manuels (absentes du modèle EF).
            await db.Database.ExecuteSqlRawAsync(@"
                ALTER TABLE ""WalletTransactions"" ADD CONSTRAINT ""CK_WalletTransactions_Status"" CHECK (""Status"" IN ('pending', 'confirmed', 'reversed'));
                ALTER TABLE ""WalletTransactions"" ADD CONSTRAINT ""CK_WalletTransactions_Owner"" CHECK ((""OwnerType"" = 'platform') = (""OwnerId"" IS NULL));
                -- Schéma de production : colonne créée en VARCHAR(20) par SQL_AddTutorBookingModule.sql,
                -- élargie par SQL_InstitutionWalletHolderAndAttachmentConsent.sql ci-dessous.
                ALTER TABLE ""TutorBookings"" ALTER COLUMN ""Status"" TYPE VARCHAR(20);");
            foreach (var script in new[] { "SQL_TutorPayersParticipantsGroupsAndAssignments.sql", "SQL_InstitutionWalletHolderAndAttachmentConsent.sql" })
            {
                var sql = await File.ReadAllTextAsync(FindMigration(script));
                await db.Database.ExecuteSqlRawAsync(sql);
                await db.Database.ExecuteSqlRawAsync(sql); // idempotence
            }
        }

        public ApplicationDbContext CreateContext() =>
            new(new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseNpgsql(ConnectionString, o => o.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery))
                .Options);

        private static string FindMigration(string file)
        {
            for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
            {
                var candidate = Path.Combine(dir.FullName, "dotnet", "Migrations", file);
                if (File.Exists(candidate)) return candidate;
            }
            throw new FileNotFoundException(file);
        }

        public async Task DisposeAsync()
        {
            if (_adminConnection == null || _dbName == null) return;
            NpgsqlConnection.ClearAllPools();
            await using var admin = new NpgsqlConnection(_adminConnection);
            await admin.OpenAsync();
            await using var drop = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{_dbName}\" WITH (FORCE)", admin);
            await drop.ExecuteNonQueryAsync();
        }
    }

    /// <summary>
    /// Création de réservation de bout en bout sur un vrai PostgreSQL (lot 9.3,
    /// suivi 17.4 et 17.14) : transaction Serializable, verrou consultatif du
    /// portefeuille, index uniques et contraintes CHECK du registre réels.
    /// Payée par solde (élève, parent pour son enfant, institution pour un élève
    /// au rattachement accepté, membre d'un groupe d'étude), refus sans lien,
    /// réservations concurrentes du même créneau, séance de groupe qui n'occupe
    /// le créneau qu'une fois, annulation et remboursement au bon portefeuille.
    /// </summary>
    public class TutorBookingPostgresIntegrationTests : IClassFixture<PostgresTestDatabase>
    {
        private readonly PostgresTestDatabase _pg;
        private readonly Mock<INtfyService> _ntfy = new();
        private readonly Mock<INotchPayService> _notchPay = new(MockBehavior.Strict);
        private static readonly IConfiguration Config = new ConfigurationBuilder().Build();

        public TutorBookingPostgresIntegrationTests(PostgresTestDatabase pg) => _pg = pg;

        private sealed record World(int Tutor, int StudentA, int StudentB, int Parent, int Child, int Stranger,
            int InstitutionAccount, int InstitutionId, int Pupil, int PendingPupil, int Member1, int Member2, int GroupId);

        /// <summary>Jeu de données propre à chaque test (e-mails uniques), sur la base partagée de la classe.</summary>
        private async Task<World> SeedAsync()
        {
            var tag = Guid.NewGuid().ToString("N")[..8];
            await using var db = _pg.CreateContext();
            User U(string name, string role, int? inst = null) => new()
            {
                Email = $"{name}-{tag}@it.local", Role = role, IsActive = true, FirstName = name, LastName = "IT", InstitutionId = inst,
            };

            var institution = new Institution { Name = $"Lycée IT {tag}", Country = "CM" };
            db.Institutions.Add(institution);
            await db.SaveChangesAsync();

            var tutor = U("tuteur", "teacher");
            var users = new Dictionary<string, User>
            {
                ["tutor"] = tutor, ["a"] = U("eleveA", "student"), ["b"] = U("eleveB", "student"), ["parent"] = U("parent", "parent"),
                ["child"] = U("enfant", "student"), ["stranger"] = U("inconnu", "student"),
                ["inst"] = U("institution", "institution", institution.Id), ["pupil"] = U("eleveInst", "student"),
                ["pending"] = U("eleveAttente", "student"), ["m1"] = U("membre1", "student"), ["m2"] = U("membre2", "student"),
            };
            db.Users.AddRange(users.Values);
            await db.SaveChangesAsync();

            var profile = new TutorProfile
            {
                UserId = tutor.Id, IsActive = true, HourlyRateXaf = 6000, OffersOnline = true, NoticeHours = 0,
                GroupPricingMethod = TutorPricing.GroupPerStudent, MaxGroupSize = 4,
                Subjects = new List<TutorSubject> { new() { Subject = "Maths" } },
                GroupDiscounts = new List<TutorGroupDiscount> { new() { MinGroupSize = 2, PercentDiscount = 20 } },
                AvailabilitySlots = Enumerable.Range(0, 7)
                    .Select(d => new TutorAvailabilitySlot { DayOfWeek = d, StartTime = new TimeSpan(8, 0, 0), EndTime = new TimeSpan(20, 0, 0) })
                    .ToList(),
            };
            db.TutorProfiles.Add(profile);
            db.ParentStudentLinks.Add(new ParentStudentLink { ParentId = users["parent"].Id, StudentId = users["child"].Id, Status = "accepted", InitiatedBy = users["parent"].Id });
            db.InstitutionStudents.AddRange(
                new InstitutionStudent { InstitutionId = institution.Id, StudentId = users["pupil"].Id, IsActive = true, ConfirmationStatus = InstitutionAttachmentStatuses.Accepted },
                new InstitutionStudent { InstitutionId = institution.Id, StudentId = users["pending"].Id, IsActive = true, ConfirmationStatus = InstitutionAttachmentStatuses.Pending });
            var group = new StudyGroup
            {
                OwnerId = users["m1"].Id, Name = $"Groupe {tag}", JoinCode = tag[..6].ToUpperInvariant(), IsActive = true,
                Members = new List<StudyGroupMember> { new() { UserId = users["m1"].Id, Role = "owner" }, new() { UserId = users["m2"].Id } },
            };
            db.StudyGroups.Add(group);
            await db.SaveChangesAsync();

            return new World(tutor.Id, users["a"].Id, users["b"].Id, users["parent"].Id, users["child"].Id, users["stranger"].Id,
                users["inst"].Id, institution.Id, users["pupil"].Id, users["pending"].Id, users["m1"].Id, users["m2"].Id, group.Id);
        }

        private async Task CreditAsync(int owner, decimal amount)
        {
            await using var db = _pg.CreateContext();
            await new WalletService(db, NullLogger<WalletService>.Instance)
                .PostAsync(new WalletEntry(owner, WalletEntryTypes.Recharge, amount, $"it-seed-{owner}-{Guid.NewGuid()}", "Recharge de test"));
        }

        private (TutorBookingService Service, WalletService Wallet) Build(ApplicationDbContext db)
        {
            var wallet = new WalletService(db, NullLogger<WalletService>.Instance);
            var refunds = new TutorRefundService(db, wallet, _ntfy.Object, NullLogger<TutorRefundService>.Instance, Config);
            return (new TutorBookingService(db, _notchPay.Object, _ntfy.Object, NullLogger<TutorBookingService>.Instance, wallet, Config, refunds), wallet);
        }

        /// <summary>Un créneau futur propre à chaque appel (jours différents : aucun préavis, aucune collision entre tests).</summary>
        private static int _dayOffset = 3;
        private static DateOnly NextDay() => DateOnly.FromDateTime(DateTime.UtcNow.AddDays(System.Threading.Interlocked.Increment(ref _dayOffset)));

        private static CreateTutorBookingRequestDto Request(int tutor, DateOnly day, string start = "10:00", int minutes = 60) => new()
        {
            TutorUserId = tutor, SessionDate = day, StartTime = start, DurationMinutes = minutes, Mode = "online",
            PaymentMethod = TutorPaymentMethods.Wallet, Subject = "Maths",
        };

        private async Task<decimal> AvailableAsync(int owner)
        {
            await using var db = _pg.CreateContext();
            return await new WalletService(db, NullLogger<WalletService>.Instance).GetAvailableAsync(owner);
        }

        [PostgresFact]
        public async Task Student_PaysWithBalance_DebitAndBookingAtomic_TutorRevenuePending()
        {
            var w = await SeedAsync();
            await CreditAsync(w.StudentA, 10000);
            var day = NextDay();

            TutorBookingCreatedResponseDto created;
            await using (var db = _pg.CreateContext())
                created = await Build(db).Service.CreateBookingAsync(w.StudentA, Request(w.Tutor, day));

            Assert.Equal("pending_tutor_approval", created.Booking.Status);
            Assert.Equal(6000, created.Booking.PriceXaf);
            Assert.Equal(4000, await AvailableAsync(w.StudentA));

            await using var check = _pg.CreateContext();
            var entries = await check.WalletTransactions.AsNoTracking().Where(t => t.SourceType == "TutorBooking" && t.SourceId == created.Booking.Id).ToListAsync();
            var debit = Assert.Single(entries, t => t.EntryType == WalletEntryTypes.TutoringPayment);
            Assert.Equal(-6000, debit.Amount);
            Assert.Equal(w.StudentA, debit.OwnerId);
            var revenue = Assert.Single(entries, t => t.EntryType == WalletEntryTypes.TutoringRevenue);
            Assert.Equal(WalletEntryStatus.Pending, revenue.Status);
            Assert.Equal(w.Tutor, revenue.OwnerId);
        }

        [PostgresFact]
        public async Task InsufficientBalance_NoBookingNoDebit()
        {
            var w = await SeedAsync();
            await CreditAsync(w.StudentB, 1000);
            await using (var db = _pg.CreateContext())
                await Assert.ThrowsAsync<InsufficientWalletBalanceException>(() => Build(db).Service.CreateBookingAsync(w.StudentB, Request(w.Tutor, NextDay())));
            await using var check = _pg.CreateContext();
            Assert.False(await check.TutorBookings.AnyAsync(b => b.StudentUserId == w.StudentB));
            Assert.Equal(1000, await AvailableAsync(w.StudentB));
        }

        [PostgresFact]
        public async Task Parent_PaysForChild_FromParentWallet_ThenCancels_RefundToParent()
        {
            var w = await SeedAsync();
            await CreditAsync(w.Parent, 20000);
            var day = NextDay();

            int bookingId;
            await using (var db = _pg.CreateContext())
            {
                var req = Request(w.Tutor, day, "14:00", 90);
                req.PayerType = TutorPayerTypes.Parent;
                req.ParticipantUserIds = new List<int> { w.Child };
                var created = await Build(db).Service.CreateBookingAsync(w.Parent, req);
                Assert.Equal(TutorPayerTypes.Parent, created.Booking.PayerType);
                Assert.Equal(w.Child, created.Booking.StudentUserId);
                Assert.Equal(9000, created.Booking.PriceXaf);
                bookingId = created.Booking.Id;
            }
            Assert.Equal(11000, await AvailableAsync(w.Parent));
            Assert.Equal(0, await AvailableAsync(w.Child));

            // L'enfant participe mais n'a pas payé : il ne peut pas annuler.
            await using (var db = _pg.CreateContext())
                await Assert.ThrowsAsync<InvalidOperationException>(() => Build(db).Service.CancelBookingAsync(w.Child, bookingId, null));

            // Annulation par le parent plus de 24 h avant : remboursement intégral
            // (payé par solde, aucun frais d'encaissement) au portefeuille parent.
            await using (var db = _pg.CreateContext())
                await Build(db).Service.CancelBookingAsync(w.Parent, bookingId, "Empêchement");

            Assert.Equal(20000, await AvailableAsync(w.Parent));
            Assert.Equal(0, await AvailableAsync(w.Child));
            await using var check = _pg.CreateContext();
            var refund = await check.WalletTransactions.AsNoTracking().SingleAsync(t => t.SourceId == bookingId && t.EntryType == WalletEntryTypes.TutoringRefund);
            Assert.Equal(w.Parent, refund.OwnerId);
            Assert.Equal(WalletOwnerTypes.Parent, refund.OwnerType);
            Assert.Equal(9000, refund.Amount);
            var revenue = await check.WalletTransactions.AsNoTracking().SingleAsync(t => t.SourceId == bookingId && t.EntryType == WalletEntryTypes.TutoringRevenue);
            Assert.Equal(WalletEntryStatus.Reversed, revenue.Status);
        }

        [PostgresFact]
        public async Task Booking_WithoutVerifiedLink_IsRefused_NothingWritten()
        {
            var w = await SeedAsync();
            await CreditAsync(w.Parent, 20000);
            await using (var db = _pg.CreateContext())
            {
                var req = Request(w.Tutor, NextDay());
                req.PayerType = TutorPayerTypes.Parent;
                req.ParticipantUserIds = new List<int> { w.Stranger };
                await Assert.ThrowsAsync<TutorBookingForbiddenException>(() => Build(db).Service.CreateBookingAsync(w.Parent, req));

                var inst = Request(w.Tutor, NextDay());
                inst.PayerType = TutorPayerTypes.Institution;
                inst.ParticipantUserIds = new List<int> { w.PendingPupil }; // 17.14 : rattachement non confirmé
                await Assert.ThrowsAsync<TutorBookingForbiddenException>(() => Build(db).Service.CreateBookingAsync(w.InstitutionAccount, inst));
            }
            await using var check = _pg.CreateContext();
            Assert.False(await check.TutorBookings.AnyAsync(b => b.StudentUserId == w.Stranger || b.StudentUserId == w.PendingPupil));
            Assert.Equal(20000, await AvailableAsync(w.Parent));
        }

        [PostgresFact]
        public async Task Institution_PaysForAcceptedPupil_FromHolderWallet_TutorDeclines_RefundToHolder()
        {
            var w = await SeedAsync();
            int holder;
            await using (var db = _pg.CreateContext())
                holder = await InstitutionWallet.ResolveHolderAsync(db, w.InstitutionId, w.InstitutionAccount);
            Assert.Equal(w.InstitutionAccount, holder);
            await CreditAsync(holder, 15000);

            int bookingId;
            await using (var db = _pg.CreateContext())
            {
                var req = Request(w.Tutor, NextDay());
                req.PayerType = TutorPayerTypes.Institution;
                req.ParticipantUserIds = new List<int> { w.Pupil };
                var created = await Build(db).Service.CreateBookingAsync(w.InstitutionAccount, req);
                Assert.Equal(w.InstitutionId, created.Booking.PayerInstitutionId);
                bookingId = created.Booking.Id;
            }
            Assert.Equal(9000, await AvailableAsync(holder));

            await using (var db = _pg.CreateContext())
                await Build(db).Service.DeclineBookingAsync(w.Tutor, bookingId, "Indisponible");

            Assert.Equal(15000, await AvailableAsync(holder));
            Assert.Equal(0, await AvailableAsync(w.Pupil));
            await using var check = _pg.CreateContext();
            var refund = await check.WalletTransactions.AsNoTracking().SingleAsync(t => t.SourceId == bookingId && t.EntryType == WalletEntryTypes.TutoringRefund);
            Assert.Equal(WalletOwnerTypes.Institution, refund.OwnerType);
            Assert.Equal(holder, refund.OwnerId);
        }

        [PostgresFact]
        public async Task StudyGroup_GroupPrice_OccupiesTheSlotOnce()
        {
            var w = await SeedAsync();
            await CreditAsync(w.Member2, 20000);
            await CreditAsync(w.StudentA, 20000);
            var day = NextDay();

            await using (var db = _pg.CreateContext())
            {
                var req = Request(w.Tutor, day, "16:00", 60);
                req.PayerType = TutorPayerTypes.StudyGroup;
                req.StudyGroupId = w.GroupId;
                req.ParticipantUserIds = new List<int> { w.Member1, w.Member2 };
                var created = await Build(db).Service.CreateBookingAsync(w.Member2, req);
                // 6 000 par élève, -20 % dès 2 élèves : 4 800 × 2.
                Assert.Equal(9600, created.Booking.PriceXaf);
                Assert.Equal(2, created.Booking.ParticipantCount);
                Assert.Equal(w.Member2, created.Booking.PayerUserId);
            }
            Assert.Equal(10400, await AvailableAsync(w.Member2));

            // Le créneau est pris une fois : un autre élève ne peut pas réserver par-dessus.
            await using (var db = _pg.CreateContext())
                await Assert.ThrowsAsync<InvalidOperationException>(() => Build(db).Service.CreateBookingAsync(w.StudentA, Request(w.Tutor, day, "16:30", 30)));

            await using var check = _pg.CreateContext();
            var bookings = await check.TutorBookings.AsNoTracking().Include(b => b.Participants)
                .Where(b => b.TutorProfile!.UserId == w.Tutor && b.SessionDate == day).ToListAsync();
            var only = Assert.Single(bookings);
            Assert.Equal(2, only.Participants.Count);
            Assert.Equal(20000, await AvailableAsync(w.StudentA));
        }

        [PostgresFact]
        public async Task ConcurrentBookings_SameSlot_OnlyOneSucceeds_OnlyOneDebit()
        {
            var w = await SeedAsync();
            await CreditAsync(w.StudentA, 10000);
            await CreditAsync(w.StudentB, 10000);
            var day = NextDay();

            var gate = new TaskCompletionSource();
            async Task<bool> Attempt(int student)
            {
                await gate.Task;
                await using var db = _pg.CreateContext();
                try
                {
                    await Build(db).Service.CreateBookingAsync(student, Request(w.Tutor, day, "09:00", 60));
                    return true;
                }
                catch (InvalidOperationException) { return false; }
            }

            var runs = new[] { Task.Run(() => Attempt(w.StudentA)), Task.Run(() => Attempt(w.StudentB)) };
            gate.SetResult();
            var results = await Task.WhenAll(runs);

            Assert.Equal(1, results.Count(ok => ok));
            await using var check = _pg.CreateContext();
            var booking = await check.TutorBookings.AsNoTracking().SingleAsync(b => b.TutorProfile!.UserId == w.Tutor && b.SessionDate == day);
            var debits = await check.WalletTransactions.AsNoTracking().Where(t => t.EntryType == WalletEntryTypes.TutoringPayment
                && (t.OwnerId == w.StudentA || t.OwnerId == w.StudentB)).ToListAsync();
            var debit = Assert.Single(debits);
            Assert.Equal(booking.PayerUserId, debit.OwnerId);
            Assert.Equal(20000 - 6000, await AvailableAsync(w.StudentA) + await AvailableAsync(w.StudentB));
        }

        [PostgresFact]
        public async Task InstitutionHolderChange_OnPostgres_TwoEntriesInOneTransaction_ThenRefundGoesToNewHolder()
        {
            var w = await SeedAsync();
            int second;
            await using (var db = _pg.CreateContext())
            {
                var other = new User { Email = $"inst2-{Guid.NewGuid():N}@it.local", Role = "institution", IsActive = true, InstitutionId = w.InstitutionId };
                db.Users.Add(other);
                await db.SaveChangesAsync();
                second = other.Id;
                await InstitutionWallet.ResolveHolderAsync(db, w.InstitutionId, w.InstitutionAccount);
            }
            await CreditAsync(w.InstitutionAccount, 12000);

            int bookingId;
            await using (var db = _pg.CreateContext())
            {
                var req = Request(w.Tutor, NextDay());
                req.PayerType = TutorPayerTypes.Institution;
                req.ParticipantUserIds = new List<int> { w.Pupil };
                bookingId = (await Build(db).Service.CreateBookingAsync(w.InstitutionAccount, req)).Booking.Id;
            }

            await using (var db = _pg.CreateContext())
            {
                var holders = new InstitutionWalletHolderService(db, new WalletService(db, NullLogger<WalletService>.Instance), _ntfy.Object,
                    NullLogger<InstitutionWalletHolderService>.Instance);
                await holders.ChangeHolderAsync(w.InstitutionId, second, w.Tutor, "Test d'intégration", "it-1");
                Assert.True((await holders.ChangeHolderAsync(w.InstitutionId, second, w.Tutor, "Test d'intégration", "it-1")).AlreadyDone);
            }
            Assert.Equal(0, await AvailableAsync(w.InstitutionAccount));
            Assert.Equal(6000, await AvailableAsync(second));

            // La séance payée avant le changement est remboursée au nouveau titulaire.
            await using (var db = _pg.CreateContext())
                await Build(db).Service.DeclineBookingAsync(w.Tutor, bookingId, null);
            Assert.Equal(12000, await AvailableAsync(second));
            Assert.Equal(0, await AvailableAsync(w.InstitutionAccount));
        }
    }
}
