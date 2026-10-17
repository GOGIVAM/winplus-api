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
    /// <summary>Restes 9.7b, point 4. fonctions IA décomptées et course_builder.</summary>
    public partial class Restes97bTests
    {
        // ── 4. Fonctions IA décomptées ───────────────────────────────────────

        [Fact]
        public async Task WeeklyGoals_DebitsRealTokens()
        {
            using var db = Db();
            Subscriptions97bTestGrid.Seed(db);
            var ent = Ent(db);
            var quota = new QuotaSpy();
            var handler = new FakeHandler("{\"suggestions\":[{\"title\":\"x\"}],\"insight\":\"ok\"}", 1234);
            var ctl = new WeeklyGoalSuggestionsController(db, Factory(handler), NullLogger<WeeklyGoalSuggestionsController>.Instance, Gate(ent, quota));
            Bind(ctl, Student, "student");

            var res = await ctl.Get(CancellationToken.None);
            Assert.Contains("\"aiPowered\":true", Json(res));
            Assert.Equal(1, handler.Calls);
            Assert.Equal(new[] { (Student, "res-1", 1234) }, quota.Finalized);
            Assert.Equal("f:weekly_goals", quota.Sources.Single());
        }

        [Fact]
        public async Task WeeklyGoals_BudgetExhausted_FallsBackLocally_WithoutCallingWinAi()
        {
            using var db = Db();
            Subscriptions97bTestGrid.Seed(db);
            var ent = Ent(db);
            var quota = new QuotaSpy(AiQuotaOutcome.LimitReached);
            var handler = new FakeHandler("{}", 999);
            var ctl = new WeeklyGoalSuggestionsController(db, Factory(handler), NullLogger<WeeklyGoalSuggestionsController>.Instance, Gate(ent, quota));
            Bind(ctl, Student, "student");

            var json = Json(await ctl.Get(CancellationToken.None));
            Assert.Contains("\"aiPowered\":false", json);
            Assert.Contains("\"aiQuotaReached\":true", json);
            Assert.Equal(0, handler.Calls);
            Assert.Empty(quota.Finalized);
        }

        [Fact]
        public async Task TutorAndTeacherAnalyses_AreDebitedOnTheTeacherBudget()
        {
            using var db = Db();
            Subscriptions97bTestGrid.Seed(db);
            var ent = Ent(db);
            var quota = new QuotaSpy();
            var gate = Gate(ent, quota);
            var handler = new FakeHandler("{\"summary\":\"ok\"}", 777);

            var client = Factory(handler).CreateClient("FastApiClient");
            using var req = new HttpRequestMessage(HttpMethod.Post, "/api/teacher/session-summary");
            var ctl = new Mock<ControllerBase> { CallBase = true }.Object;
            Bind(ctl, Teacher, "teacher");
            var result = await AiGateProxy.ProxyAsync(ctl, gate, Teacher, EntitlementCatalog.AiTeacherAssist, "", client, req, CancellationToken.None);
            Assert.IsType<ContentResult>(result);
            Assert.Equal(new[] { (Teacher, "res-1", 777) }, quota.Finalized);
            Assert.Equal("f:teacher_assist", quota.Sources.Single());
        }

        [Fact]
        public async Task IncludedFunction_RefusedWhenBudgetExhausted_AndReservationReleasedWhenNothingServed()
        {
            using var db = Db();
            Subscriptions97bTestGrid.Seed(db);
            var ent = Ent(db);

            var exhausted = new QuotaSpy(AiQuotaOutcome.LimitReached);
            var t = await Gate(ent, exhausted).BeginIncludedAsync(Student, "revision_sheet");
            Assert.False(t.Allowed);
            Assert.Equal(402, Status(t.Refusal!));

            var quota = new QuotaSpy();
            var gate = Gate(ent, quota);
            var ok = await gate.BeginIncludedAsync(Student, "revision_sheet");
            Assert.True(ok.Allowed);
            await gate.CompleteAsync(ok, served: false);
            Assert.Equal(new[] { "res-1" }, quota.Released);
            Assert.Equal("f:revision_sheet", quota.Sources.Single());
        }

        [Fact]
        public async Task CourseBuilder_RefusedWithoutEntitlement_DebitedWithIt()
        {
            using var db = Db();
            var g = Subscriptions97bTestGrid.Seed(db);
            var ent = Ent(db);
            var quota = new QuotaSpy();
            var handler = new FakeHandler("{\"weeks\":[]}", 4321);
            var ctl = CourseController(db, ent, Gate(ent, quota), Factory(handler));
            Bind(ctl, Teacher, "teacher");

            // Plan gratuit (et Fondateur) : pas de création de formation assistée.
            var refused = await ctl.GenerateSyllabus(new { subject = "Maths" }, CancellationToken.None);
            Assert.Equal(403, Status(refused));
            Assert.Contains("feature_not_in_plan", Json(refused));
            Assert.Contains(EntitlementCatalog.AiCourseBuilder, Json(refused));
            Assert.Equal(0, handler.Calls);
            Assert.Empty(quota.Sources);

            var course = NewCourse(db, Teacher, "draft", "Syllabus");
            var import = await ctl.ImportSyllabus(course.Id, new ImportSyllabusRequest { Weeks = new() { new SyllabusWeekDto { Week = 1, Title = "Intro" } } });
            Assert.Equal(403, Status(import));

            // Plan Pro : génération servie et débitée au coût réel.
            Subscriptions97bTestGrid.Subscribe(db, Teacher, g["teacher-pro"], "teacher");
            ent.Invalidate();
            var served = await ctl.GenerateSyllabus(new { subject = "Maths" }, CancellationToken.None);
            Assert.IsType<ContentResult>(served);
            Assert.Equal(1, handler.Calls);
            Assert.Equal(new[] { (Teacher, "res-1", 4321) }, quota.Finalized);
            Assert.Equal(200, Status(await ctl.ImportSyllabus(course.Id, new ImportSyllabusRequest { Weeks = new() { new SyllabusWeekDto { Week = 1, Title = "Intro" } } })));
        }
    }
}
