using System.Text.Json;
using Backend.Data;
using Backend.Models.Entities;
using Backend.Services.Marketing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Backend.Tests
{
    /// <summary>
    /// Lot 10.3 (consentement et mesure) : règles de conservation de
    /// l'attribution, registre des conversions sans doublon, file d'envoi
    /// avec reprise, corps GA4 Measurement Protocol et export CSV Google Ads.
    /// </summary>
    public class Marketing103Tests
    {
        private const int UserId = 30;
        private static readonly DateTime Now = new(2026, 10, 18, 12, 0, 0, DateTimeKind.Utc);

        private sealed class FakeDestination : IConversionDestination
        {
            public bool Configured { get; set; } = true;
            public Queue<ConversionSendResult> Results { get; } = new();
            public List<ConversionEvent> Sent { get; } = new();
            public string Name => "ga4";
            public bool IsConfigured => Configured;
            public bool Accepts(ConversionEvent c) => c.ConsentAnalytics && c.GaClientId != null;
            public Task<ConversionSendResult> SendAsync(ConversionEvent c, CancellationToken ct)
            {
                Sent.Add(c);
                return Task.FromResult(Results.Count > 0 ? Results.Dequeue() : ConversionSendResult.Ok);
            }
        }

        private static AttributionDto Dto(bool analytics = true, bool ads = true) => new()
        {
            Gclid = "Cj0KCQjw-abc_123",
            UtmSource = "google",
            UtmMedium = "cpc",
            UtmCampaign = "bac-2027",
            Referrer = "https://www.google.com/search?q=bac+cameroun",
            LandingPage = "/concours/bac?gclid=Cj0KCQjw-abc_123",
            GaClientId = "1234567890.1700000000",
            GaSessionId = "1700000000",
            CapturedAt = Now.AddDays(-2),
            ConsentAnalytics = analytics,
            ConsentAds = ads,
        };

        private static TestApplicationDbContext Seed(string? name = null, InMemoryDatabaseRoot? root = null, string orderStatus = "completed",
            string orderNumber = "ORD-20261018-1234", MarketingAttribution? orderAttribution = null, MarketingAttribution? lastTouch = null)
        {
            var db = root == null ? TestApplicationDbContext.Create(name) : TestApplicationDbContext.Create(name!, root);
            db.Users.Add(new User { Id = UserId, Email = "eleve@test.local", Role = "student", IsActive = true, LastTouch = lastTouch });
            db.Subjects.Add(new Subject { Id = 7, Title = "BAC C Mathématiques 2024" });
            db.Orders.Add(new Order
            {
                Id = 100, UserId = UserId, OrderNumber = orderNumber, TotalAmount = 2500, Status = orderStatus,
                CompletedDate = Now, Attribution = orderAttribution,
                Items = new List<OrderItem> { new() { SubjectId = 7, PriceAtPurchase = 2500 } },
            });
            db.SaveChanges();
            return db;
        }

        // ── Règles de conservation ─────────────────────────────────────────

        [Fact]
        public void Sanitize_WithoutConsent_KeepsNothing()
        {
            Assert.Null(AttributionRules.Sanitize(Dto(analytics: false, ads: false), Now));
            Assert.Null(AttributionRules.Sanitize(null, Now));
        }

        [Fact]
        public void Sanitize_AnalyticsOnly_DropsClickIdentifiers_KeepsCampaignAndClientId()
        {
            var a = AttributionRules.Sanitize(Dto(analytics: true, ads: false), Now)!;
            Assert.Null(a.Gclid);
            Assert.Equal("google", a.UtmSource);
            Assert.Equal("1234567890.1700000000", a.GaClientId);
            Assert.Equal("www.google.com", a.Referrer); // domaine seulement, jamais la recherche
            Assert.Equal("/concours/bac", a.LandingPage); // sans paramètres
        }

        [Fact]
        public void Sanitize_AdsOnly_KeepsGclid_DropsGaClientId()
        {
            var a = AttributionRules.Sanitize(Dto(analytics: false, ads: true), Now)!;
            Assert.Equal("Cj0KCQjw-abc_123", a.Gclid);
            Assert.Null(a.GaClientId);
            Assert.Null(a.GaSessionId);
        }

        [Fact]
        public void Sanitize_RejectsPersonalDataAndMalformedValues()
        {
            var dto = Dto();
            dto.UtmContent = "parent@example.com";
            dto.Gclid = "abc def<script>";
            dto.GaClientId = "GA1.1.123.456";
            dto.CapturedAt = Now.AddYears(-1);
            var a = AttributionRules.Sanitize(dto, Now)!;
            Assert.Null(a.UtmContent);
            Assert.Null(a.Gclid);
            Assert.Null(a.GaClientId);
            Assert.Equal(Now, a.CapturedAt); // hors fenêtre : ramené à maintenant
        }

        // ── Compte et commande ─────────────────────────────────────────────

        [Fact]
        public async Task RecordForUser_KeepsFirstTouch_ReplacesLastTouch_AndClearsOnWithdrawal()
        {
            using var db = Seed();
            var svc = new AttributionService(db, NullLogger<AttributionService>.Instance);

            await svc.RecordForUserAsync(UserId, Dto());
            var second = Dto(); second.UtmCampaign = "probatoire"; second.Gclid = "second-click";
            await svc.RecordForUserAsync(UserId, second);

            var user = await db.Users.FirstAsync(u => u.Id == UserId);
            Assert.Equal("bac-2027", user.FirstTouch!.UtmCampaign);
            Assert.Equal("probatoire", user.LastTouch!.UtmCampaign);
            Assert.Equal("second-click", user.LastTouch.Gclid);

            await svc.RecordForUserAsync(UserId, new AttributionDto { ConsentAnalytics = false, ConsentAds = false });
            user = await db.Users.FirstAsync(u => u.Id == UserId);
            Assert.Null(user.FirstTouch);
            Assert.Null(user.LastTouch);
        }

        [Fact]
        public async Task RecordForUser_UsesBrowserFirstTouch_ForANewAccount()
        {
            using var db = Seed();
            var svc = new AttributionService(db, NullLogger<AttributionService>.Instance);
            var dto = Dto();
            dto.UtmCampaign = "probatoire";
            dto.First = new AttributionDto { UtmSource = "facebook", UtmMedium = "social", CapturedAt = Now.AddDays(-10) };

            await svc.RecordForUserAsync(UserId, dto);

            var user = await db.Users.FirstAsync(u => u.Id == UserId);
            Assert.Equal("facebook", user.FirstTouch!.UtmSource);
            Assert.True(user.FirstTouch.ConsentAnalytics);
            Assert.Equal("probatoire", user.LastTouch!.UtmCampaign);
        }

        [Fact]
        public async Task RecordForUser_ConsentOnlyUpdate_KeepsSource_ButDropsGclidWhenAdsRefused()
        {
            using var db = Seed();
            var svc = new AttributionService(db, NullLogger<AttributionService>.Instance);
            await svc.RecordForUserAsync(UserId, Dto());

            await svc.RecordForUserAsync(UserId, new AttributionDto { ConsentAnalytics = true, ConsentAds = false, GaClientId = "1.2" });

            var user = await db.Users.FirstAsync(u => u.Id == UserId);
            Assert.Equal("bac-2027", user.LastTouch!.UtmCampaign);
            Assert.Null(user.LastTouch.Gclid);
            Assert.False(user.LastTouch.ConsentAds);
        }

        [Fact]
        public async Task ApplyToOrder_WithoutPayload_FallsBackToUserLastTouch()
        {
            var last = AttributionRules.Sanitize(Dto(), DateTime.UtcNow)!;
            last.CapturedAt = DateTime.UtcNow.AddDays(-1);
            using var db = Seed(lastTouch: last, orderStatus: "pending");
            var svc = new AttributionService(db, NullLogger<AttributionService>.Instance);

            await svc.ApplyToOrderAsync(100, UserId, null);

            var order = await db.Orders.FirstAsync(o => o.Id == 100);
            Assert.Equal("Cj0KCQjw-abc_123", order.Attribution!.Gclid);
        }

        // ── Registre des conversions ───────────────────────────────────────

        [Fact]
        public async Task RecordOrder_PaidWithConsent_CreatesOneConversion_AndOneGa4Delivery()
        {
            using var db = Seed(orderAttribution: AttributionRules.Sanitize(Dto(), Now));
            var dest = new FakeDestination();
            var recorder = new ConversionRecorder(db, new[] { dest });

            var first = await recorder.RecordOrderAsync(100);
            var replay = await recorder.RecordOrderAsync(100);

            Assert.NotNull(first);
            Assert.Null(replay); // webhook rejoué ou synchronisation : aucun doublon
            var conversion = await db.ConversionEvents.Include(c => c.Deliveries).SingleAsync();
            Assert.Equal("ORD-20261018-1234", conversion.TransactionId);
            Assert.Equal(2500, conversion.ValueXaf);
            Assert.Equal("XAF", conversion.Currency);
            Assert.Equal("epreuve", conversion.Category);
            Assert.Single(conversion.Deliveries);
            Assert.Contains("BAC C Math", conversion.ItemsJson);
        }

        [Fact]
        public async Task RecordOrder_WithoutConsent_OrUnpaid_OrTopUp_RecordsNothing()
        {
            using (var db = Seed())
                Assert.Null(await new ConversionRecorder(db, new[] { new FakeDestination() }).RecordOrderAsync(100));
            using (var db = Seed(orderStatus: "pending", orderAttribution: AttributionRules.Sanitize(Dto(), Now)))
                Assert.Null(await new ConversionRecorder(db, new[] { new FakeDestination() }).RecordOrderAsync(100));
            using (var db = Seed(orderNumber: "WP-RCH-20261018-ABCDEF", orderAttribution: AttributionRules.Sanitize(Dto(), Now)))
                Assert.Null(await new ConversionRecorder(db, new[] { new FakeDestination() }).RecordOrderAsync(100));
        }

        [Fact]
        public async Task RecordOrder_Ga4NotConfigured_KeepsConversionForGoogleAdsExport_WithoutDelivery()
        {
            using var db = Seed(orderAttribution: AttributionRules.Sanitize(Dto(), Now));
            var recorder = new ConversionRecorder(db, new[] { new FakeDestination { Configured = false } });

            var conversion = await recorder.RecordOrderAsync(100);

            Assert.NotNull(conversion);
            Assert.Empty(await db.ConversionDeliveries.ToListAsync());
        }

        [Fact]
        public async Task RecordTutorBooking_UsesPayerLastTouch()
        {
            var last = AttributionRules.Sanitize(Dto(), DateTime.UtcNow)!;
            last.CapturedAt = DateTime.UtcNow.AddDays(-1);
            using var db = Seed(lastTouch: last);
            db.TutorBookings.Add(new TutorBooking
            {
                Id = 5, TutorProfileId = 1, StudentUserId = UserId, PayerUserId = UserId, PriceXaf = 9000,
                Status = "pending_confirmation", PaymentStatus = "completed", NotchpayReference = "TBK-5-xyz",
                SessionDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(3)), StartTime = new TimeSpan(10, 0, 0), EndTime = new TimeSpan(11, 0, 0),
            });
            db.SaveChanges();

            var conversion = await new ConversionRecorder(db, new[] { new FakeDestination() }).RecordTutorBookingAsync("TBK-5-xyz");

            Assert.NotNull(conversion);
            Assert.Equal("repetiteur", conversion!.Category);
            Assert.Equal(9000, conversion.ValueXaf);
        }

        [Fact]
        public async Task Tracker_UsesItsOwnScope_AndNeverThrows()
        {
            var root = new InMemoryDatabaseRoot();
            var name = $"tracker-{Guid.NewGuid()}";
            using (Seed(name, root, orderAttribution: AttributionRules.Sanitize(Dto(), Now))) { }

            var services = new ServiceCollection();
            services.AddScoped<ApplicationDbContext>(_ => TestApplicationDbContext.Create(name, root));
            services.AddScoped<IConversionDestination, FakeDestination>();
            using var provider = services.BuildServiceProvider();
            var tracker = new ConversionTracker(provider.GetRequiredService<IServiceScopeFactory>(), new ConversionSignal(), NullLogger<ConversionTracker>.Instance);

            await tracker.RecordOrderPurchaseAsync(100);
            await tracker.RecordOrderPurchaseAsync(999); // commande inconnue : silencieux

            using var check = TestApplicationDbContext.Create(name, root);
            Assert.Equal(1, await check.ConversionEvents.CountAsync());
        }

        // ── File d'envoi ───────────────────────────────────────────────────

        [Fact]
        public async Task Dispatcher_SendsOnce_RetriesTemporaryErrors_AndStopsOnRejection()
        {
            using var db = Seed(orderAttribution: AttributionRules.Sanitize(Dto(), Now));
            var dest = new FakeDestination();
            await new ConversionRecorder(db, new[] { dest }).RecordOrderAsync(100);
            var dispatcher = new ConversionDispatcher(db, new[] { dest });
            var t0 = DateTime.UtcNow.AddMinutes(1);

            dest.Results.Enqueue(new ConversionSendResult(ConversionSendOutcome.Retry, "HTTP 503"));
            Assert.Equal(0, await dispatcher.ProcessDueAsync(t0));
            var delivery = await db.ConversionDeliveries.SingleAsync();
            Assert.Equal(ConversionDeliveryStatus.Pending, delivery.Status);
            Assert.Equal(1, delivery.Attempts);
            Assert.Equal(t0 + ConversionDispatcher.DelayAfter(1), delivery.NextAttemptAt);

            Assert.Equal(0, await dispatcher.ProcessDueAsync(t0)); // pas encore dû
            Assert.Single(dest.Sent);

            Assert.Equal(1, await dispatcher.ProcessDueAsync(t0.AddHours(1)));
            Assert.Equal(0, await dispatcher.ProcessDueAsync(t0.AddDays(2))); // déjà envoyé : rien
            Assert.Equal(2, dest.Sent.Count);
            Assert.Equal(ConversionDeliveryStatus.Sent, (await db.ConversionDeliveries.SingleAsync()).Status);
        }

        [Fact]
        public async Task Dispatcher_RejectedOrTooManyAttempts_MarksFailed()
        {
            using var db = Seed(orderAttribution: AttributionRules.Sanitize(Dto(), Now));
            var dest = new FakeDestination();
            await new ConversionRecorder(db, new[] { dest }).RecordOrderAsync(100);
            var dispatcher = new ConversionDispatcher(db, new[] { dest });

            dest.Results.Enqueue(new ConversionSendResult(ConversionSendOutcome.Rejected, "HTTP 400"));
            await dispatcher.ProcessDueAsync(DateTime.UtcNow.AddMinutes(1));
            Assert.Equal(ConversionDeliveryStatus.Failed, (await db.ConversionDeliveries.SingleAsync()).Status);
        }

        [Fact]
        public async Task Dispatcher_DestinationNotConfigured_DoesNothing()
        {
            using var db = Seed(orderAttribution: AttributionRules.Sanitize(Dto(), Now));
            var dest = new FakeDestination();
            await new ConversionRecorder(db, new[] { dest }).RecordOrderAsync(100);
            dest.Configured = false;

            Assert.Equal(0, await new ConversionDispatcher(db, new[] { dest }).ProcessDueAsync(DateTime.UtcNow.AddMinutes(1)));
            Assert.Empty(dest.Sent);
        }

        // ── GA4 et Google Ads ──────────────────────────────────────────────

        [Fact]
        public void Ga4Body_CarriesTransactionId_XafValue_ClientIdAndConsent_WithoutPersonalData()
        {
            var c = new ConversionEvent
            {
                TransactionId = "ORD-1", ValueXaf = 2500.4m, Currency = "XAF", Category = "epreuve", OccurredAt = Now.AddHours(-1),
                GaClientId = "123.456", GaSessionId = "1700000000", ConsentAnalytics = true, ConsentAds = false,
                ItemsJson = "[{\"item_id\":\"epreuve-7\",\"price\":2500,\"quantity\":1}]",
            };
            using var doc = JsonDocument.Parse(Ga4MeasurementProtocolDestination.BuildBody(c, Now));
            var root = doc.RootElement;
            Assert.Equal("123.456", root.GetProperty("client_id").GetString());
            Assert.Equal("DENIED", root.GetProperty("consent").GetProperty("ad_user_data").GetString());
            Assert.True(root.TryGetProperty("timestamp_micros", out _));
            var ev = root.GetProperty("events")[0];
            Assert.Equal("purchase", ev.GetProperty("name").GetString());
            var p = ev.GetProperty("params");
            Assert.Equal("ORD-1", p.GetProperty("transaction_id").GetString());
            Assert.Equal("XAF", p.GetProperty("currency").GetString());
            Assert.Equal(2500, p.GetProperty("value").GetDecimal());
            Assert.Equal(1700000000, p.GetProperty("session_id").GetInt64());
            Assert.Equal("epreuve-7", p.GetProperty("items")[0].GetProperty("item_id").GetString());
            Assert.DoesNotContain("@", root.GetRawText());

            // Au-delà de 72 h, GA4 refuse l'horodatage : l'achat part sans.
            c.OccurredAt = Now.AddDays(-4);
            using var old = JsonDocument.Parse(Ga4MeasurementProtocolDestination.BuildBody(c, Now));
            Assert.False(old.RootElement.TryGetProperty("timestamp_micros", out _));
        }

        [Fact]
        public void MarketingConfig_WithoutVariables_IsNotConfigured()
        {
            var empty = MarketingConfig.From(new ConfigurationBuilder().Build());
            Assert.False(empty.Ga4Configured);

            var set = MarketingConfig.From(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["GA4_MEASUREMENT_ID"] = "G-ABC123", ["GA4_API_SECRET"] = "secret",
            }).Build());
            Assert.True(set.Ga4Configured);
        }

        [Fact]
        public void GoogleAdsCsv_UsesImportTemplate_DoualaTime_AndOnlyConsentedGclids()
        {
            var rows = new[]
            {
                new ConversionEvent { TransactionId = "A", Gclid = "gclid-1", ConsentAds = true, ValueXaf = 2500, Currency = "XAF", OccurredAt = new DateTime(2026, 10, 18, 23, 30, 0, DateTimeKind.Utc) },
                new ConversionEvent { TransactionId = "B", Gclid = "gclid-2", ConsentAds = false, ValueXaf = 1000, Currency = "XAF", OccurredAt = Now },
                new ConversionEvent { TransactionId = "C", Gclid = null, ConsentAds = true, ValueXaf = 1000, Currency = "XAF", OccurredAt = Now },
            };

            var lines = GoogleAdsOfflineExport.BuildCsv(rows, "Achat WinPlus").Split("\r\n", StringSplitOptions.RemoveEmptyEntries);

            Assert.Equal("Parameters:TimeZone=Africa/Douala", lines[0]);
            Assert.Equal("Google Click ID,Conversion Name,Conversion Time,Conversion Value,Conversion Currency", lines[1]);
            Assert.Equal(3, lines.Length);
            Assert.Equal("gclid-1,Achat WinPlus,2026-10-19 00:30:00,2500,XAF", lines[2]);
        }
    }
}
