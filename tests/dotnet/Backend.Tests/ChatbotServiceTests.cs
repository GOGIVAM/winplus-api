using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Backend.Models.DTOs;
using Backend.Models.Entities;
using Backend.Repositories;
using Backend.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Backend.Tests
{
    /// <summary>
    /// Outillage commun aux tests du chatbot : dépôt simulé, service IA simulé
    /// par un gestionnaire HTTP (corps de requête capturé), contexte en mémoire.
    /// </summary>
    internal sealed class ChatbotHarness
    {
        public sealed class StubHandler : HttpMessageHandler
        {
            private readonly Func<HttpRequestMessage, HttpResponseMessage> _reply;
            public string? LastBody;
            public HttpRequestMessage? Last;
            public int Calls;
            public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> reply) => _reply = reply;
            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            {
                Calls++;
                Last = request;
                LastBody = request.Content == null ? null : await request.Content.ReadAsStringAsync(ct);
                return _reply(request);
            }
        }

        public TestApplicationDbContext Db = TestApplicationDbContext.Create();
        public Mock<IChatbotRepository> Repo = new();
        public StubHandler Handler = null!;
        public ChatbotService Svc = null!;
        public List<Message> Messages = new();
        public List<Message> Created = new();
        private int _nextMessageId = 100;

        public static ChatbotHarness Build(Func<HttpRequestMessage, HttpResponseMessage>? reply = null, IPlatformSettings? settings = null, params (string Key, string Value)[] config)
        {
            var h = new ChatbotHarness();
            h.Handler = new StubHandler(reply ?? (_ => new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new StringContent("{\"content\":\"Voici la réponse\",\"tokens_used\":42,\"success\":true}") }));
            var factory = new Mock<IHttpClientFactory>();
            factory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(() => new HttpClient(h.Handler) { BaseAddress = new Uri("http://ia.test") });
            h.Repo.Setup(r => r.CreateConversationAsync(It.IsAny<Conversation>())).ReturnsAsync((Conversation c) => { c.Id = 7; return c; });
            h.Repo.Setup(r => r.CreateMessageAsync(It.IsAny<Message>())).ReturnsAsync((Message m) =>
            {
                m.Id = h._nextMessageId++;
                h.Created.Add(m);
                h.Messages.Add(m);
                return m;
            });
            h.Repo.Setup(r => r.GetMessagesForConversationAsync(It.IsAny<int>(), It.IsAny<int?>())).ReturnsAsync(() => h.Messages.ToList());
            var cfg = new ConfigurationBuilder().AddInMemoryCollection(config.Select(c => new KeyValuePair<string, string?>(c.Key, c.Value))).Build();
            h.Svc = new ChatbotService(h.Repo.Object, factory.Object, NullLogger<ChatbotService>.Instance, cfg, h.Db, settings);
            return h;
        }

        public FastApiChatRequest? SentRequest() =>
            LastBodyAs<FastApiChatRequest>();

        private T? LastBodyAs<T>() => Handler.LastBody == null ? default
            : JsonSerializer.Deserialize<T>(Handler.LastBody, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower });
    }

    /// <summary>
    /// Envoi d'un message au chatbot : création de la conversation avec un titre
    /// court, enregistrement des deux messages, contenu et jetons de l'IA,
    /// bornes de longueur de réponse, contexte recalculé (rôle, enfants liés),
    /// et messages de repli sans exception quand le service IA échoue.
    /// </summary>
    public class ChatbotServiceMessageTests
    {
        private const int U = 1;

        [Fact]
        public async Task Un_nouveau_message_cree_la_conversation_et_enregistre_les_deux_messages()
        {
            var h = ChatbotHarness.Build();
            var res = await h.Svc.SendMessageAsync(U, new SendMessageRequest { Content = "Explique les fractions" });
            Assert.Equal(7, res.ConversationId);
            Assert.Equal("user", res.UserMessage.Role);
            Assert.Equal("assistant", res.AssistantMessage.Role);
            Assert.Equal("Voici la réponse", res.AssistantMessage.Content);
            Assert.Equal(42, res.TotalTokensUsed);
            Assert.True(res.AiServiceSucceeded);
            Assert.Equal(2, h.Created.Count);
        }

        [Theory]
        [InlineData("Bonjour", "Bonjour")]
        [InlineData("un deux trois quatre cinq six", "un deux trois quatre cinq six")]
        [InlineData("un deux trois quatre cinq six sept", "un deux trois quatre cinq six...")]
        [InlineData("  un   deux  ", "un deux")]
        public async Task Le_titre_reprend_les_six_premiers_mots(string content, string expectedTitle)
        {
            var h = ChatbotHarness.Build();
            await h.Svc.SendMessageAsync(U, new SendMessageRequest { Content = content });
            h.Repo.Verify(r => r.CreateConversationAsync(It.Is<Conversation>(c => c.Title == expectedTitle && c.UserId == U)), Times.Once);
        }

        [Fact]
        public async Task Le_titre_est_limite_a_cent_caracteres()
        {
            var h = ChatbotHarness.Build();
            await h.Svc.SendMessageAsync(U, new SendMessageRequest { Content = new string('a', 300) });
            h.Repo.Verify(r => r.CreateConversationAsync(It.Is<Conversation>(c => c.Title.Length == 100)), Times.Once);
        }

        [Fact]
        public async Task Une_conversation_inconnue_ou_d_un_autre_est_refusee_sans_appel_a_l_ia()
        {
            var h = ChatbotHarness.Build();
            h.Repo.Setup(r => r.GetConversationByIdForUserAsync(9, U, true)).ReturnsAsync((Conversation?)null);
            await Assert.ThrowsAsync<InvalidOperationException>(() => h.Svc.SendMessageAsync(U, new SendMessageRequest { ConversationId = 9, Content = "x" }));
            Assert.Equal(0, h.Handler.Calls);
        }

        [Fact]
        public async Task Les_pieces_jointes_et_la_reserve_de_quota_sont_transmises()
        {
            var h = ChatbotHarness.Build();
            await h.Svc.SendMessageAsync(U, new SendMessageRequest
            {
                Content = "Regarde",
                KnowledgeBase = true,
                Attachments = new List<AttachmentDto> { new() { FileName = "devoir.png" } },
            }, quotaReservationId: "RES-1");
            Assert.Contains("\"client_message_id\":\"RES-1\"", h.Handler.LastBody);
            Assert.Contains("\"knowledge_base\":true", h.Handler.LastBody);
            Assert.Contains("devoir.png", h.Created[0].Attachments);
        }

        [Fact]
        public async Task La_requete_envoie_les_champs_en_snake_case_a_la_bonne_route()
        {
            var h = ChatbotHarness.Build();
            await h.Svc.SendMessageAsync(U, new SendMessageRequest { Content = "Salut", IncludeContext = false });
            Assert.Equal("http://ia.test/api/chatbot/chat", h.Handler.Last!.RequestUri!.ToString());
            Assert.Contains("\"max_tokens\"", h.Handler.LastBody);
            Assert.Contains("\"messages\"", h.Handler.LastBody);
            Assert.DoesNotContain("maxTokens", h.Handler.LastBody);
        }

        [Theory]
        [InlineData("50", 100)]
        [InlineData("2500", 2500)]
        [InlineData("9000", 4000)]
        public async Task La_longueur_maximale_de_reponse_est_bornee_de_100_a_4000(string configured, int expected)
        {
            var h = ChatbotHarness.Build(config: ("Chatbot:MaxTokens", configured));
            await h.Svc.SendMessageAsync(U, new SendMessageRequest { Content = "x", IncludeContext = false });
            Assert.Equal(expected, h.SentRequest()!.MaxTokens);
        }

        [Fact]
        public async Task Le_jeton_de_l_utilisateur_est_relaye_au_service_IA()
        {
            var h = ChatbotHarness.Build();
            var http = new Microsoft.AspNetCore.Http.DefaultHttpContext();
            http.Request.Headers["Authorization"] = "Bearer jeton-eleve";
            var accessor = new Mock<Microsoft.AspNetCore.Http.IHttpContextAccessor>();
            accessor.Setup(a => a.HttpContext).Returns(http);
            var factory = new Mock<IHttpClientFactory>();
            factory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(() => new HttpClient(h.Handler) { BaseAddress = new Uri("http://ia.test") });
            var svc = new ChatbotService(h.Repo.Object, factory.Object, NullLogger<ChatbotService>.Instance, new ConfigurationBuilder().Build(), h.Db, null, accessor.Object);
            await svc.SendMessageAsync(U, new SendMessageRequest { Content = "x", IncludeContext = false });
            Assert.Equal("Bearer jeton-eleve", h.Handler.Last!.Headers.Authorization!.ToString());
        }

        [Fact]
        public async Task Le_contexte_d_un_parent_contient_son_role_et_ses_enfants_acceptes_seulement()
        {
            var h = ChatbotHarness.Build();
            h.Db.Users.AddRange(
                new User { Id = U, Email = "p@t.l", Role = "parent" },
                new User { Id = 2, Email = "c1@t.l", Role = "student" },
                new User { Id = 3, Email = "c2@t.l", Role = "student" });
            h.Db.ParentStudentLinks.AddRange(
                new ParentStudentLink { ParentId = U, StudentId = 2, Status = "accepted" },
                new ParentStudentLink { ParentId = U, StudentId = 3, Status = "pending" });
            h.Db.SaveChanges();
            h.Repo.Setup(r => r.GetContextForUserAsync(U)).ReturnsAsync((ChatbotContext?)null);
            await h.Svc.SendMessageAsync(U, new SendMessageRequest { Content = "Comment va mon fils ?" });
            var ctx = h.SentRequest()!.UserContext!;
            Assert.Equal("parent", ctx.Role);
            Assert.Equal(new[] { 2 }, ctx.ChildIds);
        }

        [Fact]
        public async Task Un_eleve_n_a_pas_d_identifiants_d_enfants_dans_le_contexte()
        {
            var h = ChatbotHarness.Build();
            h.Db.Users.Add(new User { Id = U, Email = "s@t.l", Role = "student" });
            h.Db.SaveChanges();
            h.Repo.Setup(r => r.GetContextForUserAsync(U)).ReturnsAsync((ChatbotContext?)null);
            await h.Svc.SendMessageAsync(U, new SendMessageRequest { Content = "x" });
            var ctx = h.SentRequest()!.UserContext!;
            Assert.Equal("student", ctx.Role);
            Assert.True(ctx.ChildIds == null || ctx.ChildIds.Count == 0);
        }

        [Fact]
        public async Task Une_erreur_http_de_l_ia_donne_un_message_de_repli_et_un_echec_signale()
        {
            var h = ChatbotHarness.Build(_ => new HttpResponseMessage(HttpStatusCode.UnprocessableEntity)
            { Content = new StringContent("{\"detail\":[{\"loc\":[\"body\",\"max_tokens\"],\"msg\":\"trop grand\"}]}") });
            var res = await h.Svc.SendMessageAsync(U, new SendMessageRequest { Content = "x", IncludeContext = false });
            Assert.False(res.AiServiceSucceeded);
            Assert.Contains("ne peux pas répondre", res.AssistantMessage.Content);
            Assert.Equal(2, h.Created.Count);
        }

        [Fact]
        public async Task Une_panne_reseau_ou_un_delai_depasse_donnent_des_messages_de_repli_distincts()
        {
            var down = ChatbotHarness.Build(_ => throw new HttpRequestException("injoignable"));
            var r1 = await down.Svc.SendMessageAsync(U, new SendMessageRequest { Content = "x", IncludeContext = false });
            Assert.False(r1.AiServiceSucceeded);
            Assert.Contains("pas disponible", r1.AssistantMessage.Content);

            var slow = ChatbotHarness.Build(_ => throw new TaskCanceledException("timeout"));
            var r2 = await slow.Svc.SendMessageAsync(U, new SendMessageRequest { Content = "x", IncludeContext = false });
            Assert.False(r2.AiServiceSucceeded);
            Assert.Contains("trop de temps", r2.AssistantMessage.Content);
        }

        [Fact]
        public async Task Une_reponse_vide_est_traitee_comme_un_echec()
        {
            var h = ChatbotHarness.Build(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("null") });
            var res = await h.Svc.SendMessageAsync(U, new SendMessageRequest { Content = "x", IncludeContext = false });
            Assert.False(res.AiServiceSucceeded);
        }
    }

    /// <summary>
    /// Conversations du chatbot : pagination (pages, page suivante ou précédente),
    /// propriété des conversations, mise à jour partielle, feedback réservé au
    /// propriétaire, synchronisation du contexte (listes sérialisées).
    /// </summary>
    public class ChatbotServiceConversationTests
    {
        private const int U = 1, Other = 2;

        [Fact]
        public async Task La_pagination_calcule_pages_et_navigation()
        {
            var h = ChatbotHarness.Build();
            h.Repo.Setup(r => r.GetConversationsForUserAsync(U, 2, 10, false)).ReturnsAsync(new List<Conversation> { new() { Id = 1, UserId = U, Title = "t" } });
            h.Repo.Setup(r => r.GetConversationCountForUserAsync(U, false)).ReturnsAsync(25);
            var res = await h.Svc.GetConversationsAsync(U, 2, 10);
            Assert.Equal(25, res.TotalCount);
            Assert.Equal(3, res.TotalPages);
            Assert.True(res.HasNextPage);
            Assert.True(res.HasPreviousPage);
            var last = await h.Svc.GetConversationsAsync(U, 2, 13);
            Assert.False(last.HasNextPage);
        }

        [Fact]
        public async Task Creer_une_conversation_serialise_les_etiquettes()
        {
            var h = ChatbotHarness.Build();
            var res = await h.Svc.CreateConversationAsync(U, new CreateConversationRequest { Title = "Maths", Tags = new List<string> { "algèbre", "bac" } });
            Assert.Equal("Maths", res.Title);
            Assert.Equal(new[] { "algèbre", "bac" }, res.Tags);
        }

        [Fact]
        public async Task La_mise_a_jour_est_partielle_et_refusee_pour_une_conversation_d_autrui()
        {
            var h = ChatbotHarness.Build();
            var conv = new Conversation { Id = 5, UserId = U, Title = "Ancien", IsActive = true };
            h.Repo.Setup(r => r.GetConversationByIdForUserAsync(5, U, false)).ReturnsAsync(conv);
            h.Repo.Setup(r => r.GetConversationByIdForUserAsync(5, Other, false)).ReturnsAsync((Conversation?)null);
            h.Repo.Setup(r => r.UpdateConversationAsync(It.IsAny<Conversation>())).ReturnsAsync((Conversation c) => c);
            Assert.Null(await h.Svc.UpdateConversationAsync(Other, 5, new UpdateConversationRequest { Title = "Pirate" }));
            var upd = await h.Svc.UpdateConversationAsync(U, 5, new UpdateConversationRequest { IsActive = false });
            Assert.Equal("Ancien", upd!.Title);
            Assert.False(upd.IsActive);
        }

        [Fact]
        public async Task Lire_une_conversation_inconnue_donne_rien_et_la_suppression_delegue_au_depot()
        {
            var h = ChatbotHarness.Build();
            h.Repo.Setup(r => r.GetConversationByIdForUserAsync(9, U, true)).ReturnsAsync((Conversation?)null);
            h.Repo.Setup(r => r.DeleteConversationAsync(5, U, false)).ReturnsAsync(true);
            Assert.Null(await h.Svc.GetConversationByIdAsync(U, 9));
            Assert.True(await h.Svc.DeleteConversationAsync(U, 5));
            Assert.False(await h.Svc.DeleteConversationAsync(U, 6));
        }

        [Fact]
        public async Task Le_feedback_est_reserve_au_proprietaire_de_la_conversation()
        {
            var h = ChatbotHarness.Build();
            var msg = new Message { Id = 50, ConversationId = 5, Role = "assistant", Content = "r" };
            h.Repo.Setup(r => r.GetMessageByIdAsync(50)).ReturnsAsync(msg);
            h.Repo.Setup(r => r.GetMessageByIdAsync(51)).ReturnsAsync((Message?)null);
            h.Repo.Setup(r => r.GetConversationByIdForUserAsync(5, U, false)).ReturnsAsync(new Conversation { Id = 5, UserId = U });
            h.Repo.Setup(r => r.GetConversationByIdForUserAsync(5, Other, false)).ReturnsAsync((Conversation?)null);
            h.Repo.Setup(r => r.UpdateMessageAsync(It.IsAny<Message>())).ReturnsAsync((Message m) => m);
            Assert.Null(await h.Svc.AddFeedbackAsync(Other, 50, new MessageFeedbackRequest { Rating = 1 }));
            Assert.Null(await h.Svc.AddFeedbackAsync(U, 51, new MessageFeedbackRequest { Rating = 1 }));
            var ok = await h.Svc.AddFeedbackAsync(U, 50, new MessageFeedbackRequest { Rating = 5, Comment = "Clair" });
            Assert.Equal(5, ok!.FeedbackRating);
            Assert.Equal("Clair", ok.FeedbackComment);
        }

        [Fact]
        public async Task La_synchronisation_du_contexte_serialise_les_listes_et_renvoie_le_contexte_enregistre()
        {
            var h = ChatbotHarness.Build();
            h.Repo.Setup(r => r.CreateOrUpdateContextAsync(It.IsAny<ChatbotContext>())).ReturnsAsync((ChatbotContext c) => c);
            var res = await h.Svc.SyncContextAsync(U, new SyncContextRequest { Grade = "Terminale", EducationLevel = "lycee", Objectives = new List<string> { "bac" } });
            Assert.Equal(U, res.UserId);
            Assert.Equal("Terminale", res.Grade);
            Assert.Equal(new[] { "bac" }, res.Objectives);
            h.Repo.Setup(r => r.GetContextForUserAsync(Other)).ReturnsAsync((ChatbotContext?)null);
            Assert.Null(await h.Svc.GetContextAsync(Other));
        }
    }
}
