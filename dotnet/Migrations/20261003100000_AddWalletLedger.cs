using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Backend.Migrations;

/// <summary>
/// Module 1 (lot 2) : journal de portefeuille <c>WalletTransactions</c>.
///
/// Le chemin d'application réel est le script jumeau
/// <c>Migrations/SQL_AddWalletLedger.sql</c> (exécuté à la main, puis reprise
/// d'historique par <c>POST /api/admin/wallet/backfill</c>). Comme toutes les
/// migrations manuelles de ce projet, cette classe ne porte pas d'attribut
/// <c>[Migration]</c> : elle documente le changement de schéma et n'est pas
/// embarquée par <c>dotnet ef database update</c>.
/// </summary>
public partial class AddWalletLedger : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Contenu porté par SQL_AddWalletLedger.sql (table, index uniques,
        // contraintes CHECK et déclencheur d'ajout seul), seul chemin appliqué.
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Volontairement vide : le journal est un registre comptable, il ne se
        // supprime pas par un retour de migration.
    }
}
