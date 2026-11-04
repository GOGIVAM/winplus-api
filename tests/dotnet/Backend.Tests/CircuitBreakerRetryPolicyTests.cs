using System;
using System.Threading.Tasks;
using Backend.Utilities;
using Xunit;

namespace Backend.Tests
{
    /// <summary>
    /// Disjoncteur et politique de reprise utilisés pour les appels FastAPI.
    /// Pour ne dépendre ni de l'horloge ni d'attentes réelles : le délai de
    /// réarmement vaut zéro (le circuit se réarme à l'instant) ou une heure (il
    /// ne se réarme pas pendant le test), et le délai initial de reprise est nul.
    /// </summary>
    public class CircuitBreakerRetryPolicyTests
    {
        private static CircuitBreaker Breaker(int threshold, TimeSpan reset) =>
            new(threshold, TimeSpan.FromSeconds(30), reset);

        // ── CircuitBreaker ──────────────────────────────────────────────

        [Fact]
        public void Un_circuit_neuf_est_ferme_et_laisse_passer_les_appels()
        {
            var cb = Breaker(3, TimeSpan.FromHours(1));
            Assert.Equal(CircuitState.Closed, cb.State);
            Assert.Equal(0, cb.FailureCount);
            Assert.True(cb.IsCallAllowed());
        }

        [Fact]
        public void Le_circuit_reste_ferme_sous_le_seuil_d_echecs()
        {
            var cb = Breaker(3, TimeSpan.FromHours(1));
            cb.RecordFailure();
            cb.RecordFailure();
            Assert.Equal(CircuitState.Closed, cb.State);
            Assert.Equal(2, cb.FailureCount);
            Assert.True(cb.IsCallAllowed());
        }

        [Fact]
        public void Le_circuit_s_ouvre_au_seuil_et_refuse_les_appels_tant_que_le_delai_n_est_pas_ecoule()
        {
            var cb = Breaker(3, TimeSpan.FromHours(1));
            for (var i = 0; i < 3; i++) cb.RecordFailure();
            Assert.Equal(CircuitState.Open, cb.State);
            Assert.False(cb.IsCallAllowed());
            Assert.False(cb.CanAttemptReset());
            Assert.Equal(CircuitState.Open, cb.State);
        }

        [Fact]
        public void Un_succes_remet_le_compteur_a_zero_et_ferme_le_circuit()
        {
            var cb = Breaker(3, TimeSpan.FromHours(1));
            cb.RecordFailure();
            cb.RecordFailure();
            cb.RecordSuccess();
            Assert.Equal(0, cb.FailureCount);
            Assert.Equal(CircuitState.Closed, cb.State);
            // Il faut de nouveau trois echecs consecutifs pour ouvrir.
            cb.RecordFailure();
            cb.RecordFailure();
            Assert.Equal(CircuitState.Closed, cb.State);
        }

        [Fact]
        public void Apres_le_delai_de_rearmement_un_circuit_ouvert_passe_en_demi_ouvert_et_laisse_passer_un_essai()
        {
            var cb = Breaker(2, TimeSpan.Zero);
            cb.RecordFailure();
            cb.RecordFailure();
            Assert.Equal(CircuitState.Open, cb.State);

            Assert.True(cb.IsCallAllowed());
            Assert.Equal(CircuitState.HalfOpen, cb.State);
            // Deja demi-ouvert : l'appel reste autorise.
            Assert.True(cb.IsCallAllowed());
        }

        [Fact]
        public void CanAttemptReset_n_agit_que_sur_un_circuit_ouvert()
        {
            var cb = Breaker(2, TimeSpan.Zero);
            Assert.False(cb.CanAttemptReset());          // ferme
            cb.RecordFailure();
            cb.RecordFailure();
            Assert.True(cb.CanAttemptReset());           // ouvert -> demi-ouvert
            Assert.False(cb.CanAttemptReset());          // deja demi-ouvert
        }

