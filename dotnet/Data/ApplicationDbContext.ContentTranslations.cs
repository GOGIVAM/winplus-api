using Microsoft.EntityFrameworkCore;
using Backend.Models.Entities;

namespace Backend.Data;

/// <summary>Contenus bilingues. Schéma posé par Migrations/SQL_ContentTranslations.sql.</summary>
public partial class ApplicationDbContext
{
    public DbSet<ContentTranslation> ContentTranslations => Set<ContentTranslation>();
    public DbSet<TranslationCacheEntry> TranslationCache => Set<TranslationCacheEntry>();

    private void OnModelCreatingContentTranslations(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ContentTranslation>(entity =>
        {
            entity.ToTable("ContentTranslations");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Kind).IsRequired().HasMaxLength(40);
            entity.Property(e => e.ValueFr).IsRequired().HasMaxLength(300);
            entity.Property(e => e.ValueEn).IsRequired().HasMaxLength(300);
            entity.Property(e => e.Origin).IsRequired().HasMaxLength(10);
            entity.HasIndex(e => new { e.Kind, e.ValueFr }).IsUnique();
        });

        modelBuilder.Entity<TranslationCacheEntry>(entity =>
        {
            entity.ToTable("TranslationCache");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.SourceHash).IsRequired().HasMaxLength(64);
            entity.Property(e => e.TargetLang).IsRequired().HasMaxLength(5);
            entity.HasIndex(e => new { e.SourceHash, e.TargetLang }).IsUnique();
        });

        modelBuilder.Entity<Course>(entity =>
        {
            entity.Property(e => e.TagsEn).HasColumnType("text[]");
            entity.Property(e => e.RequirementsEn).HasColumnType("text[]");
            entity.Property(e => e.ObjectivesEn).HasColumnType("text[]");
        });

        foreach (var type in new[] { typeof(Subject), typeof(Exam), typeof(Course), typeof(CourseSection), typeof(CourseLesson) })
        {
            var entity = modelBuilder.Entity(type);
            entity.Property(nameof(ITranslatableContent.TranslationOrigin)).HasMaxLength(10);
            entity.Property(nameof(ITranslatableContent.TranslationState)).HasMaxLength(10);
            entity.Property(nameof(ITranslatableContent.TranslationSourceHash)).HasMaxLength(64);
        }
    }
}
