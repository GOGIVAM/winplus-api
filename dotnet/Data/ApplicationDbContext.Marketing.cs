using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Backend.Models.Entities;

namespace Backend.Data;

/// <summary>
/// Lot 10.3 (consentement et mesure) : attribution d'acquisition sur les
/// commandes et les comptes, registre des conversions et file d'envoi.
/// Schéma posé par Migrations/SQL_AddMarketingAttribution.sql.
/// </summary>
public partial class ApplicationDbContext
{
    public DbSet<ConversionEvent> ConversionEvents => Set<ConversionEvent>();
    public DbSet<ConversionDelivery> ConversionDeliveries => Set<ConversionDelivery>();
    public DbSet<UtmLink> UtmLinks => Set<UtmLink>();

    private void OnModelCreatingMarketing(ModelBuilder modelBuilder)
    {
        // Colonnes par défaut d'EF pour un type possédé : "<Navigation>_<Propriété>".
        modelBuilder.Entity<Order>().OwnsOne(o => o.Attribution, ConfigureAttribution);
        modelBuilder.Entity<User>().OwnsOne(u => u.FirstTouch, ConfigureAttribution);
        modelBuilder.Entity<User>().OwnsOne(u => u.LastTouch, ConfigureAttribution);

        modelBuilder.Entity<ConversionEvent>(entity =>
        {
            entity.ToTable("ConversionEvents");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.TransactionId).IsRequired().HasMaxLength(100);
            entity.Property(e => e.EventName).IsRequired().HasMaxLength(50);
            entity.Property(e => e.Category).IsRequired().HasMaxLength(30);
            entity.Property(e => e.SourceType).IsRequired().HasMaxLength(30);
            entity.Property(e => e.ValueXaf).HasPrecision(12, 2);
            entity.Property(e => e.Currency).IsRequired().HasMaxLength(3);
            entity.Property(e => e.Gclid).HasMaxLength(255);
            entity.Property(e => e.Gbraid).HasMaxLength(255);
            entity.Property(e => e.Wbraid).HasMaxLength(255);
            entity.Property(e => e.GaClientId).HasMaxLength(100);
            entity.Property(e => e.GaSessionId).HasMaxLength(50);
            entity.Property(e => e.Fbc).HasMaxLength(300);
            entity.Property(e => e.Fbp).HasMaxLength(100);
            entity.Property(e => e.ClientIp).HasMaxLength(64);
            entity.Property(e => e.ClientUserAgent).HasMaxLength(500);
            entity.Property(e => e.LandingPage).HasMaxLength(300);
            entity.Property(e => e.UtmSource).HasMaxLength(150);
            entity.Property(e => e.UtmMedium).HasMaxLength(150);
            entity.Property(e => e.UtmCampaign).HasMaxLength(150);
            entity.HasIndex(e => new { e.EventName, e.TransactionId }).IsUnique();
            entity.HasIndex(e => e.OccurredAt);
        });

        modelBuilder.Entity<ConversionDelivery>(entity =>
        {
            entity.ToTable("ConversionDeliveries");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Destination).IsRequired().HasMaxLength(30);
            entity.Property(e => e.Status).IsRequired().HasMaxLength(20);
            entity.Property(e => e.LastError).HasMaxLength(1000);
            entity.HasIndex(e => new { e.ConversionEventId, e.Destination }).IsUnique();
            entity.HasIndex(e => new { e.Status, e.NextAttemptAt });
            entity.HasOne(e => e.ConversionEvent)
                .WithMany(c => c.Deliveries)
                .HasForeignKey(e => e.ConversionEventId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // Lot 10.3 bis : historique du générateur de liens UTM (Migrations/SQL_AddUtmLinks.sql).
        modelBuilder.Entity<UtmLink>(entity =>
        {
            entity.ToTable("UtmLinks");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Url).IsRequired().HasMaxLength(1000);
            entity.Property(e => e.Destination).IsRequired().HasMaxLength(300);
            entity.Property(e => e.UtmSource).IsRequired().HasMaxLength(150);
            entity.Property(e => e.UtmMedium).IsRequired().HasMaxLength(150);
            entity.Property(e => e.UtmCampaign).IsRequired().HasMaxLength(150);
            entity.Property(e => e.UtmContent).HasMaxLength(150);
            entity.Property(e => e.Label).HasMaxLength(150);
            entity.HasIndex(e => e.CreatedAt);
        });
    }

    private static void ConfigureAttribution<TOwner>(OwnedNavigationBuilder<TOwner, MarketingAttribution> b)
        where TOwner : class
    {
        b.Property(a => a.Gclid).HasMaxLength(255);
        b.Property(a => a.Gbraid).HasMaxLength(255);
        b.Property(a => a.Wbraid).HasMaxLength(255);
        b.Property(a => a.UtmSource).HasMaxLength(150);
        b.Property(a => a.UtmMedium).HasMaxLength(150);
        b.Property(a => a.UtmCampaign).HasMaxLength(150);
        b.Property(a => a.UtmTerm).HasMaxLength(150);
        b.Property(a => a.UtmContent).HasMaxLength(150);
        b.Property(a => a.Referrer).HasMaxLength(255);
        b.Property(a => a.LandingPage).HasMaxLength(300);
        b.Property(a => a.GaClientId).HasMaxLength(100);
        b.Property(a => a.GaSessionId).HasMaxLength(50);
        // Lot 10.3 bis (Meta), colonnes posées par Migrations/SQL_AddMetaConversions.sql.
        b.Property(a => a.Fbclid).HasMaxLength(255);
        b.Property(a => a.Fbc).HasMaxLength(300);
        b.Property(a => a.Fbp).HasMaxLength(100);
        b.Property(a => a.ClientIp).HasMaxLength(64);
        b.Property(a => a.ClientUserAgent).HasMaxLength(500);
    }
}
