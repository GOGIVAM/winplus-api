using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Backend.Data;
using Backend.Models.DTOs;
using Backend.Models.Entities;
using Backend.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Backend.Tests
{
    /// <summary>Stockage privé en mémoire : ce que les services y écrivent, et ce qu'ils suppriment.</summary>
    internal sealed class InMemoryPrivateFileStore : IPrivateFileStore
    {
        public readonly Dictionary<string, byte[]> Files = new();

        public async Task<string> PutAsync(Stream content, string key, string? contentType, CancellationToken ct = default)
        {
            using var ms = new MemoryStream();
            await content.CopyToAsync(ms, ct);
            var reference = PrivateFileStore.S3Prefix + PrivateFileStore.NormalizeKey(key);
            Files[reference] = ms.ToArray();
            return reference;
        }

        public string? GetTemporaryUrl(string reference, TimeSpan ttl) => $"https://signed.example/{reference}?ttl={ttl.TotalMinutes}";
        public Task<(Stream Content, string ContentType)?> OpenLocalAsync(string reference, CancellationToken ct = default) =>
            Task.FromResult<(Stream, string)?>(null);
        public Task<bool> DeleteAsync(string reference, CancellationToken ct = default) => Task.FromResult(Files.Remove(reference) || true);
    }

    /// <summary>
    /// Lot 9.5 (suivi 17.11, 17.12, 17.16) sur un vrai PostgreSQL, schéma
    /// produit par le script SQL manuel : présentiel refusé à un tuteur non
    /// vérifié (devis, réservation, engagement, réglages, recherche, profil
    /// public), trois voies de vérification, entretien réussi sans identité =
    /// non vérifié, révocation de la caution et du rattachement, un avis par
    /// séance et par personne, droit de noter, suspension automatique au 4e
    /// signalement en 30 jours, levée par l'admin.
    /// </summary>
    public class TutorVerification95Tests : IClassFixture<PostgresTestDatabase>
    {
        private readonly PostgresTestDatabase _pg;
        private readonly Mock<INtfyService> _ntfy = new();
        private readonly Mock<INotchPayService> _notchPay = new(MockBehavior.Strict);
        private readonly InMemoryPrivateFileStore _files = new();
        private static readonly IConfiguration Config = new ConfigurationBuilder().Build();
        private static readonly byte[] Jpeg = { 0xFF, 0xD8, 0xFF, 0xE0, 0, 16, (byte)'J', (byte)'F', (byte)'I', (byte)'F', 0, 1, 1, 1 };

        public TutorVerification95Tests(PostgresTestDatabase pg) => _pg = pg;

        private sealed record World(int Tutor, int ProfileId, int Admin, int[] Students, int Parent, int Child, int Stranger,
            int InstitutionAccount, int InstitutionId, int OtherInstitutionAccount, DateOnly Day);

        private async Task<World> SeedAsync()
        {
            var tag = Guid.NewGuid().ToString("N")[..8];
            await using var db = _pg.CreateContext();
            var inst = new Institution { Name = $"Lycée Caution {tag}", Country = "CM" };
            var other = new Institution { Name = $"Collège Autre {tag}", Country = "CM" };
            db.Institutions.AddRange(inst, other);
            await db.SaveChangesAsync();

            User U(string name, string role, int? institution = null) => new()
            {
                Email = $"{name}-{tag}@it.local", Role = role, IsActive = true, FirstName = name, LastName = "IT", InstitutionId = institution,
            };
            var tutor = U("tuteur", "teacher");
            var admin = U("admin", "admin");
            var students = Enumerable.Range(1, 5).Select(i => U($"eleve{i}", "student")).ToList();
            var parent = U("parent", "parent");
            var child = U("enfant", "student");
            var stranger = U("inconnu", "student");
            var instAccount = U("institution", "institution", inst.Id);
            var otherAccount = U("autreinst", "institution", other.Id);
            db.Users.AddRange(new[] { tutor, admin, parent, child, stranger, instAccount, otherAccount }.Concat(students));
            await db.SaveChangesAsync();

            var profile = new TutorProfile
            {
                UserId = tutor.Id, IsActive = true, HourlyRateXaf = 6000, NoticeHours = 0,
                // Réglages présentiels déjà enregistrés avant le 9.5 : sans vérification, ils ne comptent plus.
                OffersOnline = true, OffersAtStudentHome = true,
                Subjects = new List<TutorSubject> { new() { Subject = "Maths" } },
                Levels = new List<TutorLevel> { new() { Level = "Terminale" } },
                AvailabilitySlots = Enumerable.Range(0, 7)
                    .Select(d => new TutorAvailabilitySlot { DayOfWeek = d, StartTime = new TimeSpan(8, 0, 0), EndTime = new TimeSpan(20, 0, 0) }).ToList(),
                Packages = new List<TutorPackage> { new() { Name = "Engagement mensuel", Kind = TutorPackageKinds.Monthly, IsActive = true, DiscountPercent = 0 } },
            };
            db.TutorProfiles.Add(profile);
            db.ParentStudentLinks.Add(new ParentStudentLink { ParentId = parent.Id, StudentId = child.Id, Status = "accepted", InitiatedBy = parent.Id });
            db.InstitutionTeacherLinks.Add(new InstitutionTeacherLink { InstitutionId = inst.Id, TeacherId = tutor.Id, Status = "accepted", InitiatedBy = tutor.Id });
            await db.SaveChangesAsync();

            var day = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(3));
            return new World(tutor.Id, profile.Id, admin.Id, students.Select(s => s.Id).ToArray(), parent.Id, child.Id, stranger.Id,
                instAccount.Id, inst.Id, otherAccount.Id, day);
        }

        private async Task CreditAsync(int owner, decimal amount)
        {
            await using var db = _pg.CreateContext();
            await new WalletService(db, NullLogger<WalletService>.Instance)
                .PostAsync(new WalletEntry(owner, WalletEntryTypes.Recharge, amount, $"it-seed-{owner}-{Guid.NewGuid()}", "Recharge de test"));
        }

        private TutorVerificationService Verification(ApplicationDbContext db) =>
            new(db, _files, _ntfy.Object, NullLogger<TutorVerificationService>.Instance, Config);

        private TutorReportService Reports(ApplicationDbContext db) =>
            new(db, _ntfy.Object, Verification(db), NullLogger<TutorReportService>.Instance, Config,
                new TutorRefundService(db, new WalletService(db, NullLogger<WalletService>.Instance), _ntfy.Object, NullLogger<TutorRefundService>.Instance, Config));

        private TutorBookingService Bookings(ApplicationDbContext db)
        {
            var wallet = new WalletService(db, NullLogger<WalletService>.Instance);
            var refunds = new TutorRefundService(db, wallet, _ntfy.Object, NullLogger<TutorRefundService>.Instance, Config);
            return new TutorBookingService(db, _notchPay.Object, _ntfy.Object, NullLogger<TutorBookingService>.Instance, wallet, Config, refunds);
        }

        private TutorEngagementService Engagements(ApplicationDbContext db)
        {
            var wallet = new WalletService(db, NullLogger<WalletService>.Instance);
            var refunds = new TutorRefundService(db, wallet, _ntfy.Object, NullLogger<TutorRefundService>.Instance, Config);
            return new TutorEngagementService(db, _notchPay.Object, _ntfy.Object, NullLogger<TutorEngagementService>.Instance, wallet, Config, refunds);
        }

        private static CreateTutorBookingRequestDto Booking(World w, string mode, string start = "10:00") => new()
        {
            TutorUserId = w.Tutor, SessionDate = w.Day, StartTime = start, DurationMinutes = 60, Mode = mode,
            Subject = "Maths", PaymentMethod = TutorPaymentMethods.Wallet,
        };

        private static TutorIdentityUpload File(string name) => new(new MemoryStream(Jpeg), name, "image/jpeg", Jpeg.Length);

        private async Task<int> SubmitAndApproveIdentityAsync(World w)
        {
            await using (var db = _pg.CreateContext())
                await Verification(db).SubmitIdentityAsync(w.Tutor, "cni", File("recto.jpg"), null, File("selfie.jpg"));
            await using (var db = _pg.CreateContext())
            {
                var id = await db.TutorIdentityChecks.Where(c => c.TutorProfileId == w.ProfileId && c.Status == "pending").Select(c => c.Id).SingleAsync();
                await Verification(db).ReviewIdentityAsync(w.Admin, id, true, null);
                return id;
            }
        }

        private async Task<TutorProfile> ProfileAsync(World w)
        {
            await using var db = _pg.CreateContext();
            return await db.TutorProfiles.AsNoTracking().SingleAsync(p => p.Id == w.ProfileId);
        }

        // ── 17.11 : présentiel refusé à un tuteur non vérifié ─────────────────

        [PostgresFact]
        public async Task UnverifiedTutor_InPersonRefused_Everywhere_OnlineStillPossible()
        {
            var w = await SeedAsync();
            await CreditAsync(w.Students[0], 50000);
            var profiles = (ApplicationDbContext db) => new TutorProfileService(db, NullLogger<TutorProfileService>.Instance);

            await using (var db = _pg.CreateContext())
            {
                // Devis et réservation présentiels refusés ; devis en ligne accepté.
                await Assert.ThrowsAsync<InvalidOperationException>(() => Bookings(db).GetQuoteAsync(new TutorPriceQuoteRequestDto
                { TutorUserId = w.Tutor, SessionDate = w.Day, StartTime = "10:00", DurationMinutes = 60, Mode = "student_home" }));
                var online = await Bookings(db).GetQuoteAsync(new TutorPriceQuoteRequestDto
                { TutorUserId = w.Tutor, SessionDate = w.Day, StartTime = "10:00", DurationMinutes = 60, Mode = "online" });
                Assert.True(online.IsAvailable);
                await Assert.ThrowsAsync<InvalidOperationException>(() => Bookings(db).CreateBookingAsync(w.Students[0], Booking(w, "neutral_place")));
            }
            await using (var db = _pg.CreateContext())
            {
                // Engagement mensuel présentiel : devis et création refusés.
                var slots = new List<TutorEngagementSlotDto> { new() { DayOfWeek = 2, StartTime = "18:00", DurationMinutes = 60 } };
                var start = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(7));
                await Assert.ThrowsAsync<InvalidOperationException>(() => Engagements(db).GetQuoteAsync(new TutorEngagementQuoteRequestDto
                { TutorUserId = w.Tutor, StartDate = start, Mode = "tutor_home", Slots = slots }));
                await Assert.ThrowsAsync<InvalidOperationException>(() => Engagements(db).CreateAsync(w.Students[0], new CreateTutorEngagementRequestDto
                { TutorUserId = w.Tutor, StartDate = start, Mode = "student_home", Slots = slots, PaymentMethod = TutorPaymentMethods.Wallet }));
            }
            await using (var db = _pg.CreateContext())
            {
                // 17.17 : enregistrer un mode présentiel est accepté (conservé, inactif).
                var saved = await profiles(db).UpdateAsync(w.Tutor, new UpdateTutorProfileRequestDto { OffersNeutralPlace = true });
                Assert.True(saved.SavedOffersNeutralPlace);
                Assert.False(saved.OffersNeutralPlace);
            }
            await using (var db = _pg.CreateContext())
            {
                var mine = await profiles(db).UpdateAsync(w.Tutor, new UpdateTutorProfileRequestDto { Title = "Prof de maths", OffersNeutralPlace = false });
                Assert.False(mine.InPersonAllowed);
                Assert.NotNull(mine.InPersonBlockedReason);
                // Affichage public : le mode présentiel enregistré n'est pas affiché.
                var pub = await profiles(db).GetPublicProfileAsync(w.Tutor);
                Assert.False(pub!.OffersAtStudentHome);
                Assert.False(pub.IsInPersonVerified);
                var search = await profiles(db).SearchAsync(null, null, null, false, 1, 50, mode: "student_home");
                Assert.DoesNotContain(search, r => r.UserId == w.Tutor);
            }
            await using (var db = _pg.CreateContext())
            {
                var created = await Bookings(db).CreateBookingAsync(w.Students[0], Booking(w, "online"));
                Assert.Equal("online", created.Booking.Mode);
            }
            await using (var check = _pg.CreateContext())
                Assert.False(await check.TutorBookings.AnyAsync(b => b.TutorProfileId == w.ProfileId && b.Mode != "online"));
        }

        // ── Voie 1 : pièce d'identité + selfie ────────────────────────────────

        [PostgresFact]
        public async Task IdentityRoute_RejectThenApprove_VerifiesAndOpensInPerson()
        {
            var w = await SeedAsync();
            await CreditAsync(w.Students[0], 50000);

            int firstId;
            await using (var db = _pg.CreateContext())
            {
                var status = await Verification(db).SubmitIdentityAsync(w.Tutor, "cni", File("recto.jpg"), File("verso.jpg"), File("selfie.jpg"));
                Assert.False(status.IsVerified);
                Assert.Equal("pending", status.LatestIdentity!.Status);
                firstId = status.LatestIdentity.Id;
            }
            // Fichiers privés : références « s3:private/… », jamais d'URL publique en base ; lecture admin par URL signée.
            await using (var db = _pg.CreateContext())
            {
                var check = await db.TutorIdentityChecks.AsNoTracking().SingleAsync(c => c.Id == firstId);
                Assert.StartsWith("s3:private/tutor-identity/", check.FrontFileRef);
                Assert.True(_files.Files.ContainsKey(check.SelfieFileRef!));
                var urls = await Verification(db).GetIdentityFilesAsync(firstId, "/api/admin/x");
                Assert.StartsWith("https://signed.example/", urls.SelfieUrl);
                // Un fichier qui n'est pas une image est refusé (signature contrôlée).
                await Assert.ThrowsAsync<InvalidOperationException>(() => Verification(db).SubmitIdentityAsync(w.Tutor, "cni",
                    new TutorIdentityUpload(new MemoryStream(new byte[] { 1, 2, 3, 4, 5 }), "faux.jpg", "image/jpeg", 5), null, File("selfie.jpg")));
            }
            await using (var db = _pg.CreateContext())
            {
                await Assert.ThrowsAsync<InvalidOperationException>(() => Verification(db).ReviewIdentityAsync(w.Admin, firstId, false, " "));
                await Verification(db).ReviewIdentityAsync(w.Admin, firstId, false, "Photo floue");
            }
            await using (var db = _pg.CreateContext())
            {
                var status = await Verification(db).GetStatusForTutorAsync(w.Tutor);
                Assert.False(status.IsVerified);
                Assert.Equal("rejected", status.LatestIdentity!.Status);
                Assert.Equal("Photo floue", status.LatestIdentity.RejectionReason);
                Assert.Contains(status.History, h => h.Category == "identity" && h.Action == "rejected" && h.Reason == "Photo floue");
            }

            await SubmitAndApproveIdentityAsync(w);
            var profile = await ProfileAsync(w);
            Assert.NotNull(profile.InPersonVerifiedAt);
            Assert.Equal("identity", profile.InPersonVerificationRoutes);

            await using (var db = _pg.CreateContext())
            {
                var created = await Bookings(db).CreateBookingAsync(w.Students[0], Booking(w, "student_home"));
                Assert.Equal("student_home", created.Booking.Mode);
                var profiles = new TutorProfileService(db, NullLogger<TutorProfileService>.Instance);
                Assert.True((await profiles.GetPublicProfileAsync(w.Tutor))!.OffersAtStudentHome);
                Assert.Contains(await profiles.SearchAsync(null, null, null, false, 1, 50, mode: "student_home"), r => r.UserId == w.Tutor && r.IsInPersonVerified);
            }

            // Règle de conservation : fichiers supprimés 30 jours après la validation, la validation reste.
            await using (var db = _pg.CreateContext())
                Assert.True(await Verification(db).PurgeExpiredIdentityFilesAsync(DateTime.UtcNow.AddDays(31)) >= 2);
            await using (var db = _pg.CreateContext())
            {
                Assert.All(await db.TutorIdentityChecks.AsNoTracking().Where(c => c.TutorProfileId == w.ProfileId && c.Status != "superseded").ToListAsync(),
                    c => { Assert.NotNull(c.FilesPurgedAt); Assert.Null(c.FrontFileRef); Assert.Null(c.SelfieFileRef); });
                Assert.NotNull((await db.TutorProfiles.AsNoTracking().SingleAsync(p => p.Id == w.ProfileId)).InPersonVerifiedAt);
            }
        }

        // ── Voie 2 : entretien (ne remplace pas l'identité) ──────────────────

        [PostgresFact]
        public async Task InterviewPassed_WithoutValidatedIdentity_DoesNotVerify_ThenIdentityApproved_Verifies()
        {
            var w = await SeedAsync();
            int interviewId;
            await using (var db = _pg.CreateContext())
            {
                await Assert.ThrowsAsync<InvalidOperationException>(() => Verification(db).RequestInterviewAsync(w.Tutor,
                    new RequestTutorInterviewDto { Format = "in_person", Availability = "Samedi matin" })); // ville manquante
                var status = await Verification(db).RequestInterviewAsync(w.Tutor,
                    new RequestTutorInterviewDto { Format = "video", Availability = "Mardi et jeudi après 17 h" });
                interviewId = status.LatestInterview!.Id;
                await Assert.ThrowsAsync<InvalidOperationException>(() => Verification(db).RequestInterviewAsync(w.Tutor,
                    new RequestTutorInterviewDto { Format = "video", Availability = "Lundi 18 h" })); // déjà une demande
            }
            await using (var db = _pg.CreateContext())
            {
                await Assert.ThrowsAsync<InvalidOperationException>(() => Verification(db).CompleteInterviewAsync(w.Admin, interviewId,
                    new CompleteInterviewDto { Outcome = "passed", FinalReport = new string('x', 40) })); // pas encore planifié
                await Verification(db).ScheduleInterviewAsync(w.Admin, interviewId,
                    new ScheduleInterviewDto { ScheduledAt = DateTime.UtcNow.AddDays(1), MeetingUrl = "https://meet.example/abc" });
                await Verification(db).SaveInterviewNotesAsync(w.Admin, interviewId, new SaveInterviewNotesDto { Notes = "Bonne maîtrise des suites.", ScoresJson = "{\"mastery\":4}" });
                // L'agent joint la pièce et le selfie pris pendant l'entretien.
                await Verification(db).SubmitIdentityForInterviewAsync(w.Admin, interviewId, "passport", File("passeport.jpg"), null, File("selfie.jpg"));
            }
            await using (var db = _pg.CreateContext())
            {
                var done = await Verification(db).CompleteInterviewAsync(w.Admin, interviewId,
                    new CompleteInterviewDto { Outcome = "passed", FinalReport = "Entretien concluant : maîtrise, pédagogie et règles de sécurité connues." });
                Assert.Equal("passed", done.Status);
                Assert.Equal("pending", done.Identity!.Status);
                Assert.False(done.HasApprovedIdentity);
            }
            // Entretien réussi, identité pas encore validée : NON vérifié.
            Assert.Null((await ProfileAsync(w)).InPersonVerifiedAt);
            await using (var db = _pg.CreateContext())
                await Assert.ThrowsAsync<InvalidOperationException>(() => Bookings(db).CreateBookingAsync(w.Students[0], Booking(w, "student_home")));

            await using (var db = _pg.CreateContext())
            {
                var id = await db.TutorIdentityChecks.Where(c => c.TutorVerificationInterviewId == interviewId).Select(c => c.Id).SingleAsync();
                var check = await db.TutorIdentityChecks.AsNoTracking().SingleAsync(c => c.Id == id);
                Assert.Equal("interview", check.Source);
                Assert.Equal(w.Admin, check.SubmittedByUserId);
                await Verification(db).ReviewIdentityAsync(w.Admin, id, true, null);
            }
            var profile = await ProfileAsync(w);
            Assert.NotNull(profile.InPersonVerifiedAt);
            Assert.Equal("identity,interview", profile.InPersonVerificationRoutes);
        }

        // ── Voie 3 : caution d'institution, révocations ──────────────────────

        [PostgresFact]
        public async Task InstitutionEndorsement_Verifies_RevocationRemovesOnlyThatRoute()
        {
            var w = await SeedAsync();
            var request = new CreateTutorEndorsementDto
            {
                TutorUserId = w.Tutor, Statement = "Entretien avec le proviseur et vérification de la CNI en mains propres.",
                VerifierName = "M. Proviseur", VerifiedOn = DateOnly.FromDateTime(DateTime.UtcNow),
            };
            await using (var db = _pg.CreateContext())
            {
                // Institution sans rattachement avec ce tuteur : refusé.
                await Assert.ThrowsAsync<InvalidOperationException>(() => Verification(db).EndorseAsync(w.OtherInstitutionAccount, request));
                // Un élève ne peut pas cautionner.
                await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Verification(db).EndorseAsync(w.Students[0], request));
            }
            int endorsementId;
            await using (var db = _pg.CreateContext())
                endorsementId = (await Verification(db).EndorseAsync(w.InstitutionAccount, request)).Id;
            var profile = await ProfileAsync(w);
            Assert.NotNull(profile.InPersonVerifiedAt);
            Assert.Equal("institution", profile.InPersonVerificationRoutes);
            await using (var db = _pg.CreateContext())
            {
                var pub = await new TutorProfileService(db, NullLogger<TutorProfileService>.Instance).GetPublicProfileAsync(w.Tutor);
                Assert.Single(pub!.EndorsedByInstitutions);
                // Une autre institution ne peut pas révoquer cette caution.
                await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Verification(db).RevokeEndorsementAsync(w.OtherInstitutionAccount, endorsementId, "x"));
            }

            // Révocation de la caution : vérification retirée immédiatement.
            await using (var db = _pg.CreateContext())
                await Verification(db).RevokeEndorsementAsync(w.InstitutionAccount, endorsementId, "Fin de collaboration");
            profile = await ProfileAsync(w);
            Assert.Null(profile.InPersonVerifiedAt);
            Assert.Null(profile.InPersonVerificationRoutes);

            // Nouvelle caution + identité validée, puis révocation du RATTACHEMENT :
            // seule la voie « institution » tombe, la vérification par identité reste.
            await using (var db = _pg.CreateContext())
                await Verification(db).EndorseAsync(w.InstitutionAccount, request);
            await SubmitAndApproveIdentityAsync(w);
            Assert.Equal("identity,institution", (await ProfileAsync(w)).InPersonVerificationRoutes);
            await using (var db = _pg.CreateContext())
            {
                var link = await db.InstitutionTeacherLinks.SingleAsync(l => l.InstitutionId == w.InstitutionId && l.TeacherId == w.Tutor);
                db.InstitutionTeacherLinks.Remove(link);
                await db.SaveChangesAsync();
                await Verification(db).OnAffiliationEndedAsync(w.InstitutionId, w.Tutor, w.InstitutionAccount, "institution");
            }
            profile = await ProfileAsync(w);
            Assert.NotNull(profile.InPersonVerifiedAt);
            Assert.Equal("identity", profile.InPersonVerificationRoutes);
            await using (var db = _pg.CreateContext())
            {
                Assert.False(await db.TutorInstitutionEndorsements.AnyAsync(e => e.TutorProfileId == w.ProfileId && e.Status == "active"));
                var history = await Verification(db).GetHistoryAsync(w.ProfileId, forTutor: false);
                Assert.Contains(history, h => h.Category == "institution" && h.Action == "revoked" && h.ActorUserId == w.InstitutionAccount);
            }
        }

        // ── 17.12 / 17.16 : avis ──────────────────────────────────────────────

        private async Task<int> SeedBookingAsync(World w, int participant, string status, string mode = "online", int? payer = null, string? payerType = null, int dayOffset = -2)
        {
            await using var db = _pg.CreateContext();
            var booking = new TutorBooking
            {
                TutorProfileId = w.ProfileId, StudentUserId = participant,
                SessionDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(dayOffset)), StartTime = new TimeSpan(10, 0, 0), EndTime = new TimeSpan(11, 0, 0),
                Mode = mode, PriceXaf = 6000, Status = status, PaymentStatus = "completed", PaymentMethod = TutorPaymentMethods.Wallet,
                PayerUserId = payer ?? participant, PayerType = payerType ?? TutorPayerTypes.Student, BookedByUserId = payer ?? participant,
                Participants = new List<TutorBookingParticipant> { new() { StudentUserId = participant } },
            };
            db.TutorBookings.Add(booking);
            await db.SaveChangesAsync();
            return booking.Id;
        }

        [PostgresFact]
        public async Task Reviews_OnePerSessionAndPerson_ParticipantAndPayerOnly()
        {
            var w = await SeedAsync();
            var bookingId = await SeedBookingAsync(w, w.Child, "completed", payer: w.Parent, payerType: TutorPayerTypes.Parent);
            var cancelledId = await SeedBookingAsync(w, w.Students[0], "cancelled");

            await using (var db = _pg.CreateContext())
            {
                var reviews = new TutorReviewService(db, _ntfy.Object);
                var child = await reviews.SubmitAsync(w.Child, new SubmitTutorReviewRequestDto { TutorBookingId = bookingId, Rating = 5, Comment = "Super" });
                Assert.Equal("participant", child.ReviewerRole);
                var parent = await reviews.SubmitAsync(w.Parent, new SubmitTutorReviewRequestDto { TutorBookingId = bookingId, Rating = 3 });
                Assert.Equal("payer", parent.ReviewerRole);
            }
            await using (var db = _pg.CreateContext())
            {
                var reviews = new TutorReviewService(db, _ntfy.Object);
                // Second avis de la même personne sur la même séance : refusé.
                await Assert.ThrowsAsync<InvalidOperationException>(() => reviews.SubmitAsync(w.Child, new SubmitTutorReviewRequestDto { TutorBookingId = bookingId, Rating = 1 }));
                // Hors participants et payeur : refusé ; le tuteur non plus.
                await Assert.ThrowsAsync<InvalidOperationException>(() => reviews.SubmitAsync(w.Stranger, new SubmitTutorReviewRequestDto { TutorBookingId = bookingId, Rating = 1 }));
                await Assert.ThrowsAsync<InvalidOperationException>(() => reviews.SubmitAsync(w.Tutor, new SubmitTutorReviewRequestDto { TutorBookingId = bookingId, Rating = 5 }));
                // Séance non effectuée : pas d'avis.
                await Assert.ThrowsAsync<InvalidOperationException>(() => reviews.SubmitAsync(w.Students[0], new SubmitTutorReviewRequestDto { TutorBookingId = cancelledId, Rating = 1 }));
            }
            await using (var db = _pg.CreateContext())
            {
                // Index unique séance + auteur en base (le script a remplacé l'ancien index « un avis par séance »).
                db.TutorReviews.Add(new TutorReview { TutorBookingId = bookingId, TutorProfileId = w.ProfileId, StudentUserId = w.Child, Rating = 2 });
                await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
            }
            await using (var db = _pg.CreateContext())
            {
                var pub = await new TutorProfileService(db, NullLogger<TutorProfileService>.Instance).GetPublicProfileAsync(w.Tutor);
                Assert.Equal(2, pub!.ReviewCount);
                Assert.Equal(4.0, pub.AverageRating);
                var parentView = await Bookings(db).GetByIdAsync(w.Parent, bookingId);
                Assert.False(parentView.CanReview);
                Assert.Equal(3, parentView.MyReviewRating);
                Assert.True(parentView.CanReport);
            }
        }

        // ── 17.12 : signalements et suspension automatique ────────────────────

        [PostgresFact]
        public async Task FourthReportIn30Days_SuspendsInPerson_OnlineStays_AdminLifts()
        {
            var w = await SeedAsync();
            await SubmitAndApproveIdentityAsync(w);
            await CreditAsync(w.Students[4], 50000);
            var bookings = new List<int>
            {
                await SeedBookingAsync(w, w.Students[0], "completed"),
                await SeedBookingAsync(w, w.Students[1], "cancelled"),   // séance annulée : signalable
                await SeedBookingAsync(w, w.Students[2], "confirmed", dayOffset: 0), // séance en cours : signalable
                await SeedBookingAsync(w, w.Students[3], "completed"),
            };
            var upcomingInPerson = await SeedBookingAsync(w, w.Students[4], "confirmed", mode: "student_home", dayOffset: 5);

            await using (var db = _pg.CreateContext())
            {
                await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Reports(db).SubmitAsync(w.Stranger,
                    new CreateTutorReportDto { TutorBookingId = bookings[0], Reason = TutorReportReasons.NoShow }));
                await Assert.ThrowsAsync<InvalidOperationException>(() => Reports(db).SubmitAsync(w.Students[0],
                    new CreateTutorReportDto { TutorBookingId = bookings[0], Reason = "inconnu" }));
            }
            for (var i = 0; i < 3; i++)
            {
                await using var db = _pg.CreateContext();
                await Reports(db).SubmitAsync(w.Students[i], new CreateTutorReportDto { TutorBookingId = bookings[i], Reason = TutorReportReasons.InappropriateBehavior, Description = "Propos déplacés" });
            }
            // 3 signalements : seuil atteint mais pas dépassé.
            Assert.Null((await ProfileAsync(w)).InPersonSuspendedAt);
            await using (var db = _pg.CreateContext())
                await Assert.ThrowsAsync<InvalidOperationException>(() => Reports(db).SubmitAsync(w.Students[0],
                    new CreateTutorReportDto { TutorBookingId = bookings[0], Reason = TutorReportReasons.NoShow })); // un seul par séance et par personne

            await using (var db = _pg.CreateContext())
                await Reports(db).SubmitAsync(w.Students[3], new CreateTutorReportDto { TutorBookingId = bookings[3], Reason = TutorReportReasons.SafetyConcern });
            var profile = await ProfileAsync(w);
            Assert.NotNull(profile.InPersonSuspendedAt);
            Assert.NotNull(profile.InPersonVerifiedAt); // toujours vérifié, mais suspendu
            _ntfy.Verify(n => n.PublishAdminAsync(It.Is<string>(t => t.StartsWith("Signalement")), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string[]>(), It.IsAny<string>(), "TutorReport", It.IsAny<int?>()), Times.Exactly(4));
            _ntfy.Verify(n => n.PublishAdminAsync("Présentiel suspendu automatiquement", It.Is<string>(m => m.Contains($"#{upcomingInPerson}")),
                It.IsAny<string>(), It.IsAny<string[]>(), It.IsAny<string>(), "TutorProfile", w.ProfileId), Times.Once);

            await using (var db = _pg.CreateContext())
            {
                // Présentiel refusé, en ligne possible ; la séance présentielle déjà réservée n'est pas annulée.
                await Assert.ThrowsAsync<InvalidOperationException>(() => Bookings(db).CreateBookingAsync(w.Students[4], Booking(w, "student_home", "14:00")));
                var online = await Bookings(db).CreateBookingAsync(w.Students[4], Booking(w, "online", "15:00"));
                Assert.Equal("online", online.Booking.Mode);
                Assert.Equal("confirmed", (await db.TutorBookings.AsNoTracking().SingleAsync(b => b.Id == upcomingInPerson)).Status);
                var suspended = await Reports(db).GetSuspendedAsync();
                var mine = Assert.Single(suspended, s => s.Tutor.TutorProfileId == w.ProfileId);
                Assert.Equal(4, mine.ReportsInWindow);
                Assert.Contains(mine.UpcomingInPersonBookings, b => b.BookingId == upcomingInPerson);
                Assert.Contains((await Verification(db).GetInPersonControlAsync()).Bookings, b => b.BookingId == upcomingInPerson);
            }

            await using (var db = _pg.CreateContext())
            {
                await Assert.ThrowsAsync<InvalidOperationException>(() => Reports(db).LiftSuspensionAsync(w.Admin, w.ProfileId, ""));
                var file = await Reports(db).LiftSuspensionAsync(w.Admin, w.ProfileId, "Signalements examinés : malentendu avec une famille.");
                Assert.Null(file.SuspendedAt);
                Assert.All(file.Reports, r => Assert.Equal("reviewed", r.Status));
                Assert.Contains(file.History, h => h.Action == "lifted" && h.ActorUserId == w.Admin);
                Assert.Contains(file.History, h => h.Action == "suspended");
            }
            Assert.True(TutorInPersonPolicy.IsEligible(await ProfileAsync(w)));
            await using (var db = _pg.CreateContext())
            {
                var created = await Bookings(db).CreateBookingAsync(w.Students[4], Booking(w, "student_home", "16:00"));
                Assert.Equal("student_home", created.Booking.Mode);
                // Le tuteur ne voit pas les signalements dans son historique, seulement la suspension et sa levée.
                var history = await Verification(db).GetHistoryAsync(w.ProfileId, forTutor: true);
                Assert.DoesNotContain(history, h => h.Category == "report");
                Assert.Contains(history, h => h.Action == "suspended");
            }
        }

        // ── 17.17 : réglages présentiels conservés, puis réactivés ───────────

        [PostgresFact]
        public async Task InPersonSettings_KeptWhileUnverified_ReactivatedOnVerification()
        {
            var w = await SeedAsync();
            var profiles = (ApplicationDbContext db) => new TutorProfileService(db, NullLogger<TutorProfileService>.Instance);

            await using (var db = _pg.CreateContext())
            {
                // Étape « Intervention » enregistrée par un tuteur non vérifié : aucun refus, choix conservés.
                var mine = await profiles(db).UpdateAsync(w.Tutor, new UpdateTutorProfileRequestDto
                {
                    OffersOnline = true, OffersAtStudentHome = true, OffersAtTutorHome = true, OffersNeutralPlace = false,
                    TutorHomeAddressHint = "Bastos",
                });
                Assert.True(mine.SavedOffersAtStudentHome);
                Assert.True(mine.SavedOffersAtTutorHome);
                Assert.False(mine.SavedOffersNeutralPlace);
                // Inactifs : modes effectifs à « non », présentiel indisponible.
                Assert.False(mine.OffersAtStudentHome);
                Assert.False(mine.OffersAtTutorHome);
                Assert.False(mine.InPersonAllowed);
            }
            var stored = await ProfileAsync(w);
            Assert.True(stored.OffersAtStudentHome);
            Assert.True(stored.OffersAtTutorHome);

            await using (var db = _pg.CreateContext())
            {
                // Ni affichés publiquement, ni proposés en recherche, ni réservables.
                var pub = await profiles(db).GetPublicProfileAsync(w.Tutor);
                Assert.False(pub!.OffersAtStudentHome);
                Assert.False(pub.OffersAtTutorHome);
                Assert.Null(pub.SavedOffersAtStudentHome);
                Assert.Null(pub.TutorHomeAddressHint);
                Assert.DoesNotContain(await profiles(db).SearchAsync(null, null, null, false, 1, 50, mode: "tutor_home"), r => r.UserId == w.Tutor);
                Assert.DoesNotContain(await profiles(db).SearchAsync(null, null, null, false, 1, 50), r => r.UserId == w.Tutor && r.OffersInPerson);
                await Assert.ThrowsAsync<InvalidOperationException>(() => Bookings(db).GetQuoteAsync(new TutorPriceQuoteRequestDto
                { TutorUserId = w.Tutor, SessionDate = w.Day, StartTime = "10:00", DurationMinutes = 60, Mode = "tutor_home" }));
            }
            // Un nouvel enregistrement des réglages sans ces champs ne les efface pas.
            await using (var db = _pg.CreateContext())
                await profiles(db).UpdateAsync(w.Tutor, new UpdateTutorProfileRequestDto { Title = "Maths en Terminale" });
            Assert.True((await ProfileAsync(w)).OffersAtTutorHome);

            // Vérification : les choix conservés redeviennent actifs d'eux-mêmes.
            await SubmitAndApproveIdentityAsync(w);
            await using (var db = _pg.CreateContext())
            {
                var pub = await profiles(db).GetPublicProfileAsync(w.Tutor);
                Assert.True(pub!.OffersAtStudentHome);
                Assert.True(pub.OffersAtTutorHome);
                Assert.False(pub.OffersNeutralPlace);
                Assert.Equal("Bastos", pub.TutorHomeAddressHint);
                Assert.Contains(await profiles(db).SearchAsync(null, null, null, false, 1, 50, mode: "tutor_home"), r => r.UserId == w.Tutor && r.OffersInPerson);
                var quote = await Bookings(db).GetQuoteAsync(new TutorPriceQuoteRequestDto
                { TutorUserId = w.Tutor, SessionDate = w.Day, StartTime = "10:00", DurationMinutes = 60, Mode = "tutor_home" });
                Assert.True(quote.IsAvailable);
            }
            _ntfy.Verify(n => n.PublishAsync($"winplus-user-{w.Tutor}", "Vérifié pour le présentiel",
                It.Is<string>(m => m.Contains("que tu avais choisis sont maintenant actifs")), It.IsAny<string>(), It.IsAny<string[]>(),
                w.Tutor, It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<int?>()), Times.Once);
        }

        // ── 17.17 : suspension, demandes présentielles en attente refusées et remboursées ──

        [PostgresFact]
        public async Task Suspension_RefusesAndRefundsPendingInPersonRequests_NotifiesStudentsAndPayer()
        {
            var w = await SeedAsync();
            await SubmitAndApproveIdentityAsync(w);
            await CreditAsync(w.Parent, 100000);
            await CreditAsync(w.Students[4], 50000);

            int inPersonId, onlineId, engagementId;
            await using (var db = _pg.CreateContext())
            {
                // Le parent réserve une séance à domicile pour son enfant : payée par portefeuille, en attente du tuteur.
                var req = Booking(w, "student_home");
                req.ParticipantUserIds = new List<int> { w.Child };
                req.PayerType = TutorPayerTypes.Parent;
                var created = await Bookings(db).CreateBookingAsync(w.Parent, req);
                Assert.Equal("pending_tutor_approval", created.Booking.Status);
                inPersonId = created.Booking.Id;
                // Demande en ligne : elle n'est pas touchée par la suspension.
                onlineId = (await Bookings(db).CreateBookingAsync(w.Students[4], Booking(w, "online", "14:00"))).Booking.Id;
            }
            await using (var db = _pg.CreateContext())
            {
                var slots = new List<TutorEngagementSlotDto> { new() { DayOfWeek = 2, StartTime = "18:00", DurationMinutes = 60 } };
                var created = await Engagements(db).CreateAsync(w.Students[4], new CreateTutorEngagementRequestDto
                {
                    TutorUserId = w.Tutor, StartDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(8)), Mode = "tutor_home",
                    Slots = slots, PaymentMethod = TutorPaymentMethods.Wallet, Subject = "Maths",
                });
                Assert.Equal(TutorEngagementStatuses.PendingTutorApproval, created.Engagement.Status);
                engagementId = created.Engagement.Id;
            }
            decimal parentAfterBooking, studentAfterBooking;
            await using (var db = _pg.CreateContext())
            {
                var wallet = new WalletService(db, NullLogger<WalletService>.Instance);
                parentAfterBooking = await wallet.GetAvailableAsync(w.Parent);
                studentAfterBooking = await wallet.GetAvailableAsync(w.Students[4]);
            }
            Assert.True(parentAfterBooking < 100000);

            // Quatre signalements en 30 jours : suspension automatique.
            for (var i = 0; i < 4; i++)
            {
                var b = await SeedBookingAsync(w, w.Students[i], "completed");
                await using var db = _pg.CreateContext();
                await Reports(db).SubmitAsync(w.Students[i], new CreateTutorReportDto { TutorBookingId = b, Reason = TutorReportReasons.NoShow });
            }
            Assert.NotNull((await ProfileAsync(w)).InPersonSuspendedAt);

            await using (var db = _pg.CreateContext())
            {
                var booking = await db.TutorBookings.AsNoTracking().SingleAsync(b => b.Id == inPersonId);
                Assert.Equal("rejected", booking.Status);
                Assert.Equal("in_person_blocked", booking.RefundCause);
                Assert.Equal(100, booking.RefundPercent);
                Assert.Equal(booking.PriceXaf, booking.RefundAmountXaf);
                Assert.Equal(0m, booking.TutorFeeChargedXaf);
                Assert.Equal("pending_tutor_approval", (await db.TutorBookings.AsNoTracking().SingleAsync(b => b.Id == onlineId)).Status);
                var engagement = await db.TutorEngagements.AsNoTracking().Include(e => e.Sessions).SingleAsync(e => e.Id == engagementId);
                Assert.Equal(TutorEngagementStatuses.Rejected, engagement.Status);
                Assert.All(engagement.Sessions, s => { Assert.Equal("rejected", s.Status); Assert.Equal("in_person_blocked", s.RefundCause); });

                // Remboursement intégral au portefeuille du payeur.
                var wallet = new WalletService(db, NullLogger<WalletService>.Instance);
                Assert.Equal(100000m, await wallet.GetAvailableAsync(w.Parent));
                Assert.Equal(studentAfterBooking + engagement.TotalXaf, await wallet.GetAvailableAsync(w.Students[4]));
            }
            // Élève participant ET payeur prévenus (séance), élève payeur prévenu (engagement).
            _ntfy.Verify(n => n.PublishAsync($"winplus-user-{w.Child}", "Demande de séance refusée", It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string[]>(), w.Child, It.IsAny<string>(), "TutorBooking", inPersonId), Times.Once);
            _ntfy.Verify(n => n.PublishAsync($"winplus-user-{w.Parent}", "Demande de séance refusée", It.Is<string>(m => m.Contains("remboursement intégral")),
                It.IsAny<string>(), It.IsAny<string[]>(), w.Parent, It.IsAny<string>(), "TutorBooking", inPersonId), Times.Once);
            _ntfy.Verify(n => n.PublishAsync($"winplus-user-{w.Students[4]}", "Engagement mensuel refusé", It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string[]>(), w.Students[4], It.IsAny<string>(), "TutorEngagement", engagementId), Times.Once);
        }

        [PostgresFact]
        public async Task SuspendedTutor_CannotAcceptPendingInPersonRequest_RefusedAndRefunded()
        {
            var w = await SeedAsync();
            await SubmitAndApproveIdentityAsync(w);
            await CreditAsync(w.Students[0], 50000);
            int id;
            await using (var db = _pg.CreateContext())
                id = (await Bookings(db).CreateBookingAsync(w.Students[0], Booking(w, "neutral_place"))).Booking.Id;
            // Suspension posée sans passer par le refus groupé (ex. course entre la suspension et l'acceptation).
            await using (var db = _pg.CreateContext())
                await db.TutorProfiles.Where(p => p.Id == w.ProfileId)
                    .ExecuteUpdateAsync(s => s.SetProperty(p => p.InPersonSuspendedAt, DateTime.UtcNow));

            await using (var db = _pg.CreateContext())
            {
                var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => Bookings(db).ConfirmBookingAsync(w.Tutor, id));
                Assert.Contains("suspendu", ex.Message);
            }
            await using (var db = _pg.CreateContext())
            {
                var booking = await db.TutorBookings.AsNoTracking().SingleAsync(b => b.Id == id);
                Assert.Equal("rejected", booking.Status);
                Assert.Equal("in_person_blocked", booking.RefundCause);
                Assert.Equal(50000m, await new WalletService(db, NullLogger<WalletService>.Instance).GetAvailableAsync(w.Students[0]));
            }
            _ntfy.Verify(n => n.PublishAsync($"winplus-user-{w.Students[0]}", "Demande de séance refusée", It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string[]>(), w.Students[0], It.IsAny<string>(), "TutorBooking", id), Times.Once);
        }

        // ── 17.17 : un seul avis par institution et par séance ────────────────

        [PostgresFact]
        public async Task InstitutionPayer_OneReviewPerInstitution_WhicheverAccount()
        {
            var w = await SeedAsync();
            var participant = w.Students[0];
            var bookingId = await SeedBookingAsync(w, participant, "completed", payer: w.InstitutionAccount, payerType: TutorPayerTypes.Institution);
            int secondAccount;
            await using (var db = _pg.CreateContext())
            {
                await db.TutorBookings.Where(b => b.Id == bookingId).ExecuteUpdateAsync(s => s.SetProperty(b => b.PayerInstitutionId, w.InstitutionId));
                var second = new User
                {
                    Email = $"institution2-{Guid.NewGuid():N}@it.local", Role = "institution", IsActive = true,
                    FirstName = "Secretariat", LastName = "IT", InstitutionId = w.InstitutionId,
                };
                db.Users.Add(second);
                await db.SaveChangesAsync();
                secondAccount = second.Id;
            }

            await using (var db = _pg.CreateContext())
            {
                var review = await new TutorReviewService(db, _ntfy.Object).SubmitAsync(w.InstitutionAccount,
                    new SubmitTutorReviewRequestDto { TutorBookingId = bookingId, Rating = 4 });
                Assert.Equal("payer", review.ReviewerRole);
            }
            await using (var db = _pg.CreateContext())
            {
                // Un autre compte de la même institution : refusé (contrôle serveur).
                var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => new TutorReviewService(db, _ntfy.Object).SubmitAsync(secondAccount,
                    new SubmitTutorReviewRequestDto { TutorBookingId = bookingId, Rating = 1 }));
                Assert.Contains("institution", ex.Message);
                // Le participant garde son propre avis.
                await new TutorReviewService(db, _ntfy.Object).SubmitAsync(participant, new SubmitTutorReviewRequestDto { TutorBookingId = bookingId, Rating = 5 });
                // L'autre compte voit l'avis de son institution : plus de bouton « Noter ».
                var view = await Bookings(db).GetByIdAsync(secondAccount, bookingId);
                Assert.False(view.CanReview);
                Assert.Equal(4, view.MyReviewRating);
            }
            await using (var db = _pg.CreateContext())
            {
                // Index unique partiel séance + institution en base (envois simultanés).
                db.TutorReviews.Add(new TutorReview
                {
                    TutorBookingId = bookingId, TutorProfileId = w.ProfileId, StudentUserId = secondAccount,
                    ReviewerRole = "payer", ReviewerInstitutionId = w.InstitutionId, Rating = 2,
                });
                await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
            }
            await using (var db = _pg.CreateContext())
                Assert.Equal(2, await db.TutorReviews.CountAsync(r => r.TutorBookingId == bookingId));
        }

        // ── 17.17 : pièces d'identité d'un compte supprimé ───────────────────

        [PostgresFact]
        public async Task DeletedAccount_IdentityFiles_KeptUntilNormalTerm_ThenPurged()
        {
            var w = await SeedAsync();
            await SubmitAndApproveIdentityAsync(w);
            List<string> refs;
            await using (var db = _pg.CreateContext())
            {
                var check = await db.TutorIdentityChecks.AsNoTracking().SingleAsync(c => c.TutorProfileId == w.ProfileId && c.Status == "approved");
                refs = new[] { check.FrontFileRef, check.BackFileRef, check.SelfieFileRef }.Where(r => r != null).Select(r => r!).ToList();
                Assert.Equal(2, refs.Count);
                // Suppression du compte : cascade Users -> TutorProfiles -> TutorIdentityChecks.
                await db.Database.ExecuteSqlRawAsync("DELETE FROM \"TutorProfiles\" WHERE \"Id\" = {0}", w.ProfileId);
                Assert.False(await db.TutorIdentityChecks.AnyAsync(c => c.TutorProfileId == w.ProfileId));
                // Les références sont gardées pour la purge.
                var queued = await db.TutorIdentityFilePurges.AsNoTracking().Where(f => refs.Contains(f.FileRef)).ToListAsync();
                Assert.Equal(2, queued.Count);
                Assert.All(queued, f => Assert.Equal("approved", f.Status));
            }
            // Avant le terme normal (30 jours après la validation) : rien n'est supprimé.
            await using (var db = _pg.CreateContext())
                await Verification(db).PurgeExpiredIdentityFilesAsync(DateTime.UtcNow.AddDays(29));
            Assert.All(refs, r => Assert.True(_files.Files.ContainsKey(r)));
            // Au terme : fichiers supprimés du stockage privé, références retirées.
            await using (var db = _pg.CreateContext())
                Assert.True(await Verification(db).PurgeExpiredIdentityFilesAsync(DateTime.UtcNow.AddDays(31)) >= 2);
            Assert.All(refs, r => Assert.False(_files.Files.ContainsKey(r)));
            await using (var db = _pg.CreateContext())
                Assert.False(await db.TutorIdentityFilePurges.AnyAsync(f => refs.Contains(f.FileRef)));
        }
    }
}
