using System;
using Backend.Services;
using Xunit;

namespace Backend.Tests
{
    /// <summary>
    /// Règles de statut d'une commande (décisions 4.D, 10.3 à 10.11) : quand le
    /// client peut annuler, quand un nouveau paiement est refusé, et quand une
    /// transition automatique (webhook, expiration) doit être ignorée. Fonctions
    /// pures, aucune base requise.
    /// </summary>
    public class OrderStatusRulesTests
    {
        // ── Annulation par le client ────────────────────────────────────

        [Theory]
        [InlineData("pending")]
        [InlineData("failed")]
        [InlineData("cancelled")]
        [InlineData("Pending")]
        [InlineData("")]
        [InlineData(null)]
        [InlineData("statut_inconnu")]
        public void Une_commande_non_protegee_peut_etre_annulee(string? status)
        {
            Assert.Null(OrderStatusRules.CancellationBlockReason(status));
        }

        [Fact]
        public void Chaque_statut_protege_renvoie_son_propre_motif_d_annulation()
        {
            Assert.Equal(OrderStatusRules.CompletedCancelMessage, OrderStatusRules.CancellationBlockReason("completed"));
            Assert.Equal(OrderStatusRules.PaidCancelMessage, OrderStatusRules.CancellationBlockReason("paid"));
            Assert.Equal(OrderStatusRules.RefundPendingCancelMessage, OrderStatusRules.CancellationBlockReason("refund_requested"));
            Assert.Equal(OrderStatusRules.RefundedCancelMessage, OrderStatusRules.CancellationBlockReason("refunded"));
        }

        [Fact]
        public void Le_statut_est_normalise_avant_la_comparaison()
        {
            Assert.Equal(OrderStatusRules.PaidCancelMessage, OrderStatusRules.CancellationBlockReason("  PAID "));
            Assert.Equal(OrderStatusRules.RefundedCancelMessage, OrderStatusRules.CancellationBlockReason("Refunded"));
            Assert.Equal(OrderStatusRules.RefundPendingPaymentMessage, OrderStatusRules.PaymentBlockReason(" REFUND_REQUESTED"));
        }

        // ── Nouveau paiement ────────────────────────────────────────────

        [Theory]
        [InlineData("pending")]
        [InlineData("failed")]
        [InlineData("cancelled")]
        [InlineData(null)]
        public void Une_commande_en_attente_ou_echouee_peut_etre_payee(string? status)
        {
            Assert.Null(OrderStatusRules.PaymentBlockReason(status));
        }

        [Fact]
        public void Une_commande_deja_reglee_refuse_un_nouveau_paiement_avec_le_meme_message()
        {
            Assert.Equal(OrderStatusRules.AlreadySettledPaymentMessage, OrderStatusRules.PaymentBlockReason("completed"));
            Assert.Equal(OrderStatusRules.AlreadySettledPaymentMessage, OrderStatusRules.PaymentBlockReason("paid"));
        }

        [Fact]
        public void Une_demande_de_remboursement_ou_un_remboursement_refuse_un_nouveau_paiement()
        {
            Assert.Equal(OrderStatusRules.RefundPendingPaymentMessage, OrderStatusRules.PaymentBlockReason("refund_requested"));
            Assert.Equal(OrderStatusRules.RefundedPaymentMessage, OrderStatusRules.PaymentBlockReason("refunded"));
        }

        [Fact]
        public void Les_messages_de_refus_partagent_le_meme_constat_et_orientent_vers_l_administrateur_ou_une_nouvelle_commande()
        {
            Assert.StartsWith(OrderStatusRules.RefundPendingNotice, OrderStatusRules.RefundPendingCancelMessage);
            Assert.StartsWith(OrderStatusRules.RefundPendingNotice, OrderStatusRules.RefundPendingPaymentMessage);
            Assert.StartsWith(OrderStatusRules.RefundedNotice, OrderStatusRules.RefundedCancelMessage);
            Assert.StartsWith(OrderStatusRules.RefundedNotice, OrderStatusRules.RefundedPaymentMessage);
            Assert.Contains("administrateur", OrderStatusRules.RefundPendingCancelMessage);
            Assert.Contains("nouvelle commande", OrderStatusRules.RefundedPaymentMessage);
        }

        // ── Statut protégé ──────────────────────────────────────────────

        [Theory]
        [InlineData("completed", true)]
        [InlineData("paid", true)]
        [InlineData("refund_requested", true)]
        [InlineData("refunded", true)]
        [InlineData("pending", false)]
        [InlineData("failed", false)]
        [InlineData("cancelled", false)]
        [InlineData(null, false)]
        public void Le_statut_protege_est_derive_des_refus_de_paiement(string? status, bool expected)
        {
            Assert.Equal(expected, OrderStatusRules.IsProtected(status));
        }

        // ── Transitions automatiques ────────────────────────────────────

        [Theory]
        [InlineData("completed", "failed")]
        [InlineData("completed", "pending")]
        [InlineData("completed", "cancelled")]
        [InlineData("paid", "failed")]
        [InlineData("refund_requested", "pending")]
        [InlineData("refund_requested", "cancelled")]
        public void Une_transition_automatique_ne_fait_pas_sortir_une_commande_protegee_vers_un_statut_annulable(string current, string target)
        {
            Assert.True(OrderStatusRules.BlocksAutomaticTransition(current, target));
        }

        [Theory]
        [InlineData("completed", "refunded")]
        [InlineData("paid", "completed")]
        [InlineData("refund_requested", "refunded")]
        [InlineData("pending", "failed")]
        [InlineData("pending", "completed")]
        [InlineData("failed", "pending")]
        [InlineData(null, "completed")]
        public void Une_transition_vers_un_statut_protege_ou_depuis_un_statut_libre_est_permise(string? current, string target)
        {
            Assert.False(OrderStatusRules.BlocksAutomaticTransition(current, target));
        }

        [Theory]
        [InlineData("completed")]
        [InlineData("paid")]
        [InlineData("pending")]
        [InlineData("failed")]
        [InlineData("refund_requested")]
        public void Une_commande_remboursee_est_terminale_pour_les_chemins_automatiques(string target)
        {
            // Un paiement confirme apres l'approbation du remboursement ne rouvre pas l'acces.
            Assert.True(OrderStatusRules.BlocksAutomaticTransition("refunded", target));
        }

        [Fact]
        public void Une_commande_remboursee_reste_remboursee_sans_etre_consideree_comme_bloquee()
        {
            Assert.False(OrderStatusRules.BlocksAutomaticTransition("refunded", "refunded"));
            Assert.False(OrderStatusRules.BlocksAutomaticTransition("REFUNDED", " refunded "));
        }

        // ── Exception de paiement refusé ────────────────────────────────

        [Fact]
        public void OrderNotPayableException_reste_une_InvalidOperationException_et_conserve_le_motif()
        {
            var ex = new OrderNotPayableException(OrderStatusRules.AlreadySettledPaymentMessage);
            Assert.IsAssignableFrom<InvalidOperationException>(ex);
            Assert.Equal(OrderStatusRules.AlreadySettledPaymentMessage, ex.Message);
        }
    }
}
