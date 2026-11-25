using System;
using System.Linq;
using System.Threading.Tasks;
using Backend.Models.Entities;
using Backend.Services;
using Xunit;

namespace Backend.Tests
{
    /// <summary>Comparaison avec les pairs par classe, niveau et matière (25/11/2026).</summary>
    public class PeerComparisonServiceTests
    {
        private const int Me = 1;
        private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow);

        private static TestApplicationDbContext Db()
        {
            var db = TestApplicationDbContext.Create();
            db.Subjects.Add(new Subject { Id = 10, Title = "Mathématiques" });
            db.Subjects.Add(new Subject { Id = 20, Title = "Physique" });
            db.Quizzes.Add(new Quiz { Id = 100, Title = "Q maths", Subject = "Mathématiques", SubjectId = 10 });
            db.Quizzes.Add(new Quiz { Id = 200, Title = "Q physique", Subject = "Physique", SubjectId = 20 });
            db.SaveChanges();
            return db;
        }

        private static void Score(TestApplicationDbContext db, int userId, decimal score, int? quizId = 100)
        {
            db.DailyScores.Add(new DailyScore { UserId = userId, Date = Today, AverageScore = score });
            if (quizId != null)
                db.QuizAttempts.Add(new QuizAttempt { UserId = userId, QuizId = quizId.Value, Score = score, IsCompleted = true, CompletedAt = DateTime.UtcNow });
        }

        private static void InClass(TestApplicationDbContext db, int userId, string group = "Tle C2", int institution = 7) =>
            db.InstitutionStudents.Add(new InstitutionStudent { InstitutionId = institution, StudentId = userId, GroupName = group, IsActive = true });

        [Fact]
        public async Task Plateforme_par_defaut_garde_les_champs_historiques()
        {
            using var db = Db();
            Score(db, Me, 60);
            foreach (var (id, s) in new[] { (2, 40m), (3, 50m), (4, 70m), (5, 80m), (6, 90m) }) Score(db, id, s);
            db.SaveChanges();

            var r = await new PeerComparisonService(db).ComputeAsync(Me, null, null);

            Assert.True(r.HasData);
            Assert.Equal("platform", r.Scope);
            Assert.Equal(60, r.UserAvg);
            Assert.Equal(66, r.PeerAvg);
            Assert.Equal(r.PeerAvg, r.GroupAvg);
            Assert.Equal(70, r.GroupMedian);
            Assert.Equal(40, r.Percentile); // 2 pairs sur 5 en dessous
            Assert.Equal(5, r.SampleSize);
            Assert.Null(r.TopPerformersAvg); // le top 20 % ne porterait que sur 1 élève
        }

        [Fact]
        public async Task Classe_compare_seulement_au_groupe_de_l_etablissement()
        {
            using var db = Db();
            Score(db, Me, 50); InClass(db, Me);
            for (var id = 2; id <= 6; id++) { Score(db, id, 80); InClass(db, id, id == 6 ? " tle c2 " : "Tle C2"); }
            Score(db, 7, 10); InClass(db, 7, "Tle D");            // autre classe
            Score(db, 8, 10); InClass(db, 8, "Tle C2", institution: 9); // autre établissement
            db.SaveChanges();

            var r = await new PeerComparisonService(db).ComputeAsync(Me, "class", null);

            Assert.True(r.HasData);
            Assert.Equal("Tle C2", r.Group!.Label);
            Assert.Equal(5, r.Group.Size);
            Assert.Equal(80, r.GroupAvg);
            Assert.Equal(0, r.Percentile);
            Assert.Contains(r.AvailableScopes, s => s.Scope == "class" && s.Available);
        }

        [Fact]
        public async Task Classe_trop_petite_donne_une_raison_lisible_sans_chiffres_du_groupe()
        {
            using var db = Db();
            Score(db, Me, 50); InClass(db, Me);
            for (var id = 2; id <= 4; id++) { Score(db, id, 80); InClass(db, id); }
            db.SaveChanges();

            var r = await new PeerComparisonService(db).ComputeAsync(Me, "class", null);

            Assert.False(r.HasData);
            Assert.Equal("group_too_small", r.Reason);
            Assert.Contains("ta classe", r.ReasonMessage);
            Assert.Null(r.GroupAvg);
            Assert.Null(r.GroupMedian);
            Assert.Null(r.Percentile);
            Assert.Equal(50, r.UserAvg);
            Assert.Equal(3, r.Group!.Size);
        }

