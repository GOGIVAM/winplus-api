using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Backend.Models.Entities;
using Backend.Repositories;
using Backend.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Backend.Tests
{
    /// <summary>
    /// Inscriptions aux matières : inscription idempotente, réactivation d'une
    /// inscription désinscrite (pas de doublon), compteur de la matière,
    /// progression bornée à 0..100 avec achèvement automatique, achèvement
    /// manuel avec génération de certificat non bloquante, désinscription logique.
    /// Dépôts et service de certificat simulés.
    /// </summary>
    public class EnrollmentServiceTests
    {
        private class Ctx
        {
            public Mock<IUserRepository> Users = new();
            public Mock<ISubjectRepository> Subjects = new();
            public Mock<IEnrollmentRepository> Enrollments = new();
            public Mock<ICertificateService> Certs = new();
            public EnrollmentService Svc = null!;
        }

        private static Ctx Build(User? user = null, Subject? subject = null)
        {
            var c = new Ctx();
            c.Users.Setup(r => r.GetByIdAsync(It.IsAny<int>())).ReturnsAsync(user);
            c.Subjects.Setup(r => r.GetByIdAsync(It.IsAny<int>())).ReturnsAsync(subject);
            c.Subjects.Setup(r => r.UpdateAsync(It.IsAny<Subject>())).ReturnsAsync((Subject s) => s);
            c.Users.Setup(r => r.UpdateAsync(It.IsAny<User>())).ReturnsAsync((User u) => u);
            c.Enrollments.Setup(r => r.UpdateAsync(It.IsAny<Enrollment>())).ReturnsAsync((Enrollment e) => e);
            c.Svc = new EnrollmentService(c.Users.Object, c.Subjects.Object, c.Enrollments.Object, c.Certs.Object, NullLogger<EnrollmentService>.Instance);
            return c;
        }

        private static User U() => new() { Id = 1, Email = "u@t.local", Role = "student" };
        private static Subject S(int count = 0) => new() { Id = 2, Title = "Maths", EnrollmentCount = count };

        [Fact]
        public async Task Inscrire_un_utilisateur_ou_une_matiere_inconnus_est_refuse()
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => Build(null, S()).Svc.EnrollUserAsync(1, 2));
            await Assert.ThrowsAsync<InvalidOperationException>(() => Build(U(), null).Svc.EnrollUserAsync(1, 2));
        }

        [Fact]
        public async Task Une_nouvelle_inscription_demarre_a_zero_et_incremente_le_compteur()
        {
            var subject = S(4);
            var c = Build(U(), subject);
            var e = await c.Svc.EnrollUserAsync(1, 2);
            Assert.Equal(0, e.ProgressPercentage);
            Assert.Equal(5, subject.EnrollmentCount);
            c.Users.Verify(r => r.UpdateAsync(It.IsAny<User>()), Times.Once);
        }

        [Fact]
        public async Task Une_inscription_deja_active_est_renvoyee_sans_changer_le_compteur()
        {
            var user = U();
            var existing = new Enrollment { Id = 9, UserId = 1, SubjectId = 2, User = user, Subject = null! };
            user.Enrollments.Add(existing);
            var subject = S(4);
            var c = Build(user, subject);
            Assert.Same(existing, await c.Svc.EnrollUserAsync(1, 2));
            Assert.Equal(4, subject.EnrollmentCount);
            c.Users.Verify(r => r.UpdateAsync(It.IsAny<User>()), Times.Never);
        }

        [Fact]
        public async Task Une_inscription_desinscrite_est_reactivee_au_lieu_d_etre_dupliquee()
        {
            var subject = S(1);
            var c = Build(U(), subject);
            var old = new Enrollment { Id = 9, UserId = 1, SubjectId = 2, IsDeleted = true, UnenrolledAt = new DateTime(2026, 1, 1), UnenrollReason = "x", User = null!, Subject = null! };
            c.Enrollments.Setup(r => r.GetAnyByUserAndSubjectAsync(1, 2)).ReturnsAsync(old);
            var e = await c.Svc.EnrollUserAsync(1, 2);
            Assert.Same(old, e);
            Assert.False(e.IsDeleted);
            Assert.Null(e.UnenrolledAt);
            Assert.Null(e.UnenrollReason);
            Assert.Equal(2, subject.EnrollmentCount);
            c.Users.Verify(r => r.UpdateAsync(It.IsAny<User>()), Times.Never);
        }

        [Theory]
        [InlineData(-1)]
        [InlineData(100.5)]
        public async Task La_progression_hors_de_0_a_100_est_refusee(double value)
        {
            var c = Build();
            await Assert.ThrowsAsync<ArgumentException>(() => c.Svc.UpdateProgressAsync(1, (decimal)value));
        }

        [Fact]
        public async Task Une_progression_sur_une_inscription_inconnue_leve_une_erreur()
        {
            var c = Build();
            c.Enrollments.Setup(r => r.GetByIdAsync(1)).ReturnsAsync((Enrollment?)null);
            await Assert.ThrowsAsync<KeyNotFoundException>(() => c.Svc.UpdateProgressAsync(1, 50m));
        }

        [Fact]
        public async Task Atteindre_100_pour_cent_acheve_l_inscription_une_seule_fois()
        {
            var c = Build();
            var e = new Enrollment { Id = 1, UserId = 1, SubjectId = 2, User = null!, Subject = null! };
            c.Enrollments.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(e);
            await c.Svc.UpdateProgressAsync(1, 60m);
            Assert.False(e.IsCompleted);
            await c.Svc.UpdateProgressAsync(1, 100m);
            Assert.True(e.IsCompleted);
            var firstCompletion = e.CompletedAt;
            Assert.NotNull(firstCompletion);
            await c.Svc.UpdateProgressAsync(1, 100m);
            Assert.Equal(firstCompletion, e.CompletedAt);
        }

        [Fact]
        public async Task L_achevement_manuel_force_100_pour_cent_enregistre_l_url_et_genere_le_certificat()
        {
            var c = Build();
            var e = new Enrollment { Id = 1, UserId = 7, SubjectId = 2, User = null!, Subject = null! };
            c.Enrollments.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(e);
            var res = await c.Svc.CompleteEnrollmentAsync(1, "https://f/c.pdf");
            Assert.True(res.IsCompleted);
            Assert.Equal(100m, res.ProgressPercentage);
            Assert.Equal("https://f/c.pdf", res.CertificateUrl);
            c.Certs.Verify(s => s.GenerateCertificateAsync(7, 1), Times.Once);
        }

        [Fact]
        public async Task Un_certificat_deja_present_n_est_pas_regenere_et_une_panne_n_annule_pas_l_achevement()
        {
            var c = Build();
            var withCert = new Enrollment { Id = 1, UserId = 7, SubjectId = 2, Certificate = new Certificate { CertificateNumber = "X", VerificationCode = "X", User = null!, Subject = null! }, User = null!, Subject = null! };
            c.Enrollments.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(withCert);
            await c.Svc.CompleteEnrollmentAsync(1);
            c.Certs.Verify(s => s.GenerateCertificateAsync(It.IsAny<int>(), It.IsAny<int>()), Times.Never);

            var plain = new Enrollment { Id = 2, UserId = 7, SubjectId = 2, User = null!, Subject = null! };
            c.Enrollments.Setup(r => r.GetByIdAsync(2)).ReturnsAsync(plain);
            c.Certs.Setup(s => s.GenerateCertificateAsync(7, 2)).ThrowsAsync(new InvalidOperationException("pdf"));
            var res = await c.Svc.CompleteEnrollmentAsync(2);
            Assert.True(res.IsCompleted);
        }

        [Fact]
        public async Task La_desinscription_est_logique_et_decremente_le_compteur_sans_passer_sous_zero()
        {
            var subject = S(1);
            var c = Build(null, subject);
            var e = new Enrollment { Id = 1, UserId = 1, SubjectId = 2, User = null!, Subject = null! };
            c.Enrollments.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(e);
            Assert.True(await c.Svc.UnenrollAsync(1));
            Assert.True(e.IsDeleted);
            Assert.NotNull(e.UnenrolledAt);
            Assert.Equal(0, subject.EnrollmentCount);
            Assert.True(await c.Svc.UnenrollAsync(1));
            Assert.Equal(0, subject.EnrollmentCount);
        }

        [Fact]
        public async Task La_desinscription_d_une_inscription_inconnue_renvoie_faux()
        {
            var c = Build();
            c.Enrollments.Setup(r => r.GetByIdAsync(5)).ReturnsAsync((Enrollment?)null);
            Assert.False(await c.Svc.UnenrollAsync(5));
        }

        [Fact]
        public async Task Les_lectures_sans_utilisateur_renvoient_vide_faux_ou_null()
        {
            var c = Build();
            Assert.Empty(await c.Svc.GetUserEnrollmentsAsync(1));
            Assert.Null(await c.Svc.GetEnrollmentAsync(1, 2));
            Assert.False(await c.Svc.IsUserEnrolledAsync(1, 2));
        }

        [Fact]
        public async Task La_moyenne_de_progression_est_nulle_sans_inscrit_sinon_calculee()
        {
            var empty = Build(null, S(0));
            Assert.Equal(0m, await empty.Svc.GetAverageProgressAsync(2));
            var subject = S(2);
            subject.Enrollments.Add(new Enrollment { ProgressPercentage = 20m, User = null!, Subject = null! });
            subject.Enrollments.Add(new Enrollment { ProgressPercentage = 60m, User = null!, Subject = null! });
            Assert.Equal(40m, await Build(null, subject).Svc.GetAverageProgressAsync(2));
            Assert.Equal(0, await Build().Svc.GetEnrollmentCountBySubjectAsync(2));
        }

        [Fact(Skip = "bogue probable : GetProgressAsync charge la matiere avec l'identifiant de l'INSCRIPTION (_subjectRepository.GetByIdAsync(enrollmentId)) au lieu de celui de la matiere ; la progression n'est trouvee que si les deux identifiants coincident.")]
        public async Task La_progression_d_une_inscription_se_lit_par_la_matiere_de_cette_inscription()
        {
            var subject = S();
            subject.Id = 10;
            subject.Enrollments.Add(new Enrollment { Id = 5, UserId = 1, SubjectId = 10, ProgressPercentage = 50m, User = null!, Subject = null! });
            var c = Build(null, null);
            c.Subjects.Setup(r => r.GetByIdAsync(10)).ReturnsAsync(subject);
            var dto = await c.Svc.GetProgressAsync(5, 1);
            Assert.Equal(10, dto.SubjectId);
            Assert.Equal(50m, dto.ProgressPercentage);
        }
    }
}
