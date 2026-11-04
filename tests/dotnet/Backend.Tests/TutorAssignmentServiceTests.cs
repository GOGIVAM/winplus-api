using System;
using System.Linq;
using System.Threading.Tasks;
using Backend.Models.Entities;
using Backend.Services;
using Moq;
using Xunit;

namespace Backend.Tests
{
    /// <summary>
    /// Attributions de tuteur : réponse du tuteur (accepter ou refuser une seule
    /// fois, tuteur concerné seulement, note tronquée), fin d'attribution par
    /// l'attribuant ou le tuteur, lecture côté tuteur (en attente d'abord) et
    /// côté élève (actives). Notifications simulées. Création non testée ici :
    /// elle dépend du résolveur de bénéficiaires.
    /// </summary>
    public class TutorAssignmentServiceTests
    {
        private const int Tutor = 1, Parent = 2, Kid = 3, Stranger = 4;

        private static (TestApplicationDbContext Db, TutorAssignmentService Svc, Mock<INtfyService> Ntfy) Build(string status = TutorAssignmentStatuses.Pending)
        {
            var db = TestApplicationDbContext.Create();
            db.Users.AddRange(
                new User { Id = Tutor, Email = "t@t.local", Role = "student", FirstName = "Paul", LastName = "Tuteur" },
                new User { Id = Parent, Email = "p@t.local", Role = "parent" },
                new User { Id = Kid, Email = "k@t.local", Role = "student" },
                new User { Id = Stranger, Email = "x@t.local", Role = "parent" });
            db.TutorProfiles.Add(new TutorProfile { Id = 10, UserId = Tutor, IsActive = true });
            db.TutorAssignments.Add(new TutorAssignment
            {
                Id = 100, TutorProfileId = 10, AssignedByUserId = Parent, AssignerType = TutorPayerTypes.Parent, Status = status,
                Beneficiaries = { new TutorAssignmentBeneficiary { StudentUserId = Kid } },
            });
            db.SaveChanges();
            var ntfy = new Mock<INtfyService>();
            return (db, new TutorAssignmentService(db, ntfy.Object), ntfy);
        }

        [Fact]
        public async Task Le_tuteur_accepte_une_attribution_en_attente_et_les_parties_sont_notifiees()
        {
            var (db, svc, ntfy) = Build();
            var dto = await svc.AcceptAsync(Tutor, 100, "  avec plaisir  ");
            Assert.Equal(TutorAssignmentStatuses.Active, dto.Status);
            Assert.Equal("avec plaisir", dto.ResponseNote);
            Assert.NotNull(dto.RespondedAt);
            Assert.Equal("Paul Tuteur", dto.TutorName);
            // un message à l'attribuant et un à l'élève bénéficiaire
            ntfy.Verify(n => n.PublishAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string[]?>(), It.IsAny<int?>(), "TutorAssignment", It.IsAny<string?>(), It.IsAny<int?>()), Times.Exactly(2));
            Assert.Equal(TutorAssignmentStatuses.Active, db.TutorAssignments.Single().Status);
        }

        [Fact]
        public async Task Refuser_notifie_seulement_l_attribuant()
        {
            var (_, svc, ntfy) = Build();
            var dto = await svc.DeclineAsync(Tutor, 100, null);
            Assert.Equal(TutorAssignmentStatuses.Declined, dto.Status);
            Assert.Null(dto.ResponseNote);
            ntfy.Verify(n => n.PublishAsync($"winplus-user-{Parent}", It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string[]?>(), Parent, "TutorAssignment", It.IsAny<string?>(), It.IsAny<int?>()), Times.Once);
            ntfy.Verify(n => n.PublishAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string[]?>(), It.IsAny<int?>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<int?>()), Times.Once);
        }

        [Fact]
        public async Task La_note_de_reponse_est_tronquee_a_300_caracteres()
        {
            var (_, svc, _) = Build();
            var dto = await svc.AcceptAsync(Tutor, 100, new string('x', 400));
            Assert.Equal(300, dto.ResponseNote!.Length);
        }

        [Fact]
        public async Task Seul_le_tuteur_concerne_peut_repondre_et_une_seule_fois()
        {
            var (_, svc, _) = Build();
            await Assert.ThrowsAsync<InvalidOperationException>(() => svc.AcceptAsync(Stranger, 100, null));
            await Assert.ThrowsAsync<InvalidOperationException>(() => svc.AcceptAsync(Tutor, 999, null));
            await svc.AcceptAsync(Tutor, 100, null);
            await Assert.ThrowsAsync<InvalidOperationException>(() => svc.DeclineAsync(Tutor, 100, null));
        }

        [Fact]
        public async Task L_attribuant_ou_le_tuteur_peuvent_terminer_une_attribution_ouverte()
        {
            var (db, svc, _) = Build(TutorAssignmentStatuses.Active);
            var dto = await svc.EndAsync(Parent, 100);
            Assert.Equal(TutorAssignmentStatuses.Ended, dto.Status);
            Assert.NotNull(dto.EndedAt);
            Assert.Equal(Parent, db.TutorAssignments.Single().EndedByUserId);
            var (_, svc2, _) = Build(TutorAssignmentStatuses.Pending);
            Assert.Equal(TutorAssignmentStatuses.Ended, (await svc2.EndAsync(Tutor, 100)).Status);
        }

        [Fact]
        public async Task Terminer_est_refuse_a_un_tiers_a_une_attribution_inconnue_ou_deja_close()
        {
            var (_, svc, _) = Build(TutorAssignmentStatuses.Active);
            await Assert.ThrowsAsync<InvalidOperationException>(() => svc.EndAsync(Stranger, 100));
            await Assert.ThrowsAsync<InvalidOperationException>(() => svc.EndAsync(Parent, 999));
            await svc.EndAsync(Parent, 100);
            await Assert.ThrowsAsync<InvalidOperationException>(() => svc.EndAsync(Parent, 100));
            var (_, declined, _) = Build(TutorAssignmentStatuses.Declined);
            await Assert.ThrowsAsync<InvalidOperationException>(() => declined.EndAsync(Parent, 100));
        }

        [Fact]
        public async Task Les_lectures_filtrent_par_role_et_l_eleve_ne_voit_que_les_attributions_actives()
        {
            var (_, svc, _) = Build();
            Assert.Single(await svc.GetAsAssignerAsync(Parent));
            Assert.Empty(await svc.GetAsAssignerAsync(Stranger));
            Assert.Single(await svc.GetAsTutorAsync(Tutor));
            Assert.Empty(await svc.GetForStudentAsync(Kid));
            await svc.AcceptAsync(Tutor, 100, null);
            var forKid = Assert.Single(await svc.GetForStudentAsync(Kid));
            Assert.Equal(Kid, forKid.Beneficiaries.Single().UserId);
            Assert.Empty(await svc.GetForStudentAsync(Stranger));
        }
    }
}
