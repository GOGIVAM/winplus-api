-- Lot 10.4 : calendriers des concours et examens remplacés par des données sourcées
-- (recherche du 2026-10-19). Les données de démonstration du 2026-09-01 sont corrigées.
-- Règles :
--   * frais d'inscription remis à NULL : aucun montant officiel vérifié ;
--   * une date est laissée vide quand les sources se contredisent ou ne la donnent pas ;
--   * chaque ligne cite sa source dans Notes ; seul le communiqué officiel fait foi.
-- Rejouable : les UPDATE ciblent l'Id et la ligne 2026 ; les INSERT 2027 sont gardés par NOT EXISTS.
-- Les heures sont fixées à midi UTC pour qu'aucun fuseau ne décale le jour.

BEGIN;

-- ENS Yaoundé, session 2026 (concours ouvert par le MINESUP le 13 janvier 2026, dépôts prorogés)
UPDATE "ConcoursEvents" SET
  "Name" = 'ENS Yaoundé (concours d''entrée, 1er et 2nd cycles)',
  "RegistrationStartDate" = '2026-01-13 12:00:00+00',
  "RegistrationEndDate"   = '2026-03-06 12:00:00+00',
  "ExamDate"              = '2026-03-14 12:00:00+00',
  "ResultsDate"           = '2026-04-06 12:00:00+00',
  "EnrollmentFeeXaf" = NULL,
  "OfficialRegistrationUrl" = 'https://www.ens-yde.cm',
  "Notes" = 'Dépôt des dossiers prorogé : 4 mars 2026 en régions, 6 mars 2026 à 15 h au campus. Écrits : 14 mars (1er cycle) et 15 mars 2026 (2nd cycle). Source : www.ens-yde.cm. Seul le communiqué officiel fait foi.',
  "UpdatedAt" = now()
WHERE "Id" = 1 AND "Slug" = 'ens' AND "Year" = 2026;

-- ENSP Yaoundé (Polytechnique), session 2026
UPDATE "ConcoursEvents" SET
  "Name" = 'École Nationale Supérieure Polytechnique de Yaoundé (ENSPY)',
  "RegistrationStartDate" = '2026-05-13 12:00:00+00',
  "RegistrationEndDate"   = '2026-07-08 12:00:00+00',
  "ExamDate"              = NULL,
  "ResultsDate"           = '2026-08-04 12:00:00+00',
  "EnrollmentFeeXaf" = NULL,
  "OfficialRegistrationUrl" = 'https://concours.polytechnique.cm',
  "Notes" = 'Inscriptions en ligne du 13 mai au 8 juillet 2026 (jusqu''au 18 août pour le site d''Eseka). Épreuves en juillet 2026 au campus de Yaoundé et en août à Eseka, dans les 10 chefs-lieux de région. Résultats publiés par le MINESUP (communiqué du 4 août 2026). Sources : concours.polytechnique.cm, minesup.gov.cm. Seul le communiqué officiel fait foi.',
  "UpdatedAt" = now()
WHERE "Id" = 2 AND "Slug" = 'polytechnique' AND "Year" = 2026;

-- ENAM, session 2026 (dates par division)
UPDATE "ConcoursEvents" SET
  "Name" = 'ENAM (École Nationale d''Administration et de Magistrature)',
  "RegistrationStartDate" = NULL,
  "RegistrationEndDate"   = '2026-08-14 12:00:00+00',
  "ExamDate"              = '2026-08-01 12:00:00+00',
  "ResultsDate"           = NULL,
  "EnrollmentFeeXaf" = NULL,
  "OfficialRegistrationUrl" = 'https://concours.enam.cm',
  "Notes" = 'Inscription en ligne puis dépôt du dossier, au plus tard le 14 août 2026 selon le site officiel (certaines divisions plus tôt). Écrits échelonnés : auditeurs de justice 1er et 2 août ; greffes 8 et 9 août ; cycle B régies financières 29 et 30 août, administratif 5 et 6 septembre ; cycle A administratif 12 et 13 septembre, régies financières 19 et 20 septembre 2026. Source : concours.enam.cm. Seul l''arrêté de chaque division fait foi.',
  "UpdatedAt" = now()
