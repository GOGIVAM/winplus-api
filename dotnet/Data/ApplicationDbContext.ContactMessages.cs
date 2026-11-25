using Microsoft.EntityFrameworkCore;
using Backend.Models.Entities;

namespace Backend.Data;

/// <summary>Formulaire de contact. Schéma posé par Migrations/SQL_ContactMessages.sql.</summary>
public partial class ApplicationDbContext
{
    public DbSet<ContactMessage> ContactMessages => Set<ContactMessage>();

    private void OnModelCreatingContactMessages(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ContactMessage>(entity =>
        {
            entity.ToTable("ContactMessages");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).HasMaxLength(100);
            entity.Property(e => e.Email).HasMaxLength(254);
            entity.Property(e => e.Phone).HasMaxLength(30);
            entity.Property(e => e.Subject).HasMaxLength(150);
            entity.Property(e => e.Message).HasMaxLength(5000);
            entity.Property(e => e.Source).HasMaxLength(20);
            entity.Property(e => e.Locale).HasMaxLength(10);
            entity.Property(e => e.Status).HasMaxLength(20);
            entity.HasIndex(e => e.CreatedAt);
            entity.HasIndex(e => e.Status);
        });
    }
}
