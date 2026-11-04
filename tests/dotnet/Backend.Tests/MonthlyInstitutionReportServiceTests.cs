using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Backend.Data;
using Backend.Models.Entities;
using Backend.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Backend.Tests
{
    /// <summary>
    /// Rapport mensuel d'un établissement envoyé à la demande : refus si le
    /// compte est inconnu ou sans adresse, message explicite sans élève
    /// rattaché (aucun appel IA), plan d'action WinAI repris dans le courriel,
    /// repli sur un texte générique si l'IA échoue. Courriel et IA simulés.
    /// </summary>
    public class MonthlyInstitutionReportServiceTests
    {
        private const int Director = 1, Student = 2, InstId = 10;

        private sealed class StubHandler : HttpMessageHandler
        {
            private readonly Func<HttpResponseMessage> _reply;
            public int Calls;
            public StubHandler(Func<HttpResponseMessage> reply) => _reply = reply;
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            {
                Calls++;
                return Task.FromResult(_reply());
            }
        }

        private class Ctx
        {
            public TestApplicationDbContext Db = TestApplicationDbContext.Create();
            public Mock<IEmailService> Email = new();
            public StubHandler Handler = null!;
            public MonthlyInstitutionReportService Svc = null!;
            public string? Subject, Html, To;
        }

        private static Ctx Build(Func<HttpResponseMessage>? ai = null, bool withStudent = false, string? directorEmail = "dir@t.l")
        {
            var c = new Ctx();
            c.Db.Institutions.Add(new Institution { Id = InstId, Name = "Lycée Test", Country = "Cameroun" });
            c.Db.Users.Add(new User { Id = Director, Email = directorEmail!, Role = "institution", FirstName = "Mme Directrice", InstitutionId = InstId });
            if (withStudent)
            {
                c.Db.Users.Add(new User { Id = Student, Email = "e@t.l", Role = "student" });
                c.Db.InstitutionStudents.Add(new InstitutionStudent { Id = 1, InstitutionId = InstId, StudentId = Student, IsActive = true, ConfirmationStatus = InstitutionAttachmentStatuses.Accepted });
            }
            c.Db.SaveChanges();
            c.Email.Setup(e => e.SendGenericEmailAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<NotificationCategory>()))
                .Callback((string to, string subject, string html, NotificationCategory cat) => { c.To = to; c.Subject = subject; c.Html = html; })
                .ReturnsAsync(true);
            c.Handler = new StubHandler(ai ?? (() => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"actions\":[]}") }));
            var factory = new Mock<IHttpClientFactory>();
            factory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(() => new HttpClient(c.Handler) { BaseAddress = new Uri("http://ia.test") });
            var token = new Mock<IServiceTokenProvider>();
            token.Setup(t => t.CreateAuthorizationHeader(It.IsAny<string>())).Returns("Bearer x");
            var services = new ServiceCollection();
            services.AddSingleton<ApplicationDbContext>(c.Db);
            services.AddSingleton(c.Email.Object);
            var provider = services.BuildServiceProvider();
            c.Svc = new MonthlyInstitutionReportService(provider.GetRequiredService<IServiceScopeFactory>(), NullLogger<MonthlyInstitutionReportService>.Instance, factory.Object, token.Object);
            return c;
        }

        [Fact]
        public async Task Un_compte_inconnu_ne_recoit_rien()
        {
            var c = Build();
            Assert.False(await c.Svc.SendNowAsync(999));
            c.Email.Verify(e => e.SendGenericEmailAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<NotificationCategory>()), Times.Never);
        }

        [Fact]
        public async Task Un_compte_sans_adresse_ne_recoit_rien()
        {
            var c = Build(directorEmail: " ");
            Assert.False(await c.Svc.SendNowAsync(Director));
            Assert.Null(c.Subject);
        }

        [Fact]
        public async Task Sans_eleve_rattache_le_courriel_part_sans_appel_a_l_ia()
        {
            var c = Build();
            Assert.True(await c.Svc.SendNowAsync(Director));
            Assert.Equal("dir@t.l", c.To);
            Assert.Contains("Rapport mensuel", c.Subject);
            Assert.Contains("Aucun élève rattaché", c.Html);
            Assert.Equal(0, c.Handler.Calls);
        }

        [Fact]
        public async Task Les_actions_de_l_ia_sont_numerotees_dans_le_courriel()
        {
            var c = Build(() => new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new StringContent("{\"actions\":[{\"action\":\"Relancer les absents\",\"effort\":\"faible\"},{\"action\":\"Planifier un examen blanc\"}]}") }, withStudent: true);
            Assert.True(await c.Svc.SendNowAsync(Director));
            Assert.Equal(1, c.Handler.Calls);
            Assert.Contains("1. Relancer les absents (faible)", c.Html);
            Assert.Contains("2. Planifier un examen blanc", c.Html);
            Assert.Contains("Lycée Test", c.Html);
        }

        [Fact]
        public async Task Une_erreur_ou_une_reponse_vide_de_l_ia_donne_le_texte_de_repli()
        {
            var failing = Build(() => new HttpResponseMessage(HttpStatusCode.InternalServerError) { Content = new StringContent("oups") }, withStudent: true);
            Assert.True(await failing.Svc.SendNowAsync(Director));
            Assert.Contains("Analyse WinAI indisponible", failing.Html);
            var empty = Build(withStudent: true);
            Assert.True(await empty.Svc.SendNowAsync(Director));
            Assert.Contains("Analyse WinAI indisponible", empty.Html);
        }

        [Fact]
        public async Task Une_panne_reseau_de_l_ia_n_empeche_pas_l_envoi()
        {
            var c = Build(() => throw new HttpRequestException("réseau"), withStudent: true);
            Assert.True(await c.Svc.SendNowAsync(Director));
            Assert.Contains("Analyse WinAI indisponible", c.Html);
        }
    }
}
