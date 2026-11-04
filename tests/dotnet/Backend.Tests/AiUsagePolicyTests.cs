using System;
using System.Collections.Generic;
using Backend.Services;
using Xunit;

namespace Backend.Tests
{
    /// <summary>
    /// Quota WinAI (décision 8.10) : limites dérivées de la référence, fenêtre
    /// glissante de session (5 h) et de semaine (7 j), limite qui bloque, rejeu
    /// d'un ClientMessageId et libellés de refus. Classe statique sans dépendance :
    /// l'heure est toujours passée en paramètre, aucune base requise.
    /// </summary>
    public class AiUsagePolicyTests
    {
        // Mardi 13 octobre 2026, 10:05 UTC (11:05 à Douala).
        private static readonly DateTime Now = new(2026, 10, 13, 10, 5, 0, DateTimeKind.Utc);

        private static AiWindowState State(bool reached, DateTime? resetsAt) =>
            new(100, reached ? 100 : 0, null, reached, resetsAt);

        // ── Constantes et limites dérivées ──────────────────────────────

        [Fact]
        public void Les_durees_de_fenetre_et_de_rejeu_suivent_les_constantes()
        {
            Assert.Equal(TimeSpan.FromHours(5), AiUsagePolicy.SessionLength);
            Assert.Equal(TimeSpan.FromDays(7), AiUsagePolicy.WeekLength);
            Assert.Equal(TimeSpan.FromMinutes(15), AiUsagePolicy.ReplayWindow);
        }

        [Fact]
        public void La_limite_hebdomadaire_est_le_quart_de_la_reference_et_la_session_le_tiers_de_la_semaine()
        {
            Assert.Equal(250L, AiUsagePolicy.WeeklyLimit(1000));
            Assert.Equal(83L, AiUsagePolicy.SessionLimit(1000)); // 250 / 3 en division entiere
            Assert.Equal(250_000L, AiUsagePolicy.WeeklyLimit(1_000_000));
            Assert.Equal(83_333L, AiUsagePolicy.SessionLimit(1_000_000));
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(3)]
        public void Les_limites_ne_descendent_jamais_sous_un_token(long reference)
        {
            Assert.Equal(1L, AiUsagePolicy.WeeklyLimit(reference));
            Assert.Equal(1L, AiUsagePolicy.SessionLimit(reference));
        }

        [Fact]
        public void Le_multiplicateur_est_le_rapport_arrondi_a_la_reference_du_plan_gratuit()
        {
            Assert.Equal(5, AiUsagePolicy.MultiplierFor(5000, 1000));
            Assert.Equal(1, AiUsagePolicy.MultiplierFor(1000, 1000));
            Assert.Equal(2, AiUsagePolicy.MultiplierFor(1500, 1000));
        }

        [Fact]
        public void Le_multiplicateur_vaut_au_moins_un_et_tolere_une_reference_gratuite_nulle()
        {
            Assert.Equal(1, AiUsagePolicy.MultiplierFor(10, 1000));
            Assert.Equal(1, AiUsagePolicy.MultiplierFor(0, 1000));
            // Reference gratuite 0 : le diviseur est ramene a 1, pas de division par zero.
            Assert.Equal(5, AiUsagePolicy.MultiplierFor(5, 0));
        }

        [Theory]
        [InlineData(0, 1000)]
        [InlineData(3, 1000)]
        [InlineData(400, 1100)]
        [InlineData(4000, 2000)]
        [InlineData(-50, 1000)]
        public void Le_cout_estime_ajoute_le_forfait_et_un_token_par_quatre_caracteres(int chars, int expected)
        {
            Assert.Equal(expected, AiUsagePolicy.EstimateServedTokens(chars));
        }

        // ── Fenêtre glissante ───────────────────────────────────────────

        [Fact]
        public void Sans_enregistrement_la_fenetre_est_vide_et_sans_ancre()
        {
            var s = AiUsagePolicy.Evaluate(Array.Empty<AiUsageRecord>(), Now, AiUsagePolicy.SessionLength, 100);
            Assert.Equal(0L, s.Used);
            Assert.Null(s.AnchorAt);
            Assert.False(s.IsReached);
            Assert.Null(s.ResetsAt);
            Assert.Equal(100L, s.Limit);
        }

        [Fact]
        public void Les_enregistrements_hors_fenetre_ne_comptent_pas_et_l_ancre_est_le_premier_dans_la_fenetre()
        {
            var records = new[]
            {
                new AiUsageRecord(Now.AddHours(-6), 50),   // sorti de la fenetre de 5 h
                new AiUsageRecord(Now.AddHours(-1), 30),
                new AiUsageRecord(Now.AddHours(-4), 40),   // desordonne a dessein
            };
            var s = AiUsagePolicy.Evaluate(records, Now, AiUsagePolicy.SessionLength, 100);
            Assert.Equal(70L, s.Used);
            Assert.Equal(Now.AddHours(-4), s.AnchorAt);
            Assert.False(s.IsReached);
            Assert.Null(s.ResetsAt);
        }

