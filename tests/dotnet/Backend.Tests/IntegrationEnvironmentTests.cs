using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Backend.Controllers;
using Backend.Models.DTOs;
using Backend.Models.Entities;
using Backend.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Backend.Tests
{
    /// <summary>
    /// Environnement d'intégration (integration.winplus.cm) : paiements simulés
    /// impossibles à activer en production, sujets ntfy préfixés, e-mails
    /// redirigés, objets S3 jamais supprimés.
    /// </summary>
    public class IntegrationEnvironmentTests
    {
        private static IConfiguration Config(Dictionary<string, string?> values) =>
            new ConfigurationBuilder().AddInMemoryCollection(values).Build();

        /// <summary>Configuration de l'intégration telle que dans infrastructure/integration/env/backend.env.example.</summary>
        private static Dictionary<string, string?> IntegrationValues() => new()
        {
            ["PAYMENTS_SIMULATED"] = "true",
            ["WINPLUS_ENV"] = "integration",
            ["App:FrontendUrl"] = "https://integration.winplus.cm",
            ["NotchPay:SecretKey"] = "",
        };

        /// <summary>Configuration de la production (.env.dotnet) à laquelle on aurait ajouté PAYMENTS_SIMULATED=true par erreur.</summary>
        private static Dictionary<string, string?> ProductionValues() => new()
        {
            ["PAYMENTS_SIMULATED"] = "true",
            ["App:FrontendUrl"] = "https://winplus.cm",
            ["NotchPay:SecretKey"] = "sk.production",
        };

        // ── Garde du mode simulé ────────────────────────────────────────────

        [Fact]
        public void Simulation_IsEnabled_OnIntegration()
        {
            Assert.True(PaymentSimulationGuard.IsEnabled(Config(IntegrationValues())));
        }

        [Fact]
        public void Simulation_IsInactive_InProduction_EvenWithTheVariable()
        {
            var reason = PaymentSimulationGuard.RefusalReason(Config(ProductionValues()));
            Assert.NotNull(reason);
            Assert.Contains("WINPLUS_ENV", reason);
        }

        [Theory]
        [InlineData("PAYMENTS_SIMULATED", "false")]
        [InlineData("PAYMENTS_SIMULATED", null)]
        [InlineData("WINPLUS_ENV", "production")]
        [InlineData("WINPLUS_ENV", null)]
        [InlineData("App:FrontendUrl", "https://winplus.cm")]
        [InlineData("App:FrontendUrl", "https://www.winplus.cm/")]
        [InlineData("App:FrontendUrl", null)]
        [InlineData("NotchPay:SecretKey", "sk.live")]
        public void Simulation_RequiresEveryGuard(string key, string? value)
        {
            var values = IntegrationValues();
            values[key] = value;
            Assert.False(PaymentSimulationGuard.IsEnabled(Config(values)));
        }

        [Fact]
        public void Simulation_ProductionWithIntegrationMarkerButProductionHost_IsStillRefused()
        {
            // Une erreur de copie de WINPLUS_ENV=integration dans le fichier de
            // production ne suffit pas non plus : l'hôte et la clé NotchPay bloquent.
            var values = ProductionValues();
            values["WINPLUS_ENV"] = "integration";
            Assert.False(PaymentSimulationGuard.IsEnabled(Config(values)));
        }

        // ── Service NotchPay simulé ─────────────────────────────────────────

        [Fact]
        public async Task SimulatedNotchPay_ReturnsMerchantReference_AndNeverCallsTheNetwork()
        {
            var store = new SimulatedPaymentStore();
            var svc = new SimulatedNotchPayService(store, NullLogger<SimulatedNotchPayService>.Instance);

            var init = await svc.InitiatePaymentAsync("237690000000", 2500, 42, "Épreuves", "eleve30@integration.winplus.cm", "Élève Test", "cm.mtn", "TBK");

            var reference = init.Transaction!.Reference!;
            Assert.StartsWith("TBK-42-", reference);
            Assert.Equal("pending", init.Transaction.Status);
            Assert.Equal("pending", (await svc.GetTransactionStatusAsync(reference)).Status);
            Assert.False(svc.VerifyWebhookSignature("{}", "anything"));

            Assert.True(store.Decide(reference, success: true));
            Assert.Equal("complete", (await svc.GetTransactionStatusAsync(reference)).Status);
            Assert.False(store.Decide(reference, success: false));
        }

        [Fact]
        public async Task SimulatedTransfer_CompletesImmediately()
        {
            var store = new SimulatedPaymentStore();
            var svc = new SimulatedNotchPayService(store, NullLogger<SimulatedNotchPayService>.Instance);
            var t = await svc.CreateTransferAsync(new NotchPayTransferRequest { AmountXaf = 5000, Reference = "WDR-7", BeneficiaryPhone = "237690000000" });
            Assert.Equal("complete", t.Status);
            Assert.Equal("complete", (await svc.GetTransferAsync("WDR-7"))!.Status);
        }

        // ── Panneau de test (contrôleur) ────────────────────────────────────

        private static (SimulatedPaymentsController ctrl, Mock<INotchPayEventDispatcher> dispatcher, SimulatedPaymentStore store)
            Controller(IConfiguration config, int userId, string role, TestApplicationDbContext db)
        {
            var store = new SimulatedPaymentStore();
            var dispatcher = new Mock<INotchPayEventDispatcher>();
            var services = new ServiceCollection()
                .AddSingleton(store)
                .AddSingleton(dispatcher.Object)
                .BuildServiceProvider();
            var ctrl = new SimulatedPaymentsController(config, services, db, NullLogger<SimulatedPaymentsController>.Instance)
            {
                ControllerContext = new ControllerContext
                {
                    HttpContext = new DefaultHttpContext
                    {
                        User = new ClaimsPrincipal(new ClaimsIdentity(new[]
                        {
                            new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
                            new Claim(ClaimTypes.Role, role),
                        }, "test")),
                    },
                },
            };
            return (ctrl, dispatcher, store);
        }

        private static TestApplicationDbContext DbWithUser(int id, string email)
        {
            var db = TestApplicationDbContext.Create();
            db.Users.Add(new User { Id = id, Email = email, Role = "student", IsActive = true, CreatedAt = DateTime.UtcNow });
            db.SaveChanges();
            return db;
        }

        [Fact]
        public async Task Panel_Returns404_InProduction()
        {
            using var db = DbWithUser(30, "eleve30@integration.winplus.cm");
            var (ctrl, dispatcher, _) = Controller(Config(ProductionValues()), 30, "student", db);
            Assert.IsType<NotFoundResult>(await ctrl.Pending());
            Assert.IsType<NotFoundResult>(await ctrl.Decide("WP-1-1", "confirm"));
            dispatcher.VerifyNoOtherCalls();
        }

        [Fact]
        public async Task Panel_Confirm_DispatchesACompletedPayment_LikeTheWebhook()
        {
            using var db = DbWithUser(30, "eleve30@integration.winplus.cm");
            var (ctrl, dispatcher, store) = Controller(Config(IntegrationValues()), 30, "student", db);
            store.Add(new SimulatedPayment { Reference = "WP-12-1", Amount = 1500, CustomerEmail = "eleve30@integration.winplus.cm" });

            Assert.IsType<OkObjectResult>(await ctrl.Decide("WP-12-1", "confirm"));

            dispatcher.Verify(d => d.DispatchAsync("sim:WP-12-1:complete", "payment.complete",
                It.Is<NotchPayWebhookTransaction>(t => t.Reference == "WP-12-1" && t.Status == "complete" && t.Amount == 1500)), Times.Once);
            Assert.IsType<ConflictObjectResult>(await ctrl.Decide("WP-12-1", "refuse"));
        }

        [Fact]
        public async Task Panel_Refuse_DispatchesAFailedPayment()
        {
            using var db = DbWithUser(30, "eleve30@integration.winplus.cm");
            var (ctrl, dispatcher, store) = Controller(Config(IntegrationValues()), 30, "student", db);
            store.Add(new SimulatedPayment { Reference = "TEN-3-1", Amount = 20000, CustomerEmail = "eleve30@integration.winplus.cm" });

            Assert.IsType<OkObjectResult>(await ctrl.Decide("TEN-3-1", "refuse"));
            dispatcher.Verify(d => d.DispatchAsync("sim:TEN-3-1:failed", "payment.failed",
                It.Is<NotchPayWebhookTransaction>(t => t.Status == "failed")), Times.Once);
        }

        [Fact]
        public async Task Panel_UserCannotDecideSomeoneElsesPayment()
        {
            using var db = DbWithUser(30, "eleve30@integration.winplus.cm");
            var (ctrl, dispatcher, store) = Controller(Config(IntegrationValues()), 30, "student", db);
            store.Add(new SimulatedPayment { Reference = "WP-9-1", Amount = 500, CustomerEmail = "parent26@integration.winplus.cm" });

            var result = await ctrl.Decide("WP-9-1", "confirm");
            Assert.Equal(403, Assert.IsType<ObjectResult>(result).StatusCode);
            dispatcher.VerifyNoOtherCalls();
            Assert.Equal("pending", store.Get("WP-9-1")!.Status);
        }

        // ── Sujets ntfy ─────────────────────────────────────────────────────

        [Fact]
        public void NtfyTopics_ProductionUnchanged()
        {
            var topics = NtfyTopics.From(Config(new() { ["Ntfy:BaseUrl"] = "https://ntfy.winplus.cm" }));
            Assert.Equal("winplus-user-30", topics.UserTopic(30));
            Assert.Equal("winplus-admin", topics.Resolve("winplus-admin"));
        }

        [Fact]
        public void NtfyTopics_IntegrationIsPrefixed()
        {
            var topics = NtfyTopics.From(Config(new() { ["WINPLUS_ENV"] = "integration", ["NTFY_TOPIC_PREFIX"] = "integration-" }));
            Assert.Equal("integration-winplus-user-30", topics.UserTopic(30));
            Assert.Equal("integration-winplus-admin", topics.Resolve("winplus-admin"));
            Assert.Equal("integration-winplus-admin", topics.Resolve("integration-winplus-admin"));
        }

        [Fact]
        public void NtfyTopics_IntegrationWithoutPrefix_PublishesNothing()
        {
            var topics = NtfyTopics.From(Config(new() { ["WINPLUS_ENV"] = "integration" }));
            Assert.Null(topics.UserTopic(30));
            Assert.Null(topics.Resolve("winplus-admin"));
        }

        private sealed class CapturingHandler : HttpMessageHandler
        {
            public readonly List<string> Urls = new();
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                Urls.Add(request.RequestUri!.ToString());
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("") });
            }
        }

        private static (NtfyService svc, CapturingHandler handler) Ntfy(Dictionary<string, string?> values)
        {
            var handler = new CapturingHandler();
            var factory = new Mock<IHttpClientFactory>();
            factory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(() => new HttpClient(handler, disposeHandler: false));
            var scopes = new ServiceCollection().BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();
            var svc = new NtfyService(factory.Object, scopes, new Mock<INotificationPreferenceService>().Object,
                NullLogger<NtfyService>.Instance, Config(values));
            return (svc, handler);
        }

        [Fact]
        public async Task NtfyService_Integration_PublishesOnPrefixedTopicOnly()
        {
            var (svc, handler) = Ntfy(new() { ["Ntfy:BaseUrl"] = "https://ntfy.winplus.cm", ["WINPLUS_ENV"] = "integration", ["NTFY_TOPIC_PREFIX"] = "integration-" });
            await svc.PublishAsync("winplus-user-30", "Titre", "Message");
            Assert.Equal(new[] { "https://ntfy.winplus.cm/integration-winplus-user-30" }, handler.Urls);
        }

        [Fact]
        public async Task NtfyService_Production_TopicUnchanged()
        {
            var (svc, handler) = Ntfy(new() { ["Ntfy:BaseUrl"] = "https://ntfy.winplus.cm" });
            await svc.PublishAsync("winplus-user-30", "Titre", "Message");
            Assert.Equal(new[] { "https://ntfy.winplus.cm/winplus-user-30" }, handler.Urls);
        }

        [Fact]
        public async Task NtfyService_IntegrationWithoutPrefix_SendsNothing()
        {
            var (svc, handler) = Ntfy(new() { ["Ntfy:BaseUrl"] = "https://ntfy.winplus.cm", ["WINPLUS_ENV"] = "integration" });
            await svc.PublishAsync("winplus-user-30", "Titre", "Message");
            Assert.Empty(handler.Urls);
        }

        [Fact]
        public async Task NotificationsSse_Integration_ListensOnPrefixedTopic()
        {
            var handler = new CapturingHandler();
            var factory = new Mock<IHttpClientFactory>();
            factory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(() => new HttpClient(handler, disposeHandler: false));
            using var db = TestApplicationDbContext.Create();
            var ctrl = new NotificationsController(db, NullLogger<NotificationsController>.Instance, factory.Object,
                Config(new() { ["Ntfy:BaseUrl"] = "https://ntfy.winplus.cm", ["WINPLUS_ENV"] = "integration", ["NTFY_TOPIC_PREFIX"] = "integration-" }))
            {
                ControllerContext = new ControllerContext
                {
                    HttpContext = new DefaultHttpContext
                    {
                        User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, "30") }, "test")),
                    },
                },
            };
            await ctrl.StreamSSE(CancellationToken.None);
            Assert.Equal(new[] { "https://ntfy.winplus.cm/integration-winplus-user-30/sse" }, handler.Urls);
        }

        // ── E-mails ─────────────────────────────────────────────────────────

        [Fact]
        public void Email_Production_SentToTheRecipient_RedirectIgnored()
        {
            var routing = EmailRouting.From(Config(new() { ["EMAIL_REDIRECT_TO"] = "support@winplus.cm" }));
            Assert.Equal(("eleve@example.com", "Votre code"), routing.Route("eleve@example.com", "Votre code"));
        }

        [Fact]
        public void Email_Integration_RedirectedWithOriginalRecipientInSubject()
        {
            var routing = EmailRouting.From(Config(new() { ["WINPLUS_ENV"] = "integration", ["EMAIL_REDIRECT_TO"] = "support@winplus.cm" }));
            Assert.Equal(("support@winplus.cm", "[INTÉGRATION → eleve30@integration.winplus.cm] Votre code"),
                routing.Route("eleve30@integration.winplus.cm", "Votre code"));
        }

        [Fact]
        public void Email_IntegrationWithoutRedirect_SendsNothing()
        {
            var routing = EmailRouting.From(Config(new() { ["WINPLUS_ENV"] = "integration" }));
            Assert.Null(routing.Route("eleve30@integration.winplus.cm", "Votre code"));
        }

        [Fact]
        public async Task EmailService_Integration_PostsToRedirectAddress()
        {
            var handler = new BodyCapturingHandler();
            var factory = new Mock<IHttpClientFactory>();
            factory.Setup(f => f.CreateClient("ResendClient")).Returns(new HttpClient(handler) { BaseAddress = new Uri("https://api.resend.com") });
            var svc = new EmailService(factory.Object,
                Config(new() { ["WINPLUS_ENV"] = "integration", ["EMAIL_REDIRECT_TO"] = "support@winplus.cm", ["Resend:ApiKey"] = "re_test" }),
                NullLogger<EmailService>.Instance, new Mock<INotificationPreferenceService>().Object);

            Assert.True(await svc.SendGenericEmailAsync("eleve30@integration.winplus.cm", "Bienvenue", "<p>x</p>", NotificationCategory.Transactional));
            var body = Assert.Single(handler.Bodies);
            Assert.Contains("\"to\":[\"support@winplus.cm\"]", body);
            Assert.Contains("INT\\u00C9GRATION", body);
            Assert.DoesNotContain("\"to\":[\"eleve30", body);
        }

        [Fact]
        public async Task EmailService_IntegrationWithoutKeyNorRedirect_StartsAndSendsNothing()
        {
            var handler = new BodyCapturingHandler();
            var factory = new Mock<IHttpClientFactory>();
            factory.Setup(f => f.CreateClient("ResendClient")).Returns(new HttpClient(handler));
            var svc = new EmailService(factory.Object, Config(new() { ["WINPLUS_ENV"] = "integration" }),
                NullLogger<EmailService>.Instance, new Mock<INotificationPreferenceService>().Object);
            await svc.SendGenericEmailAsync("eleve30@integration.winplus.cm", "Bienvenue", "<p>x</p>", NotificationCategory.Transactional);
            Assert.Empty(handler.Bodies);
        }

        [Fact]
        public void EmailService_Production_StillRequiresAResendKey()
        {
            var factory = new Mock<IHttpClientFactory>();
            factory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(new HttpClient());
            Assert.Throws<InvalidOperationException>(() => new EmailService(factory.Object, Config(new()),
                NullLogger<EmailService>.Instance, new Mock<INotificationPreferenceService>().Object));
        }

        private sealed class BodyCapturingHandler : HttpMessageHandler
        {
            public readonly List<string> Bodies = new();
            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                Bodies.Add(request.Content == null ? "" : await request.Content.ReadAsStringAsync(cancellationToken));
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") };
            }
        }

        // ── Stockage ────────────────────────────────────────────────────────

        [Fact]
        public void Storage_RemoteObjectsPreserved_OnlyInIntegration()
        {
            Assert.True(StorageService.PreserveRemoteObjects(Config(new() { ["WINPLUS_ENV"] = "integration" })));
            Assert.False(StorageService.PreserveRemoteObjects(Config(new())));
        }
    }
}
