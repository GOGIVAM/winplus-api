using Microsoft.EntityFrameworkCore;
using Backend.Models.Entities;

namespace Backend.Data;

/// <summary>
/// Espace institution, phase 2 : devoirs par classe.
/// Schéma posé par Migrations/SQL_InstitutionHomework.sql.
/// </summary>
public partial class ApplicationDbContext
{
    public DbSet<InstitutionHomework> InstitutionHomework => Set<InstitutionHomework>();

    private void OnModelCreatingInstitutionHomework(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<InstitutionHomework>(entity =>
        {
            entity.ToTable("InstitutionHomework");
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.StudyGroupId);
            entity.HasIndex(e => new { e.InstitutionId, e.CreatedAt });
            entity.HasOne(e => e.StudyGroup)
                .WithMany()
                .HasForeignKey(e => e.StudyGroupId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.Quiz)
                .WithMany()
                .HasForeignKey(e => e.QuizId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
