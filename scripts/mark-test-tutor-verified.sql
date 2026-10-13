-- =====================================================================
-- Compte professeur de test Id 50 marqué conforme pour le présentiel
-- (suivi 17.17, décision du product owner du 2026-10-13)
--
--   professeur Id 50 : ekambimoukoko4@gmail.com (compte de test)
--
-- SCRIPT MANUEL, À LANCER UNE SEULE FOIS PAR L'ADMINISTRATEUR, APRÈS le
-- déploiement du lot 9.5 et APRÈS les scripts
-- dotnet/Migrations/SQL_TutorInPersonVerificationAndReports.sql et
-- dotnet/Migrations/SQL_TutorInPersonVerificationDelivery.sql.
-- Volontairement hors de dotnet/Migrations/ : il ne doit jamais être rejoué
-- automatiquement ni appliqué à un autre compte.
--
-- Voie traçable : une vérification d'identité APPROUVÉE (voie « identity »),
-- exactement ce que laisse en base une validation admin
-- (TutorVerificationService.ReviewIdentityAsync puis RecomputeAsync) :
--   1. "TutorIdentityChecks"      : un dépôt « approved », source « tutor »,
--                                   type « other », SANS fichier (aucune pièce
--                                   n'a été déposée : FilesPurgedAt posé tout
--                                   de suite, références nulles) ;
--   2. "TutorVerificationEvents"  : « identity / submitted » puis
--                                   « identity / approved » (acteur : admin),
--                                   motif « compte de test, validé par le
--                                   product owner le 2026-10-13 » ;
--   3. Recalcul du statut comme RecomputeAsync : voies valides dans l'ordre
--      identity, interview (entretien réussi ET identité validée), institution
--      (caution active ET rattachement accepté) -> "InPersonVerificationRoutes" ;
--      "InPersonVerifiedAt" posé s'il était nul, avec l'événement
--      « status / verified ».
-- Une suspension éventuelle du présentiel ("InPersonSuspendedAt") n'est PAS
-- levée : seule la levée par un admin, avec motif, le fait.
-- Pas de notification au tuteur (le serveur l'envoie lors d'une vraie
-- validation ; ici le compte de test n'en a pas besoin).
--
-- Idempotence : si le profil a déjà une vérification d'identité approuvée
-- (celle de ce script ou une vraie), aucun dépôt n'est ajouté ; le recalcul
-- ne pose "InPersonVerifiedAt" et l'événement « verified » que s'il était nul.
-- Rejouer le script ne crée rien de plus. Une seule transaction : un compte
-- absent, inactif, supprimé, d'un autre rôle ou d'une autre adresse, ou sans
-- profil répétiteur, annule l'ensemble.
--
-- Exécution (base de production, par l'administrateur uniquement) :
--   psql -v ON_ERROR_STOP=1 -h <hôte> -U <utilisateur> -d <base> -f mark-test-tutor-verified.sql
-- Le SELECT final affiche le statut du profil et son historique de vérification.
-- =====================================================================

SET client_encoding = 'UTF8';

BEGIN;

DO $$
DECLARE
    c_user_id   CONSTANT INTEGER := 50;
    c_email     CONSTANT TEXT    := 'ekambimoukoko4@gmail.com';
    c_note      CONSTANT TEXT    := 'compte de test, validé par le product owner le 2026-10-13';
    v_role      TEXT;
    v_email     TEXT;
    v_active    BOOLEAN;
    v_deleted   BOOLEAN;
    v_profile   INTEGER;
    v_check     INTEGER;
    v_routes    TEXT[] := ARRAY[]::TEXT[];
    v_identity  BOOLEAN;
    v_interview BOOLEAN;
    v_endorsed  BOOLEAN;
    v_verified  TIMESTAMPTZ;
    v_now       TIMESTAMPTZ := now();
BEGIN
    -- Compte : existant, actif, non supprimé, professeur, adresse attendue.
    SELECT lower(trim(u."Role")), lower(trim(u."Email")), u."IsActive", u."IsDeleted"
      INTO v_role, v_email, v_active, v_deleted
      FROM "Users" u WHERE u."Id" = c_user_id;
    IF NOT FOUND THEN
        RAISE EXCEPTION 'Compte % introuvable', c_user_id;
    END IF;
    IF v_role IS DISTINCT FROM 'teacher' THEN
        RAISE EXCEPTION 'Le compte % a le rôle « % », « teacher » attendu', c_user_id, v_role;
    END IF;
    IF v_email IS DISTINCT FROM c_email THEN
        RAISE EXCEPTION 'Le compte % a l''adresse « % », « % » attendue', c_user_id, v_email, c_email;
    END IF;
    IF NOT v_active OR v_deleted THEN
        RAISE EXCEPTION 'Le compte % est inactif ou supprimé', c_user_id;
    END IF;

    SELECT p."Id", p."InPersonVerifiedAt" INTO v_profile, v_verified
      FROM "TutorProfiles" p WHERE p."UserId" = c_user_id
       FOR UPDATE;
    IF v_profile IS NULL THEN
        RAISE EXCEPTION 'Le compte % n''a pas de profil répétiteur : activer d''abord le mode Répétiteur', c_user_id;
    END IF;

    -- 1-2. Vérification d'identité approuvée (seulement s'il n'y en a aucune).
    SELECT c."Id" INTO v_check
      FROM "TutorIdentityChecks" c
     WHERE c."TutorProfileId" = v_profile AND c."Status" = 'approved'
     ORDER BY c."ReviewedAt" NULLS LAST, c."Id"
     LIMIT 1;
    IF v_check IS NULL THEN
        INSERT INTO "TutorIdentityChecks" ("TutorProfileId", "Source", "TutorVerificationInterviewId", "DocumentType",
                                           "FrontFileRef", "BackFileRef", "SelfieFileRef", "Status", "RejectionReason",
                                           "SubmittedByUserId", "SubmittedAt", "ReviewedByUserId", "ReviewedAt", "FilesPurgedAt")
        VALUES (v_profile, 'tutor', NULL, 'other',
                NULL, NULL, NULL, 'approved', NULL,
                c_user_id, v_now, NULL, v_now, v_now)
        RETURNING "Id" INTO v_check;

        INSERT INTO "TutorVerificationEvents" ("TutorProfileId", "Category", "Action", "ActorUserId", "ActorRole", "Reason", "RelatedId", "CreatedAt")
        VALUES (v_profile, 'identity', 'submitted', c_user_id, 'tutor', 'Sans pièce jointe : ' || c_note || '.', v_check, v_now),
               (v_profile, 'identity', 'approved',  NULL,      'admin', 'Validation sans pièce : ' || c_note || '.', v_check, v_now);
        RAISE NOTICE 'Profil répétiteur % : vérification d''identité % approuvée (%)', v_profile, v_check, c_note;
    ELSE
        RAISE NOTICE 'Profil répétiteur % : vérification d''identité % déjà approuvée, aucun dépôt ajouté', v_profile, v_check;
    END IF;

    -- 3. Recalcul du statut (TutorVerificationService.RecomputeAsync).
    v_identity := EXISTS (SELECT 1 FROM "TutorIdentityChecks" c WHERE c."TutorProfileId" = v_profile AND c."Status" = 'approved');
    v_interview := EXISTS (SELECT 1 FROM "TutorVerificationInterviews" i WHERE i."TutorProfileId" = v_profile AND i."Status" = 'passed');
    v_endorsed := EXISTS (
        SELECT 1 FROM "TutorInstitutionEndorsements" e
          JOIN "InstitutionTeacherLinks" l
            ON l."InstitutionId" = e."InstitutionId" AND l."TeacherId" = c_user_id AND l."Status" = 'accepted'
         WHERE e."TutorProfileId" = v_profile AND e."Status" = 'active');
    IF v_identity THEN v_routes := array_append(v_routes, 'identity'); END IF;
    IF v_interview AND v_identity THEN v_routes := array_append(v_routes, 'interview'); END IF;
    IF v_endorsed THEN v_routes := array_append(v_routes, 'institution'); END IF;

    UPDATE "TutorProfiles"
       SET "InPersonVerificationRoutes" = CASE WHEN cardinality(v_routes) > 0 THEN array_to_string(v_routes, ',') END,
           "InPersonVerifiedAt" = COALESCE("InPersonVerifiedAt", v_now),
           "UpdatedAt" = v_now
     WHERE "Id" = v_profile;

    IF v_verified IS NULL THEN
        INSERT INTO "TutorVerificationEvents" ("TutorProfileId", "Category", "Action", "ActorUserId", "ActorRole", "Reason", "RelatedId", "CreatedAt")
        VALUES (v_profile, 'status', 'verified', NULL, 'admin', 'Pièce d''identité et selfie validés (' || c_note || ').', NULL, v_now);
        RAISE NOTICE 'Profil répétiteur % : vérifié pour le présentiel (voies : %)', v_profile, array_to_string(v_routes, ', ');
    ELSE
        RAISE NOTICE 'Profil répétiteur % : déjà vérifié depuis % (voies : %)', v_profile, v_verified, array_to_string(v_routes, ', ');
    END IF;
END $$;

COMMIT;

-- Contrôle : statut du profil et historique de vérification.
SELECT p."Id" AS tutor_profile_id, p."UserId" AS user_id, p."InPersonVerifiedAt", p."InPersonVerificationRoutes",
       p."InPersonSuspendedAt", p."OffersOnline", p."OffersAtStudentHome", p."OffersAtTutorHome", p."OffersNeutralPlace"
  FROM "TutorProfiles" p WHERE p."UserId" = 50;

SELECT e."CreatedAt", e."Category", e."Action", e."ActorRole", e."Reason", e."RelatedId"
  FROM "TutorVerificationEvents" e
  JOIN "TutorProfiles" p ON p."Id" = e."TutorProfileId"
 WHERE p."UserId" = 50
 ORDER BY e."CreatedAt", e."Id";