        [Fact]
        public void Un_enregistrement_pile_a_la_limite_de_la_fenetre_est_exclu()
        {
            var records = new[] { new AiUsageRecord(Now.AddHours(-5), 999) };
            var s = AiUsagePolicy.Evaluate(records, Now, AiUsagePolicy.SessionLength, 100);
            Assert.Equal(0L, s.Used);
            Assert.False(s.IsReached);
        }

        [Fact]
        public void La_limite_est_atteinte_a_egalite_et_la_reinitialisation_est_celle_de_l_enregistrement_qui_fait_repasser_sous_la_limite()
        {
            var records = new[]
            {
                new AiUsageRecord(Now.AddHours(-4), 60),
                new AiUsageRecord(Now.AddHours(-1), 40),
            };
            var s = AiUsagePolicy.Evaluate(records, Now, AiUsagePolicy.SessionLength, 100);
            Assert.True(s.IsReached);
            Assert.Equal(100L, s.Used);
            // Une fois le premier enregistrement (60) sorti, il reste 40 < 100 : reset a -4 h + 5 h.
            Assert.Equal(Now.AddHours(1), s.ResetsAt);
        }

        [Fact]
        public void La_reinitialisation_peut_exiger_la_sortie_de_plusieurs_enregistrements()
        {
            var records = new[]
            {
                new AiUsageRecord(Now.AddHours(-4), 10),
                new AiUsageRecord(Now.AddHours(-3), 10),
                new AiUsageRecord(Now.AddHours(-2), 90),
            };
            // Total 110, limite 100 : retirer 10 laisse 100 (pas encore sous la limite),
            // il faut aussi sortir le deuxieme enregistrement (-3 h + 5 h).
            var s = AiUsagePolicy.Evaluate(records, Now, AiUsagePolicy.SessionLength, 100);
            Assert.Equal(Now.AddHours(2), s.ResetsAt);

            // Limite 50 : il faut retirer 10 + 10 + 90 > 60, soit jusqu'au troisieme enregistrement.
            var s2 = AiUsagePolicy.Evaluate(records, Now, AiUsagePolicy.SessionLength, 50);
            Assert.Equal(Now.AddHours(3), s2.ResetsAt);
        }

        [Fact]
        public void Les_tokens_negatifs_sont_comptes_pour_zero()
        {
            var records = new[]
            {
                new AiUsageRecord(Now.AddHours(-2), -500),
                new AiUsageRecord(Now.AddHours(-1), 20),
            };
            var s = AiUsagePolicy.Evaluate(records, Now, AiUsagePolicy.SessionLength, 100);
            Assert.Equal(20L, s.Used);
        }

        [Fact]
        public void La_fenetre_de_semaine_garde_ce_que_la_session_a_deja_oublie()
        {
            var records = new[] { new AiUsageRecord(Now.AddDays(-3), 80) };
            var session = AiUsagePolicy.Evaluate(records, Now, AiUsagePolicy.SessionLength, 50);
            var week = AiUsagePolicy.Evaluate(records, Now, AiUsagePolicy.WeekLength, 50);
            Assert.False(session.IsReached);
            Assert.True(week.IsReached);
            Assert.Equal(Now.AddDays(4), week.ResetsAt);
        }

        // ── Limite qui bloque ───────────────────────────────────────────

        [Fact]
        public void Aucune_limite_atteinte_ne_bloque_rien()
        {
            var (kind, resets) = AiUsagePolicy.Binding(State(false, null), State(false, null));
            Assert.Null(kind);
            Assert.Null(resets);
        }

        private static void AssertBinding(AiLimitKind? kind, DateTime? resets, AiWindowState session, AiWindowState week)
        {
            var (k, r) = AiUsagePolicy.Binding(session, week);
            Assert.Equal(kind, k);
            Assert.Equal(resets, r);
        }

        [Fact]
        public void Une_seule_limite_atteinte_est_celle_qui_bloque()
        {
            var r = Now.AddHours(2);
            AssertBinding(AiLimitKind.Week, r, State(false, null), State(true, r));
            AssertBinding(AiLimitKind.Session, r, State(true, r), State(false, null));
        }

        [Fact]
        public void Si_les_deux_sont_atteintes_la_reinitialisation_la_plus_tardive_decide()
        {
            var early = Now.AddHours(1);
            var late = Now.AddDays(2);
            AssertBinding(AiLimitKind.Week, late, State(true, early), State(true, late));
            AssertBinding(AiLimitKind.Session, late, State(true, late), State(true, early));
        }

