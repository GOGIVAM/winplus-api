using Microsoft.EntityFrameworkCore;
using Backend.Models.Entities;

namespace Backend.Data;

/// <summary>
/// Espace institution, phase 2 : annonces aux élèves et aux parents.
/// Schéma posé par Migrations/SQL_InstitutionAnnouncements.sql.
/// </summary>
public partial class ApplicationDbContext
{
    public DbSet<InstitutionAnnouncement> InstitutionAnnouncements => Set<InstitutionAnnouncement>();

    private void OnModelCreatingInstitutionAnnouncements(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<InstitutionAnnouncement>(entity =>
        {
            entity.ToTable("InstitutionAnnouncements");
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.InstitutionId, e.CreatedAt });
            entity.HasIndex(e => e.StudyGroupId);
            entity.HasOne(e => e.Institution)
                .WithMany()
                .HasForeignKey(e => e.InstitutionId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.StudyGroup)
                .WithMany()
                .HasForeignKey(e => e.StudyGroupId)
                .OnDelete(DeleteBehavior.SetNull);
        });
    }
}
