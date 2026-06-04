using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace backend.Migrations;

/// <inheritdoc />
public partial class AddDeletedByToSoftDelete : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Ajouter DeletedBy colonne à Users
        migrationBuilder.AddColumn<int>(
            name: "DeletedBy",
            table: "Users",
            type: "integer",
            nullable: true);

        // Créer l'index pour performance
        migrationBuilder.CreateIndex(
            name: "IX_Users_IsDeleted",
            table: "Users",
            column: "IsDeleted");

        // Ajouter la clé étrangère (self-reference pour audit)
        migrationBuilder.AddForeignKey(
            name: "FK_Users_Users_DeletedBy",
            table: "Users",
            column: "DeletedBy",
            principalTable: "Users",
            principalColumn: "Id",
            onDelete: ReferentialAction.SetNull);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropForeignKey(
            name: "FK_Users_Users_DeletedBy",
            table: "Users");

        migrationBuilder.DropIndex(
            name: "IX_Users_IsDeleted",
            table: "Users");

        migrationBuilder.DropColumn(
            name: "DeletedBy",
            table: "Users");
    }
}
