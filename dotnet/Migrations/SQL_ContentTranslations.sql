-- Migration: ContentTranslations
-- Contenus de la base en anglais (le web est bilingue fr/en depuis le lot 10.6).
--
--   * ContentTranslations : libellés fixes de taxonomie (matières, types
--     d'examen, niveaux, séries, cycles, sous-systèmes, statuts, sessions) et
--     leur version anglaise. Pré-remplie ci-dessous avec le vocabulaire validé
--     (frontend/src/i18n/locales/en). Toute valeur nouvelle rencontrée à
--     l'enregistrement d'un contenu est ajoutée par WinAI (Origin = 'auto',
--     NeedsReview = TRUE), à relire dans la page admin « Traductions ».
--   * TranslationCache : cache des traductions automatiques (un texte déjà
--     traduit ne repart jamais vers WinAI) et tokens consommés (coût plateforme).
--   * colonnes anglaises des contenus libres : Subjects, Exams (TitleEn,
--     DescriptionEn), Courses (TitleEn, ShortDescriptionEn, DescriptionEn,
--     TagsEn, RequirementsEn, ObjectivesEn), CourseSections et CourseLessons
--     (TitleEn, DescriptionEn), avec pour chaque ligne :
--       TranslationOrigin     'auto' (WinAI) ou 'human' (jamais écrasée) ;
--       TranslationState      NULL (jamais demandé), 'pending', 'failed', 'ok',
--                             'review' (traduction humaine dont le français a changé) ;
--       TranslatedAt          date de la traduction ;
--       TranslationSourceHash empreinte SHA-256 du français traduit.
--   * ConcoursEvents.TranslationMetaJson : origine des champs *En existants
--     (auto ou human) pour ne jamais écraser une saisie manuelle.
--
-- Equivalent EF : Models/Entities/ContentTranslation.cs,
-- Data/ApplicationDbContext.ContentTranslations.cs.
-- Idempotent et indépendant de l'ordre : rejouable sans effet sur une base
-- déjà migrée, ne dépend d'aucune autre migration du lot.
-- Additif : aucune colonne retirée ni renommée, aucune donnée existante modifiée.
-- À exécuter AVANT le déploiement du code.

CREATE TABLE IF NOT EXISTS "ContentTranslations" (
    "Id"          SERIAL PRIMARY KEY,
    "Kind"        VARCHAR(40)  NOT NULL,
    "ValueFr"     VARCHAR(300) NOT NULL,
    "ValueEn"     VARCHAR(300) NOT NULL,
    "Origin"      VARCHAR(10)  NOT NULL DEFAULT 'human',
    "NeedsReview" BOOLEAN      NOT NULL DEFAULT FALSE,
    "CreatedAt"   TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW(),
    "UpdatedAt"   TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW()
);
CREATE UNIQUE INDEX IF NOT EXISTS "IX_ContentTranslations_Kind_ValueFr" ON "ContentTranslations"("Kind", "ValueFr");

CREATE TABLE IF NOT EXISTS "TranslationCache" (
    "Id"             SERIAL PRIMARY KEY,
    "SourceHash"     VARCHAR(64) NOT NULL,
    "SourceText"     TEXT        NOT NULL,
    "TranslatedText" TEXT        NOT NULL,
    "TargetLang"     VARCHAR(5)  NOT NULL DEFAULT 'en',
    "TokensUsed"     INTEGER     NOT NULL DEFAULT 0,
    "CreatedAt"      TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW()
);
CREATE UNIQUE INDEX IF NOT EXISTS "IX_TranslationCache_SourceHash_TargetLang" ON "TranslationCache"("SourceHash", "TargetLang");

-- Colonnes communes de suivi, ajoutées à chaque table de contenu.
DO $$
DECLARE t TEXT;
BEGIN
    FOREACH t IN ARRAY ARRAY['Subjects', 'Exams', 'Courses', 'CourseSections', 'CourseLessons'] LOOP
        IF to_regclass(format('%I', t)) IS NOT NULL THEN
            EXECUTE format('ALTER TABLE %I ADD COLUMN IF NOT EXISTS "TitleEn" VARCHAR(500) NULL', t);
            EXECUTE format('ALTER TABLE %I ADD COLUMN IF NOT EXISTS "DescriptionEn" TEXT NULL', t);
            EXECUTE format('ALTER TABLE %I ADD COLUMN IF NOT EXISTS "TranslationOrigin" VARCHAR(10) NULL', t);
            EXECUTE format('ALTER TABLE %I ADD COLUMN IF NOT EXISTS "TranslationState" VARCHAR(10) NULL', t);
            EXECUTE format('ALTER TABLE %I ADD COLUMN IF NOT EXISTS "TranslatedAt" TIMESTAMP WITH TIME ZONE NULL', t);
            EXECUTE format('ALTER TABLE %I ADD COLUMN IF NOT EXISTS "TranslationSourceHash" VARCHAR(64) NULL', t);
            EXECUTE format('CREATE INDEX IF NOT EXISTS %I ON %I ("TranslationState")', 'IX_' || t || '_TranslationState', t);
        END IF;
    END LOOP;
END $$;

DO $$
BEGIN
    IF to_regclass('"Courses"') IS NOT NULL THEN
        ALTER TABLE "Courses" ADD COLUMN IF NOT EXISTS "ShortDescriptionEn" TEXT   NULL;
        ALTER TABLE "Courses" ADD COLUMN IF NOT EXISTS "TagsEn"             TEXT[] NULL;
        ALTER TABLE "Courses" ADD COLUMN IF NOT EXISTS "RequirementsEn"     TEXT[] NULL;
        ALTER TABLE "Courses" ADD COLUMN IF NOT EXISTS "ObjectivesEn"       TEXT[] NULL;
    END IF;
    IF to_regclass('"ConcoursEvents"') IS NOT NULL THEN
        ALTER TABLE "ConcoursEvents" ADD COLUMN IF NOT EXISTS "TranslationMetaJson" TEXT NULL;
    END IF;
END $$;

-- Libellés de taxonomie pré-remplis : valeurs distinctes présentes en base
-- (sauvegarde du 11/09/2026), listes du web (lib/constants.ts, MATIERE_LABELS_EN
-- de lib/concours/examGroups.ts) et vocabulaire validé des locales anglaises.
-- Les noms de classes camerounais (Terminale, Première…), les sigles et les
-- noms d'écoles restent tels quels ; ils sont listés pour ne pas partir en
-- traduction automatique. Une ligne déjà présente (corrigée en admin) n'est
-- jamais modifiée.
-- 184 libellés
INSERT INTO "ContentTranslations" ("Kind", "ValueFr", "ValueEn", "Origin", "NeedsReview", "CreatedAt", "UpdatedAt")
SELECT v.kind, v.fr, v.en, 'human', FALSE, NOW(), NOW() FROM (VALUES
    ('category', 'Mathématiques', 'Mathematics'),
    ('category', 'Maths', 'Maths'),
    ('category', 'Mathématiques industrielle', 'Industrial mathematics'),
    ('category', 'Physique', 'Physics'),
    ('category', 'Physique-Chimie', 'Physics and chemistry'),
    ('category', 'Chimie', 'Chemistry'),
    ('category', 'Chimi', 'Chemistry'),
    ('category', 'Biologie', 'Biology'),
    ('category', 'SVT', 'Life and earth sciences'),
    ('category', 'Sciences de la vie et de la Terre', 'Life and earth sciences'),
    ('category', 'Sciences naturelles', 'Natural sciences'),
    ('category', 'Sciences Naturelles', 'Natural sciences'),
    ('category', 'Sciences', 'Science'),
    ('category', 'Culture générale', 'General knowledge'),
    ('category', 'Culture général', 'General knowledge'),
    ('category', 'Spécialité', 'Specialty'),
    ('category', 'Spécialité BTP', 'Specialty: building and public works'),
    ('category', 'Génie civil', 'Civil engineering'),
    ('category', 'Génie Civil', 'Civil engineering'),
    ('category', 'Génie électrique', 'Electrical engineering'),
    ('category', 'Génie informatique', 'Software engineering'),
    ('category', 'Génie chimique', 'Chemical engineering'),
    ('category', 'Mécanique', 'Mechanics'),
    ('category', 'Mécanique appliqué et RDM', 'Applied mechanics and strength of materials'),
    ('category', 'Électronique', 'Electronics'),
    ('category', 'Électrotechnique', 'Electrical technology'),
    ('category', 'Technologie', 'Technology'),
    ('category', 'Informatique', 'Computer science'),
    ('category', 'Français', 'French'),
    ('category', 'Anglais', 'English'),
    ('category', 'Allemand', 'German'),
    ('category', 'Espagnol', 'Spanish'),
    ('category', 'Latin', 'Latin'),
    ('category', 'Langues', 'Languages'),
    ('category', 'Lettres', 'Literature'),
    ('category', 'Littérature', 'Literature'),
    ('category', 'Histoire', 'History'),
    ('category', 'Géographie', 'Geography'),
    ('category', 'Histoire-Géo', 'History and geography'),
    ('category', 'Histoire-Géographie', 'History and geography'),
    ('category', 'Philosophie', 'Philosophy'),
    ('category', 'Dessin', 'Drawing'),
    ('category', 'DESSIN', 'Drawing'),
    ('category', 'Arts', 'Arts'),
    ('category', 'Arts plastiques', 'Visual arts'),
    ('category', 'Musique', 'Music'),
    ('category', 'Droit', 'Law'),
    ('category', 'Économie', 'Economics'),
    ('category', 'Sciences économiques', 'Economics'),
    ('category', 'Gestion', 'Management'),
    ('category', 'Comptabilité', 'Accounting'),
    ('category', 'Sciences politiques', 'Political science'),
    ('category', 'Relations internationales', 'International relations'),
    ('category', 'Médecine', 'Medicine'),
    ('category', 'Pharmacie', 'Pharmacy'),
    ('category', 'Architecture', 'Architecture'),
    ('category', 'Agronomie', 'Agricultural sciences'),
    ('category', 'Sociologie', 'Sociology'),
    ('category', 'Psychologie', 'Psychology'),
    ('category', 'Communication', 'Communication'),
    ('category', 'Journalisme', 'Journalism'),
    ('category', 'Éducation civique', 'Civic education'),
    ('category', 'Éducation à la citoyenneté et à la morale', 'Citizenship and moral education'),
    ('category', 'ECM', 'Citizenship and moral education'),
    ('category', 'EPS', 'Physical education'),
    ('category', 'Marketing', 'Marketing'),
    ('category', 'Statistiques', 'Statistics'),
    ('category', 'Thermodynamique', 'Thermodynamics'),
    ('examType', 'Baccalauréat', 'Baccalaureate'),
    ('examType', 'BAC', 'BAC'),
    ('examType', 'Bac', 'Bac'),
    ('examType', 'Probatoire', 'Probatoire'),
    ('examType', 'BEPC', 'BEPC'),
    ('examType', 'CEP', 'CEP'),
    ('examType', 'CAP', 'CAP'),
    ('examType', 'ENS', 'ENS'),
    ('examType', 'ENSET', 'ENSET'),
    ('examType', 'ENAM', 'ENAM'),
    ('examType', 'ESSEC', 'ESSEC'),
    ('examType', 'FASA', 'FASA'),
    ('examType', 'FMSB', 'FMSB'),
    ('examType', 'IBAF', 'IBAF'),
    ('examType', 'IUT', 'IUT'),
    ('examType', 'Polytechnique', 'Polytechnique'),
    ('examType', 'ENSP', 'ENSP'),
    ('examType', 'Concours', 'Entrance exam'),
    ('examType', 'Concours d''entrée', 'Entrance exam'),
    ('examType', 'Examen blanc', 'Mock exam'),
    ('examType', 'Contrôle continu', 'Continuous assessment'),
    ('examType', 'Devoir surveillé', 'Class test'),
    ('level', 'Primaire', 'Primary'),
    ('level', 'Collège', 'Collège (lower secondary)'),
    ('level', 'Lycée', 'Lycée (upper secondary)'),
    ('level', 'Supérieur', 'Higher education'),
    ('level', 'Université', 'University'),
    ('level', '6ème', '6ème'),
    ('level', '5ème', '5ème'),
    ('level', '4ème', '4ème'),
    ('level', '3ème', '3ème'),
    ('level', '3e', '3e'),
    ('level', 'Troisième', 'Troisième'),
    ('level', 'Seconde', 'Seconde'),
    ('level', 'Première', 'Première'),
    ('level', 'Terminale', 'Terminale'),
    ('level', 'Première A', 'Première A'),
    ('level', 'Première C', 'Première C'),
    ('level', 'Première D', 'Première D'),
    ('level', 'Terminale A', 'Terminale A'),
    ('level', 'Terminale C', 'Terminale C'),
    ('level', 'Terminale D', 'Terminale D'),
    ('level', 'Terminale TI', 'Terminale TI'),
    ('level', 'Terminale C/D/TI', 'Terminale C/D/TI'),
    ('level', 'Toutes séries', 'All series'),
    ('level', 'BACCALAUREAUT', 'Baccalaureate'),
    ('level', 'Baccalauréat C/D', 'Baccalaureate C/D'),
    ('level', 'Baccalauréat C/D/E', 'Baccalaureate C/D/E'),
    ('level', 'Baccalauréat C/D/F/E', 'Baccalaureate C/D/F/E'),
    ('level', 'Baccalauréat F', 'Baccalaureate F'),
    ('level', 'Baccalauréat F4, BA', 'Baccalaureate F4, BA'),
    ('level', 'Baccalaureat  C/D/F1/E/F4/BT', 'Baccalaureate C/D/F1/E/F4/BT'),
    ('level', 'Baccalaureat  C/D/TI/E', 'Baccalaureate C/D/TI/E'),
    ('level', 'Baccalauréat  C/D/TI/E', 'Baccalaureate C/D/TI/E'),
    ('level', '1ère année', 'First year'),
    ('level', '1ère année médecine', 'First year of medicine'),
    ('level', '1ère année Médecine', 'First year of medicine'),
    ('level', 'Licence 1', 'Licence 1'),
    ('level', 'Licence 2', 'Licence 2'),
    ('level', 'Licence 3', 'Licence 3'),
    ('level', 'Master 1', 'Master 1'),
    ('level', 'Master 2', 'Master 2'),
    ('level', 'Licence (Université)', 'Licence (University)'),
    ('level', 'Master (Université)', 'Master (University)'),
    ('level', 'Doctorat (Université)', 'Doctorate (University)'),
    ('level', 'LICENCE INDUSTRIELLE', 'Industrial bachelor''s degree'),
    ('level', 'DUT', 'DUT'),
    ('level', 'BTS', 'BTS'),
    ('level', 'BTS / DUT / DTS', 'BTS / DUT / DTS'),
    ('level', 'Cycle ingénieur', 'Engineering cycle'),
    ('level', 'Cycle Ingénieur', 'Engineering cycle'),
    ('level', 'Prépa', 'Preparatory classes'),
    ('level', 'Préparation Concours', 'Entrance exam preparation'),
    ('level', 'Formation Professionnelle', 'Vocational training'),
    ('level', 'Division administrative', 'Administrative division'),
    ('level', 'Division judiciaire', 'Judicial division'),
    ('series', 'Série A', 'Series A (Arts)'),
    ('series', 'Série C', 'Series C (Maths and Physics)'),
    ('series', 'Série D', 'Series D (Life Sciences and Maths)'),
    ('series', 'Série E', 'Series E (Maths and Technology)'),
    ('series', 'Série TI', 'Series TI (Computer Science)'),
    ('series', 'Série F', 'Series F (Technical)'),
    ('series', 'Série G', 'Series G (Business and Management)'),
    ('series', 'Toutes séries', 'All series'),
    ('cycle', 'Primaire', 'Primary'),
    ('cycle', 'Secondaire', 'Secondary'),
    ('cycle', 'Supérieur', 'Higher education'),
    ('cycle', 'Concours', 'Competitive exams'),
    ('subsystem', 'Francophone', 'Francophone'),
    ('subsystem', 'Anglophone', 'Anglophone'),
    ('subsystem', 'Sous-système francophone', 'Francophone subsystem'),
    ('subsystem', 'Sous-système anglophone', 'Anglophone subsystem'),
    ('status', 'Publié', 'Published'),
    ('status', 'Brouillon', 'Draft'),
    ('status', 'En attente', 'Pending'),
    ('status', 'En attente de validation', 'Pending review'),
    ('status', 'Validé', 'Approved'),
    ('status', 'Rejeté', 'Rejected'),
    ('status', 'Archivé', 'Archived'),
    ('session', 'session_1', 'Session 1'),
    ('session', 'Session normale', 'Regular session'),
    ('session', 'Session de rattrapage', 'Resit session'),
    ('session', 'Janvier', 'January'),
    ('session', 'Février', 'February'),
    ('session', 'Mars', 'March'),
    ('session', 'Avril', 'April'),
    ('session', 'Mai', 'May'),
    ('session', 'Juin', 'June'),
    ('session', 'juin', 'June'),
    ('session', 'Juillet', 'July'),
    ('session', 'Août', 'August'),
    ('session', 'AOUT', 'August'),
    ('session', 'Septembre', 'September'),
    ('session', 'Octobre', 'October'),
    ('session', 'Novembre', 'November'),
    ('session', 'Décembre', 'December')
) AS v(kind, fr, en)
ON CONFLICT ("Kind", "ValueFr") DO NOTHING;

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20261021120000_ContentTranslations', '8.0.0')
ON CONFLICT DO NOTHING;
