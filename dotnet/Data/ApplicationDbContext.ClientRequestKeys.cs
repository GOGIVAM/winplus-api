using Microsoft.EntityFrameworkCore;
using Backend.Models.Entities;

namespace Backend.Data;

/// <summary>
/// Lot 3 du mobile (mode hors ligne) : écritures déjà traitées par
/// Idempotency-Key. Schéma posé par Migrations/SQL_ClientRequestKeys.sql.
/// </summary>
public partial class ApplicationDbContext
{
    public DbSet<ClientRequestKey> ClientRequestKeys => Set<ClientRequestKey>();

    private void OnModelCreatingClientRequestKeys(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ClientRequestKey>(entity =>
        {
            entity.ToTable("ClientRequestKeys");
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.UserId, e.Key }).IsUnique();
            entity.HasIndex(e => e.CreatedAt);
        });
    }
}
