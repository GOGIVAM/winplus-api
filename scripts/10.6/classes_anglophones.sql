-- Lot 10.6 : noms de classes en anglais = équivalents du sous-système anglophone
-- camerounais (table ContentTranslations, créée par SQL_ContentTranslations.sql).
--
-- Jusqu'ici les classes francophones gardaient leur nom en anglais
-- (« Terminale » → « Terminale »). Elles prennent l'équivalent anglophone, à
-- année d'études égale (MINESEC : premier cycle de 4 ans + second cycle de
-- 3 ans côté francophone, 5 ans + 2 ans côté anglophone, 7 ans dans les deux) :
--   6ème = Form 1, 5ème = Form 2, 4ème = Form 3, 3ème = Form 4,
--   Seconde = Form 5, Première = Lower Sixth, Terminale = Upper Sixth ;
--   SIL = Class 1, CP = Class 2, CE1 = Class 3, CE2 = Class 4, CM1 = Class 5, CM2 = Class 6 ;
--   enseignement technique : même logique, « (technical) » en plus.
-- Les lettres de série et les examens (BEPC, Probatoire, BAC) ne changent pas.
--
-- Seul l'affichage change : les valeurs enregistrées dans les contenus
-- (Exams.Level, Subjects.Level, InstitutionStudents.Level…) restent françaises ;
-- les filtres reçus en anglais retrouvent le français (FrenchEquivalents).
--
-- Règles (convention de SQL_ContentTranslations.sql) :
--   * Origin = 'human', NeedsReview = FALSE (vocabulaire validé) ; une ligne
--     reste corrigeable dans la page admin « Traductions » ;
--   * une ligne déjà corrigée en admin n'est jamais écrasée : la mise à jour ne
--     touche que les lignes encore à leur valeur pré-remplie (anglais = français,
--     jamais modifiée depuis sa création) ou ajoutées par WinAI (Origin = 'auto') ;
--   * rejouable : une deuxième exécution ne modifie aucune ligne.
-- Aucun changement de schéma. À exécuter après SQL_ContentTranslations.sql ;
-- l'index des traductions se recharge de lui-même (10 minutes au plus).

BEGIN;

INSERT INTO "ContentTranslations" ("Kind", "ValueFr", "ValueEn", "Origin", "NeedsReview", "CreatedAt", "UpdatedAt")
SELECT 'level', v.fr, v.en, 'human', FALSE, NOW(), NOW() FROM (VALUES
    -- Secondaire général, premier cycle
    ('6ème', 'Form 1'),
    ('6e', 'Form 1'),
    ('Sixième', 'Form 1'),
    ('5ème', 'Form 2'),
    ('5e', 'Form 2'),
    ('Cinquième', 'Form 2'),
    ('4ème', 'Form 3'),
    ('4e', 'Form 3'),
    ('Quatrième', 'Form 3'),
    ('3ème', 'Form 4'),
    ('3e', 'Form 4'),
    ('Troisième', 'Form 4'),
    -- Secondaire général, second cycle
    ('Seconde', 'Form 5'),
    ('2nde', 'Form 5'),
    ('2de', 'Form 5'),
    ('Première', 'Lower Sixth'),
    ('1ère', 'Lower Sixth'),
    ('1re', 'Lower Sixth'),
    ('Terminale', 'Upper Sixth'),
    ('Tle', 'Upper Sixth'),
    -- Classes avec série (la lettre de série reste telle quelle)
    ('2nde A', 'Form 5 A'),
    ('2nde C', 'Form 5 C'),
    ('Seconde A', 'Form 5 A'),
    ('Seconde C', 'Form 5 C'),
    ('Première A', 'Lower Sixth A'),
    ('Première C', 'Lower Sixth C'),
    ('Première D', 'Lower Sixth D'),
    ('Première TI', 'Lower Sixth TI'),
    ('1ère A', 'Lower Sixth A'),
    ('1ère C', 'Lower Sixth C'),
    ('1ère D', 'Lower Sixth D'),
    ('1ère TI', 'Lower Sixth TI'),
    ('Terminale A', 'Upper Sixth A'),
    ('Terminale C', 'Upper Sixth C'),
    ('Terminale D', 'Upper Sixth D'),
    ('Terminale E', 'Upper Sixth E'),
    ('Terminale TI', 'Upper Sixth TI'),
    ('Terminale C/D/TI', 'Upper Sixth C/D/TI'),
    ('Tle A', 'Upper Sixth A'),
    ('Tle C', 'Upper Sixth C'),
    ('Tle D', 'Upper Sixth D'),
    -- Primaire
    ('SIL', 'Class 1'),
    ('CP', 'Class 2'),
    ('CE1', 'Class 3'),
    ('CE2', 'Class 4'),
    ('CM1', 'Class 5'),
    ('CM2', 'Class 6'),
    -- Enseignement technique (libellés du profil, src/pages/CompleteProfile.tsx)
    ('1ère année (technique)', 'Form 1 (technical)'),
    ('2ème année (technique)', 'Form 2 (technical)'),
    ('3ème année (technique)', 'Form 3 (technical)'),
    ('4ème année (CAP)', 'Form 4 (technical, CAP)'),
    ('Seconde technique', 'Form 5 (technical)'),
    ('Première technique', 'Lower Sixth (technical)'),
    ('Terminale technique (F, G, TI)', 'Upper Sixth (technical, F, G, TI)')
) AS v(fr, en)
ON CONFLICT ("Kind", "ValueFr") DO UPDATE
    SET "ValueEn" = EXCLUDED."ValueEn", "Origin" = 'human', "NeedsReview" = FALSE, "UpdatedAt" = NOW()
    WHERE "ContentTranslations"."ValueEn" <> EXCLUDED."ValueEn"
      AND ("ContentTranslations"."Origin" = 'auto'
           OR ("ContentTranslations"."ValueEn" = "ContentTranslations"."ValueFr"
               AND "ContentTranslations"."UpdatedAt" = "ContentTranslations"."CreatedAt"));

COMMIT;

-- Contrôle : SELECT "ValueFr", "ValueEn", "Origin" FROM "ContentTranslations"
--            WHERE "Kind" = 'level' ORDER BY "ValueFr";
