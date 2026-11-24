using Microsoft.EntityFrameworkCore;
using Backend.Models.Entities;

namespace Backend.Data;

/// <summary>Cours en direct (visio WinPlus) : présences et enregistrements. Schéma posé par Migrations/SQL_Visio.sql.</summary>
public partial class ApplicationDbContext
{
    public DbSet<VisioPresence> VisioPresences => Set<VisioPresence>();
    public DbSet<VisioEnregistrement> VisioEnregistrements => Set<VisioEnregistrement>();

    private void OnModelCreatingVisio(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<VisioPresence>(entity =>
        {
            entity.ToTable("VisioPresences");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Type).HasMaxLength(10);
            entity.HasIndex(e => new { e.Type, e.RefId });
            entity.HasOne(e => e.User).WithMany().HasForeignKey(e => e.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<VisioEnregistrement>(entity =>
        {
            entity.ToTable("VisioEnregistrements");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Type).HasMaxLength(10);
            entity.Property(e => e.StorageKey).HasMaxLength(300);
            entity.Property(e => e.FileName).HasMaxLength(200);
            entity.Property(e => e.ContentType).HasMaxLength(100);
            entity.HasIndex(e => new { e.Type, e.RefId });
        });
    }
}
