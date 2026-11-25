using Microsoft.EntityFrameworkCore;
using Backend.Models.Entities;

namespace Backend.Data;

/// <summary>Atelier WinAI : fiches recto verso et résumés audio. Schéma posé par Migrations/SQL_WinAiProductions.sql.</summary>
public partial class ApplicationDbContext
{
    public DbSet<WinAiProduction> WinAiProductions => Set<WinAiProduction>();

    private void OnModelCreatingWinAiProductions(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<WinAiProduction>(entity =>
        {
            entity.ToTable("WinAiProductions");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Kind).HasMaxLength(20);
            entity.Property(e => e.Title).HasMaxLength(200);
            entity.Property(e => e.Subject).HasMaxLength(150);
            entity.Property(e => e.Topic).HasMaxLength(200);
            entity.Property(e => e.Level).HasMaxLength(100);
            entity.HasIndex(e => new { e.UserId, e.Kind, e.CreatedAt });
            entity.HasOne(e => e.User).WithMany().HasForeignKey(e => e.UserId).OnDelete(DeleteBehavior.Cascade);
        });
    }
}
