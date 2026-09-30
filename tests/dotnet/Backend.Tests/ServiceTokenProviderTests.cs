using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Text;
using Backend.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace Backend.Tests
{
    /// <summary>
    /// Jeton technique des tâches de fond (Modules 23/36). Le secret est un
    /// secret de TEST injecté en mémoire : aucun fichier de configuration lu.
    /// </summary>
    public class ServiceTokenProviderTests
    {
        private const string TestSecret = "unit-test-secret-not-a-real-one-0123456789";

        private static ServiceTokenProvider Create() => new(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["JWT:SecretKey"] = TestSecret,
                ["JWT:Issuer"] = "WinPlusApp",
            })
            .Build());

        [Fact]
        public void Token_CarriesServiceAudienceScopeAndShortLifetime()
        {
            var header = Create().CreateAuthorizationHeader(ServiceScopes.Decrochage);
            Assert.StartsWith("Bearer ", header);

            var jwt = new JwtSecurityTokenHandler().ReadJwtToken(header["Bearer ".Length..]);
            Assert.Equal("HS256", jwt.Header.Alg);
            Assert.Equal(ServiceTokenProvider.ServiceAudience, jwt.Audiences.Single());
            Assert.Equal("service", jwt.Claims.Single(c => c.Type == "token_use").Value);
            Assert.Equal("ai.decrochage", jwt.Claims.Single(c => c.Type == "scope").Value);
            Assert.DoesNotContain(jwt.Claims, c => c.Type is "user_id" or "role");
            Assert.True(jwt.ValidTo <= DateTime.UtcNow.AddMinutes(5).AddSeconds(5));
        }

        [Fact]
        public void Token_IsRejectedByUserAudienceValidation()
        {
            // Même paramètres que le JwtBearer .NET (audience utilisateur) : un
            // jeton technique ne doit jamais ouvrir un endpoint .NET [Authorize].
            var token = Create().CreateAuthorizationHeader(ServiceScopes.Notification)["Bearer ".Length..];
            var parameters = new TokenValidationParameters
            {
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(TestSecret)),
                ValidIssuer = "WinPlusApp",
                ValidAudience = "WinPlusUsers",
            };

            Assert.ThrowsAny<SecurityTokenException>(() =>
                new JwtSecurityTokenHandler().ValidateToken(token, parameters, out _));
        }

        [Fact]
        public void EmptyScope_IsRefused()
        {
            Assert.Throws<ArgumentException>(() => Create().CreateAuthorizationHeader(" "));
        }
    }
}
