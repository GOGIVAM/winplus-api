using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Backend.Migrations;

/// <summary>
/// Ajoute Orders."UpdatedAt".
///
/// Le modèle EF `Order` déclare `UpdatedAt` (Models/Entities/Order.cs) mais
/// aucune migration ne l'a jamais créée : InitialCreate ne la contient pas, et
/// les migrations suivantes (AddSoftDeleteSupport, AddPerformanceIndexes,
/// AddPromoCodes) touchent la table sans l'ajouter.
///
/// Conséquence observée en production :
///
///   Npgsql.PostgresException: 42703: column o.UpdatedAt does not exist
///   at Backend.Repositories.UserRepository.GetByIdAsync(Int32 id)
///
/// Le LEFT JOIN "Orders" de GetByIdAsync lève, la méthode ne retourne rien, et
/// GET /api/users/me répond 404.
///
/// Application :
///   dotnet ef database update --project dotnet
///
/// Si la base est mise à jour à la main (pas de dotnet ef en production),
/// utiliser sql/001_fix_orders_updatedat.sql — même résultat, plus la
/// rétro-alimentation des lignes existantes.
/// </summary>
public partial class AddOrderUpdatedAt : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<DateTime>(
            name: "UpdatedAt",
            table: "Orders",
            type: "timestamp with time zone",
            nullable: true);

        // Valeur de départ cohérente pour l'historique : dernière date connue
        // de la commande. Nullable à dessein — voir le commentaire du modèle.
        migrationBuilder.Sql(@"
            UPDATE ""Orders""
               SET ""UpdatedAt"" = COALESCE(""CompletedDate"", ""OrderDate"", ""CreatedAt"")
             WHERE ""UpdatedAt"" IS NULL;
        ");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "UpdatedAt",
            table: "Orders");
    }
}
