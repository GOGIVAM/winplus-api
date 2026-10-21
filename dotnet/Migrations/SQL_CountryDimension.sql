-- Migration: CountryDimension (lot 10.8, modèle pays)
-- Le pays devient une donnée, et pas seulement une adresse (étude 10, C.5).
--
--   * Subjects."Country", Exams."Country", ConcoursEvents."Country" : code
--     ISO 3166-1 alpha-2, 'CM' par défaut (tout le contenu actuel est camerounais).
--   * Users."Country" : pays de l'utilisateur, NULL si inconnu. Rempli ici à
--     partir de l'indicatif du téléphone, avec la règle de Utils/PhoneNumbers.cs
--     (CountryOf) : 9 chiffres sans indicatif = Cameroun ; +237/241/242/235/236/240
--     suivi du bon nombre de chiffres = pays CEMAC correspondant.
--
-- Le paiement reste camerounais (NotchPay, MTN et Orange) ; aucune vente hors
-- Cameroun n'est ouverte (VatPolicy.RateFor refuse les autres pays).
-- Equivalent EF : Models/Entities/{Subject,Exam,ConcoursEvent,User}.cs.
-- Idempotent : rejouable sans effet sur une base déjà migrée.
-- Additif : aucune colonne retirée ni renommée ; Users."Country" n'est rempli
-- que là où il est vide.
-- À exécuter AVANT le déploiement du code.

ALTER TABLE "Subjects"       ADD COLUMN IF NOT EXISTS "Country" VARCHAR(2) NOT NULL DEFAULT 'CM';
ALTER TABLE "Exams"          ADD COLUMN IF NOT EXISTS "Country" VARCHAR(2) NOT NULL DEFAULT 'CM';
ALTER TABLE "ConcoursEvents" ADD COLUMN IF NOT EXISTS "Country" VARCHAR(2) NOT NULL DEFAULT 'CM';
ALTER TABLE "Users"          ADD COLUMN IF NOT EXISTS "Country" VARCHAR(2) NULL;

CREATE INDEX IF NOT EXISTS "IX_Subjects_Country" ON "Subjects" ("Country");
CREATE INDEX IF NOT EXISTS "IX_Exams_Country"    ON "Exams" ("Country");

WITH phones AS (
    SELECT "Id",
           btrim("Phone") AS raw,
           regexp_replace(regexp_replace(COALESCE("Phone", ''), '\D', '', 'g'), '^00', '') AS digits
      FROM "Users"
     WHERE "Country" IS NULL AND COALESCE("Phone", '') <> ''
)
UPDATE "Users" u
   SET "Country" = CASE
         WHEN p.raw !~ '^(\+|00)' AND length(p.digits) = 9 THEN 'CM'
         WHEN p.digits ~ '^237\d{9}$' THEN 'CM'
         WHEN p.digits ~ '^241\d{8}$' THEN 'GA'
         WHEN p.digits ~ '^242\d{9}$' THEN 'CG'
         WHEN p.digits ~ '^235\d{8}$' THEN 'TD'
         WHEN p.digits ~ '^236\d{8}$' THEN 'CF'
         WHEN p.digits ~ '^240\d{9}$' THEN 'GQ'
       END
  FROM phones p
 WHERE u."Id" = p."Id";

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20261021200000_CountryDimension', '8.0.0')
ON CONFLICT DO NOTHING;
