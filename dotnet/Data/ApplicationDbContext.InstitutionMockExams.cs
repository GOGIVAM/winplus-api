using Microsoft.EntityFrameworkCore;
using Backend.Models.Entities;

namespace Backend.Data;

/// <summary>
/// Espace institution, phase 2 : examens blancs.
/// Schéma posé par Migrations/SQL_InstitutionMockExams.sql.
/// </summary>
public partial class ApplicationDbContext
{
    public DbSet<InstitutionMockExam> InstitutionMockExams => Set<InstitutionMockExam>();

    private void OnModelCreatingInstitutionMockExams(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<InstitutionMockExam>(entity =>
        {
            entity.ToTable("InstitutionMockExams");
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.InstitutionId, e.OpensAt });
            entity.HasOne(e => e.Quiz)
                .WithMany()
                .HasForeignKey(e => e.QuizId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
