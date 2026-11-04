using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Security.Claims;
using Backend.Services;
using Backend.Utilities;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Backend.Tests
{
    /// <summary>
    /// Émission et validation des jetons : JwtService (jeton d'accès, jeton de
    /// rafraîchissement, jeton de réinitialisation) et JwtTokenGenerator
    /// (utilitaire). Les secrets sont fictifs, générés dans le test ; aucune
    /// configuration réelle n'est lue.
    /// </summary>
    public class JwtServiceTests
    {
        private const string Secret = "cle-de-test-uniquement-au-moins-32-caracteres-0123456789";
        private const string OtherSecret = "une-autre-cle-de-test-differente-0123456789-abcdefgh";

        private static IConfiguration Config(string? secret = Secret, string? expiration = null, string? issuer = null, string? audience = null)
        {
            var dict = new Dictionary<string, string?>();
            if (secret != null) dict["JWT:SecretKey"] = secret;
            if (expiration != null) dict["JWT:AccessTokenExpirationMinutes"] = expiration;
            if (issuer != null) dict["JWT:Issuer"] = issuer;
            if (audience != null) dict["JWT:Audience"] = audience;
            return new ConfigurationBuilder().AddInMemoryCollection(dict).Build();
        }

        private static JwtService Service(IConfiguration? config = null) =>
            new(config ?? Config(), Microsoft.Extensions.Logging.Abstractions.NullLogger<JwtService>.Instance);

        // ── Construction ────────────────────────────────────────────────

        [Fact]
        public void Sans_cle_secrete_le_service_refuse_de_demarrer()
        {
            var ex = Assert.Throws<InvalidOperationException>(() => Service(Config(secret: null)));
            Assert.Contains("JWT:SecretKey", ex.Message);
        }

        // ── Jeton d'accès ───────────────────────────────────────────────

        [Fact]
        public void Le_jeton_d_acces_porte_identifiant_email_role_et_verification()
        {
            var token = Service().GenerateAccessToken(42, "eleve@test.local", "teacher", emailVerified: true);
            var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);

            string Claim(string type) => jwt.Claims.First(c => c.Type == type).Value;
            Assert.Equal("42", Claim("user_id"));
            Assert.Equal("42", Claim("sub"));
            Assert.Equal("eleve@test.local", Claim("email"));
            Assert.Equal("teacher", Claim("role"));
            Assert.Equal("true", Claim("email_verified"));
        }

        [Fact]
        public void Par_defaut_le_role_est_student_et_l_email_non_verifie()
        {
            var jwt = new JwtSecurityTokenHandler().ReadJwtToken(Service().GenerateAccessToken(1, "a@test.local"));
            Assert.Equal("student", jwt.Claims.First(c => c.Type == "role").Value);
            Assert.Equal("false", jwt.Claims.First(c => c.Type == "email_verified").Value);
        }

        [Fact]
        public void L_algorithme_est_la_forme_compacte_HS256_attendue_par_le_service_Python()
        {
            var jwt = new JwtSecurityTokenHandler().ReadJwtToken(Service().GenerateAccessToken(1, "a@test.local"));
            Assert.Equal("HS256", jwt.Header.Alg);
        }

        [Fact]
        public void Les_valeurs_par_defaut_de_l_emetteur_de_l_audience_et_de_la_duree_sont_appliquees()
        {
            var jwt = new JwtSecurityTokenHandler().ReadJwtToken(Service().GenerateAccessToken(1, "a@test.local"));
            Assert.Equal("WinPlusApp", jwt.Issuer);
            Assert.Contains("WinPlusUsers", jwt.Audiences);
            var lifetime = jwt.ValidTo - DateTime.UtcNow;
            Assert.InRange(lifetime.TotalMinutes, 13, 15.1);
        }

        [Fact]
        public void La_configuration_fixe_l_emetteur_l_audience_et_la_duree()
        {
            var service = Service(Config(expiration: "120", issuer: "EmetteurTest", audience: "AudienceTest"));
            var jwt = new JwtSecurityTokenHandler().ReadJwtToken(service.GenerateAccessToken(1, "a@test.local"));
            Assert.Equal("EmetteurTest", jwt.Issuer);
            Assert.Contains("AudienceTest", jwt.Audiences);
            Assert.InRange((jwt.ValidTo - DateTime.UtcNow).TotalMinutes, 118, 120.1);
        }

        [Fact]
        public void Une_duree_illisible_retombe_sur_quinze_minutes()
        {
            var service = Service(Config(expiration: "bientot"));
            var jwt = new JwtSecurityTokenHandler().ReadJwtToken(service.GenerateAccessToken(1, "a@test.local"));
            Assert.InRange((jwt.ValidTo - DateTime.UtcNow).TotalMinutes, 13, 15.1);
        }

        [Fact]
        public void Les_claims_supplementaires_sont_ajoutes_en_texte()
        {
            var extra = new Dictionary<string, object> { ["institution_id"] = 7, ["plan"] = "famille" };
            var jwt = new JwtSecurityTokenHandler().ReadJwtToken(Service().GenerateAccessToken(1, "a@test.local", additionalClaims: extra));
            Assert.Equal("7", jwt.Claims.First(c => c.Type == "institution_id").Value);
            Assert.Equal("famille", jwt.Claims.First(c => c.Type == "plan").Value);
        }

        // ── Validation ──────────────────────────────────────────────────

        [Fact]
        public void Un_jeton_emis_par_le_service_est_valide_et_restitue_ses_claims()
        {
            var service = Service();
            var principal = service.ValidateToken(service.GenerateAccessToken(42, "a@test.local", "parent"));
            Assert.NotNull(principal);
            Assert.Equal("42", principal!.FindFirst("user_id")!.Value);
            Assert.Equal("parent", principal.FindFirst(ClaimTypes.Role)!.Value);
        }

        [Fact]
        public void Un_jeton_signe_avec_une_autre_cle_est_rejete()
        {
            var token = Service(Config(OtherSecret)).GenerateAccessToken(1, "a@test.local");
            Assert.Null(Service().ValidateToken(token));
        }

        [Fact]
        public void Un_jeton_d_un_autre_emetteur_est_rejete()
        {
            var token = Service(Config(issuer: "Autre")).GenerateAccessToken(1, "a@test.local");
            Assert.Null(Service().ValidateToken(token));
        }

        [Fact]
        public void Un_jeton_d_une_autre_audience_est_rejete()
        {
            var token = Service(Config(audience: "AutrePublic")).GenerateAccessToken(1, "a@test.local");
            Assert.Null(Service().ValidateToken(token));
        }

        [Fact]
        public void Un_jeton_expire_est_rejete_sans_tolerance_d_horloge()
        {
            var token = Service(Config(expiration: "-1")).GenerateAccessToken(1, "a@test.local");
            Assert.Null(Service().ValidateToken(token));
        }

        [Theory]
        [InlineData("")]
        [InlineData("pas.un.jeton")]
        [InlineData("abc")]
        public void Un_texte_qui_n_est_pas_un_jeton_est_rejete(string token)
        {
            Assert.Null(Service().ValidateToken(token));
        }

        // ── Jeton de rafraîchissement et de réinitialisation ────────────

        [Fact]
        public void Les_jetons_de_rafraichissement_sont_uniques_et_en_base64_de_64_octets()
        {
            var service = Service();
            var a = service.GenerateRefreshToken();
            var b = service.GenerateRefreshToken();
            Assert.NotEqual(a, b);
            Assert.Equal(64, Convert.FromBase64String(a).Length);
        }

        [Fact]
        public void Le_jeton_de_reinitialisation_a_la_meme_forme_qu_un_jeton_de_rafraichissement()
        {
            Assert.Equal(64, Convert.FromBase64String(Service().GeneratePasswordResetToken()).Length);
        }

        [Fact]
        public void Un_jeton_de_rafraichissement_bien_forme_est_accepte_sans_principal()
        {
            var service = Service();
            var (valid, principal) = service.ValidateRefreshToken(service.GenerateRefreshToken());
            Assert.True(valid);
            Assert.Null(principal);
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("ceci n'est pas du base64 !")]
        public void Un_jeton_de_rafraichissement_vide_ou_mal_forme_est_refuse(string token)
        {
            var (valid, principal) = Service().ValidateRefreshToken(token);
            Assert.False(valid);
            Assert.Null(principal);
        }

        [Fact]
        public void Un_jeton_de_reinitialisation_bien_forme_est_accepte_et_sans_email()
        {
            var service = Service();
            var (valid, email) = service.ValidatePasswordResetToken(service.GeneratePasswordResetToken());
            Assert.True(valid);
            Assert.Null(email);
        }

        [Theory]
        [InlineData("")]
        [InlineData("  ")]
        [InlineData("%%%")]
        public void Un_jeton_de_reinitialisation_vide_ou_mal_forme_est_refuse(string token)
        {
            Assert.False(Service().ValidatePasswordResetToken(token).isValid);
        }

        // ── JwtTokenGenerator ───────────────────────────────────────────

        private static JwtTokenGenerator Generator(string? secret = Secret, string? minutes = null)
        {
            var dict = new Dictionary<string, string?>();
            if (secret != null) dict["Jwt:SecretKey"] = secret;
            if (minutes != null) dict["Jwt:ExpirationMinutes"] = minutes;
            return new JwtTokenGenerator(new ConfigurationBuilder().AddInMemoryCollection(dict).Build());
        }

        [Fact]
        public void L_utilitaire_emet_un_jeton_avec_les_claims_fournis_et_un_identifiant_unique()
        {
            var gen = Generator();
            var t1 = gen.GenerateToken(new Dictionary<string, object> { ["user_id"] = 5, ["role"] = "admin" });
            var t2 = gen.GenerateToken(new Dictionary<string, object> { ["user_id"] = 5, ["role"] = "admin" });
            var jwt = new JwtSecurityTokenHandler().ReadJwtToken(t1);
            Assert.Equal("5", jwt.Claims.First(c => c.Type == "user_id").Value);
            Assert.Equal("admin", jwt.Claims.First(c => c.Type == "role").Value);
            Assert.Equal("winplusApp", jwt.Issuer);
            Assert.NotEqual(
                new JwtSecurityTokenHandler().ReadJwtToken(t2).Id,
                jwt.Id);
        }

        [Fact]
        public void L_utilitaire_valide_ses_propres_jetons_et_rejette_ceux_d_une_autre_cle()
        {
            var gen = Generator();
            var token = gen.GenerateToken(new Dictionary<string, object> { ["user_id"] = 5 });
            Assert.NotNull(gen.ValidateToken(token));
            Assert.Throws<UnauthorizedAccessException>(() => Generator(OtherSecret).ValidateToken(token));
        }

        [Fact]
        public void La_duree_de_l_utilitaire_peut_etre_surchargee_et_un_jeton_expire_signale_l_expiration()
        {
            var gen = Generator(minutes: "30");
            var expired = gen.GenerateToken(new Dictionary<string, object> { ["user_id"] = 1 }, expirationMinutes: -1);
            var ex = Assert.Throws<UnauthorizedAccessException>(() => gen.ValidateToken(expired));
            Assert.Contains("expiré", ex.Message);

            var jwt = new JwtSecurityTokenHandler().ReadJwtToken(gen.GenerateToken(new Dictionary<string, object> { ["user_id"] = 1 }));
            Assert.InRange((jwt.ValidTo - DateTime.UtcNow).TotalMinutes, 28, 30.1);
        }

        [Fact]
        public void Une_valeur_nulle_dans_les_claims_devient_une_chaine_vide()
        {
            var token = Generator().GenerateToken(new Dictionary<string, object> { ["note"] = null! });
            var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);
            Assert.Equal(string.Empty, jwt.Claims.First(c => c.Type == "note").Value);
        }
    }
}
