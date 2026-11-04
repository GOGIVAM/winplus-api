using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Backend.Models.DTOs;
using Backend.Models.Entities;
using Backend.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Backend.Tests
{
    /// <summary>
    /// Certificat de matière : délivré seulement pour une inscription terminée à
    /// 100 % de l'utilisateur, idempotent par inscription, note moyenne des
    /// résultats d'apprentissage, PDF stocké (échec de stockage toléré : URL
    /// nulle), vérification par code, émission administrative sans doublon.
    /// </summary>
    public class CertificateServiceTests
    {
        private const int U = 3001, S = 3002;

        private class Ctx
        {
            public TestApplicationDbContext Db = TestApplicationDbContext.Create();
            public Mock<IPdfService> Pdf = new();
            public Mock<IStorageService> Storage = new();
            public CertificateService Svc = null!;
        }

        private static Ctx Build(bool completed = true, decimal progress = 100m)
        {
            var x = new Ctx();
            x.Db.Users.Add(new User { Id = U, Email = "u@t.local", Role = "student", FirstName = "Awa", LastName = "Ndiaye" });
            x.Db.Subjects.Add(new Subject { Id = S, Title = "Mathématiques" });
            x.Db.Enrollments.Add(new Enrollment { Id = 1, UserId = U, SubjectId = S, IsCompleted = completed, ProgressPercentage = progress, CompletedAt = new DateTime(2026, 5, 1), User = null!, Subject = null! });
            x.Db.SaveChanges();
            x.Pdf.Setup(p => p.GenerateCertificate(It.IsAny<Certificate>(), It.IsAny<User>(), It.IsAny<Subject>())).Returns(new byte[] { 1 });
            x.Storage.Setup(s => s.PutAsync(It.IsAny<Stream>(), It.IsAny<string>(), "application/pdf", It.IsAny<CancellationToken>(), true)).ReturnsAsync("https://files.test/cert.pdf");
            x.Svc = new CertificateService(x.Db, NullLogger<CertificateService>.Instance, x.Pdf.Object, new ConfigurationBuilder().Build(), x.Storage.Object);
            return x;
        }

        [Fact]
        public async Task Une_inscription_inconnue_ou_d_un_autre_utilisateur_est_introuvable()
        {
            var x = Build();
            await Assert.ThrowsAsync<KeyNotFoundException>(() => x.Svc.GenerateCertificateAsync(U, 99));
            await Assert.ThrowsAsync<KeyNotFoundException>(() => x.Svc.GenerateCertificateAsync(U + 1, 1));
        }

        [Fact]
        public async Task Une_inscription_non_terminee_ou_sous_100_pour_cent_est_refusee()
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => Build(completed: false).Svc.GenerateCertificateAsync(U, 1));
            await Assert.ThrowsAsync<InvalidOperationException>(() => Build(progress: 80m).Svc.GenerateCertificateAsync(U, 1));
        }

        [Fact]
        public async Task La_delivrance_cree_un_numero_un_code_et_stocke_le_pdf()
        {
            var x = Build();
            var dto = await x.Svc.GenerateCertificateAsync(U, 1);
            Assert.StartsWith("CERT-", dto.CertificateNumber);
            Assert.Equal(12, dto.VerificationCode.Length);
            Assert.Equal("https://files.test/cert.pdf", dto.FileUrl);
            Assert.Equal(new DateTime(2026, 5, 1), dto.CompletionDate);
            Assert.Equal("Awa Ndiaye", dto.UserName);
            Assert.Equal("Mathématiques", dto.SubjectTitle);
            x.Storage.Verify(s => s.PutAsync(It.IsAny<Stream>(), $"certificates/{dto.CertificateNumber}.pdf", "application/pdf", It.IsAny<CancellationToken>(), true), Times.Once);
        }

        [Fact]
        public async Task La_delivrance_est_idempotente_par_inscription()
        {
            var x = Build();
            var a = await x.Svc.GenerateCertificateAsync(U, 1);
            var b = await x.Svc.GenerateCertificateAsync(U, 1);
            Assert.Equal(a.CertificateNumber, b.CertificateNumber);
            Assert.Single(x.Db.Certificates);
            x.Pdf.Verify(p => p.GenerateCertificate(It.IsAny<Certificate>(), It.IsAny<User>(), It.IsAny<Subject>()), Times.Once);
        }

        [Fact]
        public async Task Un_echec_de_stockage_laisse_le_certificat_sans_url()
        {
            var x = Build();
            x.Storage.Setup(s => s.PutAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>(), It.IsAny<bool>())).ThrowsAsync(new IOException("S3"));
            var dto = await x.Svc.GenerateCertificateAsync(U, 1);
            Assert.Null(dto.FileUrl);
            Assert.Single(x.Db.Certificates);
        }

        [Fact]
        public async Task La_note_est_la_moyenne_arrondie_des_resultats_de_la_matiere()
        {
            var x = Build();
            x.Db.LearningHistories.AddRange(
                new LearningHistory { UserId = U, SubjectId = S, QuizScore = 10m, EventType = "q" },
                new LearningHistory { UserId = U, SubjectId = S, Score = 15m, EventType = "q" },
                new LearningHistory { UserId = U, SubjectId = S, EventType = "vu" },
                new LearningHistory { UserId = U, SubjectId = S + 1, QuizScore = 0m, EventType = "q" });
            x.Db.SaveChanges();
            var dto = await x.Svc.GenerateCertificateAsync(U, 1);
            Assert.Equal(12.5m, dto.Grade);
        }

        [Fact]
        public async Task Sans_resultat_la_note_est_absente()
        {
            var x = Build();
            Assert.Null((await x.Svc.GenerateCertificateAsync(U, 1)).Grade);
        }

        [Fact]
        public async Task La_verification_distingue_code_valide_et_code_inconnu()
        {
            var x = Build();
            var dto = await x.Svc.GenerateCertificateAsync(U, 1);
            var ok = await x.Svc.VerifyCertificateAsync(dto.VerificationCode);
            Assert.True(ok.IsValid);
            Assert.Equal("Awa Ndiaye", ok.StudentName);
            Assert.Equal("Mathématiques", ok.CourseName);
            Assert.False((await x.Svc.VerifyCertificateAsync("NOPE")).IsValid);
        }

        [Fact]
        public async Task Lecture_d_un_certificat_reservee_a_son_proprietaire_et_listes_filtrees()
        {
            var x = Build();
            var dto = await x.Svc.GenerateCertificateAsync(U, 1);
            Assert.Equal(dto.Id, (await x.Svc.GetCertificateAsync(dto.Id, U)).Id);
            await Assert.ThrowsAsync<KeyNotFoundException>(() => x.Svc.GetCertificateAsync(dto.Id, U + 1));
            Assert.Single(await x.Svc.GetUserCertificatesAsync(U));
            Assert.Empty(await x.Svc.GetUserCertificatesAsync(U + 1));
            Assert.Single(await x.Svc.GetSubjectCertificatesAsync(S));
            Assert.Empty(await x.Svc.GetSubjectCertificatesAsync(S + 1));
        }

        [Fact]
        public async Task La_liste_globale_filtre_par_dates_avec_la_fin_incluse_jusqu_au_lendemain()
        {
            var x = Build();
            x.Db.Certificates.AddRange(
                new Certificate { UserId = U, SubjectId = S, CertificateNumber = "A", VerificationCode = "A", IssuedAt = new DateTime(2026, 1, 10), User = null!, Subject = null! },
                new Certificate { UserId = U, SubjectId = S, CertificateNumber = "B", VerificationCode = "B", IssuedAt = new DateTime(2026, 3, 10), User = null!, Subject = null! });
            x.Db.SaveChanges();
            Assert.Equal(2, (await x.Svc.GetAllCertificatesAsync()).Count);
            Assert.Equal(new[] { "B" }, (await x.Svc.GetAllCertificatesAsync(from: new DateTime(2026, 2, 1))).Select(c => c.CertificateNumber));
            Assert.Equal(new[] { "A" }, (await x.Svc.GetAllCertificatesAsync(to: new DateTime(2026, 1, 10))).Select(c => c.CertificateNumber));
            Assert.Empty(await x.Svc.GetAllCertificatesAsync(to: new DateTime(2026, 1, 8)));
        }

        [Fact]
        public async Task L_emission_administrative_exige_une_inscription_et_ne_cree_pas_de_doublon()
        {
            var x = Build(completed: false, progress: 10m);
            await Assert.ThrowsAsync<KeyNotFoundException>(() => x.Svc.AdminIssueCertificateAsync(new AdminIssueCertificateRequest { UserId = U + 1, SubjectId = S }));
            var a = await x.Svc.AdminIssueCertificateAsync(new AdminIssueCertificateRequest { UserId = U, SubjectId = S, Grade = 17m, IssuedAt = new DateTime(2026, 6, 1) });
            var b = await x.Svc.AdminIssueCertificateAsync(new AdminIssueCertificateRequest { UserId = U, SubjectId = S, Grade = 5m });
            Assert.Equal(17m, a.Grade);
            Assert.Equal(new DateTime(2026, 6, 1), a.IssuedAt);
            Assert.Equal(a.CertificateNumber, b.CertificateNumber);
            Assert.Single(x.Db.Certificates);
        }
    }
}
