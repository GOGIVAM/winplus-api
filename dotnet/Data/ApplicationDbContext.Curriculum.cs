using Microsoft.EntityFrameworkCore;
using Backend.Models.Entities;

namespace Backend.Data;

/// <summary>Avancement dans le programme officiel. Schéma posé par Migrations/SQL_CurriculumProgress.sql.</summary>
public partial class ApplicationDbContext
{
    public DbSet<CurriculumSelection> CurriculumSelections => Set<CurriculumSelection>();
    public DbSet<CurriculumProgress> CurriculumProgress => Set<CurriculumProgress>();

    private void OnModelCreatingCurriculum(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<CurriculumSelection>(entity =>
        {
            entity.ToTable("CurriculumSelections");
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.UserId).IsUnique();
            entity.HasOne(e => e.User).WithMany().HasForeignKey(e => e.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<CurriculumProgress>(entity =>
        {
            entity.ToTable("CurriculumProgress");
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.UserId, e.ChapterKey }).IsUnique();
            entity.HasOne(e => e.User).WithMany().HasForeignKey(e => e.UserId).OnDelete(DeleteBehavior.Cascade);
        });
    }
}
