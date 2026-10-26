using System;
using System.Linq;
using System.Threading.Tasks;
using Backend.Models.Entities;
using Backend.Services;
using Backend.Utils;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace Backend.Tests
{
    /// <summary>
    /// Décisions du 2026-10-26 : pause de 24 h après 3 échecs consécutifs à un
    /// examen de formation ; e-mail vérifié obligatoire pour publier.
    /// </summary>
    public class CourseExamRetakeTests
    {
        private const string Questions = """
            [{"id":"q1","question":"2+2 ?","correctAnswer":"4","explanation":"Addition."},
             {"id":"q2","question":"Capitale du Cameroun ?","correctAnswer":"Yaoundé","topic":"Géographie"}]
            """;

        private static Backend.Data.ApplicationDbContext Db(int finalPass = 60)
        {
            var db = TestApplicationDbContext.Create();
            db.Quizzes.Add(new Quiz { Id = 10, Title = "Examen final", Subject = "Maths", QuestionsJson = Questions });
            db.Quizzes.Add(new Quiz { Id = 11, Title = "Quiz du module 1", Subject = "Maths", QuestionsJson = Questions });
            db.Quizzes.Add(new Quiz { Id = 12, Title = "Quiz libre", Subject = "Maths", QuestionsJson = Questions });
            db.Courses.Add(new Course { Id = 1, Title = "Maths", FinalExamQuizId = 10, FinalExamPassScorePercent = finalPass });
            db.CourseSections.Add(new CourseSection { Id = 100, CourseId = 1, Title = "Module 1", Position = 1 });
            db.CourseSections.Add(new CourseSection { Id = 101, CourseId = 1, Title = "Module 2", Position = 2, UnlockRule = "min_score", MinScore = 70 });
            db.CourseLessons.Add(new CourseLesson { Id = 1000, CourseId = 1, SectionId = 100, Title = "Quiz", LessonType = "quiz", QuizId = 11 });
            db.SaveChanges();
            return db;
        }

        private static void Attempt(Backend.Data.ApplicationDbContext db, int quizId, decimal score, DateTime at, string status = "Submitted",
            string answers = """[{"QuestionId":"q1","Answer":"5"},{"QuestionId":"q2","Answer":"Yaoundé"}]""")
        {
            db.QuizAttempts.Add(new QuizAttempt { UserId = 7, QuizId = quizId, Score = score, CompletedAt = at, Status = status, UserAnswersJson = answers });
            db.SaveChanges();
        }

        [Fact]
        public async Task FindsFinalExam_ModuleQuiz_AndIgnoresOtherQuizzes()
        {
            using var db = Db();
            var final = await CourseExamRetakePolicy.FindAsync(db, 10);
            Assert.True(final!.IsFinal);
            Assert.Equal(60m, final.PassScore);
            var module = await CourseExamRetakePolicy.FindAsync(db, 11);
            Assert.False(module!.IsFinal);
            Assert.Equal(100, module.SectionId);
            Assert.Equal(70m, module.PassScore); // score exigé par le module suivant
            Assert.Null(await CourseExamRetakePolicy.FindAsync(db, 12));
        }

        [Fact]
        public async Task ThreeConsecutiveFailures_Pause24h_ThenReopens()
        {
            using var db = Db();
            var now = new DateTime(2026, 10, 26, 10, 0, 0, DateTimeKind.Utc);
            Attempt(db, 10, 40, now.AddHours(-3));
            Attempt(db, 10, 50, now.AddHours(-2));
            var two = await CourseExamRetakePolicy.StatusAsync(db, 7, 10, 60, now);
            Assert.False(two.Locked);
            Assert.Equal(2, two.ConsecutiveFailures);

            Attempt(db, 10, 55, now.AddHours(-1));
            var three = await CourseExamRetakePolicy.StatusAsync(db, 7, 10, 60, now);
            Assert.True(three.Locked);
            Assert.Equal(now.AddHours(23), three.LockedUntil);

            var later = await CourseExamRetakePolicy.StatusAsync(db, 7, 10, 60, now.AddHours(24));
            Assert.False(later.Locked);
        }

        [Fact]
        public async Task SuccessBreaksTheSeries_AndPendingReviewDoesNotCount()
        {
            using var db = Db();
            var now = DateTime.UtcNow;
            Attempt(db, 10, 30, now.AddHours(-5));
            Attempt(db, 10, 80, now.AddHours(-4));
            Attempt(db, 10, 30, now.AddHours(-3));
            Attempt(db, 10, 30, now.AddHours(-2), status: "PendingReview");
            Attempt(db, 10, 30, now.AddHours(-1));
            var s = await CourseExamRetakePolicy.StatusAsync(db, 7, 10, 60, now);
            Assert.False(s.Locked);
            Assert.Equal(2, s.ConsecutiveFailures);
        }

        [Fact]
        public async Task WeakPoints_ListMissedQuestionsFirst()
        {
            using var db = Db();
            var now = DateTime.UtcNow;
            for (var i = 3; i >= 1; i--) Attempt(db, 10, 50, now.AddHours(-i));
            var weak = await CourseExamRetakePolicy.WeakPointsAsync(db, 7, db.Quizzes.Single(q => q.Id == 10));
            var top = Assert.Single(weak);
            Assert.Equal("2+2 ?", top.Question);
            Assert.Equal(3, top.Missed);
        }

        [Fact]
        public async Task Publishing_RequiresVerifiedEmail_ExceptAdmins()
        {
            using var db = TestApplicationDbContext.Create();
            db.Users.Add(new User { Id = 1, Email = "prof@test.local", Role = "teacher", IsEmailVerified = false });
            db.Users.Add(new User { Id = 2, Email = "prof2@test.local", Role = "teacher", IsEmailVerified = true });
            db.SaveChanges();
            var refused = Assert.IsType<ObjectResult>(await VerifiedEmailRequirement.CheckAsync(db, 1, isAdmin: false));
            Assert.Equal(403, refused.StatusCode);
            Assert.Null(await VerifiedEmailRequirement.CheckAsync(db, 2, isAdmin: false));
            Assert.Null(await VerifiedEmailRequirement.CheckAsync(db, 1, isAdmin: true));
        }
    }
}
