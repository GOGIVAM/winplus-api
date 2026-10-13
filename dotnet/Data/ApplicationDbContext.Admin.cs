using Microsoft.EntityFrameworkCore;
using Backend.Models.Entities;

namespace Backend.Data;

/// <summary>
/// Lot 9.7a (administration de bout en bout, suivi 18.1 à 18.9) : journal
/// d'audit, réglages de plateforme, décisions de remboursement catalogue et
/// envois d'e-mails de masse. Schéma posé par Migrations/SQL_Admin97a.sql.
/// </summary>
public partial class ApplicationDbContext
{
    public DbSet<AdminAuditLog> AdminAuditLogs => Set<AdminAuditLog>();
    public DbSet<PlatformSetting> PlatformSettings => Set<PlatformSetting>();
    public DbSet<CatalogRefundDecision> CatalogRefundDecisions => Set<CatalogRefundDecision>();
    public DbSet<AdminEmailCampaign> AdminEmailCampaigns => Set<AdminEmailCampaign>();

    private void OnModelCreatingAdmin(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AdminAuditLog>(entity =>
        {
            entity.ToTable("AdminAuditLogs");
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.CreatedAt);
            entity.HasIndex(e => new { e.ActorUserId, e.CreatedAt });
            entity.HasIndex(e => new { e.Action, e.CreatedAt });
            entity.HasIndex(e => new { e.TargetType, e.TargetId });
        });

        modelBuilder.Entity<PlatformSetting>(entity =>
        {
            entity.ToTable("PlatformSettings");
            entity.HasKey(e => e.Key);
        });

        modelBuilder.Entity<CatalogRefundDecision>(entity =>
        {
            entity.ToTable("CatalogRefundDecisions");
            entity.HasKey(e => e.OrderId);
            entity.Property(e => e.OrderId).ValueGeneratedNever();
            entity.Property(e => e.OrderTotalXaf).HasColumnType("numeric(12,2)");
            entity.Property(e => e.BalancePartXaf).HasColumnType("numeric(12,2)");
            entity.Property(e => e.FeeXaf).HasColumnType("numeric(12,2)");
            entity.Property(e => e.RefundXaf).HasColumnType("numeric(12,2)");
        });

        modelBuilder.Entity<AdminEmailCampaign>(entity =>
        {
            entity.ToTable("AdminEmailCampaigns");
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.Status, e.CreatedAt });
        });
    }
}
