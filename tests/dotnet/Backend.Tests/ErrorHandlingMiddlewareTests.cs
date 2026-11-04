using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Backend.Data;
using Backend.Middlewares;
using Backend.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Backend.Tests
{
    /// <summary>
    /// Gestion centralisée des exceptions : chaque type d'exception non gérée
    /// devient une réponse JSON standardisée avec le bon code HTTP, une requête
    /// sans exception passe sans être touchée, et l'erreur est consignée dans
    /// ApplicationLogs (au mieux, sans jamais empêcher la réponse).
    /// </summary>
    public class ErrorHandlingMiddlewareTests
    {
        private static DefaultHttpContext NewContext(string path = "/api/test")
        {
            var ctx = new DefaultHttpContext();
            ctx.Request.Path = path;
            ctx.Response.Body = new MemoryStream();
            return ctx;
        }

        private static ErrorHandlingMiddleware Build(RequestDelegate next, IServiceProvider? services = null) =>
            new(next, NullLogger<ErrorHandlingMiddleware>.Instance, services ?? new ServiceCollection().BuildServiceProvider());

        private static JsonElement Body(DefaultHttpContext ctx)
        {
            ctx.Response.Body.Seek(0, SeekOrigin.Begin);
            using var reader = new StreamReader(ctx.Response.Body);
            return JsonDocument.Parse(reader.ReadToEnd()).RootElement.Clone();
        }

        [Fact]
        public async Task Une_requete_sans_exception_passe_au_middleware_suivant_sans_modification()
        {
            var ctx = NewContext();
            var called = false;
            await Build(c => { called = true; return Task.CompletedTask; }).InvokeAsync(ctx);
            Assert.True(called);
            Assert.Equal(StatusCodes.Status200OK, ctx.Response.StatusCode);
            Assert.Equal(0, ctx.Response.Body.Length);
        }

        [Theory]
        [InlineData(typeof(UnauthorizedAccessException), 401, "Non autorisé")]
        [InlineData(typeof(ArgumentException), 400, "Argument invalide")]
        [InlineData(typeof(ArgumentNullException), 400, "Argument invalide")]
        [InlineData(typeof(KeyNotFoundException), 404, "Ressource non trouvée")]
        [InlineData(typeof(InvalidOperationException), 400, "Opération invalide")]
        [InlineData(typeof(NotSupportedException), 500, "Une erreur serveur interne s'est produite")]
        [InlineData(typeof(TimeoutException), 500, "Une erreur serveur interne s'est produite")]
        public async Task Le_type_d_exception_determine_le_code_http_et_le_libelle(Type exceptionType, int status, string label)
        {
            var ctx = NewContext();
            var exception = (Exception)Activator.CreateInstance(exceptionType, "detail technique")!;

            await Build(_ => throw exception).InvokeAsync(ctx);

            Assert.Equal(status, ctx.Response.StatusCode);
            Assert.StartsWith("application/json", ctx.Response.ContentType);
            var body = Body(ctx);
            Assert.Equal(label, body.GetProperty("error").GetString());
            Assert.Equal(exceptionType.Name, body.GetProperty("code").GetString());
        }

        [Fact]
        public async Task Le_message_de_l_exception_est_renvoye_tel_quel_et_l_horodatage_est_present()
        {
            var ctx = NewContext();
            await Build(_ => throw new KeyNotFoundException("Commande 12 introuvable")).InvokeAsync(ctx);

            var body = Body(ctx);
            Assert.Equal("Commande 12 introuvable", body.GetProperty("message").GetString());
            Assert.True(body.TryGetProperty("timestamp", out var ts));
            Assert.True(DateTime.TryParse(ts.GetString(), out _));
        }

        [Fact]
        public async Task Un_paiement_refuse_pour_l_etat_de_la_commande_reste_une_erreur_400_avec_son_motif()
        {
            var ctx = NewContext();
            await Build(_ => throw new OrderNotPayableException(OrderStatusRules.AlreadySettledPaymentMessage)).InvokeAsync(ctx);

            Assert.Equal(StatusCodes.Status400BadRequest, ctx.Response.StatusCode);
            var body = Body(ctx);
            Assert.Equal(OrderStatusRules.AlreadySettledPaymentMessage, body.GetProperty("message").GetString());
            Assert.Equal(nameof(OrderNotPayableException), body.GetProperty("code").GetString());
        }

        [Fact]
        public async Task L_erreur_est_consignee_dans_les_journaux_d_application_sans_bloquer_la_reponse()
        {
            var dbName = $"errlog-{Guid.NewGuid()}";
            var root = new InMemoryDatabaseRoot();
            var services = new ServiceCollection()
                .AddScoped<ApplicationDbContext>(_ => TestApplicationDbContext.Create(dbName, root))
                .BuildServiceProvider();
            var ctx = NewContext("/api/courses/9");

            await Build(_ => throw new InvalidOperationException("incident de test"), services).InvokeAsync(ctx);
            Assert.Equal(StatusCodes.Status400BadRequest, ctx.Response.StatusCode);

            // L'ecriture du journal est lancee sans attente : on la laisse se terminer.
            using var db = TestApplicationDbContext.Create(dbName, root);
            for (var i = 0; i < 100 && !await db.ApplicationLogs.AnyAsync(); i++) await Task.Delay(50);

            var log = Assert.Single(db.ApplicationLogs.ToList());
            Assert.Equal("Error", log.Level);
            Assert.Equal(nameof(InvalidOperationException), log.Category);
            Assert.Equal("incident de test", log.Message);
            Assert.Equal("/api/courses/9", log.RequestPath);
            Assert.Contains("incident de test", log.Exception);
        }

        [Fact]
        public async Task Un_echec_d_ecriture_du_journal_n_empeche_pas_la_reponse_d_erreur()
        {
            // Aucun ApplicationDbContext enregistre : la consignation echoue, la reponse part quand meme.
            var ctx = NewContext();
            await Build(_ => throw new UnauthorizedAccessException("jeton absent")).InvokeAsync(ctx);
            Assert.Equal(StatusCodes.Status401Unauthorized, ctx.Response.StatusCode);
            Assert.Equal("jeton absent", Body(ctx).GetProperty("message").GetString());
        }

        [Fact]
        public void ErrorResponse_a_un_code_par_defaut_et_pas_de_details()
        {
            var r = new ErrorResponse();
            Assert.Equal("ERROR", r.Code);
            Assert.Null(r.Details);
            Assert.Null(r.Error);
            Assert.Null(r.Message);
            Assert.True(r.Timestamp > DateTime.UtcNow.AddMinutes(-5));
        }
    }
}
