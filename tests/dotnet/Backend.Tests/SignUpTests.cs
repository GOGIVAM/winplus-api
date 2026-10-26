using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Backend.Models.Entities;
using Backend.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Backend.Tests
{
    /// <summary>
    /// Création de compte (corrections du 2026-10-26) : règle du mot de passe
    /// identique aux écrans, messages dans la langue du site avec un code,
    /// e-mail déjà pris quelle que soit la casse, identifiant renvoyé (attribution 10.3).
    /// </summary>
    public class SignUpTests
    {
        private static (CustomAuthService svc, Backend.Data.ApplicationDbContext db) Service()
        {
            var db = TestApplicationDbContext.Create();
            var email = new Mock<IEmailService>();
            email.Setup(e => e.SendEmailVerificationAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync(true);
            var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>()).Build();
            var svc = new CustomAuthService(db, null!, email.Object, null!, null!, config, NullLogger<CustomAuthService>.Instance);
            return (svc, db);
        }

        [Theory]
        [InlineData("WINPLUS2026!")]   // pas de minuscule : accepté avant par le web
        [InlineData("Winplus2026")]    // pas de caractère spécial : accepté avant par le mobile
        [InlineData("Wp1!")]           // trop court
        public async Task WeakPassword_IsRefused_WithCodeAndFrenchMessage(string password)
        {
            var (svc, db) = Service();
            var r = await svc.SignUpAsync("eleve@test.local", password, "Awa", "Ngo", null, "student", "fr");
            Assert.False(r.Success);
            Assert.Equal("password_weak", r.ErrorCode);
            Assert.StartsWith("Le mot de passe", r.Message);
            Assert.Empty(db.Users);
        }

        [Fact]
        public async Task Messages_FollowTheLocale()
        {
            var (svc, _) = Service();
            var r = await svc.SignUpAsync("eleve@test.local", "short", "Awa", "Ngo", null, "student", "en");
            Assert.StartsWith("The password", r.Message);
        }

        [Fact]
        public async Task ValidPassword_WithHyphen_CreatesAccount_AndReturnsId()
        {
            var (svc, db) = Service();
            var r = await svc.SignUpAsync("  Eleve@Test.local ", "Win-plus2026", "Awa", "Ngo", "+237690123456", "parent", "fr");
            Assert.True(r.Success);
            var user = db.Users.Single();
            Assert.Equal("eleve@test.local", user.Email);
            Assert.Equal("parent", user.Role);
            Assert.Equal("CM", user.Country);
            Assert.Equal(user.Id, r.User!.Id);
        }

        [Fact]
        public async Task ExistingEmail_IsRefused_WhateverTheCase()
        {
            var (svc, db) = Service();
            db.Users.Add(new User { Email = "Ancien.Compte@Test.local", Role = "student" });
            await db.SaveChangesAsync();
            var r = await svc.SignUpAsync("ancien.compte@test.local", "Win-plus2026", "A", "B", null, "student", "fr");
            Assert.False(r.Success);
            Assert.Equal("email_taken", r.ErrorCode);
            Assert.Single(db.Users);
        }
    }
}