WHERE "Id" = 3 AND "Slug" = 'enam' AND "Year" = 2026;

-- Médecine : examen national d'aptitude à la formation médicale (FMSB et autres facultés)
UPDATE "ConcoursEvents" SET
  "Name" = 'Examen national d''aptitude à la formation médicale (FMSB et facultés habilitées)',
  "RegistrationStartDate" = NULL,
  "RegistrationEndDate"   = '2026-08-21 12:00:00+00',
  "ExamDate"              = NULL,
  "ResultsDate"           = '2026-10-17 12:00:00+00',
  "EnrollmentFeeXaf" = NULL,
  "OfficialRegistrationUrl" = 'https://concoursdauq.com',
  "Notes" = 'Inscriptions uniquement en ligne jusqu''au 21 août 2026. Épreuves début septembre 2026 (les sources hésitent entre le 4 et le 6 septembre). Résultats publiés par le MINESUP le 17 octobre 2026 ; rentrée en première année le 26 octobre 2026. Sources : concoursdauq.com, campusunivers.com. Seul le communiqué officiel fait foi.',
  "UpdatedAt" = now()
WHERE "Id" = 4 AND "Slug" = 'fmsb' AND "Year" = 2026;

-- ESSEC Douala, session 2026 (filière ESC)
UPDATE "ConcoursEvents" SET
  "Name" = 'ESSEC Douala (concours ESC et EPA)',
  "RegistrationStartDate" = NULL,
  "RegistrationEndDate"   = '2026-08-24 12:00:00+00',
  "ExamDate"              = '2026-08-25 12:00:00+00',
  "ResultsDate"           = NULL,
  "EnrollmentFeeXaf" = NULL,
  "OfficialRegistrationUrl" = 'https://www.essec-dla.com/concours/',
  "Notes" = 'Filière ESC (après le BAC) : dépôt jusqu''au 24 août 2026, écrit le 25 août 2026 ; deuxième vague le 22 septembre 2026. Filière EPA (niveau licence) : écrit le 26 août 2026. Sources : essec-dla.com, campusunivers.com. Seul le communiqué officiel fait foi.',
  "UpdatedAt" = now()
WHERE "Id" = 5 AND "Slug" = 'essec' AND "Year" = 2026;

-- ENSET Douala, session 2026
UPDATE "ConcoursEvents" SET
  "Name" = 'ENSET Douala (concours d''entrée)',
  "RegistrationStartDate" = '2026-01-13 12:00:00+00',
  "RegistrationEndDate"   = '2026-03-06 12:00:00+00',
  "ExamDate"              = '2026-03-14 12:00:00+00',
  "ResultsDate"           = NULL,
  "EnrollmentFeeXaf" = NULL,
  "OfficialRegistrationUrl" = 'https://enset-douala.cm',
  "Notes" = 'Concours ouverts par le MINESUP le 13 janvier 2026 ; date limite de dépôt prorogée au 6 mars 2026. Écrits : 14 mars 2026 (1re année du 1er cycle) et 15 mars 2026 (3e année et niveau licence). Sources : enset-douala.cm, arrêté du MINESUP relayé par la presse. Seul l''arrêté fait foi.',
  "UpdatedAt" = now()
WHERE "Id" = 6 AND "Slug" = 'enset' AND "Year" = 2026;

-- Baccalauréat, session 2026 (OBC)
UPDATE "ConcoursEvents" SET
  "Name" = 'Baccalauréat (OBC)',
  "RegistrationStartDate" = NULL,
  "RegistrationEndDate"   = NULL,
  "ExamDate"              = '2026-05-25 12:00:00+00',
  "ResultsDate"           = '2026-07-15 12:00:00+00',
  "EnrollmentFeeXaf" = NULL,
  "OfficialRegistrationUrl" = 'https://officedubac.cm',
  "Notes" = 'Écrits de l''enseignement général à partir du 25 mai 2026 (enseignement technique dès le 21 mai). Résultats : BAC technique industriel le 14 juillet, enseignement général le 15 juillet 2026. Source : officedubac.cm et presse. Seul le communiqué de l''OBC fait foi.',
  "UpdatedAt" = now()
