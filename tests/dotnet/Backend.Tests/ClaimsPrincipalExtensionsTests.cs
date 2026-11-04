using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Backend.Extensions;
using Xunit;

namespace Backend.Tests
{
    /// <summary>
    /// Lecture de l'identité depuis les claims du jeton : identifiant (avec
    /// plusieurs sources de repli), rôle (défaut « student »), e-mail et tests
    /// de rôle. Aucune base requise.
    /// </summary>
    public class ClaimsPrincipalExtensionsTests
    {
        private static ClaimsPrincipal Principal(params (string Type, string Value)[] claims)
        {
            var list = new List<Claim>();
            foreach (var (type, value) in claims) list.Add(new Claim(type, value));
            return new ClaimsPrincipal(new ClaimsIdentity(list, "test"));
        }

        // ── GetUserId ───────────────────────────────────────────────────

        [Fact]
        public void L_identifiant_est_lu_dans_la_claim_user_id()
        {
            Assert.Equal(42, Principal(("user_id", "42")).GetUserId());
        }

        [Fact]
        public void La_claim_user_id_prime_sur_les_sources_de_repli()
        {
            var p = Principal((ClaimTypes.NameIdentifier, "7"), ("sub", "8"), ("user_id", "42"));
            Assert.Equal(42, p.GetUserId());
        }

        [Fact]
        public void En_l_absence_de_user_id_NameIdentifier_est_utilisee_puis_sub()
        {
            Assert.Equal(7, Principal((ClaimTypes.NameIdentifier, "7"), ("sub", "8")).GetUserId());
            Assert.Equal(8, Principal((JwtRegisteredClaimNames.Sub, "8")).GetUserId());
            Assert.Equal(9, Principal(("sub", "9")).GetUserId());
        }

        [Fact]
        public void Un_jeton_sans_identifiant_est_refuse_et_le_message_liste_les_claims_disponibles()
        {
            var ex = Assert.Throws<UnauthorizedAccessException>(() => Principal(("role", "admin"), ("email", "a@b.c")).GetUserId());
            Assert.Contains("does not contain user_id", ex.Message);
            Assert.Contains("role=admin", ex.Message);
            Assert.Contains("email=a@b.c", ex.Message);
        }

        [Fact]
        public void Un_identifiant_non_numerique_est_refuse_avec_la_valeur_fautive()
        {
            var ex = Assert.Throws<UnauthorizedAccessException>(() => Principal(("user_id", "abc")).GetUserId());
            Assert.Contains("Invalid user_id claim value: abc", ex.Message);
        }

        [Fact]
        public void Un_principal_nul_est_refuse()
        {
            var ex = Assert.Throws<UnauthorizedAccessException>(() => ClaimsPrincipalExtensions.GetUserId(null!));
            Assert.Equal("Principal is null", ex.Message);
        }

        // ── GetUserRole / HasRole / IsAdmin ─────────────────────────────

        [Fact]
        public void Le_role_par_defaut_est_student()
        {
            Assert.Equal("student", Principal(("user_id", "1")).GetUserRole());
        }

        [Fact]
        public void La_claim_role_prime_sur_ClaimTypes_Role()
        {
            Assert.Equal("teacher", Principal((ClaimTypes.Role, "parent"), ("role", "teacher")).GetUserRole());
            Assert.Equal("parent", Principal((ClaimTypes.Role, "parent")).GetUserRole());
        }

        [Fact]
        public void HasRole_ignore_la_casse()
        {
            var p = Principal(("role", "Teacher"));
            Assert.True(p.HasRole("teacher"));
            Assert.True(p.HasRole("TEACHER"));
            Assert.False(p.HasRole("parent"));
        }

        [Fact]
        public void IsAdmin_n_est_vrai_que_pour_le_role_admin()
        {
            Assert.True(Principal(("role", "admin")).IsAdmin());
            Assert.True(Principal(("role", "Admin")).IsAdmin());
            Assert.False(Principal(("role", "teacher")).IsAdmin());
            Assert.False(Principal(("user_id", "1")).IsAdmin());
        }

        // ── GetUserEmail ────────────────────────────────────────────────

        [Fact]
        public void L_email_est_lu_depuis_email_puis_ClaimTypes_Email()
        {
            Assert.Equal("a@test.local", Principal(("email", "a@test.local"), (ClaimTypes.Email, "b@test.local")).GetUserEmail());
            Assert.Equal("b@test.local", Principal((ClaimTypes.Email, "b@test.local")).GetUserEmail());
        }

        [Fact]
        public void Sans_email_la_chaine_est_vide()
        {
            Assert.Equal(string.Empty, Principal(("user_id", "1")).GetUserEmail());
        }
    }
}
