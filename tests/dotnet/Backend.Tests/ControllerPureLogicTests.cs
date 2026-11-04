using System;
using System.Threading.Tasks;
using Backend.Controllers;
using Backend.Models.Entities;
using Backend.Utils;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace Backend.Tests
{
    /// <summary>
    /// Logique propre aux contrôleurs, sans serveur : forme publique d'un retrait
    /// (numéro masqué, indicateur « en cours ») et exigence d'un e-mail vérifié
    /// avant publication ou retrait (administrateurs exemptés, message selon la
    /// langue, code stable lu par le client).
    /// </summary>
    public class ControllerPureLogicTests
    {
        private static object? P(object o, string name) => o.GetType().GetProperty(name)!.GetValue(o);

        [Theory]
        [InlineData("pending", true)]
        [InlineData("processing", true)]
        [InlineData("completed", false)]
        [InlineData("failed", false)]
        public void Le_retrait_indique_s_il_est_en_cours(string status, bool inFlight)
        {
            var dto = WithdrawalsController.ToDto(new Withdrawal { Id = 1, Phone = "237677123456", Status = status, AmountXaf = 5000m });
            Assert.Equal(inFlight, P(dto, "inProgress"));
            Assert.Equal(5000m, P(dto, "amount"));
            Assert.Equal(5000m, P(dto, "amountXaf"));
            Assert.Equal(status, P(dto, "status"));
        }

        [Fact]
        public void Le_retrait_n_expose_jamais_le_numero_complet_ni_la_note_interne()
        {
            var dto = WithdrawalsController.ToDto(new Withdrawal { Id = 1, Phone = "237677123456", AdminNote = "note interne", TransferReference = "REF-SECRET" });
            var masked = (string?)P(dto, "phoneMasked");
            Assert.NotNull(masked);
            Assert.DoesNotContain("677123456", masked);
            Assert.Null(dto.GetType().GetProperty("adminNote"));
            Assert.Null(dto.GetType().GetProperty("transferReference"));
            Assert.Null(dto.GetType().GetProperty("phone"));
        }

        [Fact]
        public async Task Un_administrateur_est_exempte_de_la_verification_d_e_mail()
        {
            using var db = TestApplicationDbContext.Create();
            db.Users.Add(new User { Id = 1, Email = "a@t.l", Role = "admin", IsEmailVerified = false });
            db.SaveChanges();
            Assert.Null(await VerifiedEmailRequirement.CheckAsync(db, 1, isAdmin: true));
        }

        [Fact]
        public async Task Un_compte_verifie_passe_et_un_compte_non_verifie_ou_inconnu_est_refuse_en_403()
        {
            using var db = TestApplicationDbContext.Create();
            db.Users.Add(new User { Id = 1, Email = "ok@t.l", Role = "teacher", IsEmailVerified = true });
            db.Users.Add(new User { Id = 2, Email = "ko@t.l", Role = "teacher", IsEmailVerified = false });
            db.SaveChanges();
            Assert.Null(await VerifiedEmailRequirement.CheckAsync(db, 1, false));
            foreach (var id in new[] { 2, 999 })
            {
                var result = Assert.IsType<ObjectResult>(await VerifiedEmailRequirement.CheckAsync(db, id, false));
                Assert.Equal(403, result.StatusCode);
                Assert.Equal(VerifiedEmailRequirement.Code, P(result.Value!, "code"));
            }
        }

        [Fact]
        public async Task Le_message_de_refus_suit_la_langue_demandee()
        {
            using var db = TestApplicationDbContext.Create();
            db.Users.Add(new User { Id = 2, Email = "ko@t.l", Role = "teacher", IsEmailVerified = false });
            db.SaveChanges();
            var fr = (string)P(((ObjectResult)(await VerifiedEmailRequirement.CheckAsync(db, 2, false))!).Value!, "error")!;
            var en = (string)P(((ObjectResult)(await VerifiedEmailRequirement.CheckAsync(db, 2, false, english: true))!).Value!, "error")!;
            Assert.StartsWith("Vérifiez", fr);
            Assert.StartsWith("Verify", en);
        }
    }
}
