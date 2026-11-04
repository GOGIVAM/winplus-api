using System;
using System.Collections;
using System.Linq;
using System.Threading.Tasks;
using Backend.Models.Entities;
using Backend.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Backend.Tests
{
    /// <summary>
    /// Données du tableau de bord élève : compteurs d'inscriptions, cours à
    /// poursuivre (non terminés, limités à 5), objectifs actifs seulement,
    /// priorités du jour (objectif proche en tête, cours peu avancés ensuite).
    /// Les résultats sont des objets anonymes : lus par réflexion.
    /// </summary>
    public class StudentServiceTests
    {
        private const int U = 2001, V = 2002;

        private static (TestApplicationDbContext Db, StudentService Svc) Build()
        {
            var db = TestApplicationDbContext.Create();
            return (db, new StudentService(db, NullLogger<StudentService>.Instance));
        }

        private static object? P(object o, string name) => o.GetType().GetProperty(name)!.GetValue(o);

        private static Enrollment E(int id, int user, int subject, bool done = false, decimal progress = 0m) =>
            new() { Id = id, UserId = user, SubjectId = subject, IsCompleted = done, ProgressPercentage = progress, User = null!, Subject = null! };

        [Fact]
        public async Task Les_statistiques_comptent_inscriptions_et_cours_termines_de_l_eleve_seulement()
        {
            var (db, svc) = Build();
            db.Enrollments.AddRange(E(1, U, 1, true), E(2, U, 2), E(3, V, 1, true));
            db.LearningHistories.AddRange(
                new LearningHistory { UserId = U, SubjectId = 1, EventType = "x" },
                new LearningHistory { UserId = U, SubjectId = 1, EventType = "x" },
                new LearningHistory { UserId = V, SubjectId = 1, EventType = "x" });
            db.SaveChanges();
            object s = await svc.GetStudentStatsAsync(U);
            Assert.Equal(2, P(s, "totalCoursesEnrolled"));
            Assert.Equal(1, P(s, "coursesCompleted"));
            Assert.Equal(4, P(s, "hoursSpent"));
        }

        [Fact]
        public async Task Sans_donnee_les_statistiques_valent_zero()
        {
            var (_, svc) = Build();
            object s = await svc.GetStudentStatsAsync(U);
            Assert.Equal(0, P(s, "totalCoursesEnrolled"));
            Assert.Equal(0, P(s, "coursesCompleted"));
        }

        [Fact]
        public async Task Les_cours_a_poursuivre_excluent_termines_et_autres_eleves_et_sont_limites_a_cinq()
        {
            var (db, svc) = Build();
            for (var i = 1; i <= 7; i++) db.Enrollments.Add(E(i, U, i, false, 10m));
            db.Enrollments.Add(E(20, U, 20, true, 100m));
            db.Enrollments.Add(E(21, V, 21));
            db.SaveChanges();
            var list = (await svc.GetLearningContinueAsync(U)).Cast<object>().ToList();
            Assert.Equal(5, list.Count);
            Assert.DoesNotContain(list, x => (int)P(x, "subjectId")! == 20);
            Assert.DoesNotContain(list, x => (int)P(x, "subjectId")! == 21);
        }

        [Fact]
        public async Task Les_objectifs_listes_sont_les_objectifs_actifs_de_l_eleve()
        {
            var (db, svc) = Build();
            var soon = DateTime.UtcNow.AddDays(30);
            db.Goals.AddRange(
                new Goal { UserId = U, Title = "actif", Status = GoalStatus.Active, TargetDate = soon },
                new Goal { UserId = U, Title = "fini", Status = GoalStatus.Completed, TargetDate = soon },
                new Goal { UserId = U, Title = "propose", Status = GoalStatus.Pending, TargetDate = soon },
                new Goal { UserId = V, Title = "autre", Status = GoalStatus.Active, TargetDate = soon });
            db.SaveChanges();
            var goals = (await svc.GetStudentGoalsAsync(U)).Cast<object>().ToList();
            Assert.Equal("actif", Assert.Single(goals).GetType().GetProperty("title")!.GetValue(goals[0]));
        }

        [Fact]
        public async Task Les_priorites_placent_l_objectif_urgent_avant_le_cours_peu_avance()
        {
            var (db, svc) = Build();
            db.Subjects.Add(new Subject { Id = 1, Title = "Maths" });
            db.Enrollments.Add(E(1, U, 1, false, 30m));
            db.Goals.Add(new Goal { UserId = U, Title = "Réviser", Status = GoalStatus.Active, TargetDate = DateTime.UtcNow.AddDays(3), Progress = 10 });
            db.Goals.Add(new Goal { UserId = U, Title = "Lointain", Status = GoalStatus.Active, TargetDate = DateTime.UtcNow.AddDays(60) });
            db.SaveChanges();
            var list = (await svc.GetTodayPrioritiesAsync(U)).Cast<object>().ToList();
            Assert.Equal(2, list.Count);
            Assert.Equal("goal", P(list[0], "type"));
            Assert.Equal("urgent", P(list[0], "priority"));
            Assert.Equal("course", P(list[1], "type"));
            Assert.Equal("high", P(list[1], "priority"));
        }

        [Fact]
        public async Task Un_cours_avance_a_moins_de_20_pour_cent_est_urgent_et_a_50_pour_cent_il_disparait()
        {
            var (db, svc) = Build();
            db.Subjects.AddRange(new Subject { Id = 1, Title = "A" }, new Subject { Id = 2, Title = "B" });
            db.Enrollments.AddRange(E(1, U, 1, false, 19m), E(2, U, 2, false, 50m));
            db.SaveChanges();
            var list = (await svc.GetTodayPrioritiesAsync(U)).Cast<object>().ToList();
            var course = Assert.Single(list);
            Assert.Equal("urgent", P(course, "priority"));
        }
    }
}
