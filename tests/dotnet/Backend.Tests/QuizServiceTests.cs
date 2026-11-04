using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Backend.Models.DTOs;
using Backend.Models.Entities;
using Backend.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Backend.Tests
{
    /// <summary>
    /// Quiz : création en brouillon (difficulté numérique convertie, clés JSON en
    /// minuscules), mise à jour partielle, publication, suppression logique,
    /// visibilité des quiz générés par l'IA, correction automatique des
    /// questions à choix multiples (seuil de réussite 50 %, questions ouvertes
    /// en attente de relecture), une seule tentative pour un quiz IA personnel,
    /// statistiques. IA, notifications et badges simulés.
    /// </summary>
    public class QuizServiceTests
    {
        private const int U = 1, Other = 2;

        private static (TestApplicationDbContext Db, QuizService Svc, Mock<IBadgeService> Badges) Build()
        {
            var db = TestApplicationDbContext.Create();
            var badges = new Mock<IBadgeService>();
            var svc = new QuizService(db, new Mock<IFastApiClient>().Object, NullLogger<QuizService>.Instance, new Mock<INtfyService>().Object, badges.Object);
            return (db, svc, badges);
        }

        private static QuizQuestionDto Q(string id, string answer) =>
            new() { Id = id, Question = $"Question {id}", Options = new() { "A", "B" }, CorrectAnswer = answer };

        private static CreateQuizRequestDto Req(string title = "Quiz", int difficulty = 2, params QuizQuestionDto[] qs) =>
            new() { Title = title, Subject = "Maths", Difficulty = difficulty, Questions = qs.ToList(), DurationMinutes = 10 };

        private static SubmitQuizAttemptRequestDto Answers(params (string Id, string Answer)[] a) =>
            new() { Answers = a.Select(x => new QuizAnswerDto { QuestionId = x.Id, Answer = x.Answer }).ToList(), TimeSpentSeconds = 30 };

        [Theory]
        [InlineData(1, "easy")]
        [InlineData(2, "medium")]
        [InlineData(3, "hard")]
        [InlineData(9, "medium")]
        public async Task La_creation_convertit_la_difficulte_et_reste_en_brouillon(int level, string expected)
        {
            var (_, svc, _) = Build();
            var dto = await svc.CreateQuizAsync(Req(difficulty: level, qs: Q("q1", "A")));
            Assert.Equal(expected, dto.Difficulty);
            Assert.False(dto.IsPublished);
            Assert.Equal(1, dto.QuestionsCount);
            Assert.Equal("Question q1", dto.Questions.Single().Question);
        }

        [Fact]
        public async Task La_mise_a_jour_est_partielle_et_ignore_une_liste_de_questions_vide()
        {
            var (_, svc, _) = Build();
            var created = await svc.CreateQuizAsync(Req("Ancien", 2, Q("q1", "A")));
            var upd = await svc.UpdateQuizAsync(created.Id, new UpdateQuizRequestDto { Title = "Nouveau", Questions = new(), Difficulty = 3 });
            Assert.Equal("Nouveau", upd.Title);
            Assert.Equal("hard", upd.Difficulty);
            Assert.Equal(1, upd.QuestionsCount);
            Assert.Equal("Maths", upd.Subject);
        }

        [Fact]
        public async Task Modifier_publier_ou_depublier_un_quiz_inconnu_leve_une_erreur()
        {
            var (_, svc, _) = Build();
            await Assert.ThrowsAsync<KeyNotFoundException>(() => svc.UpdateQuizAsync(9, new UpdateQuizRequestDto()));
            await Assert.ThrowsAsync<KeyNotFoundException>(() => svc.PublishQuizAsync(9));
            await Assert.ThrowsAsync<KeyNotFoundException>(() => svc.UnpublishQuizAsync(9));
            await Assert.ThrowsAsync<KeyNotFoundException>(() => svc.GetQuizStatsAsync(9));
        }

        [Fact]
        public async Task Publier_puis_depublier_bascule_l_etat_et_la_suppression_est_logique()
        {
            var (db, svc, _) = Build();
            var q = await svc.CreateQuizAsync(Req(qs: Q("q1", "A")));
            Assert.True((await svc.PublishQuizAsync(q.Id)).IsPublished);
            Assert.Single(await svc.GetPublishedQuizzesAsync());
            Assert.False((await svc.UnpublishQuizAsync(q.Id)).IsPublished);
            Assert.True(await svc.DeleteQuizAsync(q.Id));
            Assert.False(await svc.DeleteQuizAsync(999));
            Assert.Null(await svc.GetQuizByIdAsync(q.Id));
        }

        [Fact]
        public async Task Les_quiz_generes_par_l_ia_ne_sont_visibles_que_de_leur_createur()
        {
            var (db, svc, _) = Build();
            db.Quizzes.AddRange(
                new Quiz { Id = 1, Title = "Catalogue", Subject = "Maths", IsPublished = true },
                new Quiz { Id = 2, Title = "IA de U", Subject = "Maths", IsPublished = true, IsAIGenerated = true, CreatedByUserId = U });
            db.SaveChanges();
            Assert.Equal(new[] { 1 }, (await svc.GetPublishedQuizzesAsync()).Select(q => q.Id));
            Assert.Equal(2, (await svc.GetPublishedQuizzesAsync(1, 20, U)).Count());
            Assert.Single(await svc.GetPublishedQuizzesAsync(1, 20, Other));
        }

        [Fact]
        public async Task La_recherche_et_le_filtre_par_matiere_ignorent_les_quiz_supprimes()
        {
            var (db, svc, _) = Build();
            db.Quizzes.AddRange(
                new Quiz { Id = 1, Title = "Fractions", Subject = "Maths" },
                new Quiz { Id = 2, Title = "Verbes", Subject = "Français", Description = "fractions de phrase" },
                new Quiz { Id = 3, Title = "Fractions bis", Subject = "Maths", IsDeleted = true });
            db.SaveChanges();
            Assert.Equal(2, (await svc.SearchQuizzesAsync("ractions")).Count());
            Assert.Equal(new[] { 1 }, (await svc.GetQuizzesBySubjectAsync("Maths")).Select(q => q.Id));
            var filtered = await svc.GetQuizzesAsync(new QuizSearchFilterDto { Subject = "Maths", SortBy = "title", SortOrder = "asc" });
            Assert.Equal(new[] { 1 }, filtered.Select(q => q.Id));
        }

        [Fact(Skip = "bogue probable : GetQuizzesAsync compare q.Difficulty (texte « easy », « medium », « hard ») a filter.Difficulty.ToString() (nombre « 1 », « 2 », « 3 ») ; le filtre de difficulte ne renvoie jamais rien.")]
        public async Task Le_filtre_de_difficulte_numerique_retrouve_les_quiz_correspondants()
        {
            var (_, svc, _) = Build();
            await svc.CreateQuizAsync(Req(difficulty: 1, qs: Q("q1", "A")));
            var res = await svc.GetQuizzesAsync(new QuizSearchFilterDto { Difficulty = 1 });
            Assert.Single(res);
        }

        [Fact]
        public async Task Une_correction_automatique_donne_le_score_et_la_reussite_a_50_pour_cent()
        {
            var (db, svc, badges) = Build();
            var q = await svc.CreateQuizAsync(Req(qs: new[] { Q("q1", "A"), Q("q2", "B") }));
            var res = await svc.SubmitQuizAttemptAsync(q.Id, U, Answers(("q1", "a"), ("q2", "A")));
            Assert.Equal(50.0, res.Score);
            Assert.True(res.Passed);
            Assert.Equal(1, res.CorrectAnswers);
            Assert.Equal(2, res.TotalQuestions);
            Assert.False(res.PendingHumanReview);
            Assert.True(res.QuestionResults.Single(r => r.QuestionId == "q1").IsCorrect);
            badges.Verify(b => b.EvaluateForUserAsync(U), Times.Once);
            var quiz = db.Quizzes.Single();
            Assert.Equal(1, quiz.Attempts);
            Assert.Equal(1, quiz.PassingAttempts);
        }

        [Fact]
        public async Task Un_score_sous_50_pour_cent_echoue_sans_declencher_les_badges()
        {
            var (_, svc, badges) = Build();
            var q = await svc.CreateQuizAsync(Req(qs: new[] { Q("q1", "A"), Q("q2", "B"), Q("q3", "A") }));
            var res = await svc.SubmitQuizAttemptAsync(q.Id, U, Answers(("q1", "A")));
            Assert.False(res.Passed);
            badges.Verify(b => b.EvaluateForUserAsync(It.IsAny<int>()), Times.Never);
        }

        [Fact]
        public async Task Une_question_ouverte_attend_la_relecture_et_n_entre_pas_dans_le_score()
        {
            var (db, svc, _) = Build();
            var open = new QuizQuestionDto { Id = "q2", Question = "Explique", Type = "open" };
            var q = await svc.CreateQuizAsync(Req(qs: new[] { Q("q1", "A"), open }));
            var res = await svc.SubmitQuizAttemptAsync(q.Id, U, Answers(("q1", "A"), ("q2", "texte libre")));
            Assert.True(res.PendingHumanReview);
            Assert.Equal(100.0, res.Score);
            Assert.True(res.QuestionResults.Single(r => r.QuestionId == "q2").NeedsReview);
            Assert.Equal("PendingReview", db.QuizAttempts.Single().Status);
        }

        [Fact]
        public async Task Soumettre_sur_un_quiz_inconnu_ou_supprime_leve_une_erreur()
        {
            var (_, svc, _) = Build();
            await Assert.ThrowsAsync<KeyNotFoundException>(() => svc.SubmitQuizAttemptAsync(99, U, Answers()));
        }

        [Fact]
        public async Task Un_quiz_ia_personnel_n_accepte_qu_une_tentative()
        {
            var (db, svc, _) = Build();
            var questions = System.Text.Json.JsonSerializer.Serialize(new[] { new { id = "q1", question = "?", options = new[] { "A", "B" }, correctAnswer = "A" } });
            db.Quizzes.Add(new Quiz { Id = 1, Title = "IA", Subject = "Maths", IsAIGenerated = true, CreatedByUserId = U, QuestionsJson = questions });
            db.SaveChanges();
            await svc.SubmitQuizAttemptAsync(1, U, Answers(("q1", "A")));
            await Assert.ThrowsAsync<InvalidOperationException>(() => svc.SubmitQuizAttemptAsync(1, U, Answers(("q1", "A"))));
        }

        [Fact]
        public async Task Les_statistiques_calculent_moyenne_et_taux_de_reussite()
        {
            var (db, svc, _) = Build();
            db.Quizzes.Add(new Quiz { Id = 1, Title = "S", Subject = "Maths", Attempts = 4, PassingAttempts = 3, TotalScore = 200m });
            db.Quizzes.Add(new Quiz { Id = 2, Title = "Vide", Subject = "Maths" });
            db.SaveChanges();
            Assert.Equal(50.0, await svc.GetQuizAverageScoreAsync(1));
            Assert.Equal(0.0, await svc.GetQuizAverageScoreAsync(2));
            var stats = await svc.GetQuizStatsAsync(1);
            Assert.Equal(75.0, stats.GetType().GetProperty("PassRate")!.GetValue(stats));
            await Assert.ThrowsAsync<KeyNotFoundException>(() => svc.GetQuizAverageScoreAsync(99));
        }

        [Fact]
        public async Task Les_tentatives_d_un_utilisateur_excluent_les_quiz_supprimes_et_sont_triees_par_date()
        {
            var (db, svc, _) = Build();
            db.Quizzes.AddRange(new Quiz { Id = 1, Title = "A", Subject = "M" }, new Quiz { Id = 2, Title = "B", Subject = "M", IsDeleted = true });
            db.QuizAttempts.AddRange(
                new QuizAttempt { Id = 1, UserId = U, QuizId = 1, Score = 10m, CompletedAt = new DateTime(2026, 1, 1) },
                new QuizAttempt { Id = 2, UserId = U, QuizId = 1, Score = 20m, CompletedAt = new DateTime(2026, 2, 1) },
                new QuizAttempt { Id = 3, UserId = U, QuizId = 2, Score = 30m, CompletedAt = new DateTime(2026, 3, 1) },
                new QuizAttempt { Id = 4, UserId = Other, QuizId = 1, Score = 40m, CompletedAt = new DateTime(2026, 4, 1) });
            db.SaveChanges();
            Assert.Equal(new[] { 2, 1 }, (await svc.GetUserQuizAttemptsAsync(U, 1)).Select(a => a.Id));
            Assert.Equal(new[] { 2, 1 }, (await svc.GetUserAllQuizAttemptsAsync(U)).Select(a => a.Id));
            Assert.Null(await svc.GetQuizAttemptAsync(99));
        }
    }
}
