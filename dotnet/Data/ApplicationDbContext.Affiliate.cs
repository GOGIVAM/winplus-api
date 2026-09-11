using Microsoft.EntityFrameworkCore;
using Backend.Models.Entities;

namespace Backend.Data;

/// <summary>
/// Programme d'affiliation (profs/tuteurs, commission sur tout achat de la
/// plateforme). Isolé du fichier historique — voir ApplicationDbContext.Sprints.cs
/// pour la justification du découpage en fichiers partiels.
/// </summary>
public partial class ApplicationDbContext
{
    public DbSet<AffiliateAccount> AffiliateAccounts => Set<AffiliateAccount>();
    public DbSet<AffiliateClick> AffiliateClicks => Set<AffiliateClick>();
    public DbSet<AffiliateCommission> AffiliateCommissions => Set<AffiliateCommission>();
    public DbSet<AffiliateSettings> AffiliateSettings => Set<AffiliateSettings>();

    private void OnModelCreatingAffiliate(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AffiliateAccount>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.UserId).IsUnique();
            entity.HasIndex(e => e.Code).IsUnique();
            entity.HasOne(e => e.User)
                  .WithMany()
                  .HasForeignKey(e => e.UserId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<AffiliateClick>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.AffiliateAccountId, e.VisitorToken, e.ClickedAt });
            entity.HasOne(e => e.AffiliateAccount)
                  .WithMany(a => a.Clicks)
                  .HasForeignKey(e => e.AffiliateAccountId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<AffiliateCommission>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.OrderId).IsUnique();
            entity.HasIndex(e => new { e.AffiliateAccountId, e.Status });
            entity.Property(e => e.OrderAmount).HasColumnType("numeric(12,2)");
            entity.Property(e => e.CommissionRateApplied).HasColumnType("numeric(5,2)");
            entity.Property(e => e.CommissionAmount).HasColumnType("numeric(12,2)");
            entity.HasOne(e => e.AffiliateAccount)
                  .WithMany(a => a.Commissions)
                  .HasForeignKey(e => e.AffiliateAccountId)
                  .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.Order)
                  .WithMany()
                  .HasForeignKey(e => e.OrderId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<AffiliateSettings>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.CommissionRateCapPercent).HasColumnType("numeric(5,2)");
        });
    }
}
