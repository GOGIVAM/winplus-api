using Microsoft.EntityFrameworkCore;
using Backend.Models.Entities;

namespace Backend.Data;

/// <summary>
/// Lot 9.6 (suivi 17.5) : supports de séance générés par WinAI et résultats
/// des élèves. Schéma posé par Migrations/SQL_TutorSessionMaterials.sql.
/// </summary>
public partial class ApplicationDbContext
{
    public DbSet<TutorSessionMaterial> TutorSessionMaterials => Set<TutorSessionMaterial>();
    public DbSet<TutorSessionMaterialResult> TutorSessionMaterialResults => Set<TutorSessionMaterialResult>();

    private void OnModelCreatingTutorSessionMaterials(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<TutorSessionMaterial>(entity =>
        {
            entity.ToTable("TutorSessionMaterials");
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.TutorBookingId).IsUnique();
            entity.HasIndex(e => e.TutorUserId);
            entity.HasOne(e => e.TutorBooking)
                .WithMany()
                .HasForeignKey(e => e.TutorBookingId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<TutorSessionMaterialResult>(entity =>
        {
            entity.ToTable("TutorSessionMaterialResults");
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.TutorSessionMaterialId, e.StudentUserId }).IsUnique();
            entity.HasOne(e => e.Material)
                .WithMany(m => m.Results)
                .HasForeignKey(e => e.TutorSessionMaterialId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.Student)
                .WithMany()
                .HasForeignKey(e => e.StudentUserId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
