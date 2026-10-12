using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Backend.Controllers;
using Backend.Data;
using Backend.Models.Entities;
using Backend.Repositories;
using Backend.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Backend.Tests
{
    /// <summary>
    /// Arbitrages du 9.3 (suivi 17.14) : retraits réservés aux portefeuilles
    /// tuteur / professeur, changement de titulaire du portefeuille institution,
    /// suppression du titulaire bloquée, consentement de l'élève au rattachement.
    /// </summary>
    public class TutoringArbitrations1714Tests
    {
        private static readonly InMemoryDatabaseRoot Root = new();
        private readonly string _dbName = $"arb1714-{Guid.NewGuid()}";
        private readonly Mock<INtfyService> _ntfy = new();

        // Identifiants propres à cette classe (verrou de repli statique par utilisateur).
        private const int Teacher = 9401, Student = 9402, Parent = 9403, InstA = 9404, InstB = 9405, InstOther = 9406;
        private const int TeacherInInst = 9407, Admin = 9408, Pupil = 9409, InstC = 9410;
        private const int InstitutionId = 9450, OtherInstitutionId = 9451, TutorProfileId = 9452;

        public TutoringArbitrations1714Tests()
        {
            using var db = Fresh();
            User U(int id, string role, int? inst = null) => new() { Id = id, Email = $"u{id}@test.local", Role = role, IsActive = true, InstitutionId = inst };
            db.Users.AddRange(U(Teacher, "teacher"), U(Student, "student"), U(Parent, "parent"),
                U(InstA, "institution", InstitutionId), U(InstB, "institution", InstitutionId), U(InstC, "institution", InstitutionId),
                U(InstOther, "institution", OtherInstitutionId), U(TeacherInInst, "teacher", InstitutionId),
                U(Admin, "admin"), U(Pupil, "student"));
            db.Institutions.AddRange(
                new Institution { Id = InstitutionId, Name = "Lycée 17.14", Country = "CM", WalletOwnerUserId = InstA },
                new Institution { Id = OtherInstitutionId, Name = "Autre lycée", Country = "CM" });
            db.TutorProfiles.Add(new TutorProfile { Id = TutorProfileId, UserId = Teacher, IsActive = true });
            db.SaveChanges();
        }

        private TestApplicationDbContext Fresh() => TestApplicationDbContext.Create(_dbName, Root);
        private static WalletService Wallet(ApplicationDbContext db) => new(db, NullLogger<WalletService>.Instance);

        private async Task Credit(int owner, decimal amount, string? ownerType = null)
        {
            using var db = Fresh();
            await Wallet(db).PostAsync(new WalletEntry(owner, WalletEntryTypes.Recharge, amount, $"seed-{owner}-{Guid.NewGuid()}", "Recharge", OwnerType: ownerType));
        }

        private InstitutionWalletHolderService Holders(ApplicationDbContext db) =>
            new(db, Wallet(db), _ntfy.Object, NullLogger<InstitutionWalletHolderService>.Instance);

        private static ControllerContext As(int userId, string role) => new()
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim("user_id", userId.ToString()), new Claim("role", role) }, "test")),
            },
        };

        // ── Retraits ─────────────────────────────────────────────────────

        [Theory]
        [InlineData(Teacher, true, "teacher")]
        [InlineData(Student, false, "student")]
        [InlineData(Parent, false, "parent")]
        [InlineData(InstB, false, "institution")]
        [InlineData(Admin, false, "user")]
        public async Task WithdrawalPolicy_OnlyTutorOrTeacherWallets(int userId, bool canWithdraw, string walletType)
        {
            using var db = Fresh();
            var e = await WalletWithdrawalPolicy.CheckAsync(db, userId);
            Assert.Equal(canWithdraw, e.CanWithdraw);
            Assert.Equal(walletType, e.WalletType);
            if (!canWithdraw) Assert.False(string.IsNullOrWhiteSpace(e.Reason));
        }

        [Fact]
        public async Task WithdrawalPolicy_UsesWalletType_NotOnlyTheRole()
        {
            // Un professeur titulaire du portefeuille d'une institution, ou dont le
            // registre porte des écritures parent, ne retire pas ce solde de dépense.
            using (var db = Fresh())
            {
                (await db.Institutions.FirstAsync(i => i.Id == OtherInstitutionId)).WalletOwnerUserId = TeacherInInst;
                await db.SaveChangesAsync();
            }
            await Credit(Teacher, 1000, WalletOwnerTypes.Parent);
            using var check = Fresh();
            Assert.False((await WalletWithdrawalPolicy.CheckAsync(check, TeacherInInst)).CanWithdraw);
            var mixed = await WalletWithdrawalPolicy.CheckAsync(check, Teacher);
            Assert.False(mixed.CanWithdraw);
            Assert.Equal(WalletOwnerTypes.Parent, mixed.WalletType);
        }

        [Theory]
        [InlineData(Student)]
        [InlineData(Parent)]
        [InlineData(InstA)]
        public async Task WithdrawalService_RefusesSpendingWallets_WithoutReservationNorTransfer(int userId)
        {
            await Credit(userId, 5000);
            var notchPay = new Mock<INotchPayService>(MockBehavior.Strict);
            using var db = Fresh();
            var service = new WithdrawalService(db, Wallet(db), notchPay.Object, _ntfy.Object, NullLogger<WithdrawalService>.Instance);
            var ex = await Assert.ThrowsAsync<WithdrawalRejectedException>(() => service.RequestAsync(userId, "mtn", "677000001", 1000, "w-1"));
            Assert.Equal(403, ex.HttpStatus);
            Assert.Equal("not_eligible", ex.Code);
            Assert.Contains("Mobile Money", ex.Message);
            Assert.Equal(0, await db.Withdrawals.CountAsync(w => w.UserId == userId));
            Assert.Equal(5000, await Wallet(db).GetAvailableAsync(userId));
        }

        // ── Titulaire du portefeuille institution ────────────────────────

        [Fact]
        public async Task HolderChange_TransfersWholeBalance_WithTwoEntries_Traced_AndRepointsPendingItems()
        {
            await Credit(InstA, 12000, WalletOwnerTypes.Institution);
            int bookingId;
            using (var db = Fresh())
            {
                db.WalletTopUps.Add(new WalletTopUp { Id = 94501, UserId = InstA, OrderId = 94501, AmountXaf = 3000, Status = "pending" });
                var start = DateTime.UtcNow.AddDays(3);
                var booking = new TutorBooking
                {
                    TutorProfileId = TutorProfileId, StudentUserId = Pupil, PayerUserId = InstA, PayerType = TutorPayerTypes.Institution,
                    PayerInstitutionId = InstitutionId, BookedByUserId = InstB, SessionDate = DateOnly.FromDateTime(start),
                    StartTime = new TimeSpan(10, 0, 0), EndTime = new TimeSpan(11, 0, 0), PriceXaf = 4000,
                    Status = "pending_tutor_approval", PaymentStatus = "completed", PaymentMethod = TutorPaymentMethods.Wallet,
                };
                db.TutorBookings.Add(booking);
                await db.SaveChangesAsync();
                bookingId = booking.Id;
            }

            InstitutionWalletHolderChangeResult result;
            using (var db = Fresh())
                result = await Holders(db).ChangeHolderAsync(InstitutionId, InstB, Admin, "Départ de l'ancien gestionnaire", "req-1");

            Assert.False(result.AlreadyDone);
            using var check = Fresh();
            var wallet = Wallet(check);
            Assert.Equal(0, await wallet.GetAvailableAsync(InstA));
            Assert.Equal(12000, await wallet.GetAvailableAsync(InstB));
            Assert.Equal(InstB, (await check.Institutions.FirstAsync(i => i.Id == InstitutionId)).WalletOwnerUserId);

            var change = await check.InstitutionWalletHolderChanges.SingleAsync(c => c.InstitutionId == InstitutionId);
            Assert.Equal(InstA, change.OldHolderUserId);
            Assert.Equal(InstB, change.NewHolderUserId);
            Assert.Equal(Admin, change.ChangedByUserId);
            Assert.Equal(12000, change.AmountXaf);

            var entries = await check.WalletTransactions.Where(t => t.SourceType == InstitutionWalletHolderService.SourceType && t.SourceId == change.Id).ToListAsync();
            Assert.Equal(2, entries.Count);
            Assert.Contains(entries, t => t.OwnerId == InstA && t.Amount == -12000 && t.EntryType == WalletEntryTypes.InstitutionHolderTransferOut);
            Assert.Contains(entries, t => t.OwnerId == InstB && t.Amount == 12000 && t.EntryType == WalletEntryTypes.InstitutionHolderTransferIn);
            Assert.All(entries, t => Assert.Equal(WalletOwnerTypes.Institution, t.OwnerType));

            // Recharge en attente et remboursement à venir suivent le portefeuille.
            Assert.Equal(InstB, (await check.WalletTopUps.FirstAsync(t => t.Id == 94501)).UserId);
            Assert.Equal(InstB, (await check.TutorBookings.FirstAsync(b => b.Id == bookingId)).PayerUserId);

            // L'ancien titulaire n'est plus bloqué à la suppression, le nouveau l'est.
            await InstitutionWalletHolderService.EnsureNotWalletHolderAsync(check, InstA);
            await Assert.ThrowsAsync<WalletHolderDeletionBlockedException>(() => InstitutionWalletHolderService.EnsureNotWalletHolderAsync(check, InstB));
        }

        [Fact]
        public async Task HolderChange_IsIdempotent_SameRequestOrSameHolder()
        {
            await Credit(InstA, 5000, WalletOwnerTypes.Institution);
            using (var db = Fresh())
                await Holders(db).ChangeHolderAsync(InstitutionId, InstB, Admin, "Changement", "same-click");
            using (var db = Fresh())
            {
                var replay = await Holders(db).ChangeHolderAsync(InstitutionId, InstB, Admin, "Changement", "same-click");
                Assert.True(replay.AlreadyDone);
                var already = await Holders(db).ChangeHolderAsync(InstitutionId, InstB, Admin, "Encore", "other-click");
                Assert.True(already.AlreadyDone);
            }
            using var check = Fresh();
            Assert.Equal(1, await check.InstitutionWalletHolderChanges.CountAsync(c => c.InstitutionId == InstitutionId));
            Assert.Equal(5000, await Wallet(check).GetAvailableAsync(InstB));
            Assert.Equal(0, await Wallet(check).GetAvailableAsync(InstA));
        }

        [Theory]
        [InlineData(InstOther, "invalid_holder")]     // compte d'une autre institution
        [InlineData(TeacherInInst, "invalid_holder")] // compte de l'institution, mais pas un compte institution
        [InlineData(Student, "invalid_holder")]
        public async Task HolderChange_RefusesAccountsOutsideTheInstitution(int candidate, string code)
        {
            using var db = Fresh();
            var ex = await Assert.ThrowsAsync<InstitutionWalletException>(() => Holders(db).ChangeHolderAsync(InstitutionId, candidate, Admin, "Motif", null));
            Assert.Equal(code, ex.Code);
            Assert.Equal(400, ex.HttpStatus);
        }

        [Fact]
        public async Task HolderChange_RequiresReason_AndRefusesHolderWithOwnBalance()
        {
            using (var db = Fresh())
                Assert.Equal("reason_required", (await Assert.ThrowsAsync<InstitutionWalletException>(
                    () => Holders(db).ChangeHolderAsync(InstitutionId, InstB, Admin, " ", null))).Code);
            await Credit(InstC, 700, WalletOwnerTypes.Institution);
            using (var db = Fresh())
                Assert.Equal("holder_has_balance", (await Assert.ThrowsAsync<InstitutionWalletException>(
                    () => Holders(db).ChangeHolderAsync(InstitutionId, InstC, Admin, "Motif", null))).Code);
        }

        [Fact]
        public async Task HolderAccountDeletion_IsBlocked_InRepository()
        {
            using var db = Fresh();
            var repo = new UserRepository(db, NullLogger<UserRepository>.Instance);
            await Assert.ThrowsAsync<WalletHolderDeletionBlockedException>(() => repo.SoftDeleteAsync(InstA, Admin));
            await Assert.ThrowsAsync<WalletHolderDeletionBlockedException>(() => repo.HardDeleteAsync(InstA));
            Assert.False((await db.Users.IgnoreQueryFilters().FirstAsync(u => u.Id == InstA)).IsDeleted);
            Assert.True(await repo.SoftDeleteAsync(InstC, Admin)); // non titulaire : suppression normale
        }

        // ── Consentement de l'élève au rattachement ──────────────────────

        private InstitutionStudentsController ImportController(ApplicationDbContext db, int actor)
        {
            var services = new ServiceCollection().AddSingleton(_ntfy.Object).BuildServiceProvider();
            return new InstitutionStudentsController(db, NullLogger<InstitutionStudentsController>.Instance, services.GetRequiredService<IServiceScopeFactory>())
            { ControllerContext = As(actor, "institution") };
        }

        private StudentController StudentCtl(ApplicationDbContext db, int student) =>
            new(NullLogger<StudentController>.Instance, db, new Mock<IDailyScoreService>().Object, new Mock<IAssignmentService>().Object)
            { ControllerContext = As(student, "student") };

        [Fact]
        public async Task Import_CreatesPendingAttachment_StudentAccepts_ThenInstitutionCanBook()
        {
            using (var db = Fresh())
            {
                var res = await ImportController(db, InstB).Import(InstitutionId, new ImportStudentsRequest
                {
                    Rows = new List<ImportStudentRow>
                    {
                        new() { Email = $"u{Pupil}@test.local", Group = "Tle D" },
                        new() { Email = $"u{Pupil}@test.local", Group = "Tle D" }, // même e-mail deux fois : un seul rattachement
                    },
                });
                Assert.IsType<OkObjectResult>(res);
            }

            int linkId;
            using (var db = Fresh())
            {
                var link = await db.InstitutionStudents.SingleAsync(s => s.InstitutionId == InstitutionId && s.StudentId == Pupil);
                Assert.Equal(InstitutionAttachmentStatuses.Pending, link.ConfirmationStatus);
                linkId = link.Id;
                // En attente : l'institution ne peut ni réserver ni attribuer.
                var resolver = new TutorBookingPartyResolver(db);
                await Assert.ThrowsAsync<TutorBookingForbiddenException>(() => resolver.ResolveAsync(InstB, "institution", new[] { Pupil }, null, null, TutorProfileId));
                await Assert.ThrowsAsync<TutorBookingForbiddenException>(() => resolver.ResolveAssignmentBeneficiariesAsync(InstB, new[] { Pupil }, null));
                var ctx = await resolver.GetContextAsync(InstB);
                Assert.DoesNotContain(ctx.InstitutionStudents, s => s.UserId == Pupil);
                Assert.Equal(1, ctx.InstitutionPendingConfirmationCount);
            }

            // Un autre élève ne peut pas répondre à sa place.
            using (var db = Fresh())
                Assert.IsType<NotFoundObjectResult>(await StudentCtl(db, Student).AcceptInstitutionAttachment(linkId));

            using (var db = Fresh())
            {
                var list = Assert.IsType<OkObjectResult>(await StudentCtl(db, Pupil).GetInstitutionAttachments());
                Assert.NotNull(list.Value);
                Assert.IsType<OkObjectResult>(await StudentCtl(db, Pupil).AcceptInstitutionAttachment(linkId));
            }

            using (var db = Fresh())
            {
                var parties = await new TutorBookingPartyResolver(db).ResolveAsync(InstB, "institution", new[] { Pupil }, null, null, TutorProfileId);
                Assert.Equal(new[] { Pupil }, parties.ParticipantIds);
                Assert.Equal(InstA, parties.PayerUserId);
            }

            // Refus ultérieur : le tutorat est de nouveau bloqué, le rattachement reste.
            using (var db = Fresh())
                Assert.IsType<OkObjectResult>(await StudentCtl(db, Pupil).RefuseInstitutionAttachment(linkId));
            using (var db = Fresh())
            {
                await Assert.ThrowsAsync<TutorBookingForbiddenException>(() =>
                    new TutorBookingPartyResolver(db).ResolveAsync(InstB, "institution", new[] { Pupil }, null, null, TutorProfileId));
                Assert.True((await db.InstitutionStudents.FirstAsync(s => s.Id == linkId)).IsActive);
            }
        }
    }
}
