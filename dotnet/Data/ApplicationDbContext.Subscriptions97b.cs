using Microsoft.EntityFrameworkCore;
using Backend.Models.Entities;

namespace Backend.Data;

/// <summary>
/// Lot 9.7b (abonnements dynamiques, étude ETUDE_9_7B_ABONNEMENTS.md et suivi
/// 18.10 à 18.21) : versions de plan, prix par période et droits typés.
/// Schéma posé par Migrations/SQL_Subscriptions97bSocle.sql.
/// </summary>
public partial class ApplicationDbContext
{
    public DbSet<PlanVersion> PlanVersions => Set<PlanVersion>();
    public DbSet<PlanPrice> PlanPrices => Set<PlanPrice>();
    public DbSet<PlanEntitlement> PlanEntitlements => Set<PlanEntitlement>();

    private void OnModelCreatingSubscriptions97b(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<PricingPlan>(entity =>
        {
            entity.Property(e => e.PlanKey).HasMaxLength(50);
            entity.Property(e => e.Audience).HasMaxLength(20);
            entity.HasIndex(e => e.PlanKey).IsUnique();
        });

        modelBuilder.Entity<PlanVersion>(entity =>
        {
            entity.ToTable("PlanVersions");
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.PricingPlanId, e.VersionNumber }).IsUnique();
            entity.HasIndex(e => new { e.PricingPlanId, e.Status });
            entity.HasOne(e => e.PricingPlan)
                .WithMany(p => p.Versions)
                .HasForeignKey(e => e.PricingPlanId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<PlanPrice>(entity =>
        {
            entity.ToTable("PlanPrices");
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.PlanVersionId, e.Period }).IsUnique();
            entity.HasOne(e => e.PlanVersion)
                .WithMany(v => v.Prices)
                .HasForeignKey(e => e.PlanVersionId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<PlanEntitlement>(entity =>
        {
            entity.ToTable("PlanEntitlements");
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.PlanVersionId, e.Key }).IsUnique();
            entity.HasOne(e => e.PlanVersion)
                .WithMany(v => v.Entitlements)
                .HasForeignKey(e => e.PlanVersionId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Subscription>(entity =>
        {
            entity.Property(e => e.BillingPeriod).HasMaxLength(20);
            entity.Property(e => e.Audience).HasMaxLength(20);
            entity.HasOne(e => e.PlanVersion)
                .WithMany()
                .HasForeignKey(e => e.PlanVersionId)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }
}
