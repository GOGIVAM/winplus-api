using System;
using System.Linq;
using System.Threading.Tasks;
using Backend.Models.Entities;
using Backend.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Backend.Tests
{
    /// <summary>
    /// Double authentification : état, initialisation, refus d'un code faux,
    /// désactivation (effacement du secret) et génération des codes de secours.
    /// Le code TOTP valide dépend de l'horloge système et n'est pas testé ici ;
    /// seuls les refus (déterministes) le sont.
    /// </summary>
    public class TwoFactorServiceTests
    {
        private const int U = 9101;
        private static readonly string Secret = Convert.ToBase64String(new byte[20]);

        private static (TestApplicationDbContext Db, TwoFactorService Svc) Build()
        {
            var db = TestApplicationDbContext.Create();
            return (db, new TwoFactorService(db, NullLogger<TwoFactorService>.Instance));
        }

        private static UserTwoFactorAuthentication Row(bool enabled, string? codes = "A,B,C,D,E", int used = 0) => new()
        {
            UserId = U, IsEnabled = enabled, Method = "authenticator", TotpSecret = Secret, BackupCodes = codes, BackupCodesUsed = used,
        };

        [Fact]
        public async Task Sans_configuration_le_statut_est_desactive()
        {
            var (_, svc) = Build();
            var s = await svc.GetTwoFactorStatusAsync(U);
            Assert.False(s.IsEnabled);
            Assert.Null(s.Method);
            Assert.Equal(0, s.BackupCodesCount);
        }

        [Fact]
        public async Task Le_nombre_de_codes_de_secours_exclut_ceux_deja_utilises()
        {
            var (db, svc) = Build();
            db.UserTwoFactorAuthentications.Add(Row(true, used: 2));
            db.SaveChanges();
            var s = await svc.GetTwoFactorStatusAsync(U);
            Assert.True(s.IsEnabled);
            Assert.Equal(3, s.BackupCodesCount);
        }

        [Fact]
        public async Task Sans_codes_de_secours_le_compte_est_zero()
        {
            var (db, svc) = Build();
            db.UserTwoFactorAuthentications.Add(Row(true, codes: null));
            db.SaveChanges();
            Assert.Equal(0, (await svc.GetTwoFactorStatusAsync(U)).BackupCodesCount);
        }

        [Fact]
        public async Task L_initialisation_renvoie_secret_qr_et_cinq_codes_sans_rien_enregistrer()
        {
            var (db, svc) = Build();
            var r = await svc.InitializeTwoFactorAsync(U, "authenticator");
            Assert.False(string.IsNullOrEmpty(r.Secret));
            Assert.Equal(5, r.BackupCodes.Count);
            Assert.Contains("otpauth", Uri.UnescapeDataString(r.QrCode));
            Assert.Empty(db.UserTwoFactorAuthentications);
        }

        [Fact]
        public void Les_codes_de_secours_ont_huit_caracteres_majuscules_alphanumeriques()
        {
            var (_, svc) = Build();
            var codes = svc.GenerateBackupCodes(8);
            Assert.Equal(8, codes.Count);
            Assert.All(codes, c =>
            {
                Assert.Equal(8, c.Length);
                Assert.Equal(c.ToUpperInvariant(), c);
                Assert.DoesNotContain('+', c);
                Assert.DoesNotContain('/', c);
            });
        }

        [Fact]
        public void Le_secret_genere_est_un_base64_de_vingt_octets()
        {
            var (_, svc) = Build();
            Assert.Equal(20, Convert.FromBase64String(svc.GenerateTotpSecret()).Length);
        }

        [Theory]
        [InlineData("000000x")]
        [InlineData("12345")]
        [InlineData("")]
        public async Task L_activation_refuse_un_code_mal_forme_et_n_enregistre_rien(string code)
        {
            var (db, svc) = Build();
            await Assert.ThrowsAsync<InvalidOperationException>(() => svc.EnableTwoFactorAsync(U, code, Secret));
            Assert.Empty(db.UserTwoFactorAuthentications);
        }

        [Fact]
        public async Task L_activation_refuse_un_secret_invalide()
        {
            var (_, svc) = Build();
            await Assert.ThrowsAsync<InvalidOperationException>(() => svc.EnableTwoFactorAsync(U, "123456", "pas-du-base64!"));
        }

        [Fact]
        public async Task La_verification_exige_une_double_authentification_activee()
        {
            var (db, svc) = Build();
            db.UserTwoFactorAuthentications.Add(Row(false));
            db.SaveChanges();
            await Assert.ThrowsAsync<InvalidOperationException>(() => svc.VerifyTwoFactorAsync(U, "123456"));
        }

        [Fact]
        public async Task La_validation_renvoie_faux_sans_configuration_ou_desactivee_ou_sans_secret()
        {
            var (db, svc) = Build();
            Assert.False(await svc.ValidateTwoFactorCodeAsync(U, "123456"));
            db.UserTwoFactorAuthentications.Add(Row(false));
            db.SaveChanges();
            Assert.False(await svc.ValidateTwoFactorCodeAsync(U, "123456"));
        }

        [Fact]
        public async Task La_validation_refuse_un_code_non_numerique_de_six_caracteres()
        {
            var (db, svc) = Build();
            db.UserTwoFactorAuthentications.Add(Row(true));
            db.SaveChanges();
            Assert.False(await svc.ValidateTwoFactorCodeAsync(U, "abcdef"));
        }

        [Fact]
        public async Task La_desactivation_efface_secret_codes_et_methode()
        {
            var (db, svc) = Build();
            db.UserTwoFactorAuthentications.Add(Row(true));
            db.SaveChanges();
            await svc.DisableTwoFactorAsync(U);
            var row = db.UserTwoFactorAuthentications.Single();
            Assert.False(row.IsEnabled);
            Assert.Null(row.TotpSecret);
            Assert.Null(row.BackupCodes);
            Assert.Null(row.Method);
        }

        [Fact]
        public async Task La_desactivation_sans_configuration_ne_fait_rien()
        {
            var (db, svc) = Build();
            await svc.DisableTwoFactorAsync(U);
            Assert.Empty(db.UserTwoFactorAuthentications);
        }
    }
}