        [Fact]
        public async Task Sans_classe_raison_no_class()
        {
            using var db = Db();
            Score(db, Me, 50);
            db.SaveChanges();

            var r = await new PeerComparisonService(db).ComputeAsync(Me, "class", null);

            Assert.False(r.HasData);
            Assert.Equal("no_class", r.Reason);
            Assert.False(string.IsNullOrWhiteSpace(r.ReasonMessage));
            Assert.Contains(r.AvailableScopes, s => s.Scope == "class" && !s.Available);
        }

        [Fact]
        public async Task Classe_d_un_professeur_si_pas_de_groupe_d_etablissement()
        {
            using var db = Db();
            db.TeacherClasses.Add(new TeacherClass { Id = 3, TeacherId = 99, Name = "Maths 1re A", IsActive = true });
            foreach (var id in Enumerable.Range(1, 6))
            {
                Score(db, id, id == Me ? 90 : 60);
                db.TeacherClassStudents.Add(new TeacherClassStudent { TeacherClassId = 3, StudentId = id });
            }
            db.SaveChanges();

            var r = await new PeerComparisonService(db).ComputeAsync(Me, "class", null);

            Assert.True(r.HasData);
            Assert.Equal("Maths 1re A", r.Group!.Label);
            Assert.Equal(100, r.Percentile);
        }

        [Fact]
        public async Task Niveau_compare_aux_eleves_du_meme_niveau()
        {
            using var db = Db();
            db.Users.Add(new User { Id = Me, Email = "1@t", Role = "student", Level = "Terminale" });
            for (var id = 2; id <= 7; id++)
            {
                db.Users.Add(new User { Id = id, Email = $"{id}@t", Role = "student", Level = id == 7 ? "Seconde" : "terminale" });
                Score(db, id, id == 7 ? 0 : 70);
            }
            Score(db, Me, 70);
            db.SaveChanges();

            var r = await new PeerComparisonService(db).ComputeAsync(Me, "level", null);

            Assert.True(r.HasData);
            Assert.Equal("Terminale", r.Group!.Label);
            Assert.Equal(5, r.Group.Size);
            Assert.Equal(70, r.GroupAvg);
        }

        [Fact]
        public async Task Detail_par_matiere_et_filtre_subjectId()
        {
            using var db = Db();
            Score(db, Me, 80, 100);
            db.QuizAttempts.Add(new QuizAttempt { UserId = Me, QuizId = 200, Score = 40, IsCompleted = true, CompletedAt = DateTime.UtcNow });
            for (var id = 2; id <= 6; id++) Score(db, id, 60, 100);           // 5 pairs en maths
            db.QuizAttempts.Add(new QuizAttempt { UserId = 2, QuizId = 200, Score = 90, IsCompleted = true, CompletedAt = DateTime.UtcNow }); // 1 seul en physique
            db.SaveChanges();
            var svc = new PeerComparisonService(db);

            var r = await svc.ComputeAsync(Me, "platform", null);
            var maths = r.Subjects.Single(s => s.SubjectId == 10);
            var phys = r.Subjects.Single(s => s.SubjectId == 20);
            Assert.Equal("Mathématiques", maths.Name);
            Assert.Equal(60, maths.GroupAvg);
            Assert.Equal(100, maths.Percentile);
            Assert.Null(phys.GroupAvg);
            Assert.Equal("group_too_small", phys.Reason);
            Assert.Contains("cette matière", phys.ReasonMessage);

            var seule = await svc.ComputeAsync(Me, "platform", 10);
            Assert.True(seule.HasData);
            Assert.Equal("Mathématiques", seule.SubjectName);
            Assert.Equal(80, seule.UserAvg);
            Assert.Empty(seule.Subjects);
        }

        [Fact]
        public async Task Sans_quiz_raison_no_scores_et_portee_inconnue_refusee()
        {
            using var db = Db();
            var svc = new PeerComparisonService(db);

            var r = await svc.ComputeAsync(Me, "platform", null);
            Assert.False(r.HasData);
            Assert.Equal("no_scores", r.Reason);

            var ex = await Assert.ThrowsAsync<PeerComparisonException>(() => svc.ComputeAsync(Me, "monde", null));
            Assert.Equal("invalid_scope", ex.Code);
        }
    }
}
