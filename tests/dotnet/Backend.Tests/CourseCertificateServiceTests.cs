using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Backend.Models.Entities;
using Backend.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Backend.Tests
{
    /// <summary>
    /// Certificat de fin de formation : délivré seulement à 100 % de progression
    /// (et examen final réussi quand il existe), idempotent, avec note moyenne des
    /// quiz, badge évalué, PDF stocké ; tolérant à une panne de stockage ;
    /// vérification publique par code. PDF, stockage et badges simulés.
    /// </summary>
    public class CourseCertificateServiceTests
    {
        private const int U = 6001, C = 6002, Final = 6003;

        private class Ctx
        {
            public TestApplicationDbContext Db = TestApplicationDbContext.Create();
            public Mock<IPdfService> Pdf = new();
            public Mock<IStorageService> Storage = new();
            public Mock<IBadgeService> Badges = new();
            public CourseCertificateService Svc = null!;
        }

        private static Ctx Build(decimal progress = 100m, bool certEnabled = true, int? finalQuiz = null, bool seedEnrollment = true)
        {
            var x = new Ctx();
            x.Db.Users.Add(new User { Id = U, Email = "e@t.local", Role = "student", FirstName = "Awa", LastName = "Ndiaye" });
            x.Db.Courses.Add(new Course { Id = C, Title = "Algèbre", Instructor = null!, CertificateEnabled = certEnabled, FinalExamQuizId = finalQuiz, FinalExamPassScorePercent = 60 });
            if (seedEnrollment) x.Db.CourseEnrollments.Add(new CourseEnrollment { UserId = U, CourseId = C, ProgressPercent = progress, User = null!, Course = null! });
            x.Db.SaveChanges();
            x.Pdf.Setup(p => p.GenerateCourseCertificate(It.IsAny<CourseCertificate>(), It.IsAny<User>(), It.IsAny<Course>(), It.IsAny<byte[]>())).Returns(new byte[] { 1, 2, 3 });
            x.Storage.Setup(s => s.PutAsync(It.IsAny<Stream>(), It.IsAny<string>(), "application/pdf", It.IsAny<CancellationToken>(), true)).ReturnsAsync("https://files.test/c.pdf");
            var cfg = new ConfigurationBuilder().AddInMemoryCollection(new[] { new System.Collections.Generic.KeyValuePair<string, string?>("App:FrontendUrl", "https://front.test/") }).Build();
            x.Svc = new CourseCertificateService(x.Db, x.Pdf.Object, x.Storage.Object, cfg, NullLogger<CourseCertificateService>.Instance, x.Badges.Object);
            return x;
        }

        [Fact]
        public async Task Une_formation_inconnue_leve_une_erreur()
        {
            var x = Build();
            await Assert.ThrowsAsync<KeyNotFoundException>(() => x.Svc.GenerateForCompletedCourseAsync(U, 999));
        }

        [Fact]
        public async Task Un_certificat_desactive_est_refuse()
        {
            var x = Build(certEnabled: false);
            await Assert.ThrowsAsync<InvalidOperationException>(() => x.Svc.GenerateForCompletedCourseAsync(U, C));
        }

        [Fact]
        public async Task Sans_inscription_ou_sous_100_pour_cent_le_certificat_est_refuse()
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => Build(seedEnrollment: false).Svc.GenerateForCompletedCourseAsync(U, C));
            var x = Build(progress: 99.9m);
            await Assert.ThrowsAsync<InvalidOperationException>(() => x.Svc.GenerateForCompletedCourseAsync(U, C));
            Assert.Empty(x.Db.CourseCertificates);
        }

        [Fact]
        public async Task L_examen_final_non_passe_ou_trop_bas_bloque_le_certificat()
        {
            var x = Build(finalQuiz: Final);
            await Assert.ThrowsAsync<InvalidOperationException>(() => x.Svc.GenerateForCompletedCourseAsync(U, C));
            x.Db.QuizAttempts.Add(new QuizAttempt { UserId = U, QuizId = Final, Score = 59m, CompletedAt = new DateTime(2026, 1, 1) });
            x.Db.SaveChanges();
            await Assert.ThrowsAsync<InvalidOperationException>(() => x.Svc.GenerateForCompletedCourseAsync(U, C));
        }

        [Fact]
        public async Task L_examen_final_au_seuil_exact_permet_le_certificat()
        {
            var x = Build(finalQuiz: Final);
            x.Db.QuizAttempts.Add(new QuizAttempt { UserId = U, QuizId = Final, Score = 60m, CompletedAt = new DateTime(2026, 1, 1) });
            x.Db.SaveChanges();
            var cert = await x.Svc.GenerateForCompletedCourseAsync(U, C);
            Assert.Equal(12, cert.VerificationCode.Length);
        }

        [Fact]
        public async Task La_delivrance_stocke_le_pdf_evalue_les_badges_et_reflete_l_url_sur_l_inscription()
        {
            var x = Build();
            var cert = await x.Svc.GenerateForCompletedCourseAsync(U, C);
            Assert.Equal("https://files.test/c.pdf", cert.FileUrl);
            Assert.Equal("https://files.test/c.pdf", x.Db.CourseEnrollments.Single().CertificateUrl);
            x.Badges.Verify(b => b.EvaluateForUserAsync(U), Times.Once);
            x.Storage.Verify(s => s.PutAsync(It.IsAny<Stream>(), $"course-certificates/{cert.VerificationCode}.pdf", "application/pdf", It.IsAny<CancellationToken>(), true), Times.Once);
        }

        [Fact]
        public async Task La_note_est_la_moyenne_des_quiz_de_la_formation_seulement()
        {
            var x = Build();
            x.Db.CourseLessons.Add(new CourseLesson { Id = 1, CourseId = C, QuizId = 31, Section = null!, SectionId = 1 });
            x.Db.CourseLessons.Add(new CourseLesson { Id = 2, CourseId = C, QuizId = 32, Section = null!, SectionId = 1 });
            x.Db.QuizAttempts.Add(new QuizAttempt { UserId = U, QuizId = 31, Score = 80m, CompletedAt = new DateTime(2026, 1, 1) });
            x.Db.QuizAttempts.Add(new QuizAttempt { UserId = U, QuizId = 32, Score = 60m, CompletedAt = new DateTime(2026, 1, 1) });
            x.Db.QuizAttempts.Add(new QuizAttempt { UserId = U, QuizId = 99, Score = 0m, CompletedAt = new DateTime(2026, 1, 1) });
            x.Db.SaveChanges();
            var cert = await x.Svc.GenerateForCompletedCourseAsync(U, C);
            Assert.Equal(70m, cert.Grade);
        }

        [Fact]
        public async Task La_delivrance_est_idempotente()
        {
            var x = Build();
            var a = await x.Svc.GenerateForCompletedCourseAsync(U, C);
            var b = await x.Svc.GenerateForCompletedCourseAsync(U, C);
            Assert.Equal(a.VerificationCode, b.VerificationCode);
            Assert.Single(x.Db.CourseCertificates);
            x.Badges.Verify(bd => bd.EvaluateForUserAsync(U), Times.Once);
        }

        [Fact]
        public async Task Une_panne_de_stockage_ou_de_badge_n_empeche_pas_le_certificat()
        {
            var x = Build();
            x.Storage.Setup(s => s.PutAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>(), It.IsAny<bool>())).ThrowsAsync(new IOException("S3 indisponible"));
            x.Badges.Setup(b => b.EvaluateForUserAsync(U)).ThrowsAsync(new InvalidOperationException("badge"));
            var cert = await x.Svc.GenerateForCompletedCourseAsync(U, C);
            Assert.Null(cert.FileUrl);
            Assert.Single(x.Db.CourseCertificates);
        }

        [Fact]
        public async Task La_verification_reconnait_un_code_valide_et_refuse_un_code_inconnu()
        {
            var x = Build();
            var cert = await x.Svc.GenerateForCompletedCourseAsync(U, C);
            var ok = await x.Svc.VerifyAsync(cert.VerificationCode);
            Assert.True(ok.IsValid);
            Assert.Equal("Awa Ndiaye", ok.StudentName);
            Assert.Equal("Algèbre", ok.CourseTitle);
            var ko = await x.Svc.VerifyAsync("INCONNU");
            Assert.False(ko.IsValid);
            Assert.Null(ko.StudentName);
        }

        [Fact]
        public async Task GetMine_renvoie_le_certificat_de_l_eleve_ou_rien()
        {
            var x = Build();
            Assert.Null(await x.Svc.GetMineAsync(U, C));
            await x.Svc.GenerateForCompletedCourseAsync(U, C);
            Assert.NotNull(await x.Svc.GetMineAsync(U, C));
            Assert.Null(await x.Svc.GetMineAsync(U + 1, C));
        }
    }
}
