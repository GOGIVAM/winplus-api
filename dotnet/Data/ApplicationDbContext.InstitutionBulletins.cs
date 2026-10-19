using Microsoft.EntityFrameworkCore;
using Backend.Models.Entities;

namespace Backend.Data;

/// <summary>
/// Espace institution, phase 2 : bulletins partagés aux élèves et aux parents.
/// Schéma posé par Migrations/SQL_InstitutionBulletins.sql.
/// </summary>
public partial class ApplicationDbContext
{
    public DbSet<InstitutionBulletin> InstitutionBulletins => Set<InstitutionBulletin>();

    private void OnModelCreatingInstitutionBulletins(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<InstitutionBulletin>(entity =>
        {
            entity.ToTable("InstitutionBulletins");
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.StudentId);
            entity.HasIndex(e => new { e.InstitutionId, e.SharedAt });
            entity.HasOne(e => e.Institution)
                .WithMany()
                .HasForeignKey(e => e.InstitutionId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
