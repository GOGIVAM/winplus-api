using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Backend.Services;
using Microsoft.Extensions.Configuration;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;
using Xunit;

namespace Backend.Tests
{
    /// <summary>
    /// Cache de l'aperçu catalog.preview_pages : PDF découpé mis en cache par
    /// (sujet, version du fichier, pages), AVANT filigrane ; le filigrane
    /// nominatif reste posé à chaque appel pour l'utilisateur courant.
    /// </summary>
    public class PreviewPdfCacheTests
    {
        private static byte[] Pdf(int pages)
        {
            QuestPDF.Settings.License = LicenseType.Community;
            return Document.Create(doc =>
            {
                for (var i = 1; i <= pages; i++)
                    doc.Page(p => p.Content().Text($"Page {i} du sujet"));
            }).GeneratePdf();
        }

        private static PreviewPdfKey Key(string version = "etag-1", int allowed = 3) =>
            new(42, "Document", "subjects/42.pdf", version, allowed);

        private sealed class Loader
        {
            private readonly byte[] _bytes;
            public int Calls;
            public Loader(byte[] bytes) => _bytes = bytes;
            public Task<byte[]> Load(CancellationToken _) { Interlocked.Increment(ref Calls); return Task.FromResult(_bytes); }
        }

        [Fact]
        public async Task SecondCall_ServedFromCache_WithoutDownloadingAgain()
        {
            var wm = new DocumentWatermarkService();
            using var cache = new PreviewPdfCache(wm, new PreviewPdfCacheOptions());
            var loader = new Loader(Pdf(6));

            var first = await cache.GetOrCreateAsync(Key(), loader.Load);
            var second = await cache.GetOrCreateAsync(Key(), loader.Load);

            Assert.Equal(1, loader.Calls);
            Assert.Same(first, second);
            Assert.Equal(3, first.ServedPages);
            Assert.Equal(6, first.TotalPages);
            Assert.Equal(3, PdfPageCounter.Count(first.Bytes));
        }

        [Fact]
        public async Task NewFileVersion_OrOtherPageCount_IsANewEntry()
        {
            using var cache = new PreviewPdfCache(new DocumentWatermarkService(), new PreviewPdfCacheOptions());
            var loader = new Loader(Pdf(6));

            await cache.GetOrCreateAsync(Key("etag-1", 3), loader.Load);
            await cache.GetOrCreateAsync(Key("etag-2", 3), loader.Load);
            var two = await cache.GetOrCreateAsync(Key("etag-2", 2), loader.Load);

            Assert.Equal(3, loader.Calls);
            Assert.Equal(2, two.ServedPages);
            Assert.Equal(2, PdfPageCounter.Count(two.Bytes));
        }

        [Fact]
        public async Task Watermark_StaysPerUser_AndNeverAltersTheCachedCut()
        {
            var wm = new DocumentWatermarkService();
            using var cache = new PreviewPdfCache(wm, new PreviewPdfCacheOptions());
            var loader = new Loader(Pdf(6));

            var preview = await cache.GetOrCreateAsync(Key(), loader.Load);
            var before = (byte[])preview.Bytes.Clone();

            var forAwa = await wm.StampAsync(new MemoryStream(preview.Bytes), "Awa Ngono · #30");
            var forPaul = await wm.StampAsync(new MemoryStream(
                (await cache.GetOrCreateAsync(Key(), loader.Load)).Bytes), "Paul Essomba · #26");

            Assert.Equal(1, loader.Calls);
            Assert.Equal(before, preview.Bytes);          // le cache garde le découpage nu
            Assert.NotEqual(forAwa, forPaul);             // chaque utilisateur a son filigrane
            Assert.NotEqual(before, forAwa);
            Assert.Equal(3, PdfPageCounter.Count(forAwa));
            Assert.Equal(3, PdfPageCounter.Count(forPaul));
        }

        [Fact]
        public async Task TooShortDocument_Throws_AndIsNotCached()
        {
            using var cache = new PreviewPdfCache(new DocumentWatermarkService(), new PreviewPdfCacheOptions());
            var loader = new Loader(Pdf(1));

            await Assert.ThrowsAsync<PdfPreviewUnavailableException>(() => cache.GetOrCreateAsync(Key(), loader.Load));
            await Assert.ThrowsAsync<PdfPreviewUnavailableException>(() => cache.GetOrCreateAsync(Key(), loader.Load));
            Assert.Equal(2, loader.Calls);
            Assert.Equal(0, cache.Count);
        }

        [Fact]
        public async Task EntryLargerThanBound_IsNotCached()
        {
            using var cache = new PreviewPdfCache(new DocumentWatermarkService(), new PreviewPdfCacheOptions { MaxEntryBytes = 16 });
            var loader = new Loader(Pdf(6));

            await cache.GetOrCreateAsync(Key(), loader.Load);
            await cache.GetOrCreateAsync(Key(), loader.Load);

            Assert.Equal(2, loader.Calls);
            Assert.Equal(0, cache.Count);
        }

        [Fact]
        public async Task ExpiredEntry_IsPreparedAgain()
        {
            using var cache = new PreviewPdfCache(new DocumentWatermarkService(),
                new PreviewPdfCacheOptions { AbsoluteExpiration = TimeSpan.FromMilliseconds(50) });
            var loader = new Loader(Pdf(6));

            await cache.GetOrCreateAsync(Key(), loader.Load);
            await Task.Delay(300);
            await cache.GetOrCreateAsync(Key(), loader.Load);

            Assert.Equal(2, loader.Calls);
        }

        [Fact]
        public async Task ExtractFirstPages_KeepsTheServerGuard()
        {
            var wm = new DocumentWatermarkService();
            var cut = await wm.ExtractFirstPagesAsync(new MemoryStream(Pdf(4)), 3);
            Assert.Equal(3, PdfPageCounter.Count(cut));
            await Assert.ThrowsAsync<PdfPreviewUnavailableException>(() =>
                wm.ExtractFirstPagesAsync(new MemoryStream(Pdf(3)), 3));
        }

        [Fact]
        public void Options_ReadFromConfiguration()
        {
            var config = new Microsoft.Extensions.Configuration.ConfigurationBuilder()
                .AddInMemoryCollection(new System.Collections.Generic.Dictionary<string, string?>
                {
                    ["Catalog:PreviewCache:MaxTotalMegabytes"] = "32",
                    ["Catalog:PreviewCache:AbsoluteMinutes"] = "15",
                })
                .Build();
            var o = PreviewPdfCacheOptions.FromConfiguration(config);
            Assert.Equal(32L * 1024 * 1024, o.MaxTotalBytes);
            Assert.Equal(TimeSpan.FromMinutes(15), o.AbsoluteExpiration);
            Assert.Equal(8L * 1024 * 1024, o.MaxEntryBytes);
        }
    }
}
