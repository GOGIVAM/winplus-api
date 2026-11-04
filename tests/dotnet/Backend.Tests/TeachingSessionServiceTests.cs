using System;
using System.Linq;
using System.Threading.Tasks;
using Backend.Models.DTOs;
using Backend.Models.Entities;
using Backend.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Backend.Tests
{
    /// <summary>
    /// Sessions d'enseignement : validation à la création (titre, type, durée
    /// arrondie au quart d'heure entre 30 et 180 minutes, prix d'une session
    /// payante), inscription (gratuite, doublon, complète, paiement Mobile Money
    /// initié), annulation par son propriétaire (remboursement des inscriptions
    /// payées signalé à l'administration), résumé. Paiement, notifications,
    /// courriel et portefeuille simulés.
    /// </summary>
    public class TeachingSessionServiceTests
    {
        private const int Teacher = 1, Student = 2, Student2 = 3, Stranger = 9;
        private static readonly DateTime Start = new(2026, 6, 1, 10, 0, 0);

        private class Ctx
        {
            public TestApplicationDbContext Db = TestApplicationDbContext.Create();
            public Mock<INotchPayService> NotchPay = new();
            public Mock<INtfyService> Ntfy = new();
            public Mock<IEmailService> Email = new();
            public Mock<IWalletService> Wallet = new();
            public TeachingSessionService Svc = null!;
        }

        private static Ctx Build()
        {
            var c = new Ctx();
            c.Db.Users.AddRange(
                new User { Id = Teacher, Email = "t@t.l", Role = "teacher" },
                new User { Id = Student, Email = "s@t.l", Role = "student", FirstName = "Awa", LastName = "N" },
                new User { Id = Student2, Email = "s2@t.l", Role = "student" });
            c.Db.SaveChanges();
            c.Email.Setup(e => e.SendGenericEmailAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<NotificationCategory>())).ReturnsAsync(true);
            c.Svc = new TeachingSessionService(c.Db, c.NotchPay.Object, c.Ntfy.Object, c.Email.Object, c.Wallet.Object, NullLogger<TeachingSessionService>.Instance);
            return c;
        }

        private static CreateSessionRequestDto Req(Action<CreateSessionRequestDto>? edit = null)
        {
            var r = new CreateSessionRequestDto { Title = "Révision", Type = "live", StartDate = Start, DurationMinutes = 60, IsFree = true };
            edit?.Invoke(r);
            return r;
        }

        [Theory]
        [InlineData("ab")]
        [InlineData("  a  ")]
        public async Task Un_titre_trop_court_est_refuse(string title)
        {
            var c = Build();
            await Assert.ThrowsAsync<InvalidOperationException>(() => c.Svc.CreateAsync(Teacher, Req(r => r.Title = title)));
        }

        [Fact]
        public async Task Un_type_inconnu_ou_une_session_payante_sans_prix_est_refuse()
        {
            var c = Build();
            await Assert.ThrowsAsync<InvalidOperationException>(() => c.Svc.CreateAsync(Teacher, Req(r => r.Type = "webinaire")));
            await Assert.ThrowsAsync<InvalidOperationException>(() => c.Svc.CreateAsync(Teacher, Req(r => { r.IsFree = false; r.PriceXaf = null; })));
            await Assert.ThrowsAsync<InvalidOperationException>(() => c.Svc.CreateAsync(Teacher, Req(r => { r.IsFree = false; r.PriceXaf = 0; })));
            Assert.Empty(c.Db.Sessions);
        }

        [Theory]
        [InlineData(10, 30)]
        [InlineData(44, 30)]
        [InlineData(50, 45)]
        [InlineData(90, 90)]
        [InlineData(200, 180)]
        public async Task La_duree_est_arrondie_au_quart_d_heure_et_bornee(int asked, int expected)
        {
            var c = Build();
            var dto = await c.Svc.CreateAsync(Teacher, Req(r => r.DurationMinutes = asked));
            Assert.Equal(expected, dto.DurationMinutes);
            Assert.Equal(Start.AddMinutes(expected), dto.EndDate);
            Assert.Equal("scheduled", dto.Status);
        }

        [Fact]
        public async Task Une_session_gratuite_n_a_pas_de_prix_et_un_effectif_hors_bornes_est_ignore()
        {
            var c = Build();
            var free = await c.Svc.CreateAsync(Teacher, Req(r => { r.PriceXaf = 5000; r.MaxParticipants = 500; }));
            Assert.Null(free.PriceXaf);
            Assert.Null(free.MaxParticipants);
            var paid = await c.Svc.CreateAsync(Teacher, Req(r => { r.IsFree = false; r.PriceXaf = 2500; r.MaxParticipants = 20; }));
            Assert.Equal(2500m, paid.PriceXaf);
            Assert.Equal(20, paid.MaxParticipants);
        }

        [Fact]
        public async Task L_inscription_gratuite_est_enregistree_une_seule_fois()
        {
            var c = Build();
            var s = await c.Svc.CreateAsync(Teacher, Req());
            var dto = await c.Svc.EnrollAsync(Student, s.Id, new EnrollSessionRequestDto());
            Assert.Equal(1, dto.EnrolledCount);
            Assert.Equal("free", c.Db.SessionEnrollments.Single().PaymentStatus);
            await Assert.ThrowsAsync<InvalidOperationException>(() => c.Svc.EnrollAsync(Student, s.Id, new EnrollSessionRequestDto()));
        }

        [Fact]
        public async Task Une_session_complete_inconnue_ou_annulee_refuse_les_inscriptions()
        {
            var c = Build();
            var s = await c.Svc.CreateAsync(Teacher, Req(r => r.MaxParticipants = 1));
            await c.Svc.EnrollAsync(Student, s.Id, new EnrollSessionRequestDto());
            await Assert.ThrowsAsync<InvalidOperationException>(() => c.Svc.EnrollAsync(Student2, s.Id, new EnrollSessionRequestDto()));
            await Assert.ThrowsAsync<KeyNotFoundException>(() => c.Svc.EnrollAsync(Student, 999, new EnrollSessionRequestDto()));
            await c.Svc.CancelAsync(Teacher, s.Id, new CancelSessionRequestDto());
            await Assert.ThrowsAsync<InvalidOperationException>(() => c.Svc.EnrollAsync(Student2, s.Id, new EnrollSessionRequestDto()));
        }

        [Fact]
        public async Task Une_session_payante_exige_un_telephone_et_initie_le_paiement()
        {
            var c = Build();
            var s = await c.Svc.CreateAsync(Teacher, Req(r => { r.IsFree = false; r.PriceXaf = 3000; }));
            await Assert.ThrowsAsync<InvalidOperationException>(() => c.Svc.EnrollAsync(Student, s.Id, new EnrollSessionRequestDto { Phone = " " }));
            c.NotchPay.Setup(n => n.InitiatePaymentAsync(It.IsAny<string>(), 3000m, s.Id, It.IsAny<string>(), "s@t.l", "Awa N", It.IsAny<string>(), "SESS"))
                .ReturnsAsync(new NotchPayInitiateResponse { Transaction = new NotchPayTransaction { Reference = "SESS-1-123" } });
            await c.Svc.EnrollAsync(Student, s.Id, new EnrollSessionRequestDto { Phone = "677123456" });
            var e = c.Db.SessionEnrollments.Single();
            Assert.Equal("pending", e.PaymentStatus);
            Assert.Equal("SESS-1-123", e.NotchpayReference);
            Assert.Equal(3000m, e.PriceChargedXaf);
        }

        [Fact]
        public async Task Un_echec_d_initiation_du_paiement_n_enregistre_aucune_inscription()
        {
            var c = Build();
            var s = await c.Svc.CreateAsync(Teacher, Req(r => { r.IsFree = false; r.PriceXaf = 3000; }));
            c.NotchPay.Setup(n => n.InitiatePaymentAsync(It.IsAny<string>(), It.IsAny<decimal>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
                .ThrowsAsync(new InvalidOperationException("opérateur"));
            await Assert.ThrowsAsync<InvalidOperationException>(() => c.Svc.EnrollAsync(Student, s.Id, new EnrollSessionRequestDto { Phone = "677123456" }));
            Assert.Empty(c.Db.SessionEnrollments);
        }

        [Fact]
        public async Task Seul_le_professeur_de_la_session_peut_l_annuler_et_une_seule_fois()
        {
            var c = Build();
            var s = await c.Svc.CreateAsync(Teacher, Req());
            await Assert.ThrowsAsync<KeyNotFoundException>(() => c.Svc.CancelAsync(Stranger, s.Id, new CancelSessionRequestDto()));
            var dto = await c.Svc.CancelAsync(Teacher, s.Id, new CancelSessionRequestDto { Reason = "  " });
            Assert.Equal("cancelled", dto.Status);
            Assert.Null(dto.CancellationReason);
            await Assert.ThrowsAsync<InvalidOperationException>(() => c.Svc.CancelAsync(Teacher, s.Id, new CancelSessionRequestDto()));
        }

        [Fact]
        public async Task L_annulation_notifie_les_inscrits_et_signale_le_remboursement_des_places_payees()
        {
            var c = Build();
            var s = await c.Svc.CreateAsync(Teacher, Req(r => { r.IsFree = false; r.PriceXaf = 3000; }));
            c.Db.SessionEnrollments.AddRange(
                new SessionEnrollment { SessionId = s.Id, StudentId = Student, PaymentStatus = "paid", PriceChargedXaf = 3000m, NotchpayReference = "R1" },
                new SessionEnrollment { SessionId = s.Id, StudentId = Student2, PaymentStatus = "pending" });
            c.Db.SaveChanges();
            var dto = await c.Svc.CancelAsync(Teacher, s.Id, new CancelSessionRequestDto { Reason = "Maladie" });
            Assert.Equal("Maladie", dto.CancellationReason);
            Assert.Equal("refunded", c.Db.SessionEnrollments.Single(e => e.StudentId == Student).PaymentStatus);
            Assert.Equal("pending", c.Db.SessionEnrollments.Single(e => e.StudentId == Student2).PaymentStatus);
            c.Ntfy.Verify(n => n.PublishAdminAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string[]?>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<int?>()), Times.Once);
            c.Wallet.Verify(w => w.SyncSessionEnrollmentAsync(It.IsAny<int>()), Times.Exactly(2));
        }

        [Fact]
        public async Task Le_resume_est_reserve_au_professeur_et_les_sessions_supprimees_sont_invisibles()
        {
            var c = Build();
            var s = await c.Svc.CreateAsync(Teacher, Req());
            await c.Svc.SetSummaryAsync(Teacher, s.Id, "Résumé");
            Assert.Equal("Résumé", (await c.Svc.GetByIdAsync(s.Id))!.SummaryText);
            await Assert.ThrowsAsync<KeyNotFoundException>(() => c.Svc.SetSummaryAsync(Stranger, s.Id, "x"));
            c.Db.Sessions.Single().IsDeleted = true;
            c.Db.SaveChanges();
            Assert.Null(await c.Svc.GetByIdAsync(s.Id));
            Assert.Empty(await c.Svc.GetTeacherSessionsAsync(Teacher));
        }

        [Fact]
        public async Task La_liste_du_professeur_filtre_par_periode_et_trie_par_date()
        {
            var c = Build();
            await c.Svc.CreateAsync(Teacher, Req(r => { r.Title = "Tardive"; r.StartDate = Start.AddDays(10); }));
            await c.Svc.CreateAsync(Teacher, Req(r => { r.Title = "Précoce"; r.StartDate = Start; }));
            await c.Svc.CreateAsync(Stranger, Req(r => r.Title = "Autre prof"));
            Assert.Equal(new[] { "Précoce", "Tardive" }, (await c.Svc.GetTeacherSessionsAsync(Teacher)).Select(x => x.Title));
            Assert.Equal(new[] { "Tardive" }, (await c.Svc.GetTeacherSessionsAsync(Teacher, from: Start.AddDays(5))).Select(x => x.Title));
            Assert.Equal(new[] { "Précoce" }, (await c.Svc.GetTeacherSessionsAsync(Teacher, to: Start.AddDays(5))).Select(x => x.Title));
        }
    }
}