        [Fact]
        public void A_egalite_de_reinitialisation_la_semaine_l_emporte()
        {
            var same = Now.AddHours(3);
            AssertBinding(AiLimitKind.Week, same, State(true, same), State(true, same));
        }

        // ── Rejeu d'un ClientMessageId ──────────────────────────────────

        [Fact]
        public void Un_identifiant_inconnu_est_un_nouveau_message()
        {
            Assert.Equal(AiReplayVerdict.NewMessage, AiUsagePolicy.ClassifyExisting(false, false, Now, Now));
            // Meme si les autres arguments sont incoherents : sans ligne, rien a rattacher.
            Assert.Equal(AiReplayVerdict.NewMessage, AiUsagePolicy.ClassifyExisting(false, true, Now.AddDays(-9), Now));
        }

        [Fact]
        public void Une_ligne_non_finalisee_et_recente_est_rattachee()
        {
            Assert.Equal(AiReplayVerdict.Reattach, AiUsagePolicy.ClassifyExisting(true, false, Now.AddMinutes(-14), Now));
            Assert.Equal(AiReplayVerdict.Reattach, AiUsagePolicy.ClassifyExisting(true, false, Now, Now));
        }

        [Fact]
        public void Une_ligne_finalisee_consomme_l_identifiant_meme_si_elle_est_recente()
        {
            Assert.Equal(AiReplayVerdict.ConsumedId, AiUsagePolicy.ClassifyExisting(true, true, Now.AddMinutes(-1), Now));
        }

        [Fact]
        public void Une_ligne_non_finalisee_de_quinze_minutes_ou_plus_consomme_l_identifiant()
        {
            Assert.Equal(AiReplayVerdict.ConsumedId, AiUsagePolicy.ClassifyExisting(true, false, Now.AddMinutes(-15), Now));
            Assert.Equal(AiReplayVerdict.ConsumedId, AiUsagePolicy.ClassifyExisting(true, false, Now.AddHours(-3), Now));
        }

        // ── Libellés ────────────────────────────────────────────────────

        [Fact]
        public void L_heure_de_reinitialisation_est_formatee_en_heure_de_Douala_avec_jours_et_mois_en_francais()
        {
            Assert.Equal("le mardi 13 octobre à 11h05", AiUsagePolicy.FormatResetTime(Now));
        }

        [Fact]
        public void Le_formatage_passe_au_jour_suivant_apres_minuit_local()
        {
            // 31 octobre 2026 (samedi) 23:30 UTC = 1er novembre 00:30 a Douala (dimanche).
            var utc = new DateTime(2026, 10, 31, 23, 30, 0, DateTimeKind.Utc);
            Assert.Equal("le dimanche 1 novembre à 00h30", AiUsagePolicy.FormatResetTime(utc));
        }

        [Fact]
        public void Le_formatage_traite_une_date_sans_fuseau_comme_de_l_UTC()
        {
            var unspecified = new DateTime(2026, 10, 13, 10, 5, 0, DateTimeKind.Unspecified);
            Assert.Equal("le mardi 13 octobre à 11h05", AiUsagePolicy.FormatResetTime(unspecified));
        }

        [Fact]
        public void Le_message_de_session_sans_heure_annonce_les_cinq_heures_glissantes()
        {
            var msg = AiUsagePolicy.LimitMessage(AiLimitKind.Session, null);
            Assert.Contains("limite de session", msg);
            Assert.Contains("5 heures glissantes", msg);
        }

        [Fact]
        public void Le_message_hebdomadaire_sans_heure_annonce_les_sept_jours_glissants()
        {
            var msg = AiUsagePolicy.LimitMessage(AiLimitKind.Week, null);
            Assert.Contains("limite hebdomadaire", msg);
            Assert.Contains("7 jours glissants", msg);
        }

        [Fact]
        public void Les_messages_avec_heure_donnent_le_moment_ou_l_utilisateur_pourra_reecrire()
        {
            var session = AiUsagePolicy.LimitMessage(AiLimitKind.Session, Now);
            var week = AiUsagePolicy.LimitMessage(AiLimitKind.Week, Now);
            Assert.Contains("le mardi 13 octobre à 11h05", session);
            Assert.Contains("le mardi 13 octobre à 11h05", week);
            Assert.Contains("limite de session", session);
            Assert.Contains("limite hebdomadaire", week);
        }

        [Fact]
        public void Aucun_message_de_refus_n_expose_de_nombre_de_tokens()
        {
            foreach (var kind in new[] { AiLimitKind.Session, AiLimitKind.Week })
            foreach (DateTime? at in new DateTime?[] { null, Now })
            {
                var msg = AiUsagePolicy.LimitMessage(kind, at);
                Assert.DoesNotContain("token", msg, StringComparison.OrdinalIgnoreCase);
            }
        }
    }
}
