-- Migration: SeedPricingPlanChatQuotas
--
-- ⚠ REMPLACÉE par SQL_SeedPricingPlanTokenQuotas.sql (Partie 8 du suivi :
-- quota au token réel). Ne plus appliquer seule. Si elle a déjà été appliquée,
-- la migration Partie 8 réécrit ses valeurs (échelle « messages », < 100 000).
--
-- Renseigne "PricingPlans"."MaxChatMessages" (quota mensuel de messages WinAI)
-- sur les 10 plans existants, où la colonne est restée NULL depuis sa création.
-- Tant qu'elle est nulle, le code retombe sur une correspondance textuelle du
-- nom de plan : elle ne reconnaissait que 3 plans payants sur 7 et inversait la
-- hiérarchie des prix (« Famille » à 8 900 F obtenait 3 000 messages quand
-- « VIP » à 26 900 F n'en obtenait que 500, et « Annuel » à 89 900 F/an tombait
-- sur le plus petit palier).
--
-- ⚠ À APPLIQUER RÉELLEMENT SUR LA BASE DE PRODUCTION. C'est une migration de
-- données : le code seul ne corrige rien, il ne fait que retomber sur un repli
-- (Services/AiQuotaService.cs) tant que ces valeurs ne sont pas en base. Aucun
-- environnement de développement n'a accès à la base de production ; l'exécution
-- reste donc à la charge de l'exploitant :
--     psql -U <user> -d winplus -f Migrations/SQL_SeedPricingPlanChatQuotas.sql
--
-- Grille retenue (messages WinAI par mois), strictement croissante avec le prix
-- à l'intérieur de chaque catégorie et cohérente entre catégories :
--
--   Id  Plan       Catégorie   Prix           Quota
--    1  Starter    students          0 F/mois     0   (plan gratuit : aucun WinAI)
--    2  Standard   students      5 900 F/mois   500
--    3  Premium    students     11 900 F/mois  2000
--    4  Annuel     students     89 900 F/an    3000   (« Tout Premium » : >= Premium)
--    5  Basique    teachers          0 F/mois     0   (plan gratuit : aucun WinAI)
--    6  Pro        teachers     17 900 F/mois  2500
--    7  Expert     teachers     35 900 F/mois  5000
--    8  Famille    parents       8 900 F/mois  1000
--    9  Famille+   parents      14 900 F/mois  2000
--   10  VIP        parents      26 900 F/mois  4000
--
-- Les plans gratuits reçoivent 0 et non NULL : c'est la valeur explicite, et
-- elle est cohérente avec le mur payant (IContentAccessService n'accorde aucun
-- accès payant à un plan dont Price = 0), donc aucun quota WinAI.
--
-- Idempotent à double titre : la mise à jour est ciblée par nom + catégorie et
-- ne touche QUE les lignes dont "MaxChatMessages" est encore NULL, pour ne
-- jamais écraser une valeur qu'un administrateur aurait réglée entre-temps.
-- Equivalent EF migration: Migrations/20260924_SeedPricingPlanChatQuotas.cs

UPDATE "PricingPlans" SET "MaxChatMessages" = v."quota"
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
) AS v("name", "category", "quota")
WHERE "PricingPlans"."Name" = v."name"
  AND "PricingPlans"."Category" = v."category"
  AND "PricingPlans"."MaxChatMessages" IS NULL;

-- Filet de sécurité : tout plan payant créé plus tard (ou renommé) et laissé
-- sans quota reçoit le palier de base, au lieu de dépendre du repli textuel.
UPDATE "PricingPlans"
SET "MaxChatMessages" = 500
WHERE "MaxChatMessages" IS NULL
  AND "Price" > 0;

-- Tout plan gratuit restant : quota nul explicite.
UPDATE "PricingPlans"
SET "MaxChatMessages" = 0
WHERE "MaxChatMessages" IS NULL
  AND "Price" <= 0;

-- Historique EF : évite que `dotnet ef migrations` s'y perde si l'équipe
-- exécute un jour les migrations normalement (le reste du projet applique déjà
-- ses évolutions par SQL manuel, voir les autres fichiers SQL_*.sql).
INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20260924120000_SeedPricingPlanChatQuotas', '8.0.0')
ON CONFLICT DO NOTHING;
