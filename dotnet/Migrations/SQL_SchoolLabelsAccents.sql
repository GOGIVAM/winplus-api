-- Migration: SchoolLabelsAccents
-- Niveaux et examens visés saisis sans accent (« Troisieme », « Premiere », « Baccalaureat C », « entree ENSET »),
-- relevés dans la tournée du mobile du 26/11/2026. Même règle que Utils/SchoolLabels.cs, appliquée désormais à
-- l'enregistrement : classes en chiffres (« 3ème »), mots accentués. Seuls Users."Level" et Users."TargetExam"
-- sont touchés ; les titres de contenus (catalogue) relèvent de leurs auteurs.
--
-- Idempotent : rejouable sans effet (les valeurs corrigées ne correspondent plus aux motifs).

CREATE OR REPLACE FUNCTION pg_temp.school_label(v TEXT) RETURNS TEXT LANGUAGE sql IMMUTABLE AS $$
    SELECT
      regexp_replace(regexp_replace(regexp_replace(regexp_replace(
      regexp_replace(regexp_replace(regexp_replace(regexp_replace(
      regexp_replace(regexp_replace(regexp_replace(regexp_replace(
      regexp_replace(regexp_replace(
        v,
        '\m(sixi[eè]me)\M', '6ème', 'gi'),
        '\m(cinqui[eè]me)\M', '5ème', 'gi'),
        '\m(quatri[eè]me)\M', '4ème', 'gi'),
        '\m(troisi[eè]me)\M', '3ème', 'gi'),
        '\m([3-6])eme\M', '\1ème', 'gi'),
        '\mPremiere\M', 'Première', 'g'),
        '\mpremiere\M', 'première', 'g'),
        '\m[Bb]accalaureat\M', 'Baccalauréat', 'g'),
        '\mEntree\M', 'Entrée', 'g'),
        '\mentree\M', 'entrée', 'g'),
        '\mEcole\M', 'École', 'g'),
        '\mGenie\M', 'Génie', 'g'),
        '\mMedecine\M', 'Médecine', 'g'),
        '\mmedecine\M', 'médecine', 'g')
$$;

UPDATE "Users" SET "Level" = pg_temp.school_label("Level")
 WHERE "Level" IS NOT NULL AND "Level" <> pg_temp.school_label("Level");

UPDATE "Users" SET "TargetExam" = pg_temp.school_label("TargetExam")
 WHERE "TargetExam" IS NOT NULL AND "TargetExam" <> pg_temp.school_label("TargetExam");

DROP FUNCTION IF EXISTS pg_temp.school_label(TEXT);

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20261126233000_SchoolLabelsAccents', '8.0.0')
ON CONFLICT DO NOTHING;
