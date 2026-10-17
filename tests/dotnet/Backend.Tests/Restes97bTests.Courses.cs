using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Backend.Controllers;
using Backend.Models.Entities;
using Backend.Services;
using Backend.Services.Entitlements;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;
using Xunit;

namespace Backend.Tests
{
    /// <summary>Restes 9.7b, point 1. teacher.max_published_courses.</summary>
    public partial class Restes97bTests
    {
        // ── 1. teacher.max_published_courses ─────────────────────────────────

        [Fact]
        public async Task PublishedCourses_FreeTeacherLimitedToOne_PendingCounts_ProAllowsMore()
        {
            using var db = Db();
            var g = Subscriptions97bTestGrid.Seed(db);
            var ent = Ent(db);
            var ctl = CourseController(db, ent, Gate(ent, new QuotaSpy()));
            Bind(ctl, Teacher, "teacher");

            var first = NewCourse(db, Teacher, "draft", "Algebre");
            var second = NewCourse(db, Teacher, "draft", "Geometrie");

            Assert.Equal(200, Status(await ctl.Submit(first.Id)));
            // Gratuit = 1 formation : la première, en attente de validation, occupe la place.
            var refused = await ctl.Submit(second.Id);
            Assert.Equal(403, Status(refused));
            Assert.Contains("published_courses_limit", Json(refused));
            Assert.Contains("1 formation", Json(refused));
            Assert.Equal("draft", db.Courses.Single(c => c.Id == second.Id).Status);

            // Plan Pro (10) : la seconde passe.
            Subscriptions97bTestGrid.Subscribe(db, Teacher, g["teacher-pro"], "teacher");
            ent.Invalidate();
            Assert.Equal(200, Status(await ctl.Submit(second.Id)));

            var limit = (OkObjectResult)await ctl.GetPublicationLimit(CancellationToken.None);
            var json = JsonSerializer.Serialize(limit.Value);
            Assert.Contains("\"max\":10", json);
            Assert.Contains("\"pending\":2", json);
        }

        [Fact]
        public async Task PublishedCourses_AlreadyPublishedBeyondLimit_StaysPublished_OnlyNewOnesBlocked()
        {
            using var db = Db();
            Subscriptions97bTestGrid.Seed(db);
            var ent = Ent(db);
            // Descente de plan : 3 formations publiées sur un plan gratuit (limite 1).
            var a = NewCourse(db, Teacher, "published", "A");
            NewCourse(db, Teacher, "published", "B");
            NewCourse(db, Teacher, "published", "C");
            var d = NewCourse(db, Teacher, "pending_review", "D");

            var snap = await CoursePublicationLimits.GetAsync(db, ent, Teacher);
            Assert.Equal(1, snap.Max);
            Assert.Equal(3, snap.Published);
            Assert.Equal(0, snap.Remaining);

            // Rien n'est dépublié : une formation déjà publiée reste acceptée à l'approbation.
            Assert.True((await CoursePublicationLimits.CheckApproveAsync(db, ent, Teacher, a.Id, "published")).Allowed);
            Assert.Equal(3, db.Courses.Count(c => c.InstructorId == Teacher && c.Status == "published"));

            // Une nouvelle publication est bloquée à l'approbation par l'administration.
            var admin = new AdminCourseController(db, NullLogger<AdminCourseController>.Instance, new Mock<INtfyService>().Object,
                new Mock<IAdminAuditService>().Object, new Mock<IFastApiClient>().Object, new Mock<ICatalogCommissionService>().Object, ent);
            Bind(admin, Admin, "admin");
            var conflict = await admin.Approve(d.Id);
            Assert.Equal(409, Status(conflict));
            Assert.Contains("published_courses_limit", Json(conflict));
            Assert.Equal("pending_review", db.Courses.Single(c => c.Id == d.Id).Status);
        }
    }
}
