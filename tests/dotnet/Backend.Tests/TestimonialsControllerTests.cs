using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Backend.Controllers;
using Backend.Models.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Backend.Tests
{
    /// <summary>
    /// Témoignages publics (décision PO du 2026-10-18) : seulement des avis réels
    /// en base, jamais d'e-mail ni de nom complet, rien sans commentaire écrit.
    /// </summary>
    public class TestimonialsControllerTests
    {
        private static JsonElement Data(IActionResult result)
        {
            var ok = Assert.IsType<OkObjectResult>(result);
            var json = JsonSerializer.Serialize(ok.Value);
            return JsonDocument.Parse(json).RootElement.GetProperty("data");
        }

        [Fact]
        public async Task EmptyDatabase_ReturnsNoTestimonial()
        {
            using var db = TestApplicationDbContext.Create();
            var ctrl = new TestimonialsController(db, NullLogger<TestimonialsController>.Instance);
            Assert.Equal(0, Data(await ctrl.GetTestimonials(8)).GetArrayLength());
        }

        [Fact]
        public async Task RealReviews_FromThreeSources_WithoutEmailNorFullName()
        {
            using var db = TestApplicationDbContext.Create();
            db.Users.AddRange(
                new User { Id = 1, Email = "awa.secret@test.local", FirstName = "Awa", LastName = "Ngono", Role = "student", IsActive = true },
                new User { Id = 2, Email = "paul@test.local", FirstName = "Paul", LastName = "Essomba", Role = "student", IsActive = true },
                new User { Id = 3, Email = "prof@test.local", Role = "teacher", IsActive = true });
            db.Subjects.Add(new Subject { Id = 10, Title = "BAC C Maths 2024", Price = 500, IsPublished = true, AuthorUserId = 3 });
            db.Courses.Add(new Course { Id = 20, Title = "Physique Terminale", Slug = "physique", InstructorId = 3 });
            var t0 = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);
            db.Reviews.AddRange(
                new Review { Id = 100, UserId = 1, SubjectId = 10, Rating = 5, Comment = "Correction claire.", CreatedAt = t0.AddDays(1) },
                new Review { Id = 101, UserId = 2, SubjectId = 10, Rating = 4, Comment = "   ", CreatedAt = t0.AddDays(5) },
                new Review { Id = 102, UserId = 2, SubjectId = 10, Rating = 3, Comment = "Supprimé", IsDeleted = true, CreatedAt = t0.AddDays(6) });
            db.CourseReviews.Add(new CourseReview { Id = 200, CourseId = 20, UserId = 2, Rating = 4, Comment = "Bonne formation.", CreatedAt = t0.AddDays(2) });
            db.TutorReviews.AddRange(
                new TutorReview { Id = 300, TutorBookingId = 1, TutorProfileId = 1, StudentUserId = 1, Rating = 5, Comment = "Séance utile.", CreatedAt = t0.AddDays(3) },
                new TutorReview { Id = 301, TutorBookingId = 2, TutorProfileId = 1, StudentUserId = 2, Rating = 1, Comment = "Retiré", IsHiddenByModeration = true, CreatedAt = t0.AddDays(7) },
                new TutorReview { Id = 302, TutorBookingId = 3, TutorProfileId = 1, StudentUserId = 2, Rating = 2, Comment = "Signalé", IsReported = true, CreatedAt = t0.AddDays(8) });
            await db.SaveChangesAsync();

            var ctrl = new TestimonialsController(db, NullLogger<TestimonialsController>.Instance);
            var data = Data(await ctrl.GetTestimonials(10));
            var ids = data.EnumerateArray().Select(e => e.GetProperty("id").GetString()).ToList();

            Assert.Equal(new[] { "tutor-300", "course-200", "subject-100" }, ids);
            var raw = data.GetRawText();
            Assert.DoesNotContain("@", raw);
            Assert.DoesNotContain("Ngono", raw);
            Assert.Contains("Awa N.", raw);
            Assert.Equal("BAC C Maths 2024", data[2].GetProperty("title").GetString());

            Assert.Equal(1, Data(await ctrl.GetTestimonials(1)).GetArrayLength());
        }

        [Fact]
        public void DisplayName_NeverFullLastName()
        {
            Assert.Equal("Awa N.", TestimonialsController.DisplayName("Awa", "ngono"));
            Assert.Equal("Awa", TestimonialsController.DisplayName(" Awa ", null));
            Assert.Equal("N.", TestimonialsController.DisplayName(null, "Ngono"));
            Assert.Equal("Utilisateur WinPlus", TestimonialsController.DisplayName("", " "));
        }
    }
}
