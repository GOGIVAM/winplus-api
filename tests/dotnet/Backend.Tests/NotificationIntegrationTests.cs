using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Backend.Data;
using Backend.Models.Entities;
using Backend.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Backend.Tests
{
    /// <summary>
    /// Module 22 : test d'intégration ciblé de la couche de notification.
    ///
    /// Le service de notification est résolu par injection de dépendances, comme
    /// en production (contexte de données « scopé » partagé avec l'appelant),
    /// sur le vrai modèle EF adossé au fournisseur InMemory. Le canal ntfy est
    /// simulé par un HttpMessageHandler : aucun appel réseau.
    /// </summary>
    public class NotificationIntegrationTests
    {
        private readonly InMemoryDatabaseRoot _root = new();
        private readonly string _dbName = $"notif-{Guid.NewGuid()}";
        private readonly RecordingHandler _ntfyHandler = new();
        private readonly ServiceProvider _provider;

        public NotificationIntegrationTests()
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSingleton<IConfiguration>(new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Ntfy:BaseUrl"] = "http://ntfy.test",
                    ["Ntfy:AdminTopic"] = "winplus-admin-test",
                })
                .Build());
            var factory = new Mock<IHttpClientFactory>();
            factory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(() => new HttpClient(_ntfyHandler, disposeHandler: false));
            services.AddSingleton(factory.Object);
            services.AddScoped<ApplicationDbContext>(_ => TestApplicationDbContext.Create(_dbName, _root));
            services.AddSingleton<INotificationPreferenceService, NotificationPreferenceService>();
            services.AddScoped<INtfyService, NtfyService>();
            _provider = services.BuildServiceProvider();

            using var seed = TestApplicationDbContext.Create(_dbName, _root);
            seed.Users.AddRange(
                new User { Id = 1, Email = "eleve@test.local", FirstName = "Avant", Role = "student" },
                new User { Id = 2, Email = "admin1@test.local", Role = "admin", IsActive = true },
                new User { Id = 3, Email = "admin2@test.local", Role = "Admin", IsActive = true },
                new User { Id = 4, Email = "ancien-admin@test.local", Role = "admin", IsActive = true, IsDeleted = true });
            seed.SaveChanges();
        }

        private TestApplicationDbContext Fresh() => TestApplicationDbContext.Create(_dbName, _root);

        private void SetPreferences(int userId, Action<UserNotificationSettings> configure)
        {
            using var db = Fresh();
            var settings = new UserNotificationSettings { UserId = userId };
            configure(settings);
            db.UserNotificationSettings.Add(settings);
            db.SaveChanges();
        }

        /// <summary>
        /// Scénario exigé par le Module 22 : un contrôleur modifie une entité,
        /// notifie, puis échoue avant son propre enregistrement. La modification
        /// ne doit pas avoir été validée en base.
        /// Résultat avant correction : ce test échouait (FirstName valait
        /// « Modifié » en base), ce qui confirmait l'hypothèse de validation
        /// prématurée par NtfyService.SaveChangesAsync sur le contexte partagé.
        /// </summary>
        [Fact]
        public async Task FailedOperationAfterNotify_DoesNotCommitCallerChanges()
        {
            using (var scope = _provider.CreateScope())
            {
                var controllerDb = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var ntfy = scope.ServiceProvider.GetRequiredService<INtfyService>();

                var user = await controllerDb.Users.SingleAsync(u => u.Id == 1);
                user.FirstName = "Modifié";

                await ntfy.PublishAsync("winplus-user-1", "Titre", "Message", userId: 1, type: "goal");

                // L'opération métier échoue ici : le contrôleur n'appelle jamais
                // son propre SaveChangesAsync.
            }

            using var check = Fresh();
            Assert.Equal("Avant", (await check.Users.SingleAsync(u => u.Id == 1)).FirstName);
            // La notification, elle, est partie et reste persistée (elle n'est pas annulable).
            Assert.Equal(1, await check.Notifications.CountAsync(n => n.UserId == 1));
        }

        [Fact]
        public async Task CallerSavingAfterNotify_StillCommitsItsOwnChanges()
        {
            using (var scope = _provider.CreateScope())
            {
                var controllerDb = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var ntfy = scope.ServiceProvider.GetRequiredService<INtfyService>();

                var user = await controllerDb.Users.SingleAsync(u => u.Id == 1);
                user.FirstName = "Modifié";
                await ntfy.PublishAsync("winplus-user-1", "Titre", "Message", userId: 1, type: "goal");
                await controllerDb.SaveChangesAsync();
            }

            using var check = Fresh();
            Assert.Equal("Modifié", (await check.Users.SingleAsync(u => u.Id == 1)).FirstName);
            Assert.Equal(1, await check.Notifications.CountAsync(n => n.UserId == 1));
        }

        [Fact]
        public async Task AdminNotification_IsPersistedOncePerActiveAdmin()
        {
            using (var scope = _provider.CreateScope())
            {
                var ntfy = scope.ServiceProvider.GetRequiredService<INtfyService>();
                await ntfy.PublishAdminAsync("Nouvelle demande de retrait", "Retrait #7 de 5000 XAF",
                    type: "Withdrawal", relatedEntityType: "Withdrawal", relatedEntityId: 7);
            }

            using var check = Fresh();
            var rows = await check.Notifications.Where(n => n.RelatedEntityType == "Withdrawal").ToListAsync();
            Assert.Equal(new[] { 2, 3 }, rows.Select(r => r.UserId).OrderBy(x => x).ToArray());
            Assert.All(rows, r => Assert.Equal(7, r.RelatedEntityId));
            Assert.Single(_ntfyHandler.Topics, t => t.EndsWith("/winplus-admin-test"));
        }

        [Fact]
        public async Task PushDisabled_SkipsNtfyButKeepsInAppRecord()
        {
            SetPreferences(1, s => s.PushNotifications = false);

            using (var scope = _provider.CreateScope())
            {
                var ntfy = scope.ServiceProvider.GetRequiredService<INtfyService>();
                await ntfy.PublishAsync("winplus-user-1", "Objectif", "Message", userId: 1, type: "goal");
            }

            Assert.Empty(_ntfyHandler.Topics);
            using var check = Fresh();
            Assert.Equal(1, await check.Notifications.CountAsync(n => n.UserId == 1));
        }

        [Fact]
        public async Task CategoryDisabled_SuppressesAllChannels()
        {
            SetPreferences(1, s => s.CourseCommunity = false);

            using (var scope = _provider.CreateScope())
            {
                var ntfy = scope.ServiceProvider.GetRequiredService<INtfyService>();
                await ntfy.PublishAsync("winplus-user-1", "Forum", "Nouvelle réponse", userId: 1, type: "forum");
            }

            Assert.Empty(_ntfyHandler.Topics);
            using var check = Fresh();
            Assert.Equal(0, await check.Notifications.CountAsync(n => n.UserId == 1));
        }

        [Fact]
        public async Task TransactionalNotification_IgnoresPreferences()
        {
            SetPreferences(1, s =>
            {
                s.PushNotifications = false;
                s.EmailNotifications = false;
                s.CourseCommunity = false;
                s.LearningReminders = false;
            });

            using (var scope = _provider.CreateScope())
            {
                var ntfy = scope.ServiceProvider.GetRequiredService<INtfyService>();
                await ntfy.PublishAsync("winplus-user-1", "Paiement reçu", "OK", userId: 1, type: "payment");
            }

            Assert.Single(_ntfyHandler.Topics);
            using var check = Fresh();
            Assert.Equal(1, await check.Notifications.CountAsync(n => n.UserId == 1));
        }

        [Fact]
        public async Task EmailPreference_FiltersNonTransactionalRecipients()
        {
            SetPreferences(1, s => s.EmailNotifications = false);
            var prefs = _provider.GetRequiredService<INotificationPreferenceService>();

            Assert.False(await prefs.AllowsAsync(1, NotificationChannel.Email, NotificationCategory.General));
            Assert.True(await prefs.AllowsAsync(1, NotificationChannel.Email, NotificationCategory.Transactional));
            // Sans enregistrement de préférences : valeurs par défaut de l'entité.
            Assert.True(await prefs.AllowsAsync(2, NotificationChannel.Email, NotificationCategory.General));
            Assert.False(await prefs.AllowsAsync(2, NotificationChannel.Email, NotificationCategory.Promotions));
        }

        [Fact]
        public async Task FilterEmails_AppliesPreferencesAndKeepsUnknownAddresses()
        {
            SetPreferences(1, s => s.EmailNotifications = false);
            var prefs = _provider.GetRequiredService<INotificationPreferenceService>();

            var allowed = await prefs.FilterEmailsAsync(
                new[] { "eleve@test.local", "admin1@test.local", "inconnu@test.local" }, NotificationCategory.General);

            Assert.DoesNotContain("eleve@test.local", allowed);
            Assert.Contains("admin1@test.local", allowed);
            Assert.Contains("inconnu@test.local", allowed);
        }

        /// <summary>
        /// Partie 12.5 : si les préférences sont illisibles (base indisponible),
        /// FilterEmailsAsync applique les valeurs par défaut et envoie, comme
        /// GetSettingsAsync, au lieu de lever et de perdre l'envoi.
        /// </summary>
        [Fact]
        public async Task FilterEmails_WhenPreferencesUnreadable_FallsBackToDefaults()
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddScoped<ApplicationDbContext>(_ => throw new InvalidOperationException("base indisponible"));
            services.AddSingleton<INotificationPreferenceService, NotificationPreferenceService>();
            using var provider = services.BuildServiceProvider();
            var prefs = provider.GetRequiredService<INotificationPreferenceService>();

            var allowed = await prefs.FilterEmailsAsync(new[] { "a@test.local", "b@test.local" }, NotificationCategory.General);
            Assert.Equal(new[] { "a@test.local", "b@test.local" }, allowed);

            // Valeurs par défaut de l'entité : Promotions désactivé.
            Assert.Empty(await prefs.FilterEmailsAsync(new[] { "a@test.local" }, NotificationCategory.Promotions));
        }

        private sealed class RecordingHandler : HttpMessageHandler
        {
            public List<string> Topics { get; } = new();

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                lock (Topics) Topics.Add(request.RequestUri!.ToString());
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
            }
        }
    }
}
