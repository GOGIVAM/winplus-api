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
    /// <summary>Restes 9.7b, point 3. catalog.preview_pages.</summary>
    public partial class Restes97bTests
    {
        // ── 3. catalog.preview_pages ────────────────────────────────────────

        private static byte[] Pdf(int pages)
        {
            QuestPDF.Settings.License = LicenseType.Community;
            return Document.Create(doc =>
            {
                for (var i = 1; i <= pages; i++)
                    doc.Page(p => p.Content().Text($"Page {i} du sujet"));
            }).GeneratePdf();
        }

        [Fact]
        public void PreviewRule_NeverServesTheWholeDocument()
        {
            Assert.Equal(3, CatalogPreview.ServedPages(3, 10));
            Assert.Equal(2, CatalogPreview.ServedPages(3, 3));
            Assert.Equal(0, CatalogPreview.ServedPages(3, 1));
            Assert.Equal(0, CatalogPreview.ServedPages(0, 10));
            Assert.Equal(3, CatalogPreview.ServedPages(3, 0)); // total inconnu : contrôle qpdf ensuite
            Assert.Equal("3", EntitlementCatalog.Find(EntitlementCatalog.CatalogPreviewPages)!.DefaultValue);
        }

        [Fact]
        public async Task Preview_ServerReturnsOnlyFirstPages_Watermarked()
        {
            var source = Pdf(6);
            Assert.Equal(6, PdfPageCounter.Count(source));

            var svc = new DocumentWatermarkService();
            var preview = await svc.StampPreviewAsync(new System.IO.MemoryStream(source), "Élève Test · #9806", 3);
            Assert.Equal(3, PdfPageCounter.Count(preview));
            var full = await svc.StampAsync(new System.IO.MemoryStream(source), "Élève Test · #9806");
            Assert.Equal(6, PdfPageCounter.Count(full));

            // Garde-fou serveur : jamais d'aperçu qui livrerait tout le document.
            await Assert.ThrowsAsync<PdfPreviewUnavailableException>(() =>
                svc.StampPreviewAsync(new System.IO.MemoryStream(Pdf(3)), "x", 3));
        }

        [Fact]
        public async Task PreviewPages_FreeStudentHasThree_SubscriberHasCatalog()
        {
            using var db = Db();
            var g = Subscriptions97bTestGrid.Seed(db);
            var ent = Ent(db);
            var free = await ent.ComputeAsync(Student);
            Assert.False(free.Has(EntitlementCatalog.CatalogAccess));
            Assert.Equal(3, free.Int(EntitlementCatalog.CatalogPreviewPages));
            Subscriptions97bTestGrid.Subscribe(db, Student, g["student-standard"], "student");
            Assert.True((await ent.ComputeAsync(Student)).Has(EntitlementCatalog.CatalogAccess));
        }
    }
}
