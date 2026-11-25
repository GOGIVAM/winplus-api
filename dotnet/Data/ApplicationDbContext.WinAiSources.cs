using Microsoft.EntityFrameworkCore;
using Backend.Models.Entities;

namespace Backend.Data;

/// <summary>Atelier WinAI : documents personnels (sources). Schéma posé par Migrations/SQL_WinAiSources.sql.</summary>
public partial class ApplicationDbContext
{
    public DbSet<WinAiSource> WinAiSources => Set<WinAiSource>();

    private void OnModelCreatingWinAiSources(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<WinAiSource>(entity =>
        {
            entity.ToTable("WinAiSources");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Title).HasMaxLength(200);
            entity.Property(e => e.FileName).HasMaxLength(255);
            entity.Property(e => e.ContentType).HasMaxLength(100);
            entity.Property(e => e.StorageRef).HasMaxLength(400);
            entity.Property(e => e.DocId).HasMaxLength(120);
            entity.Property(e => e.Status).HasMaxLength(20);
            entity.Property(e => e.Error).HasMaxLength(500);
            entity.HasIndex(e => e.UserId);
            entity.HasIndex(e => e.DocId).IsUnique();
            entity.HasOne(e => e.User).WithMany().HasForeignKey(e => e.UserId).OnDelete(DeleteBehavior.Cascade);
        });
    }
}
