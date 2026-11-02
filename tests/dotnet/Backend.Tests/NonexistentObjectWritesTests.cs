using System;
using System.Net.Http;
using System.Security.Claims;
using System.Threading.Tasks;
using Backend.Controllers;
using Backend.Models.Entities;
using Backend.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Backend.Tests
{
    /// <summary>
    /// Écritures sur un objet qui n'existe pas : trouvées par le balayage du module de test (winplus-tests,
    /// plateforme/ecritures-objet-inexistant). Elles doivent répondre 404, jamais 500, et ne rien créer.
    /// </summary>
    public class NonexistentObjectWritesTests
    {
        private static ControllerContext AsUser(int userId) => new()
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim("user_id", userId.ToString()) }, "test")),
            },
        };

        [Fact]
        public async Task AddFavorite_UnknownCourse_Returns404_NotServerError()
        {
            var service = new Mock<IFavoriteService>();
            service.Setup(s => s.AddFavoriteAsync(1, 2147483000)).ThrowsAsync(new InvalidOperationException("Cours 2147483000 introuvable"));
            var ctrl = new FavoritesController(service.Object, NullLogger<FavoritesController>.Instance) { ControllerContext = AsUser(1) };

            var result = await ctrl.AddFavorite(2147483000);

            Assert.IsType<NotFoundObjectResult>(result);
        }

        [Fact]
        public async Task AddFavorite_UnexpectedFailure_StillReturns500()
        {
            var service = new Mock<IFavoriteService>();
            service.Setup(s => s.AddFavoriteAsync(1, 5)).ThrowsAsync(new InvalidOperationException("connexion perdue"));
            var ctrl = new FavoritesController(service.Object, NullLogger<FavoritesController>.Instance) { ControllerContext = AsUser(1) };

            var result = await ctrl.AddFavorite(5);

            Assert.Equal(500, Assert.IsType<ObjectResult>(result).StatusCode);
        }

        private static MessagesController Messages(TestApplicationDbContext db, int userId) => new(
            db, NullLogger<MessagesController>.Instance, new Mock<IHttpClientFactory>().Object, new Mock<IStorageService>().Object)
        { ControllerContext = AsUser(userId) };

        [Fact]
        public async Task ArchiveConversation_UnknownParticipant_Returns404_AndCreatesNothing()
        {
            var db = TestApplicationDbContext.Create();
            db.Users.Add(new User { Id = 1, Email = "a@x.test", FirstName = "A", LastName = "A", PasswordHash = "x", Role = "student" });
            await db.SaveChangesAsync();

            var result = await Messages(db, 1).ArchiveConversation(2147483000);

            Assert.IsType<NotFoundObjectResult>(result);
            Assert.Empty(db.ArchivedConversations);
        }

        [Fact]
        public async Task ArchiveConversation_ExistingParticipant_StillArchives()
        {
            var db = TestApplicationDbContext.Create();
            db.Users.Add(new User { Id = 1, Email = "a@x.test", FirstName = "A", LastName = "A", PasswordHash = "x", Role = "student" });
            db.Users.Add(new User { Id = 2, Email = "b@x.test", FirstName = "B", LastName = "B", PasswordHash = "x", Role = "student" });
            await db.SaveChangesAsync();

            var result = await Messages(db, 1).ArchiveConversation(2);

            Assert.IsType<OkObjectResult>(result);
            Assert.Single(db.ArchivedConversations);
        }
    }
}
