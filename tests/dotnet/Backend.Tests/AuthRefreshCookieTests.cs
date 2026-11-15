using System;
using System.Linq;
using System.Threading.Tasks;
using Backend.Controllers;
using Backend.Data;
using Backend.Models.DTOs;
using Backend.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Backend.Tests
{
    /// <summary>
    /// Session du site : jeton de renouvellement en cookie HttpOnly (useCookie),
    /// « Se souvenir de moi » réel, et application mobile inchangée.
    /// </summary>
    public class AuthRefreshCookieTests
    {
        private static readonly InMemoryDatabaseRoot Root = new();
        private readonly string _dbName = $"auth-cookie-{Guid.NewGuid()}";
        private readonly Mock<ICustomAuthService> _auth = new();

        private static AuthResult Session(string refresh) => new()
        {
            Success = true,
            AccessToken = "acces",
            RefreshToken = refresh,
            ExpiresIn = 900,
            User = new UserDto { Id = 7, Email = "eleve@test.local", Role = "student" },
        };

        private AuthController Controller(string? cookie = null)
        {
            var ctx = new DefaultHttpContext();
            ctx.Request.Host = new HostString("api.winplus.cm");
            if (cookie != null) ctx.Request.Headers["Cookie"] = cookie;
            ApplicationDbContext db = TestApplicationDbContext.Create(_dbName, Root);
            return new AuthController(_auth.Object, new Mock<ICartService>().Object, NullLogger<AuthController>.Instance, db)
            {
                ControllerContext = new ControllerContext { HttpContext = ctx },
            };
        }

        private static string SetCookie(AuthController c) => string.Join(" | ", c.Response.Headers["Set-Cookie"].ToArray());

        private static T Body<T>(IActionResult result) => Assert.IsType<T>(Assert.IsType<OkObjectResult>(result).Value);

        [Fact]
        public async Task Connexion_site_sans_se_souvenir_pose_un_cookie_de_session_et_ne_renvoie_pas_le_jeton()
        {
            _auth.Setup(a => a.SignInAsync("eleve@test.local", "Mdp1!", It.IsAny<HttpRequest>(), false)).ReturnsAsync(Session("rt-1"));
            var c = Controller();

            var body = Body<SignInResponse>(await c.SignIn(new SignInRequestDto { Email = "eleve@test.local", Password = "Mdp1!", UseCookie = true }));

            Assert.Equal("acces", body.AccessToken);
            Assert.Null(body.RefreshToken);
            var set = SetCookie(c);
            Assert.Contains("wp_rt=rt-1", set);
            Assert.Contains("httponly", set, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("secure", set, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("wp_rt=rt-1; expires", set, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task Se_souvenir_de_moi_pose_un_cookie_persistant()
        {
            _auth.Setup(a => a.SignInAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<HttpRequest>(), true)).ReturnsAsync(Session("rt-2"));
            var c = Controller();

            await c.SignIn(new SignInRequestDto { Email = "eleve@test.local", Password = "Mdp1!", RememberMe = true, UseCookie = true });

            var set = SetCookie(c);
            Assert.Contains("wp_rtp=rt-2", set);
            Assert.Contains("expires=", set, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task L_application_mobile_recoit_toujours_le_jeton_dans_la_reponse()
        {
            _auth.Setup(a => a.SignInAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<HttpRequest>(), It.IsAny<bool>())).ReturnsAsync(Session("rt-3"));
            var c = Controller();

            var body = Body<SignInResponse>(await c.SignIn(new SignInRequestDto { Email = "eleve@test.local", Password = "Mdp1!" }));

            Assert.Equal("rt-3", body.RefreshToken);
            Assert.False(c.Response.Headers.ContainsKey("Set-Cookie"));
        }

        [Fact]
        public async Task Le_renouvellement_lit_le_cookie_et_garde_le_type_de_session()
        {
            _auth.Setup(a => a.RefreshAccessTokenAsync("ancien")).ReturnsAsync(Session("nouveau"));
            var c = Controller("wp_rtp=ancien");

            var body = Body<RefreshTokenResponse>(await c.RefreshToken(new RefreshTokenRequestDto { UseCookie = true }));

            Assert.Equal("acces", body.AccessToken);
            Assert.Null(body.RefreshToken);
            Assert.Contains("wp_rtp=nouveau", SetCookie(c));
        }

        [Fact]
        public async Task Un_cookie_refuse_est_efface()
        {
            _auth.Setup(a => a.RefreshAccessTokenAsync("revoque")).ReturnsAsync(new AuthResult { Success = false, Message = "Invalid" });
            var c = Controller("wp_rt=revoque");

            var result = await c.RefreshToken(new RefreshTokenRequestDto { UseCookie = true });

            Assert.IsType<UnauthorizedObjectResult>(result);
            Assert.Contains("wp_rt=;", SetCookie(c));
        }

        [Fact]
        public async Task Une_ancienne_session_web_passe_au_cookie_persistant()
        {
            _auth.Setup(a => a.RefreshAccessTokenAsync("en-clair")).ReturnsAsync(Session("migre"));
            var c = Controller();

            var body = Body<RefreshTokenResponse>(await c.RefreshToken(new RefreshTokenRequestDto { RefreshToken = "en-clair", UseCookie = true }));

            Assert.Null(body.RefreshToken);
            Assert.Contains("wp_rtp=migre", SetCookie(c));
        }

        [Fact]
        public async Task La_deconnexion_revoque_le_jeton_du_cookie_et_efface_les_cookies()
        {
            var c = Controller("wp_rt=a-revoquer");

            await c.Logout(new LogoutRequestDto { UseCookie = true });

            _auth.Verify(a => a.LogoutAsync("a-revoquer"), Times.Once);
            var set = SetCookie(c);
            Assert.Contains("wp_rt=;", set);
            Assert.Contains("wp_rtp=;", set);
        }
    }
}
