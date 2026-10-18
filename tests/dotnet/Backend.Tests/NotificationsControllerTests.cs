using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Security.Claims;
using System.Text.Json;
using System.Threading.Tasks;
using Backend.Controllers;
using Backend.Models.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Backend.Tests
{
    /// <summary>
    /// Compteur et marquage des notifications : le serveur est la source de
    /// vérité du badge (toutes les notifications de l'utilisateur, pas la page
    /// chargée), chaque action renvoie le nouveau compte, et un utilisateur ne
    /// voit ni ne modifie jamais les notifications d'un autre.
    /// </summary>
    public class NotificationsControllerTests
    {
        private static readonly DateTime T0 = new(2026, 10, 1, 8, 0, 0, DateTimeKind.Utc);

        private static NotificationsController Controller(TestApplicationDbContext db, int userId)
        {
            var ctrl = new NotificationsController(
                db,
                NullLogger<NotificationsController>.Instance,
                new Mock<IHttpClientFactory>().Object,
                new ConfigurationBuilder().Build());
            ctrl.ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                        new[] { new Claim("user_id", userId.ToString()) }, "test")),
                },
            };
            return ctrl;
        }

        private static JsonElement Body(IActionResult result)
        {
            var ok = Assert.IsType<OkObjectResult>(result);
            return JsonDocument.Parse(JsonSerializer.Serialize(ok.Value)).RootElement;
        }

        /// <summary>Utilisateur 1 : <paramref name="unread"/> non lues + <paramref name="read"/> lues. Utilisateur 2 : 3 non lues.</summary>
        private static TestApplicationDbContext Seed(int unread, int read)
        {
            var db = TestApplicationDbContext.Create();
            db.Users.AddRange(
                new User { Id = 1, Email = "eleve@test.local", Role = "student", IsActive = true },
                new User { Id = 2, Email = "parent@test.local", Role = "parent", IsActive = true });
            var id = 1;
            for (var i = 0; i < unread; i++)
                db.Notifications.Add(N(id++, 1, false, T0.AddMinutes(i)));
            for (var i = 0; i < read; i++)
                db.Notifications.Add(N(id++, 1, true, T0.AddMinutes(-i - 1)));
            for (var i = 0; i < 3; i++)
                db.Notifications.Add(N(id++, 2, false, T0.AddMinutes(i)));
            db.SaveChanges();
            return db;
        }

        private static Notification N(int id, int userId, bool isRead, DateTime at) => new()
        {
            Id = id, UserId = userId, Title = $"N{id}", Message = "m", IsRead = isRead,
            ReadAt = isRead ? at : null, CreatedAt = at, User = null!,
        };

        [Fact]
        public async Task UnreadCount_CountsAllUnread_BeyondFirstPage_AndOnlyForCurrentUser()
        {
            using var db = Seed(unread: 130, read: 5);

            var count = Body(await Controller(db, 1).GetUnreadCount());
            Assert.Equal(130, count.GetProperty("unread").GetInt32());

            // La liste paginée annonce le même total, même si elle ne renvoie que 20 lignes.
            var list = Body(await Controller(db, 1).GetNotifications(1, 20));
            Assert.Equal(20, list.GetProperty("data").GetArrayLength());
            Assert.Equal(130, list.GetProperty("unread").GetInt32());
            Assert.Equal(135, list.GetProperty("total").GetInt32());

            Assert.Equal(3, Body(await Controller(db, 2).GetUnreadCount()).GetProperty("unread").GetInt32());
        }

        [Fact]
        public async Task List_UnreadFirst_StablePagesWithoutDuplicates_EvenWithSameTimestamp()
        {
            using var db = TestApplicationDbContext.Create();
            db.Users.Add(new User { Id = 1, Email = "a@test.local", Role = "student", IsActive = true });
            // Même horodatage pour toutes (alerte groupée) : la pagination doit rester stable.
            for (var i = 1; i <= 25; i++) db.Notifications.Add(N(i, 1, i % 5 == 0, T0));
            db.SaveChanges();

            var seen = new List<int>();
            for (var page = 1; page <= 3; page++)
            {
                var data = Body(await Controller(db, 1).GetNotifications(page, 10)).GetProperty("data");
                foreach (var e in data.EnumerateArray())
                {
                    seen.Add(e.GetProperty("Id").GetInt32());
                    Assert.True(e.TryGetProperty("IsRead", out _));
                }
            }
            Assert.Equal(25, seen.Count);
            Assert.Equal(25, seen.Distinct().Count());
            // Les 20 non lues passent avant les 5 lues.
            Assert.All(seen.Take(20), id => Assert.NotEqual(0, id % 5));
        }

        [Fact]
        public async Task MarkAsRead_SetsReadAt_ReturnsNewCount_AndIsIdempotent()
        {
            using var db = Seed(unread: 4, read: 0);
            var ctrl = Controller(db, 1);

            var first = Body(await ctrl.MarkAsRead(1));
            Assert.Equal(3, first.GetProperty("unread").GetInt32());
            var n = db.Notifications.Single(x => x.Id == 1);
            Assert.True(n.IsRead);
            Assert.NotNull(n.ReadAt);
            var readAt = n.ReadAt;

            // Second clic : pas de nouveau décompte, date de lecture conservée.
            var again = Body(await Controller(db, 1).MarkAsRead(1));
            Assert.Equal(3, again.GetProperty("unread").GetInt32());
            Assert.Equal(readAt, db.Notifications.Single(x => x.Id == 1).ReadAt);
        }

        [Fact]
        public async Task MarkAsRead_OtherUsersNotification_IsNotFound_AndUntouched()
        {
            using var db = Seed(unread: 1, read: 0);
            var otherId = db.Notifications.First(x => x.UserId == 2).Id;

            Assert.IsType<NotFoundObjectResult>(await Controller(db, 1).MarkAsRead(otherId));
            Assert.False(db.Notifications.Single(x => x.Id == otherId).IsRead);
            Assert.IsType<NotFoundObjectResult>(await Controller(db, 1).Delete(otherId));
            Assert.True(db.Notifications.Any(x => x.Id == otherId));
        }

        [Fact]
        public async Task MarkAllAsRead_CoversEveryPage_NotOnlyTheLoadedOne_AndSparesOtherUsers()
        {
            using var db = Seed(unread: 130, read: 2);

            var body = Body(await Controller(db, 1).MarkAllAsRead());
            Assert.Equal(130, body.GetProperty("marked").GetInt32());
            Assert.Equal(0, body.GetProperty("unread").GetInt32());

            Assert.DoesNotContain(db.Notifications.Where(x => x.UserId == 1), x => !x.IsRead || x.ReadAt == null);
            Assert.Equal(3, Body(await Controller(db, 2).GetUnreadCount()).GetProperty("unread").GetInt32());
        }

        [Fact]
        public async Task Delete_UnreadNotification_DecrementsCount()
        {
            using var db = Seed(unread: 2, read: 1);

            var body = Body(await Controller(db, 1).Delete(1));
            Assert.Equal(1, body.GetProperty("unread").GetInt32());
            Assert.False(db.Notifications.Any(x => x.Id == 1));

            // Supprimer une notification déjà lue ne change pas le compte.
            Assert.Equal(1, Body(await Controller(db, 1).Delete(3)).GetProperty("unread").GetInt32());
        }
    }
}
