-- Lot 10.4 : corrections des données d'épreuves relevées par le sous-lot 10.4
-- (inventaire en lecture seule du 2026-10-20 sur la base de production).
-- Règles :
--   * une année n'est corrigée que si le titre ou la description donne la session
--     (« session 2014 ») ; sinon elle est laissée telle quelle et signalée ;
--   * les types d'examen corrigés reprennent un libellé reconnu par
--     dotnet/Utils/ExamTypeGroups.cs et src/lib/concours/examGroups.ts (web) :
--     « ENSPD/… » se range dans le groupe « enspd » (carte ENSPD de l'accueil,
--     /catalog?exam=enspd), « FMSB/… » dans « fmsb » (page /concours/fmsb) ;
--   * les catégories (matières) mal orthographiées ou qui ne sont pas des matières
--     sont alignées sur les libellés existants ; la fiche du catalogue (Subjects)
--     liée est corrigée de la même façon.
-- Rejouable : chaque UPDATE cible un Id et garde l'ancienne valeur dans le WHERE ;
-- une deuxième exécution ne modifie aucune ligne.

BEGIN;

-- 1. Années : la session figure dans le titre (et la description pour l'IBAF).
UPDATE "Exams" e SET "Year" = v.new_year, "UpdatedAt" = now()
FROM (VALUES
  (53, 2026, 2014),  -- IBAF Physique : « session 2014 » (titre et description)
  (57, 2017, 2015),  -- FASA Culture générale : « session 2015 » (titre)
  (59, 2026, 2020),  -- FASA Sciences naturelles : « session 2020 » (titre)
  (61, 2026, 2021)   -- FASA Culture générale : « session 2021 » (titre)
) AS v(id, old_year, new_year)
WHERE e."Id" = v.id AND e."Year" = v.old_year;

-- 2. Types d'examen.
-- ENSPD (École nationale supérieure polytechnique de Douala) : sans page, typées « Concours… ».
UPDATE "Exams" e SET "ExamType" = 'ENSPD/Concours d''entrée – Épreuve de spécialité', "UpdatedAt" = now()
FROM (VALUES
  (79, 'Concours d''entrée / Épreuve de spécialité'),
  (80, 'Concours d''entrée – Épreuve de spécialité'),
  (81, 'Concours d''entrée / Épreuve de spécialité'),
  (83, 'Concours'),
  (125, 'Concours d''entrée / Épreuve de spécialité')
) AS v(id, old_type)
WHERE e."Id" = v.id AND e."ExamType" = v.old_type;

-- Examen national d'aptitude à la formation médicale typé seulement « QCM ».
UPDATE "Exams" e SET "ExamType" = 'FMSB/Examen national d''aptitude à la formation médicale (QCM)', "UpdatedAt" = now()
FROM (VALUES
  (88, 'QCM'),
  (92, 'QCM (questions à choix multiples)'),
  (98, 'QCM – concours d’entrée')
) AS v(id, old_type)
WHERE e."Id" = v.id AND e."ExamType" = v.old_type;

-- 3. Catégories (matières) : fautes d'orthographe, casse, libellés qui ne sont pas des matières.
CREATE TEMP TABLE corr_cat (id int, old_cat text, new_cat text) ON COMMIT DROP;
INSERT INTO corr_cat VALUES
  (66,  'Chimi', 'Chimie'),
  (57,  'Culture général', 'Culture générale'),
  (58,  'Culture général', 'Culture générale'),
  (61,  'Culture général', 'Culture générale'),
  (62,  'Culture général', 'Culture générale'),
  (118, 'Culture Générale', 'Culture générale'),
  (119, 'Culture Générale', 'Culture générale'),
  (113, 'Culture générale (concours médecine)', 'Culture générale'),
  (56,  'DESSIN', 'Dessin'),
  (59,  'Sciences Naturelles', 'Sciences naturelles'),
  (60,  'Sciences Naturelles', 'Sciences naturelles'),
  (67,  'Mécanique appliqué et RDM', 'Mécanique appliquée et RDM'),
  (68,  'Mathématiques industrielle', 'Mathématiques industrielles'),
  (76,  'Épreuve de Mécanique Appliquée – Génie Civil', 'Mécanique appliquée'),
  (78,  'Épreuve / Sujet d''examen', 'Français'),
  (80,  'concours', 'Génie Civil'),
  (83,  'Concours d''entrée – Épreuve de spécialité', 'Spécialité'),
  (85,  'Sciences', 'Biologie'),
  (88,  'Épreuve de biologie', 'Biologie'),
  (92,  'Sciences – Biologie', 'Biologie'),
  (121, 'Concours d''entrée / Épreuve de spécialité', 'Spécialité'),
  (122, 'SPECIALITÉ / Épreuve de concours', 'Spécialité'),
  (124, 'Épreuve de concours', 'Spécialité');

UPDATE "Exams" e SET "Category" = c.new_cat, "UpdatedAt" = now()
FROM corr_cat c
WHERE e."Id" = c.id AND e."Category" = c.old_cat;

-- Fiche du catalogue liée : même correction quand elle porte encore l'ancienne valeur.
UPDATE "Subjects" s SET "Category" = c.new_cat, "UpdatedAt" = now()
FROM corr_cat c JOIN "Exams" e ON e."Id" = c.id
WHERE s."Id" = e."SubjectId" AND s."Category" = c.old_cat;

-- Fiches dont la catégorie diffère de celle de leur épreuve.
UPDATE "Subjects" s SET "Category" = v.new_cat, "UpdatedAt" = now()
FROM (VALUES
  (59,  'Spécialité ', 'Spécialité'),                          -- espace final (épreuve 73)
  (130, 'Concours / Épreuves', 'Physique'),                    -- épreuve 103
  (150, 'Sujet d''examen / Épreuve de concours', 'Spécialité') -- épreuve 122
) AS v(id, old_cat, new_cat)
WHERE s."Id" = v.id AND s."Category" = v.old_cat;

-- 4. Titre : « (Niveau 3)2 » (épreuve 80, fiche 104).
UPDATE "Exams" SET "Title" = 'Concours ENSPD 2022 – Épreuve de spécialité Génie Civil (Niveau 3)', "UpdatedAt" = now()
WHERE "Id" = 80 AND "Title" = 'Concours ENSPD 2022 – Épreuve de spécialité Génie Civil (Niveau 3)2';
UPDATE "Subjects" SET "Title" = 'Concours ENSPD 2022 – Épreuve de spécialité Génie Civil (Niveau 3)', "UpdatedAt" = now()
WHERE "Id" = 104 AND "Title" = 'Concours ENSPD 2022 – Épreuve de spécialité Génie Civil (Niveau 3)2';

-- 5. Sessions mal orthographiées.
UPDATE "Exams" SET "Session" = 'SEPTEMBRE', "UpdatedAt" = now() WHERE "Id" = 103 AND "Session" = 'SEMPTEMBRE';
UPDATE "Exams" SET "Session" = 'Septembre', "UpdatedAt" = now() WHERE "Id" = 96 AND "Session" = 'Septebre';

COMMIT;

-- Contrôle : lignes concernées après correction.
SELECT e."Id" || ' | ' || e."ExamType" || ' | ' || e."Category" || ' | ' || e."Year" || ' | ' || coalesce(e."Session", '')
       || ' | fiche ' || coalesce(s."Id"::text, '-') || ' ' || coalesce(s."Category", '')
FROM "Exams" e LEFT JOIN "Subjects" s ON s."Id" = e."SubjectId"
WHERE e."Id" IN (53,56,57,58,59,60,61,62,66,67,68,73,76,78,79,80,81,83,85,88,92,96,98,103,113,118,119,121,122,124,125)
ORDER BY e."Id";