WHERE "Id" = 7 AND "Slug" = 'bac' AND "Year" = 2026;

-- BEPC, session 2026 (DECC du MINESEC, pas l'OBC)
UPDATE "ConcoursEvents" SET
  "Name" = 'BEPC (MINESEC, DECC)',
  "RegistrationStartDate" = NULL,
  "RegistrationEndDate"   = NULL,
  "ExamDate"              = '2026-06-01 12:00:00+00',
  "ResultsDate"           = NULL,
  "EnrollmentFeeXaf" = NULL,
  "OfficialRegistrationUrl" = 'https://www.minesec.gov.cm',
  "Notes" = 'Épreuves pratiques d''EPS du 11 au 15 mai 2026. Écrits la première semaine de juin 2026 (à partir du 1er juin selon la presse). Résultats annoncés pour la première quinzaine de juillet 2026. Le BEPC relève de la Direction des examens, des concours et de la certification (DECC) du MINESEC. Seul le communiqué du MINESEC fait foi.',
  "UpdatedAt" = now()
WHERE "Id" = 8 AND "Slug" = 'bepc' AND "Year" = 2026;

-- Probatoire, session 2026 (OBC)
UPDATE "ConcoursEvents" SET
  "Name" = 'Probatoire (OBC)',
  "RegistrationStartDate" = NULL,
  "RegistrationEndDate"   = NULL,
  "ExamDate"              = NULL,
  "ResultsDate"           = '2026-07-30 12:00:00+00',
  "EnrollmentFeeXaf" = NULL,
  "OfficialRegistrationUrl" = 'https://officedubac.cm',
  "Notes" = 'Épreuves pratiques d''EPS du 4 au 8 mai 2026. Résultats du Probatoire de l''enseignement général publiés fin juillet 2026. Source : officedubac.cm et presse. Seul le communiqué de l''OBC fait foi.',
  "UpdatedAt" = now()
WHERE "Id" = 9 AND "Slug" = 'probatoire' AND "Year" = 2026;

-- Session 2027 : seules les inscriptions OBC sont annoncées (Epim-Exam.cm, depuis le 12 octobre 2026).
INSERT INTO "ConcoursEvents" ("Slug","Name","Year","RegistrationStartDate","RegistrationEndDate","ExamDate","ResultsDate","Location","EnrollmentFeeXaf","OfficialRegistrationUrl","Notes","Tips","FaqJson","IsPublished","CreatedAt","UpdatedAt")
SELECT s.slug, s.name, 2027, '2026-10-12 12:00:00+00', '2027-01-30 12:00:00+00', NULL, NULL, NULL, NULL, 'https://epim-exam.cm',
       'Inscriptions aux examens officiels 2026/2027 en ligne sur la plateforme Epim-Exam de l''OBC depuis le 12 octobre 2026, dans les établissements jusqu''au 30 janvier 2027 sans pénalité ; du 1er au 28 février 2027 avec une pénalité de 15 000 FCFA ; au-delà, sur autorisation du ministre seulement. Dates des épreuves 2027 pas encore publiées. Source : officedubac.cm, 237online.com (16 octobre 2026). Seul le communiqué de l''OBC fait foi.',
       NULL, NULL, true, now(), now()
FROM (VALUES ('bac','Baccalauréat (OBC)'), ('probatoire','Probatoire (OBC)')) AS s(slug, name)
WHERE NOT EXISTS (SELECT 1 FROM "ConcoursEvents" e WHERE e."Slug" = s.slug AND e."Year" = 2027);

COMMIT;

SELECT "Id" || ' | ' || "Slug" || ' ' || "Year" || ' | ' || coalesce("ExamDate"::date::text,'-') || ' | ' || coalesce("ResultsDate"::date::text,'-') || ' | ' || coalesce("OfficialRegistrationUrl",'')
FROM "ConcoursEvents" ORDER BY "Slug", "Year";
