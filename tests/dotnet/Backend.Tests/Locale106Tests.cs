using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Claims;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Backend.Data;
using Backend.Models.Entities;
using Backend.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Backend.Tests
{
    /// <summary>
    /// Lot 10.6 (multilingue) : e-mails dans la langue du compte (User.Locale)
    /// et langue transmise au service IA dans X-User-Locale. Le français doit
    /// rester le texte historique.
    /// </summary>
    public class Locale106Tests
    {
        private sealed class CaptureHandler : HttpMessageHandler
        {
            public readonly List<HttpRequestMessage> Requests = new();
            public readonly List<string> Bodies = new();
            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            {
                Requests.Add(request);
                Bodies.Add(request.Content == null ? "" : await request.Content.ReadAsStringAsync(ct));
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") };
            }
        }

        private static IConfiguration Config() => new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Resend:ApiKey"] = "test",
                ["AIService:BaseUrl"] = "http://ai.internal:5000",
            }).Build();

        // ── E-mails ────────────────────────────────────────────────────────

        private static (EmailService svc, CaptureHandler cap) Email()
        {
            var cap = new CaptureHandler();
            var factory = new Mock<IHttpClientFactory>();
            factory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(() => new HttpClient(cap));
            var prefs = new Mock<INotificationPreferenceService>();
            prefs.Setup(p => p.FilterEmailsAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<NotificationCategory>(), It.IsAny<CancellationToken>()))
                 .ReturnsAsync((IEnumerable<string> e, NotificationCategory _, CancellationToken _) => e.ToList());
            return (new EmailService(factory.Object, Config(), NullLogger<EmailService>.Instance, prefs.Object), cap);
        }

        private static (string subject, string html) Sent(CaptureHandler cap)
        {
            using var doc = JsonDocument.Parse(cap.Bodies.Single());
            return (doc.RootElement.GetProperty("subject").GetString()!, doc.RootElement.GetProperty("html").GetString()!);
        }

        [Fact]
        public async Task PaymentConfirmation_InEnglish_ForEnglishAccount()
        {
            var (svc, cap) = Email();
            await svc.SendPaymentConfirmationAsync("a@test.local", "Awa", 2500m, "WP-1", new DateTime(2026, 10, 1, 9, 0, 0),
                new[] { ("BAC C 2024", 7) }, "en");
            var (subject, html) = Sent(cap);
            Assert.StartsWith("Payment receipt: ", subject);
            Assert.EndsWith(" XAF", subject);
            Assert.Contains("<html lang=\"en\"", html);
            Assert.Contains("Papers purchased", html);
            Assert.Contains("https://winplus.cm/en/catalog/7", html);
            Assert.Contains("All rights reserved.", html);
            Assert.DoesNotContain("Reçu de paiement", html);
            Assert.DoesNotContain("Tous droits réservés", html);
        }

        [Fact]
        public async Task PaymentConfirmation_FrenchByDefault_Unchanged()
        {
            var (svc, cap) = Email();
            await svc.SendPaymentConfirmationAsync("a@test.local", "Awa", 2500m, "WP-1", new DateTime(2026, 10, 1, 9, 0, 0),
                new[] { ("BAC C 2024", 7) });
            var (subject, html) = Sent(cap);
            Assert.StartsWith("Reçu de paiement", subject);
            Assert.Contains("<html lang=\"fr\"", html);
            Assert.Contains("Épreuves achetées", html);
            Assert.Contains("https://winplus.cm/catalog/7", html);
            Assert.Contains("01/10/2026 à 09:00 UTC", html);
            Assert.Contains("Tous droits réservés.", html);
        }

        [Theory]
        [InlineData("en", "Your WinPlus subscription expires in 3 days", "01 October 2026")]
        [InlineData("en-GB", "Your WinPlus subscription expires in 3 days", "01 October 2026")]
        [InlineData("fr", "Votre abonnement WinPlus expire dans 3 jours", "01 octobre 2026")]
        [InlineData("", "Votre abonnement WinPlus expire dans 3 jours", "01 octobre 2026")]
        public async Task SubscriptionReminder_FollowsLocale(string locale, string expectedSubject, string expectedDate)
        {
            var (svc, cap) = Email();
            await svc.SendSubscriptionExpiryReminderAsync("a@test.local", "Awa", new DateTime(2026, 10, 1), locale);
            var (subject, html) = Sent(cap);
            Assert.Equal(expectedSubject, subject);
            Assert.Contains(expectedDate, html);
        }

        [Fact]
        public async Task PasswordChanged_And_PeriodicCode_InEnglish()
        {
            var (svc, cap) = Email();
            await svc.SendPasswordChangedAsync("a@test.local", "Awa", "en");
            await svc.SendPeriodicConfirmationAsync("a@test.local", "Awa", "123456", "en");
            Assert.Contains("\"subject\":\"Your WinPlus password has been changed\"", cap.Bodies[0]);
            Assert.Contains("\"subject\":\"Your WinPlus confirmation code\"", cap.Bodies[1]);
        }

        // ── X-User-Locale vers le service IA ──────────────────────────────

        private static async Task<HttpRequestMessage> SendThroughHandler(string url, string? userLocale, bool authenticated = true, string? presetHeader = null)
        {
            var db = TestApplicationDbContext.Create();
            db.Users.Add(new User { Id = 42, Email = "u@test.local", Role = "student", Locale = userLocale ?? "fr" });
            db.SaveChanges();

            var services = new ServiceCollection();
            services.AddSingleton<ApplicationDbContext>(db);
            var ctx = new DefaultHttpContext { RequestServices = services.BuildServiceProvider() };
            if (authenticated)
                ctx.User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim("user_id", "42") }, "test"));
            var accessor = new HttpContextAccessor { HttpContext = ctx };

            var cap = new CaptureHandler();
            var handler = new AiLocaleHeaderHandler(accessor, Config()) { InnerHandler = cap };
            using var client = new HttpClient(handler);
            var req = new HttpRequestMessage(HttpMethod.Post, url);
            if (presetHeader != null) req.Headers.Add(AiLocaleHeaderHandler.HeaderName, presetHeader);
            await client.SendAsync(req);
            return cap.Requests.Single();
        }

        private static string? Header(HttpRequestMessage r) =>
            r.Headers.TryGetValues(AiLocaleHeaderHandler.HeaderName, out var v) ? v.Single() : null;

        [Fact]
        public async Task AiHost_ReceivesProfileLocale()
            => Assert.Equal("en", Header(await SendThroughHandler("http://ai.internal:5000/api/ai/quiz", "en")));

        [Fact]
        public async Task AiHost_FrenchProfile_SendsFr()
            => Assert.Equal("fr", Header(await SendThroughHandler("http://ai.internal:5000/api/ai/quiz", "fr")));

        [Fact]
        public async Task OtherHosts_NeverReceiveTheHeader()
            => Assert.Null(Header(await SendThroughHandler("https://api.resend.com/emails", "en")));

        [Fact]
        public async Task Anonymous_NoHeader()
            => Assert.Null(Header(await SendThroughHandler("http://ai.internal:5000/api/ai/quiz", "en", authenticated: false)));

        [Fact]
        public async Task ExplicitHeader_IsKept()
            => Assert.Equal("fr", Header(await SendThroughHandler("http://ai.internal:5000/api/ai/quiz", "en", presetHeader: "fr")));
    }
}