        [Fact]
        public void Un_succes_en_demi_ouvert_referme_le_circuit()
        {
            var cb = Breaker(2, TimeSpan.Zero);
            cb.RecordFailure();
            cb.RecordFailure();
            Assert.True(cb.IsCallAllowed());
            cb.RecordSuccess();
            Assert.Equal(CircuitState.Closed, cb.State);
            Assert.Equal(0, cb.FailureCount);
        }

        [Fact]
        public void Un_echec_en_demi_ouvert_rouvre_le_circuit()
        {
            var cb = Breaker(2, TimeSpan.Zero);
            cb.RecordFailure();
            cb.RecordFailure();
            Assert.True(cb.IsCallAllowed());
            Assert.Equal(CircuitState.HalfOpen, cb.State);

            cb.RecordFailure();
            Assert.Equal(CircuitState.Open, cb.State);
            Assert.Equal(3, cb.FailureCount);
        }

        [Fact]
        public void Reset_ferme_le_circuit_et_efface_les_echecs()
        {
            var cb = Breaker(1, TimeSpan.FromHours(1));
            cb.RecordFailure();
            Assert.Equal(CircuitState.Open, cb.State);
            cb.Reset();
            Assert.Equal(CircuitState.Closed, cb.State);
            Assert.Equal(0, cb.FailureCount);
            Assert.True(cb.IsCallAllowed());
        }

        // ── RetryPolicy ─────────────────────────────────────────────────

        private static RetryPolicy Retry(int max) => new(max, TimeSpan.Zero);

        [Fact]
        public async Task Une_operation_qui_reussit_du_premier_coup_n_est_executee_qu_une_fois()
        {
            var calls = 0;
            var result = await Retry(3).ExecuteAsync(() => { calls++; return Task.FromResult(42); });
            Assert.Equal(42, result);
            Assert.Equal(1, calls);
        }

        [Fact]
        public async Task Une_operation_est_reprise_jusqu_au_succes()
        {
            var calls = 0;
            var result = await Retry(3).ExecuteAsync(() =>
            {
                calls++;
                if (calls < 3) throw new InvalidOperationException("echec transitoire");
                return Task.FromResult("ok");
            }, "appel de test");
            Assert.Equal("ok", result);
            Assert.Equal(3, calls);
        }

        [Fact]
        public async Task Apres_le_nombre_maximal_de_reprises_la_derniere_exception_est_relancee()
        {
            var calls = 0;
            var ex = await Assert.ThrowsAsync<TimeoutException>(() => Retry(2).ExecuteAsync<int>(() =>
            {
                calls++;
                throw new TimeoutException($"essai {calls}");
            }));
            Assert.Equal(3, calls); // 1 essai + 2 reprises
            Assert.Equal("essai 3", ex.Message);
        }

        [Fact]
        public async Task Sans_reprise_autorisee_l_echec_est_immediat()
        {
            var calls = 0;
            await Assert.ThrowsAsync<ArgumentException>(() => Retry(0).ExecuteAsync<bool>(() =>
            {
                calls++;
                throw new ArgumentException("refus");
            }));
            Assert.Equal(1, calls);
        }

        [Fact]
        public async Task La_variante_sans_valeur_de_retour_reprend_aussi()
        {
            var calls = 0;
            await Retry(2).ExecuteAsync((Func<Task>)(() =>
            {
                calls++;
                if (calls == 1) throw new InvalidOperationException("premier essai");
                return Task.CompletedTask;
            }));
            Assert.Equal(2, calls);
        }

        [Fact]
        public async Task Une_exception_levee_apres_un_await_est_egalement_reprise()
        {
            var calls = 0;
            var result = await Retry(1).ExecuteAsync(async () =>
            {
                calls++;
                await Task.Yield();
                if (calls == 1) throw new InvalidOperationException("apres await");
                return calls;
            });
            Assert.Equal(2, result);
        }

        // ── Journal nul ─────────────────────────────────────────────────

        [Fact]
        public void Le_journal_nul_ne_journalise_rien()
        {
            var logger = new NullLogger<CircuitBreakerRetryPolicyTests>();
            Assert.False(logger.IsEnabled(Microsoft.Extensions.Logging.LogLevel.Critical));
            Assert.Null(logger.BeginScope("portee"));
        }
    }
}
