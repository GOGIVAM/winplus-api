using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Backend.Migrations;

/// <summary>
/// ⚠ <b>REMPLACÉE</b> par <c>20260924150000_AiTokenQuotas</c> /
/// <c>SQL_SeedPricingPlanTokenQuotas.sql</c> (Partie 8 du suivi : la colonne
/// porte désormais des tokens, plus des messages). Conservée pour l'historique.
///
/// Renseigne <c>PricingPlans."MaxChatMessages"</c> (quota mensuel de messages
/// WinAI) sur les 10 plans existants, où la colonne est restée NULL depuis sa
/// création.
///
/// Tant qu'elle est nulle, le code doit retomber sur une correspondance
/// textuelle du nom de plan : elle ne reconnaissait que 3 plans payants sur 7
/// et inversait la hiérarchie des prix (« Famille » à 8 900 F obtenait 3 000
/// messages quand « VIP » à 26 900 F n'en obtenait que 500, et « Annuel » à
/// 89 900 F/an tombait sur le plus petit palier).
///
/// ⚠ MIGRATION DE DONNÉES À APPLIQUER RÉELLEMENT SUR LA BASE DE PRODUCTION.
/// Le correctif de code (Services/AiQuotaService.cs) ne fait que rendre le
/// repli cohérent ; la source de vérité reste la colonne. Aucun environnement
/// de développement n'a accès à la base de production, l'exécution est donc à
/// la charge de l'exploitant. Comme toutes les migrations manuelles de ce
/// projet, cette classe ne porte pas d'attribut <c>[Migration]</c> et n'est
/// donc pas embarquée par <c>dotnet ef database update</c> : le chemin
/// d'application réel est le script SQL jumeau, au contenu identique :
///     psql -U &lt;user&gt; -d winplus -f Migrations/SQL_SeedPricingPlanChatQuotas.sql
///
/// Grille retenue (messages WinAI par mois), strictement croissante avec le
/// prix à l'intérieur de chaque catégorie et cohérente entre catégories :
///
///   Id  Plan       Catégorie   Prix           Quota
///    1  Starter    students          0 F/mois     0  (plan gratuit : aucun WinAI)
///    2  Standard   students      5 900 F/mois   500
///    3  Premium    students     11 900 F/mois  2000
///    4  Annuel     students     89 900 F/an    3000  (« Tout Premium » : >= Premium)
///    5  Basique    teachers          0 F/mois     0  (plan gratuit : aucun WinAI)
///    6  Pro        teachers     17 900 F/mois  2500
///    7  Expert     teachers     35 900 F/mois  5000
///    8  Famille    parents       8 900 F/mois  1000
///    9  Famille+   parents      14 900 F/mois  2000
///   10  VIP        parents      26 900 F/mois  4000
///
/// Idempotent, dans le style des migrations existantes du projet : les lignes
/// sont ciblées par nom + catégorie et seules celles dont MaxChatMessages est
/// encore NULL sont touchées, pour ne jamais écraser une valeur qu'un
/// administrateur aurait réglée entre-temps.
/// </summary>
public partial class SeedPricingPlanChatQuotas : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            @"UPDATE ""PricingPlans"" SET ""MaxChatMessages"" = v.""quota""
              FROM (VALUES
                  ('Starter',  'students',    0),
                  ('Standard', 'students',  500),
                  ('Premium',  'students', 2000),
                  ('Annuel',   'students', 3000),
                  ('Basique',  'teachers',    0),
                  ('Pro',      'teachers', 2500),
                  ('Expert',   'teachers', 5000),
                  ('Famille',  'parents',  1000),
                  ('Famille+', 'parents',  2000),
                  ('VIP',      'parents',  4000)
              ) AS v(""name"", ""category"", ""quota"")
              WHERE ""PricingPlans"".""Name"" = v.""name""
                AND ""PricingPlans"".""Category"" = v.""category""
                AND ""PricingPlans"".""MaxChatMessages"" IS NULL;");

        // Filet de sécurité : tout plan payant créé plus tard (ou renommé) et
        // laissé sans quota reçoit le palier de base, au lieu de dépendre du
        // repli textuel.
        migrationBuilder.Sql(
            @"UPDATE ""PricingPlans"" SET ""MaxChatMessages"" = 500
              WHERE ""MaxChatMessages"" IS NULL AND ""Price"" > 0;");

        // Tout plan gratuit restant : quota nul explicite, cohérent avec le mur
        // payant (un plan à 0 F ne vaut pas abonnement payant).
        migrationBuilder.Sql(
            @"UPDATE ""PricingPlans"" SET ""MaxChatMessages"" = 0
              WHERE ""MaxChatMessages"" IS NULL AND ""Price"" <= 0;");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Retour à l'état antérieur : colonne non renseignée. Le code retombe
        // alors sur le repli de AiQuotaService.
        migrationBuilder.Sql(@"UPDATE ""PricingPlans"" SET ""MaxChatMessages"" = NULL;");
    }
}
