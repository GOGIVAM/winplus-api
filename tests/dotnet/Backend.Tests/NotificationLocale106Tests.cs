using System;
using System.Threading.Tasks;
using Backend.Data;
using Backend.Models.DTOs;
using Backend.Models.Entities;
using Backend.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Backend.Tests
{
    /// <summary>
    /// Lot 10.6 (multilingue, tranche 2) : notifications ntfy et liste in-app
    /// dans la langue du destinataire (User.Locale). Le français reste le texte
    /// historique ; sans destinataire identifié, on reste en français.
    /// </summary>
    public class NotificationLocale106Tests
    {
        private static ApplicationDbContext Db(params (int id, string locale)[] users)
        {
            var db = TestApplicationDbContext.Create();
            foreach (var (id, locale) in users)
                db.Users.Add(new User { Id = id, Email = $"u{id}@test.local", Role = "student", Locale = locale, FirstName = "U", LastName = id.ToString() });
            db.SaveChanges();
            return db;
        }

        private static Mock<INtfyService> Ntfy()
        {
            var ntfy = new Mock<INtfyService>();
            ntfy.Setup(n => n.PublishAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                    It.IsAny<string[]?>(), It.IsAny<int?>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<int?>()))
                .Returns(Task.CompletedTask);
            return ntfy;
        }

        private static void VerifySent(Mock<INtfyService> ntfy, int userId, string title, string message) =>
            ntfy.Verify(n => n.PublishAsync($"winplus-user-{userId}", title, message, It.IsAny<string>(),
                It.IsAny<string[]?>(), userId, It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<int?>()), Times.Once);

        [Theory]
        [InlineData("en", "en")]
        [InlineData("EN-gb", "en")]
        [InlineData("fr", "fr")]
        [InlineData("de", "fr")]
        public async Task ForUser_ReadsAccountLocale(string stored, string expected)
        {
            using var db = Db((1, stored));
            Assert.Equal(expected, await NotificationLocales.ForUserAsync(db, 1));
        }

        [Fact]
        public async Task ForUser_FrenchWhenUnknownOrNoRecipient()
        {
            using var db = Db();
            Assert.Equal("fr", await NotificationLocales.ForUserAsync(db, 99));
            Assert.Equal("fr", await NotificationLocales.ForUserAsync(db, null));
            Assert.Equal("fr", await NotificationLocales.ForUserAsync(null, 1));
        }

        [Fact]
        public void LocalizedText_PicksLanguage()
        {
            var t = new LocalizedText("Paiement reçu ✓", "Payment received ✓");
            Assert.Equal("Payment received ✓", t.For("en"));
            Assert.Equal("Payment received ✓", t.For("en-US"));
            Assert.Equal("Paiement reçu ✓", t.For("fr"));
            Assert.Equal("Paiement reçu ✓", t.For(null));
            Assert.Equal("ab", (new LocalizedText("a", "x") + new LocalizedText("b", "y")).Fr);
        }

        [Fact]
        public async Task PublishLocalized_EachRecipientInTheirLanguage()
        {
            using var db = Db((1, "en"), (2, "fr"));
            var ntfy = Ntfy();
            var title = new LocalizedText("Paiement reçu ✓", "Payment received ✓");
            var message = new LocalizedText("Votre paiement de 2500 XAF a été confirmé.", "Your payment of 2500 XAF has been confirmed.");

            await ntfy.Object.PublishLocalizedAsync(db, "winplus-user-1", title, message, userId: 1, type: "payment");
            await ntfy.Object.PublishLocalizedAsync(db, "winplus-user-2", title, message, userId: 2, type: "payment");

            VerifySent(ntfy, 1, "Payment received ✓", "Your payment of 2500 XAF has been confirmed.");
            VerifySent(ntfy, 2, "Paiement reçu ✓", "Votre paiement de 2500 XAF a été confirmé.");
        }

        [Fact]
        public async Task NewAssignment_NotifiesEachStudentInTheirLanguage()
        {
            using var db = Db((1, "en"), (2, "fr"), (10, "fr"));
            db.TeacherClasses.Add(new TeacherClass { Id = 5, TeacherId = 10, Name = "Tle C1" });
            db.TeacherClassStudents.Add(new TeacherClassStudent { TeacherClassId = 5, StudentId = 1 });
            db.TeacherClassStudents.Add(new TeacherClassStudent { TeacherClassId = 5, StudentId = 2 });
            db.SaveChanges();
            var ntfy = Ntfy();
            var svc = new AssignmentService(db, NullLogger<AssignmentService>.Instance, ntfy.Object);

            await svc.CreateAssignmentAsync(10, new CreateAssignmentRequestDto { TeacherClassId = 5, Title = "Suites numériques" });

            VerifySent(ntfy, 1, "New homework", "\"Suites numériques\" has been set for your class \"Tle C1\".");
            VerifySent(ntfy, 2, "Nouveau devoir", "« Suites numériques » a été donné à ta classe « Tle C1 ».");
        }

        [Fact]
        public void BadgeLabels_Bilingual_FrenchUnchanged()
        {
            Assert.Equal("Score parfait", CourseGamificationService.BadgeLabel("perfect_score"));
            var label = CourseGamificationService.BadgeLabelLocalized("perfect_score");
            Assert.Equal("Score parfait", label.Fr);
            Assert.Equal("Perfect score", label.En);
            Assert.Equal("custom", CourseGamificationService.BadgeLabelLocalized("custom").En);
        }
    }
}
