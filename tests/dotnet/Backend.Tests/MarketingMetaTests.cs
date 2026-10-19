using System.Net;
using System.Text;
using System.Text.Json;
using Backend.Controllers;
using Backend.Data;
using Backend.Models.Entities;
using Backend.Services.Marketing;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Backend.Tests
{
    /// <summary>
    /// Lot 10.3 bis (pixel Meta et API Conversions Meta) : conservation des
    /// identifiants Meta selon le consentement publicité, hachage normalisé de
    /// l'e-mail et du téléphone, corps de l'API Conversions (event_id
    /// identique au navigateur), envoi par la file (reprise, refus, jeton hors
    /// de l'adresse), configuration, et liens du générateur UTM.
    /// </summary>
    public class MarketingMetaTests
    {
        private const int UserId = 30;
        private static readonly DateTime Now = new(2026, 10, 19, 12, 0, 0, DateTimeKind.Utc);
        private const string Fbc = "fb.1.1760870000000.IwAR2abc_DEF-123";
        private const string Fbp = "fb.1.1760870000000.1234567890";

        private static AttributionDto Dto(bool analytics = true, bool ads = true) => new()
        {
            Fbclid = "IwAR2abc_DEF-123",
            Fbc = Fbc,
            Fbp = Fbp,
            UtmSource = "facebook",
            UtmMedium = "paid-social",
            UtmCampaign = "bac-2027",
            LandingPage = "/concours/bac",
            GaClientId = "1234567890.1700000000",
            CapturedAt = Now.AddDays(-1),
            ConsentAnalytics = analytics,
            ConsentAds = ads,
            ClientIp = "41.202.219.10",
            ClientUserAgent = "Mozilla/5.0 (Linux; Android 14)",
        };

        private static MarketingConfig MetaConfig(string? testCode = null) => new()
        {
            MetaPixelId = "123456789012345",
            MetaAccessToken = "EAAB-secret-token",
            MetaTestEventCode = testCode,
        };

        private sealed class StubHandler : HttpMessageHandler
        {
            public Queue<(HttpStatusCode Code, string Body)> Responses { get; } = new();
            public List<(Uri Url, string Body)> Requests { get; } = new();

            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            {
                Requests.Add((request.RequestUri!, request.Content == null ? "" : await request.Content.ReadAsStringAsync(ct)));
                var (code, body) = Responses.Count > 0 ? Responses.Dequeue() : (HttpStatusCode.OK, "{\"events_received\":1}");
                return new HttpResponseMessage(code) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
            }
        }

        private sealed class StubFactory : IHttpClientFactory
        {
            private readonly HttpMessageHandler _handler;
            public StubFactory(HttpMessageHandler handler) => _handler = handler;
            public HttpClient CreateClient(string name) => new(_handler, disposeHandler: false);
        }

        private static ConversionEvent Conversion(bool ads = true, DateTime? occurred = null) => new()
        {
            Id = 1,
            TransactionId = "ORD-20261019-0001",
            EventName = "purchase",
            Category = "epreuve",
            UserId = UserId,
            ValueXaf = 2500,
            Currency = "XAF",
            OccurredAt = occurred ?? DateTime.UtcNow.AddMinutes(-5),
            ConsentAds = ads,
            ConsentAnalytics = true,
            Fbc = ads ? Fbc : null,
            Fbp = ads ? Fbp : null,
            ClientIp = ads ? "41.202.219.10" : null,
            ClientUserAgent = ads ? "Mozilla/5.0 (Linux; Android 14)" : null,
            LandingPage = "/concours/bac",
            ItemsJson = "[{\"item_id\":\"epreuve-7\",\"item_name\":\"BAC C\",\"item_category\":\"epreuve\",\"price\":2500,\"quantity\":1}]",
        };

        private static TestApplicationDbContext SeedUser(string? name = null)
        {
            var db = TestApplicationDbContext.Create(name);
            db.Users.Add(new User { Id = UserId, Email = "  Eleve@Test.LOCAL ", Phone = "+237 6 99 12 34 56", Role = "student", IsActive = true });
            db.SaveChanges();
            return db;
        }

        // ── Conservation selon le consentement ─────────────────────────────

        [Fact]
        public void Sanitize_WithAdsConsent_KeepsMetaIdentifiersAndRequestData()
        {
            var a = AttributionRules.Sanitize(Dto(), Now)!;
            Assert.Equal("IwAR2abc_DEF-123", a.Fbclid);
            Assert.Equal(Fbc, a.Fbc);
            Assert.Equal(Fbp, a.Fbp);
            Assert.Equal("41.202.219.10", a.ClientIp);
            Assert.Equal("Mozilla/5.0 (Linux; Android 14)", a.ClientUserAgent);
        }

        [Fact]
        public void Sanitize_AnalyticsOnly_DropsEverythingMeta()
        {
            var a = AttributionRules.Sanitize(Dto(analytics: true, ads: false), Now)!;
            Assert.Null(a.Fbclid);
            Assert.Null(a.Fbc);
            Assert.Null(a.Fbp);
            Assert.Null(a.ClientIp);
            Assert.Null(a.ClientUserAgent);
            Assert.Equal("bac-2027", a.UtmCampaign);
        }

        [Fact]
        public void Sanitize_RejectsMalformedMetaCookiesAndIp()
        {
            var dto = Dto();
            dto.Fbc = "fb.1.123.<script>";
            dto.Fbp = "eleve@test.local";
            dto.ClientIp = "pas-une-ip";
            var a = AttributionRules.Sanitize(dto, Now)!;
            Assert.Null(a.Fbc);
            Assert.Null(a.Fbp);
            Assert.Null(a.ClientIp);
        }

        [Fact]
        public void ClientIpAndUserAgent_AreNeverReadFromTheRequestBody()
        {
            var json = "{\"consentAds\":true,\"consentAnalytics\":true,\"clientIp\":\"1.2.3.4\",\"clientUserAgent\":\"faux\"}";
            var dto = JsonSerializer.Deserialize<AttributionDto>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
            Assert.Null(dto.ClientIp);
            Assert.Null(dto.ClientUserAgent);
        }

        [Fact]
        public void FbcFor_UsesCookie_ElseRebuildsFromFbclid_OnlyWithAdsConsent()
        {
            var a = new MarketingAttribution { ConsentAds = true, Fbclid = "IwAR2abc", CapturedAt = new DateTime(2026, 10, 19, 0, 0, 0, DateTimeKind.Utc) };
            Assert.Equal($"fb.1.{new DateTimeOffset(a.CapturedAt.Value).ToUnixTimeMilliseconds()}.IwAR2abc", AttributionRules.FbcFor(a));
            a.Fbc = Fbc;
            Assert.Equal(Fbc, AttributionRules.FbcFor(a));
            a.ConsentAds = false;
            Assert.Null(AttributionRules.FbcFor(a));
        }

        [Fact]
        public async Task RecordForUser_StampsIpAndUserAgentFromRequest_OnlyWithAdsConsent()
        {
            using var db = SeedUser();
            var ctx = new DefaultHttpContext();
            ctx.Connection.RemoteIpAddress = IPAddress.Parse("41.202.219.10");
            ctx.Request.Headers.UserAgent = "Mozilla/5.0 (Android)";
            var svc = new AttributionService(db, NullLogger<AttributionService>.Instance, new HttpContextAccessor { HttpContext = ctx });

            var dto = Dto();
            dto.ClientIp = null;
            dto.ClientUserAgent = null;
            await svc.RecordForUserAsync(UserId, dto);
            var user = await db.Users.AsNoTracking().FirstAsync(u => u.Id == UserId);
            Assert.Equal("41.202.219.10", user.LastTouch!.ClientIp);
            Assert.Equal("Mozilla/5.0 (Android)", user.LastTouch.ClientUserAgent);

            // Publicité retirée (mesure gardée) : tout ce qui est Meta disparaît.
            await svc.RecordForUserAsync(UserId, new AttributionDto { ConsentAnalytics = true, ConsentAds = false });
            user = await db.Users.AsNoTracking().FirstAsync(u => u.Id == UserId);
            Assert.NotNull(user.LastTouch);
            Assert.Null(user.LastTouch!.ClientIp);
            Assert.Null(user.LastTouch.ClientUserAgent);
            Assert.Null(user.LastTouch.Fbc);
            Assert.Null(user.LastTouch.Fbp);
            Assert.Null(user.LastTouch.Fbclid);
        }

        // ── Registre : file meta selon le consentement ─────────────────────

        private static TestApplicationDbContext SeedOrder(MarketingAttribution attribution)
        {
            var db = SeedUser();
            db.Subjects.Add(new Subject { Id = 7, Title = "BAC C Mathématiques 2024" });
            db.Orders.Add(new Order
            {
                Id = 100, UserId = UserId, OrderNumber = "ORD-20261019-0001", TotalAmount = 2500, Status = "completed",
                CompletedDate = Now, Attribution = attribution,
                Items = new List<OrderItem> { new() { SubjectId = 7, PriceAtPurchase = 2500 } },
            });
            db.SaveChanges();
            return db;
        }

        [Fact]
        public async Task Recorder_WithAdsConsent_QueuesMeta_AndCopiesMetaAndCampaign()
        {
            using var db = SeedOrder(AttributionRules.Sanitize(Dto(), Now)!);
            var meta = new MetaConversionsApiDestination(MetaConfig(), new StubFactory(new StubHandler()), db);
            var conversion = await new ConversionRecorder(db, new IConversionDestination[] { meta }).RecordOrderAsync(100);

            Assert.NotNull(conversion);
            Assert.Equal(new[] { "meta" }, conversion!.Deliveries.Select(d => d.Destination));
            Assert.Equal(Fbc, conversion.Fbc);
            Assert.Equal(Fbp, conversion.Fbp);
            Assert.Equal("facebook", conversion.UtmSource);
            Assert.Equal("bac-2027", conversion.UtmCampaign);
            Assert.Equal("/concours/bac", conversion.LandingPage);
        }

        [Fact]
        public async Task Recorder_WithoutAdsConsent_DoesNotQueueMeta()
        {
            using var db = SeedOrder(AttributionRules.Sanitize(Dto(analytics: true, ads: false), Now)!);
            var meta = new MetaConversionsApiDestination(MetaConfig(), new StubFactory(new StubHandler()), db);
            var conversion = await new ConversionRecorder(db, new IConversionDestination[] { meta }).RecordOrderAsync(100);

            Assert.NotNull(conversion);
            Assert.Empty(conversion!.Deliveries);
            Assert.Null(conversion.Fbc);
            Assert.Null(conversion.ClientIp);
        }

        [Fact]
        public async Task Recorder_MetaNotConfigured_QueuesNothing()
        {
            using var db = SeedOrder(AttributionRules.Sanitize(Dto(), Now)!);
            var meta = new MetaConversionsApiDestination(new MarketingConfig(), new StubFactory(new StubHandler()), db);
            var conversion = await new ConversionRecorder(db, new IConversionDestination[] { meta }).RecordOrderAsync(100);
            Assert.Empty(conversion!.Deliveries);
        }

        // ── Normalisation et corps ─────────────────────────────────────────

        [Fact]
        public void Normalize_EmailAndCameroonPhone()
        {
            Assert.Equal("eleve@test.local", MetaConversionsApiDestination.NormalizeEmail("  Eleve@Test.LOCAL "));
            Assert.Null(MetaConversionsApiDestination.NormalizeEmail("pas une adresse"));
            Assert.Equal("237699123456", MetaConversionsApiDestination.NormalizePhone("+237 6 99 12 34 56"));
            Assert.Equal("237699123456", MetaConversionsApiDestination.NormalizePhone("699123456"));
            Assert.Equal("237699123456", MetaConversionsApiDestination.NormalizePhone("00237699123456"));
            Assert.Equal("33612345678", MetaConversionsApiDestination.NormalizePhone("+33 6 12 34 56 78"));
            Assert.Null(MetaConversionsApiDestination.NormalizePhone("12"));
        }

        [Fact]
        public void Sha256_IsLowercaseHex()
        {
            Assert.Equal("9a153a0666ae14f0c0d5014d8aee23598b5b41afe6a2189fddb0165221c4a871", MetaConversionsApiDestination.Sha256("eleve@test.local"));
        }

        [Fact]
        public void BuildBody_PurchaseWithSameEventId_HashedContact_NoPlainPersonalData()
        {
            var json = MetaConversionsApiDestination.BuildBody(Conversion(), "  Eleve@Test.LOCAL ", "+237 6 99 12 34 56", "TEST123");
            Assert.DoesNotContain("eleve@test.local", json, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("699123456", json);
            Assert.DoesNotContain("access_token", json);

            using var doc = JsonDocument.Parse(json);
            var ev = doc.RootElement.GetProperty("data")[0];
            Assert.Equal("Purchase", ev.GetProperty("event_name").GetString());
            Assert.Equal("ORD-20261019-0001", ev.GetProperty("event_id").GetString());
            Assert.Equal("website", ev.GetProperty("action_source").GetString());
            Assert.Equal("https://winplus.cm/concours/bac", ev.GetProperty("event_source_url").GetString());
            var user = ev.GetProperty("user_data");
            Assert.Equal("9a153a0666ae14f0c0d5014d8aee23598b5b41afe6a2189fddb0165221c4a871", user.GetProperty("em")[0].GetString());
            Assert.Equal("ecc0be9534271b5e0ab7292d6b5f56de1351581811ccefc4112141886e2376c6", user.GetProperty("ph")[0].GetString());
            Assert.Equal(Fbc, user.GetProperty("fbc").GetString());
            Assert.Equal(Fbp, user.GetProperty("fbp").GetString());
            Assert.Equal("41.202.219.10", user.GetProperty("client_ip_address").GetString());
            var custom = ev.GetProperty("custom_data");
            Assert.Equal(2500m, custom.GetProperty("value").GetDecimal());
            Assert.Equal("XAF", custom.GetProperty("currency").GetString());
            Assert.Equal("epreuve-7", custom.GetProperty("content_ids")[0].GetString());
            Assert.Equal("TEST123", doc.RootElement.GetProperty("test_event_code").GetString());
        }

        [Fact]
        public void BuildBody_WithoutAdsConsent_HasNoUserData()
        {
            var json = MetaConversionsApiDestination.BuildBody(Conversion(ads: false), "eleve@test.local", "699123456", null);
            using var doc = JsonDocument.Parse(json);
            var ev = doc.RootElement.GetProperty("data")[0];
            Assert.Equal(0, ev.GetProperty("user_data").EnumerateObject().Count());
            Assert.Equal("other", ev.GetProperty("action_source").GetString());
            Assert.False(doc.RootElement.TryGetProperty("test_event_code", out _));
        }

        // ── Envoi ──────────────────────────────────────────────────────────

        [Fact]
        public async Task Send_PostsToPixelEndpoint_TokenInBodyOnly_ContactHashedFromAccount()
        {
            using var db = SeedUser();
            var handler = new StubHandler();
            var meta = new MetaConversionsApiDestination(MetaConfig(), new StubFactory(handler), db);

            var result = await meta.SendAsync(Conversion(), CancellationToken.None);

            Assert.Equal(ConversionSendOutcome.Sent, result.Outcome);
            var (url, body) = Assert.Single(handler.Requests);
            Assert.Equal($"https://graph.facebook.com/{MarketingConfig.DefaultMetaGraphVersion}/123456789012345/events", url.ToString());
            Assert.DoesNotContain("EAAB", url.ToString());
            using var doc = JsonDocument.Parse(body);
            Assert.Equal("EAAB-secret-token", doc.RootElement.GetProperty("access_token").GetString());
            Assert.Equal("9a153a0666ae14f0c0d5014d8aee23598b5b41afe6a2189fddb0165221c4a871",
                doc.RootElement.GetProperty("data")[0].GetProperty("user_data").GetProperty("em")[0].GetString());
        }

        [Fact]
        public async Task Send_ServerErrorOrTransient_Retries_BadRequest_Rejected()
        {
            using var db = SeedUser();
            var handler = new StubHandler();
            handler.Responses.Enqueue((HttpStatusCode.InternalServerError, "{}"));
            handler.Responses.Enqueue((HttpStatusCode.BadRequest, "{\"error\":{\"code\":2,\"is_transient\":true}}"));
            handler.Responses.Enqueue((HttpStatusCode.BadRequest, "{\"error\":{\"message\":\"Invalid parameter eleve@test.local\",\"code\":100,\"error_subcode\":2804003}}"));
            var meta = new MetaConversionsApiDestination(MetaConfig(), new StubFactory(handler), db);

            Assert.Equal(ConversionSendOutcome.Retry, (await meta.SendAsync(Conversion(), default)).Outcome);
            Assert.Equal(ConversionSendOutcome.Retry, (await meta.SendAsync(Conversion(), default)).Outcome);
            var rejected = await meta.SendAsync(Conversion(), default);
            Assert.Equal(ConversionSendOutcome.Rejected, rejected.Outcome);
            Assert.Equal("HTTP 400 (code 100, sous-code 2804003)", rejected.Error);
        }

        [Fact]
        public async Task Send_OlderThanSevenDays_OrWithoutAdsConsent_IsRejectedWithoutCall()
        {
            using var db = SeedUser();
            var handler = new StubHandler();
            var meta = new MetaConversionsApiDestination(MetaConfig(), new StubFactory(handler), db);

            Assert.Equal(ConversionSendOutcome.Rejected, (await meta.SendAsync(Conversion(occurred: DateTime.UtcNow.AddDays(-8)), default)).Outcome);
            Assert.Equal(ConversionSendOutcome.Rejected, (await meta.SendAsync(Conversion(ads: false), default)).Outcome);
            Assert.Empty(handler.Requests);
        }

        [Fact]
        public async Task Dispatcher_SendsMetaOnce_ThenNeverAgain()
        {
            using var db = SeedOrder(AttributionRules.Sanitize(Dto(), Now)!);
            var handler = new StubHandler();
            var meta = new MetaConversionsApiDestination(MetaConfig(), new StubFactory(handler), db);
            var conversion = await new ConversionRecorder(db, new IConversionDestination[] { meta }).RecordOrderAsync(100);
            // Achat récent : la limite de 7 jours de Meta ne s'applique pas ici.
            conversion!.OccurredAt = DateTime.UtcNow.AddMinutes(-1);
            await db.SaveChangesAsync();

            var dispatcher = new ConversionDispatcher(db, new IConversionDestination[] { meta });
            Assert.Equal(1, await dispatcher.ProcessDueAsync(DateTime.UtcNow.AddMinutes(1)));
            Assert.Equal(0, await dispatcher.ProcessDueAsync(DateTime.UtcNow.AddHours(1)));
            Assert.Single(handler.Requests);
            var delivery = await db.ConversionDeliveries.SingleAsync();
            Assert.Equal(ConversionDeliveryStatus.Sent, delivery.Status);
        }

        // ── Configuration ──────────────────────────────────────────────────

        [Fact]
        public void MarketingConfig_Meta_RequiresDigitsPixelAndToken()
        {
            Assert.False(MarketingConfig.From(new ConfigurationBuilder().Build()).MetaConfigured);
            Assert.False(MarketingConfig.From(new ConfigurationBuilder().Build()).AnyConfigured);

            var cfg = MarketingConfig.From(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["META_PIXEL_ID"] = " 123456789012345 ",
                ["META_CAPI_ACCESS_TOKEN"] = "EAAB-secret",
                ["META_TEST_EVENT_CODE"] = "TEST42",
            }).Build());
            Assert.True(cfg.MetaConfigured);
            Assert.True(cfg.AnyConfigured);
            Assert.False(cfg.Ga4Configured);
            Assert.Equal("TEST42", cfg.MetaTestEventCode);
            Assert.Equal(MarketingConfig.DefaultMetaGraphVersion, cfg.MetaGraphVersion);

            Assert.False(new MarketingConfig { MetaPixelId = "PIXEL-ABC", MetaAccessToken = "x" }.MetaConfigured);
            Assert.False(new MarketingConfig { MetaPixelId = "123456789012345" }.MetaConfigured);
        }

        // ── Générateur de liens UTM ────────────────────────────────────────

        [Fact]
        public void UtmLink_Parse_AcceptsConvention_RejectsOtherHostsAndBadValues()
        {
            var ok = AdminUtmLinksController.Parse(
                "https://winplus.cm/concours/bac?utm_source=whatsapp&utm_medium=statut&utm_campaign=bac-2027&utm_content=visuel-1", out var err);
            Assert.Null(err);
            Assert.Equal("/concours/bac", ok!.Destination);
            Assert.Equal("whatsapp", ok.UtmSource);
            Assert.Equal("statut", ok.UtmMedium);
            Assert.Equal("bac-2027", ok.UtmCampaign);
            Assert.Equal("visuel-1", ok.UtmContent);

            Assert.Null(AdminUtmLinksController.Parse("https://exemple.com/?utm_source=a&utm_medium=b&utm_campaign=c", out _));
            Assert.Null(AdminUtmLinksController.Parse("http://winplus.cm/?utm_source=a&utm_medium=b&utm_campaign=c", out _));
            Assert.Null(AdminUtmLinksController.Parse("https://winplus.cm/?utm_source=a&utm_medium=b", out _));
            Assert.Null(AdminUtmLinksController.Parse("https://winplus.cm/?utm_source=WhatsApp&utm_medium=b&utm_campaign=c", out _));
            Assert.Null(AdminUtmLinksController.Parse("https://winplus.cm/?utm_source=a&utm_medium=b&utm_campaign=bac%202027", out _));
        }
    }
}
