using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Backend.Services;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Backend.Tests
{
    /// <summary>
    /// Double authentification refaite le 28/11/2026 : secret base32 chiffré, activation en deux temps confirmée par
    /// un code, codes de secours hachés et à usage unique, refus du rejeu, jeton de connexion en attente signé.
    /// Les codes valides sont calculés ici avec l'algorithme standard (RFC 6238), comme le ferait l'application.
    /// </summary>
    public class TwoFactorServiceTests
    {
        private const int U = 9101;

        private static readonly IConfiguration Config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["JWT:SecretKey"] = "cle-de-test-suffisamment-longue-pour-hkdf-0123456789" })
            .Build();

        private static (TestApplicationDbContext Db, TwoFactorService Svc) Build()
        {
            var db = TestApplicationDbContext.Create();
            return (db, new TwoFactorService(db, NullLogger<TwoFactorService>.Instance, Config, new MemoryCache(new MemoryCacheOptions())));
        }

        // ── Algorithme de référence (côté « application d'authentification ») ──

        private static byte[] FromBase32(string s)
        {
            const string a = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
            var output = new List<byte>(); int buffer = 0, bits = 0;
            foreach (var c in s.Replace(" ", "").TrimEnd('='))
            {
                buffer = (buffer << 5) | a.IndexOf(c); bits += 5;
                if (bits >= 8) { output.Add((byte)((buffer >> (bits - 8)) & 0xff)); bits -= 8; }
            }
            return output.ToArray();
        }

        private static string Code(byte[] key, long step)
        {
            var counter = BitConverter.GetBytes(step);
            if (BitConverter.IsLittleEndian) Array.Reverse(counter);
            var h = HMACSHA1.HashData(key, counter);
            var o = h[^1] & 0x0f;
            var v = ((h[o] & 0x7f) << 24) | (h[o + 1] << 16) | (h[o + 2] << 8) | h[o + 3];
            return (v % 1_000_000).ToString("D6");
        }

        private static long Now => DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 30;

        [Fact]
        public void Vecteur_officiel_RFC_6238()
        {
            // Secret « 12345678901234567890 », T = 59 s : 94287082 sur 8 chiffres, donc 287082 sur 6.
            Assert.Equal("287082", Code(Encoding.ASCII.GetBytes("12345678901234567890"), 59 / 30));
        }

        [Fact]
        public async Task Activation_en_deux_temps_puis_connexion_et_secours()
        {
            var (db, svc) = Build();
            var setup = await svc.StartSetupAsync(U, "eleve@winplus.cm");
            Assert.Matches("^[A-Z2-7]{32}$", setup.Secret);
            Assert.StartsWith("otpauth://totp/WinPlus%3Aeleve%40winplus.cm?secret=" + setup.Secret, setup.OtpauthUrl);
            Assert.StartsWith("data:image/png;base64,", setup.QrCode);

            var row = db.UserTwoFactorAuthentications.Single();
            Assert.False(row.IsEnabled);
            Assert.StartsWith("v1:", row.TotpSecret);
            Assert.DoesNotContain(setup.Secret!, row.TotpSecret);
            Assert.True((await svc.GetTwoFactorStatusAsync(U)).PendingSetup);
            Assert.False(await svc.IsEnabledAsync(U));

            var key = FromBase32(setup.Secret!);
            var refus = await Assert.ThrowsAsync<TwoFactorException>(() => svc.ConfirmSetupAsync(U, Code(key, Now + 5)));
            Assert.Equal("code_invalide", refus.Code);

            var (status, backup) = await svc.ConfirmSetupAsync(U, Code(key, Now));
            Assert.True(status.IsEnabled);
            Assert.Equal(8, backup.Count);
            Assert.Equal(8, status.BackupCodesCount);
            Assert.True(await svc.IsEnabledAsync(U));
            Assert.All(backup, c => Assert.DoesNotContain(c, db.UserTwoFactorAuthentications.Single().BackupCodes!));

            // Le code qui vient de servir n'est pas rejouable dans sa fenêtre.
            Assert.False(await svc.VerifyLoginCodeAsync(U, Code(key, Now)));
            Assert.True(await svc.VerifyLoginCodeAsync(U, Code(key, Now + 1)));

            // Code de secours : accepté une fois (casse et tirets indifférents), puis consommé.
            var b = backup[0];
            Assert.True(await svc.VerifyLoginCodeAsync(U, b.ToLowerInvariant().Insert(4, "-")));
            Assert.False(await svc.VerifyLoginCodeAsync(U, b));
            Assert.Equal(7, (await svc.GetTwoFactorStatusAsync(U)).BackupCodesCount);

            await Assert.ThrowsAsync<TwoFactorException>(() => svc.StartSetupAsync(U, "eleve@winplus.cm"));
        }

        [Fact]
        public async Task Desactivation_exige_un_code_valide()
        {
            var (db, svc) = Build();
            var setup = await svc.StartSetupAsync(U, "a@b.c");
            var key = FromBase32(setup.Secret!);
            var (_, backup) = await svc.ConfirmSetupAsync(U, Code(key, Now - 1));

            Assert.Equal("code_invalide", (await Assert.ThrowsAsync<TwoFactorException>(() => svc.DisableAsync(U, "000000"))).Code);
            await svc.DisableAsync(U, backup[3]);
            var row = db.UserTwoFactorAuthentications.Single();
            Assert.False(row.IsEnabled);
            Assert.Null(row.TotpSecret);
            Assert.Null(row.BackupCodes);
            Assert.False((await svc.GetTwoFactorStatusAsync(U)).IsEnabled);
        }

        [Fact]
        public async Task Nouveaux_codes_de_secours_invalident_les_anciens()
        {
            var (_, svc) = Build();
            var key = FromBase32((await svc.StartSetupAsync(U, "a@b.c")).Secret!);
            var (_, old) = await svc.ConfirmSetupAsync(U, Code(key, Now - 1));
            var fresh = await svc.RegenerateBackupCodesAsync(U, Code(key, Now));
            Assert.Equal(8, fresh.Count);
            Assert.False(await svc.VerifyLoginCodeAsync(U, old[0]));
            Assert.True(await svc.VerifyLoginCodeAsync(U, fresh[0]));
        }

        [Fact]
        public void Jeton_de_connexion_signe_et_infalsifiable()
        {
            var (_, svc) = Build();
            var token = svc.CreateLoginChallenge(42, rememberMe: true, useCookie: false);
            var read = svc.ReadLoginChallenge(token);
            Assert.Equal((42, true, false), read);
            Assert.Null(svc.ReadLoginChallenge(token[..^2] + (token[^1] == 'A' ? "BB" : "AA")));
            Assert.Null(svc.ReadLoginChallenge("n'importe.quoi"));
            Assert.Null(svc.ReadLoginChallenge(""));
        }

        [Fact]
        public async Task Sans_activation_aucun_code_n_est_accepte()
        {
            var (_, svc) = Build();
            Assert.False(await svc.VerifyLoginCodeAsync(U, "123456"));
            Assert.Equal("aucune_activation", (await Assert.ThrowsAsync<TwoFactorException>(() => svc.ConfirmSetupAsync(U, "123456"))).Code);
            Assert.False((await svc.GetTwoFactorStatusAsync(U)).IsEnabled);
        }

        [Fact]
        public void Les_codes_de_secours_ont_huit_caracteres_sans_ambiguite()
        {
            var (_, svc) = Build();
            var codes = svc.GenerateBackupCodes();
            Assert.Equal(8, codes.Count);
            Assert.All(codes, c => Assert.Matches("^[A-HJ-NP-Z2-9]{8}$", c));
        }
    }
}
