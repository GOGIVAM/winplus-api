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
    /// <summary>Restes 9.7b, point 2. enfants couverts choisis par le parent.</summary>
    public partial class Restes97bTests
    {
        // ── 2. Enfants couverts choisis par le parent ────────────────────────

        private void LinkChildren(TestApplicationDbContext db)
        {
            var t0 = DateTime.UtcNow.AddDays(-30);
            db.ParentStudentLinks.AddRange(
                new ParentStudentLink { ParentId = Parent, StudentId = ChildA, Status = "accepted", InitiatedBy = Parent, CreatedAt = t0 },
                new ParentStudentLink { ParentId = Parent, StudentId = ChildB, Status = "accepted", InitiatedBy = Parent, CreatedAt = t0.AddDays(1) },
                new ParentStudentLink { ParentId = Parent, StudentId = ChildC, Status = "accepted", InitiatedBy = Parent, CreatedAt = t0.AddDays(2) });
            db.SaveChanges();
        }

        [Fact]
        public async Task CoveredChildren_DefaultIsOldestAcceptedLinks()
        {
            using var db = Db();
            var g = Subscriptions97bTestGrid.Seed(db);
            LinkChildren(db);
            Subscriptions97bTestGrid.Subscribe(db, Parent, g["parent-basique"], "parent"); // 1 enfant
            var ent = Ent(db);

            Assert.Equal(new[] { ChildA }, await EntitlementService.CoveredChildrenAsync(db, Parent, 1));
            Assert.True((await ent.ComputeAsync(ChildA)).Has(EntitlementCatalog.CatalogAccess));
            Assert.False((await ent.ComputeAsync(ChildB)).Has(EntitlementCatalog.CatalogAccess));

            var status = await ParentCoverage.GetAsync(db, ent, Parent, DateTime.UtcNow);
            Assert.True(status.SelectionNeeded);
            Assert.False(status.CustomSelection);
            Assert.True(status.CanChange);
            Assert.Equal(new[] { ChildA }, status.Children.Where(c => c.Covered).Select(c => c.StudentId));
        }

        [Fact]
        public async Task CoveredChildren_ParentChoiceAppliesToCatalogAndBonus_OneChangePerCycle()
        {
            using var db = Db();
            var g = Subscriptions97bTestGrid.Seed(db);
            LinkChildren(db);
            // Trimestre payé : trois cycles mensuels comptés depuis le début de l'abonnement.
            var sub = Subscriptions97bTestGrid.Subscribe(db, Parent, g["parent-basique"], "parent", end: DateTime.UtcNow.AddDays(85), period: "quarterly");
            var ent = Ent(db);
            var now = DateTime.UtcNow;

            var set = await ParentCoverage.SetAsync(db, ent, Parent, new[] { ChildC }, now);
            Assert.True(set.Ok, set.Error);
            Assert.Equal(new[] { ChildC }, await EntitlementService.CoveredChildrenAsync(db, Parent, 1));
            var c = await ent.ComputeAsync(ChildC);
            Assert.True(c.Has(EntitlementCatalog.CatalogAccess));
            Assert.Equal(400_000, c.ParentBonusTokens);
            Assert.False((await ent.ComputeAsync(ChildA)).Has(EntitlementCatalog.CatalogAccess));

            // Second changement dans le même cycle : refusé, date du prochain changement donnée.
            var again = await ParentCoverage.SetAsync(db, ent, Parent, new[] { ChildB }, now.AddDays(1));
            Assert.False(again.Ok);
            Assert.Equal("change_limit", again.Code);
            Assert.NotNull(again.Status!.NextChangeAt);
            // Le même choix renvoyé n'est pas un changement.
            Assert.True((await ParentCoverage.SetAsync(db, ent, Parent, new[] { ChildC }, now.AddDays(1))).Ok);

            // Cycle suivant (un mois après le début de l'abonnement) : permis.
            var next = await ParentCoverage.SetAsync(db, ent, Parent, new[] { ChildB }, sub.StartDate.AddMonths(1).AddHours(1));
            Assert.True(next.Ok, next.Error);
            Assert.Equal(new[] { ChildB }, await EntitlementService.CoveredChildrenAsync(db, Parent, 1));

            // Choix invalides.
            Assert.Equal("selection_too_large", (await ParentCoverage.SetAsync(db, ent, Parent, new[] { ChildA, ChildB }, now.AddMonths(3))).Code);
            Assert.Equal("child_not_linked", (await ParentCoverage.SetAsync(db, ent, Parent, new[] { Student }, now.AddMonths(3))).Code);
        }

        [Fact]
        public async Task CoveredChildren_ChoiceFillsRemainingSlotsWithOldest_AndNotNeededWhenAllCovered()
        {
            using var db = Db();
            var g = Subscriptions97bTestGrid.Seed(db);
            LinkChildren(db);
            Subscriptions97bTestGrid.Subscribe(db, Parent, g["parent-famille"], "parent"); // 3 enfants
            var ent = Ent(db);
            var none = await ParentCoverage.SetAsync(db, ent, Parent, new[] { ChildC }, DateTime.UtcNow);
            Assert.False(none.Ok);
            Assert.Equal("selection_not_needed", none.Code);

            // Choix enregistré avec un plan à 1 enfant puis plan à 2 : le choix d'abord, puis le plus ancien.
            db.ParentChildCoverages.Add(new ParentChildCoverage { ParentId = Parent, StudentId = ChildC, SelectedAt = DateTime.UtcNow });
            db.SaveChanges();
            Assert.Equal(new[] { ChildA, ChildC }, await EntitlementService.CoveredChildrenAsync(db, Parent, 2));
        }
    }
}
