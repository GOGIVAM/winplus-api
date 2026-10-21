-- Migration: ContentSourceLanguage (lot 10.7, Cameroun anglophone)
-- Langue d'origine des épreuves et des matières.
--
--   * Subjects."Language", Exams."Language" : 'fr' ou 'en', NULL = français.
--     Une épreuve du GCE (O Level, A Level), du FSLC, du Common Entrance, du
--     HND ou d'un niveau anglophone (Form 1-5, Lower/Upper Sixth, Class 1-6,
--     University Year, Level 100-400, Master's/Bachelor's Degree) est écrite
--     en anglais : elle n'est jamais envoyée à la traduction automatique et
--     garde lang="en" sur les pages françaises.
--   * Les lignes anglaises existantes passent en TranslationOrigin = 'source',
--     TranslationState = 'ok' (rien à traduire), sauf une traduction humaine.
--   * Courses."Language" existe déjà (défaut 'fr') : non modifiée.
--
-- La règle de détection est celle de Services/Translation/ContentSourceLanguage.cs
-- (même liste de motifs, en expression régulière PostgreSQL).
-- Equivalent EF : Models/Entities/Subject.cs, Models/Entities/Exam.cs.
-- Idempotent : rejouable sans effet sur une base déjà migrée.
-- Additif : aucune colonne retirée ni renommée ; seules les lignes reconnues
-- comme anglophones reçoivent 'en'.
-- À exécuter AVANT le déploiement du code.

ALTER TABLE "Subjects" ADD COLUMN IF NOT EXISTS "Language" VARCHAR(5) NULL;
ALTER TABLE "Exams"    ADD COLUMN IF NOT EXISTS "Language" VARCHAR(5) NULL;

DO $$
DECLARE
    anglophone CONSTANT TEXT :=
        '\mGCE\M|\mO[ -]?Level\M|\mA[ -]?Level\M|Ordinary Level|Advanced Level|\mFSLC\M|First School Leaving'
        || '|Common Entrance|\mHND\M|\mForm ?[1-5]\M|\m(Lower|Upper) +Sixth\M|\mClass ?[1-6]\M'
        || '|University Year|\mLevel ?[1-4]00\M|Master''s Degree|Bachelor''s Degree';
BEGIN
    UPDATE "Exams" e
       SET "Language" = 'en'
     WHERE e."Language" IS NULL
       AND (COALESCE(e."ExamType", '') ~* anglophone
            OR COALESCE(e."Level", '') ~* anglophone
            OR COALESCE(e."Title", '') ~* anglophone);

    UPDATE "Subjects" s
       SET "Language" = 'en'
     WHERE s."Language" IS NULL
       AND (COALESCE(s."Level", '') ~* anglophone
            OR COALESCE(s."Title", '') ~* anglophone
            OR COALESCE(s."Category", '') ~* anglophone
            OR EXISTS (SELECT 1 FROM "Exams" e WHERE e."SubjectId" = s."Id" AND e."Language" = 'en'));
END $$;

UPDATE "Subjects"
   SET "TranslationOrigin" = 'source', "TranslationState" = 'ok'
 WHERE "Language" = 'en' AND COALESCE("TranslationOrigin", '') <> 'human';

UPDATE "Exams"
   SET "TranslationOrigin" = 'source', "TranslationState" = 'ok'
 WHERE "Language" = 'en' AND COALESCE("TranslationOrigin", '') <> 'human';

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20261021180000_ContentSourceLanguage', '8.0.0')
ON CONFLICT DO NOTHING;
