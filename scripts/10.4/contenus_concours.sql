-- Lot 10.4 : contenus officiels des pages concours (conditions d'admission, filières,
-- épreuves et coefficients, programme), avec leur source. Recherche du 2026-10-20.
--
-- PRÉREQUIS : Migrations/SQL_ConcoursEventsEditorial.sql exécuté et l'écran admin
-- « Contenus des concours » déployé. Ne pas exécuter avant.
--
-- Règles :
--   * seules figurent les informations lues dans le texte officiel (arrêté, décision ou
--     communiqué du MINESUP, arrêté du MINFOPRA, document de l'OBC), cité dans SourceUrl ;
--   * quand deux sources (ou les versions française et anglaise d'un même texte) se
--     contredisent, l'information est omise ; les omissions sont signalées en commentaire ;
--   * les versions anglaises (*En) traduisent les mêmes faits, sans rien ajouter ;
--   * une date n'est renseignée que si elle était vide et que le texte officiel la donne.
-- Rejouable sans effet : chaque UPDATE cible l'Id, le slug et l'année et n'agit que sur
-- une fiche encore sans source (SourceUrl IS NULL), ce qui protège aussi les saisies faites
-- depuis dans l'admin ; les INSERT sont gardés par NOT EXISTS (slug, année). Les heures sont fixées à midi UTC.
-- Rien de fiable trouvé : BEPC (aucun texte officiel sur les épreuves et coefficients).

BEGIN;

-- ── ENS Yaoundé, session 2026 (arrêtés 26/00038 et 26/00037/MINESUP du 13 janvier 2026) ──
-- Omis : calendrier du relais presse (12 et 13 mars) contraire à l'arrêté (14 et 15 mars).
UPDATE "ConcoursEvents" SET
  "AdmissionConditions" = $t$**1re année du 1er cycle**
- Camerounais des deux sexes ; étrangers admis aux mêmes conditions, dans la limite des places.
- Sciences fondamentales : BAC scientifique ou GCE A/L in Sciences.
- Arts, langues et littérature : BAC littéraire ou GCE A/L in Arts.
- Sciences humaines et sociales : tout BAC ou GCE A/L.
- Non-fonctionnaires : 29 ans au plus au 1er janvier 2026, sans dispense d'âge.
- Les bacheliers de l'année peuvent composer sous réserve.

**1re année du 2nd cycle**
- Licence ou Bachelor correspondant à l'option (licences professionnelles correspondantes acceptées).
- Non-fonctionnaires : 32 ans au plus au 1er janvier 2026, sans dispense d'âge.

Frais de candidature : 20 000 FCFA. Certificat médical d'aptitude à l'enseignement exigé.$t$,
  "AdmissionConditionsEn" = $t$**First year, first cycle**
- Cameroonians of both sexes; foreigners admitted on the same terms, within the places available.
- Basic sciences: science Baccalauréat or GCE A/L in Sciences.
- Arts, languages and literature: arts Baccalauréat or GCE A/L in Arts.
- Humanities and social sciences: any Baccalauréat or GCE A/L.
- Non civil servants: at most 29 years old on 1 January 2026, no age waiver.
- Candidates sitting the Baccalauréat this year may apply provisionally.

**First year, second cycle**
- Licence or Bachelor's degree matching the option (matching professional degrees accepted).
- Non civil servants: at most 32 years old on 1 January 2026, no age waiver.

Application fee: 20,000 FCFA. Medical certificate of fitness to teach required.$t$,
  "Tracks" = $t$**1er cycle (175 places)** : Mathématiques-Informatique (60) ; Physique-Chimie-Technologie (40) ; Biologie-Chimie-Géologie et Environnement (25) ; Français (10) ; Anglais (10) ; Langue étrangère (10) ; Philosophie (10) ; Histoire-Géographie-Éducation à la citoyenneté-TIC (10).

**2nd cycle (150 places)** : Mathématiques-Informatique (40) ; Physique-Chimie-Technologie (25) ; Biologie-Chimie-Géologie et Environnement (15) ; Français (10) ; Anglais (10) ; Langue étrangère (10) ; Philosophie (10) ; Lettres bilingues (5) ; Histoire-Géographie (10) ; Économie-Éducation à la citoyenneté-TIC-Entrepreneuriat (10) ; Conseillers d'orientation et Techniques de l'éducation inclusive (5).$t$,
  "TracksEn" = $t$**First cycle (175 places)**: Mathematics-Computer science (60); Physics-Chemistry-Technology (40); Biology-Chemistry-Geology and Environment (25); French (10); English (10); Foreign language (10); Philosophy (10); History-Geography-Citizenship-ICT (10).

**Second cycle (150 places)**: Mathematics-Computer science (40); Physics-Chemistry-Technology (25); Biology-Chemistry-Geology and Environment (15); French (10); English (10); Foreign language (10); Philosophy (10); Bilingual letters (5); History-Geography (10); Economics-Citizenship-ICT-Entrepreneurship (10); Guidance counsellors and Inclusive education (5).$t$,
  "Coefficients" = $t$- Note de scolarité : 25 % (âge, années d'études, notes au Probatoire ou GCE O/L et au BAC ou GCE A/L ; au 2nd cycle, notes de licence).
- Épreuves écrites : 75 %.
- 1re épreuve, propre à la filière : 3 h, coefficient 4.
- 2e épreuve, culture générale : 2 h, coefficient 2.
- Une note inférieure à 5/20 est éliminatoire.$t$,
  "CoefficientsEn" = $t$- School record: 25% (age, years of study, Probatoire or GCE O/L and Baccalauréat or GCE A/L marks; for the second cycle, degree marks).
- Written papers: 75%.
- Paper 1, specific to the programme: 3 hours, weighting 4.
- Paper 2, general knowledge: 2 hours, weighting 2.
- A mark below 5/20 is eliminatory.$t$,
  "Programme" = $t$- 1er cycle : programmes du BAC ou du GCE A/L de chaque spécialité.
- 2nd cycle : programme de la licence ou du Bachelor de la spécialité.$t$,
  "ProgrammeEn" = $t$- First cycle: Baccalauréat or GCE A/L syllabus of each specialty.
- Second cycle: Licence or Bachelor's syllabus of the specialty.$t$,
  "SourceUrl" = $t$https://www.minesup.gov.cm/wp-content/uploads/2026/01/Concours-ENS-Yaounde-1-2026_1er-cycle.pdf
https://www.minesup.gov.cm/wp-content/uploads/2026/01/Concours-ENS-Yaounde-1-2026_2nd-cycle.pdf
https://www.ens-yde.cm$t$,
  "SourceCheckedAt" = '2026-10-20 12:00:00+00',
  "UpdatedAt" = now()
WHERE "Id" = 1 AND "Slug" = 'ens' AND "Year" = 2026
  AND "SourceUrl" IS NULL;

-- ── ENSP Yaoundé (Polytechnique), concours 2026 (communiqués 26-00238 et 26-00239 du 13 mai 2026) ──
-- Omis : détail du GCE A/L (les versions française et anglaise du communiqué diffèrent) ;
-- « moins de 23 ans » d'un site non officiel, contraire au communiqué (29 ans).
UPDATE "ConcoursEvents" SET
  "AdmissionConditions" = $t$**1re année (Sciences et Technologies)**
- Élèves de Terminale C, D, E, TI ou F, ou d'Upper Sixth scientifique, ou titulaires du BAC de ces séries, du GCE A/L scientifique ou d'un diplôme équivalent (détail du GCE : voir le communiqué).
- 29 ans au plus au 1er janvier 2026.
- Frais : 20 000 FCFA. Inscription en ligne sur polytechnique.cm/concours, dossiers au plus tard le 8 juillet 2026.
- Admission directe possible avec mention « Très Bien » au BAC C ou E 2026, « Excellent » au BAC D, TI ou F 2026, ou 5 matières au grade A au GCE A/L 2026.

**3e année**
- Licence en ingénierie, physique, informatique ou mathématiques, DUT, DEUG ou équivalent ; candidats en attente de résultats acceptés.
- Frais : 25 000 FCFA, dossiers au plus tard le 8 juillet 2026.$t$,
  "AdmissionConditionsEn" = $t$**First year (Sciences and Technologies)**
- Upper Sixth science students or Terminale C, D, E, TI or F students, or holders of the Baccalauréat in these series, a science GCE A/L or an equivalent qualification (GCE details: see the announcement).
- At most 29 years old on 1 January 2026.
- Fee: 20,000 FCFA. Online registration at polytechnique.cm/concours, files by 8 July 2026.
- Direct admission possible with "Très Bien" in Baccalauréat C or E 2026, "Excellent" in Baccalauréat D, TI or F 2026, or 5 subjects at grade A in the 2026 GCE A/L.

**Third year**
- Degree in engineering, physics, computer science or mathematics, DUT, DEUG or equivalent; candidates awaiting results accepted.
- Fee: 25,000 FCFA, files by 8 July 2026.$t$,
  "Tracks" = $t$- 1re année : 400 places au cycle des ingénieurs en Sciences et Technologies (sans répartition par filière dans le communiqué).
- 3e année : 60 places en génie civil, génie électrique, génie industriel, génie informatique, génie mécanique, génie des télécommunications, finance et sciences actuarielles, météorologie.$t$,
  "TracksEn" = $t$- First year: 400 places in the Sciences and Technologies engineering cycle (no split by programme in the announcement).
- Third year: 60 places in civil, electrical, industrial, computer, mechanical and telecommunications engineering, finance and actuarial science, meteorology.$t$,
  "Coefficients" = $t$- 1re année : deux épreuves de mathématiques et deux épreuves de physique, de 3 h chacune, d'égale importance ; pas d'oral.
- 3e année : mathématiques (4 h), physique (4 h) et informatique (4 h), d'égale importance.
- Écrits les 16 et 17 juillet 2026.$t$,
  "CoefficientsEn" = $t$- First year: two mathematics papers and two physics papers, 3 hours each, of equal weight; no oral.
- Third year: mathematics (4 h), physics (4 h) and computer science (4 h), of equal weight.
- Written papers on 16 and 17 July 2026.$t$,
  "Programme" = $t$- 1re année : programmes de Première et de Terminale des séries citées, et de Lower et Upper Sixth scientifiques.
- 3e année : programmes des classes menant aux diplômes de niveau BAC + 2.$t$,
  "ProgrammeEn" = $t$- First year: Première and Terminale syllabuses of the listed series, and Lower and Upper Sixth science syllabuses.
- Third year: syllabuses of courses leading to two-year post-Baccalauréat qualifications.$t$,
  "ExamDate" = coalesce("ExamDate", '2026-07-16 12:00:00+00'),
  "SourceUrl" = $t$https://www.minesup.gov.cm/wp-content/uploads/2026/05/ENSPY_L1_SceTech_Yaounde.pdf
https://www.minesup.gov.cm/wp-content/uploads/2026/05/ENSPY_L3_SceTech_Yaounde.pdf
https://concours.polytechnique.cm$t$,
  "SourceCheckedAt" = '2026-10-20 12:00:00+00',
  "UpdatedAt" = now()
WHERE "Id" = 2 AND "Slug" = 'polytechnique' AND "Year" = 2026
  AND "SourceUrl" IS NULL;

-- ── ENAM, session 2026 (arrêtés MINFOPRA du 4 juin 2026) ──
-- Omis : dates limites de dépôt (concours.enam.cm : 14 août ; arrêtés DA : 21 août).
-- Arrêtés non lus : Régies financières cycle B, greffes ; leurs épreuves ne sont pas saisies.
UPDATE "ConcoursEvents" SET
  "AdmissionConditions" = $t$- Toutes divisions : conditions générales d'accès aux emplois publics ; de 17 à 32 ans au 1er janvier 2026 (jusqu'à 5 ans de dispense pour les titulaires d'une carte nationale d'invalidité). Frais : 25 000 FCFA.
- Division administrative, cycle A (externes) : licence (générale ou professionnelle) en sciences juridiques, économiques, sociales ou en management, obtenue au Cameroun, ou diplôme équivalent. Internes : fonctionnaires de catégorie B des corps cités par l'arrêté, 5 ans d'ancienneté dans le grade, 45 ans au plus.
- Division administrative, cycle B : BAC, GCE A Level en au moins 2 matières (hors Religious Knowledge) à la même session, capacité en droit ou équivalent.
- Auditeurs de justice : Master I ou maîtrise en droit privé ou public d'une université camerounaise, ou diplôme étranger équivalent.
- Régies financières, cycle A (externes uniquement) : mêmes licences que la division administrative, cycle A.$t$,
  "AdmissionConditionsEn" = $t$- All divisions: general conditions for public employment; aged 17 to 32 on 1 January 2026 (up to 5 years' waiver for holders of a national disability card). Fee: 25,000 FCFA.
- Administrative division, cycle A (external): degree (general or professional) in law, economics, social sciences or management obtained in Cameroon, or equivalent. Internal: category B civil servants of the corps listed in the order, 5 years in grade, at most 45.
- Administrative division, cycle B: Baccalauréat, GCE A Level in at least 2 subjects (Religious Knowledge excluded) at the same sitting, law certificate (capacité en droit) or equivalent.
- Trainee magistrates (auditeurs de justice): Master I or maîtrise in private or public law from a Cameroonian university, or equivalent foreign degree.
- Financial services, cycle A (external only): same degrees as the administrative division, cycle A.$t$,
  "Tracks" = $t$- Division administrative, cycle A (70 places) : administration générale (40), administration du travail (20), affaires sociales (10).
- Division administrative, cycle B (50 places) : administration générale (30), travail (10), affaires sociales (10).
- Auditeurs de justice (40 places) : section judiciaire (20), section administrative (20).
- Régies financières, cycle A (20 places) : comptabilité-matières (10), prix, poids et mesures (10).
- Régies financières cycle B (20), administrateurs des greffes (10), élèves-greffiers (10). Total : 220 places.$t$,
  "TracksEn" = $t$- Administrative division, cycle A (70 places): general administration (40), labour administration (20), social affairs (10).
- Administrative division, cycle B (50 places): general administration (30), labour (10), social affairs (10).
- Trainee magistrates (40 places): judicial section (20), administrative section (20).
- Financial services, cycle A (20 places): materials accounting (10), prices, weights and measures (10).
- Financial services cycle B (20), registry administrators (10), trainee court clerks (10). Total: 220 places.$t$,
  "Coefficients" = $t$Écrits au centre unique de Yaoundé, 4 h chacun.
- Division administrative, cycles A et B : culture générale (coef. 4), droit constitutionnel (coef. 3), économie générale (coef. 2), spécialité (coef. 5 : droit administratif, droit du travail ou psychologie selon la section).
- Régies financières, cycle A : culture générale (coef. 4), droit constitutionnel (coef. 2), économie générale (coef. 3), spécialité (coef. 5 : comptabilité générale ou statistiques et économétrie).
- Auditeurs de justice : culture générale (coef. 2) et trois épreuves de spécialité (coef. 4, 5 et 3) selon la section.
- Oraux d'admission : grand oral (coef. 1) et oral de langue (coef. 1).
- Écrits : auditeurs 1er et 2 août ; cycle B 5 et 6 septembre ; cycle A administratif 12 et 13 septembre ; régies financières A 19 et 20 septembre 2026.$t$,
  "CoefficientsEn" = $t$Written papers at the single Yaoundé centre, 4 hours each.
- Administrative division, cycles A and B: general knowledge (weighting 4), constitutional law (3), general economics (2), specialty (5: administrative law, labour law or psychology depending on the section).
- Financial services, cycle A: general knowledge (4), constitutional law (2), general economics (3), specialty (5: general accounting or statistics and econometrics).
- Trainee magistrates: general knowledge (2) and three specialty papers (4, 5 and 3) depending on the section.
- Admission orals: main oral (1) and language oral (1).
- Written papers: magistrates 1 and 2 August; cycle B 5 and 6 September; administrative cycle A 12 and 13 September; financial services A 19 and 20 September 2026.$t$,
  "Programme" = $t$Programme officiel en annexe de l'arrêté (division administrative, cycle A) : culture générale (littérature, philosophie, histoire contemporaine, sciences et technologies, arts et culture, genre et développement, intégration régionale, géopolitique, TIC) ; droit constitutionnel et institutions politiques ; droit administratif ; économie (micro, macro, monétaire, internationale, population et développement, économie numérique) ; droit du travail ; psychologie. Le grand oral est un entretien de personnalité ; l'oral de langue se passe en anglais pour les francophones et en français pour les anglophones.$t$,
  "ProgrammeEn" = $t$Official syllabus annexed to the order (administrative division, cycle A): general knowledge (literature, philosophy, contemporary history, science and technology, arts and culture, gender and development, regional integration, geopolitics, ICT); constitutional law and political institutions; administrative law; economics (micro, macro, monetary, international, population and development, digital economy); labour law; psychology. The main oral is a personality interview; the language oral is in English for French speakers and in French for English speakers.$t$,
  "SourceUrl" = $t$https://www.minfopra.gov.cm/index.php/fr/publications/2363-concours-administratifs-la-saison-2026-est-ouverte
https://www.minfopra.gov.cm/images/2026/DDRHE/CONCOURS/ENAM/70_Elves_Div_Administrative_A_Fr.pdf
https://www.minfopra.gov.cm/images/2026/DDRHE/CONCOURS/ENAM/50_Elves_Div_Administrative_B_Fr.pdf
https://www.minfopra.gov.cm/images/2026/DDRHE/CONCOURS/ENAM/40_Auditeurs_de_Justice_DMG_Fr.pdf
https://www.minfopra.gov.cm/images/2026/DDRHE/CONCOURS/ENAM/20_Elves_Div_RF_A_Fr.pdf$t$,
  "SourceCheckedAt" = '2026-10-20 12:00:00+00',
  "UpdatedAt" = now()
WHERE "Id" = 3 AND "Slug" = 'enam' AND "Year" = 2026
  AND "SourceUrl" IS NULL;

-- ── Examen national d'aptitude à la formation médicale (arrêté 26/00769/MINESUP du 6 juillet 2026) ──
UPDATE "ConcoursEvents" SET
  "AdmissionConditions" = $t$- Camerounais des deux sexes, en session unique ; étrangers admis dans la limite des places.
- 23 ans au plus en 2026.
- BAC général série C ou D, ou GCE A Level avec Biology et Chemistry à la même session, ou diplôme admis en équivalence.
- Un seul établissement et une seule filière par candidat.
- Inscription uniquement en ligne (concoursdauq.com), frais de 20 000 FCFA, dossiers au plus tard le 21 août 2026.$t$,
  "AdmissionConditionsEn" = $t$- Cameroonians of both sexes, single sitting; foreigners admitted within the places available.
- At most 23 years old in 2026.
- General Baccalauréat series C or D, or GCE A Level with Biology and Chemistry at the same sitting, or a recognised equivalent.
- One institution and one programme per candidate.
- Online registration only (concoursdauq.com), fee 20,000 FCFA, files by 21 August 2026.$t$,
  "Tracks" = $t$1 140 places : médecine (770), pharmacie (250), odontostomatologie (120).
- FMSB Yaoundé I : 170 / 30 / 60
- FMSP Douala : 100 / 60 / 0
- FHS Buea : 80 / 20 / 0
- FHS Bamenda : 70 / 20 / 0
- FMSP Dschang : 60 / 25 / 0
- FMSB Garoua : 70 / 0 / 0
- FMSP Sangmélima (Université d'Ebolowa) : 70 / 30 / 0
- SHMS CATUC Kumbo : 50 / 0 / 0
- ISSS Université des Montagnes : 100 / 65 / 60
(places en médecine / pharmacie / odontostomatologie)$t$,
  "TracksEn" = $t$1,140 places: medicine (770), pharmacy (250), dentistry (120).
- FMSB Yaoundé I: 170 / 30 / 60
- FMSP Douala: 100 / 60 / 0
- FHS Buea: 80 / 20 / 0
- FHS Bamenda: 70 / 20 / 0
- FMSP Dschang: 60 / 25 / 0
- FMSB Garoua: 70 / 0 / 0
- FMSP Sangmélima (University of Ebolowa): 70 / 30 / 0
- SHMS CATUC Kumbo: 50 / 0 / 0
- ISSS Université des Montagnes: 100 / 65 / 60
(places in medicine / pharmacy / dentistry)$t$,
  "Coefficients" = $t$- Étude du dossier scolaire (Probatoire ou GCE O/L, BAC ou GCE A/L, âge) : coefficient 1.
- Écrit de biologie, chimie et physique (3 h au total) : coefficient 4. Poids internes : médecine biologie 2, physique 1, chimie 1 ; pharmacie biologie 1, physique 1, chimie 2 ; odontostomatologie biologie 2, physique 1, chimie 1.
- Écrit de culture générale (connaissances générales, logique, français ou anglais) : coefficient 1.
- Épreuves notées sur 100 ; une note inférieure à 20 est éliminatoire.
- Épreuves le vendredi 4 septembre 2026.$t$,
  "CoefficientsEn" = $t$- School record (Probatoire or GCE O/L, Baccalauréat or GCE A/L, age): weighting 1.
- Written biology, chemistry and physics paper (3 hours in total): weighting 4. Internal weights: medicine biology 2, physics 1, chemistry 1; pharmacy biology 1, physics 1, chemistry 2; dentistry biology 2, physics 1, chemistry 1.
- Written general knowledge paper (general knowledge, logic, French or English): weighting 1.
- Papers marked out of 100; a mark below 20 is eliminatory.
- Papers on Friday 4 September 2026.$t$,
  "Programme" = $t$Programme du BAC C ou D, ou du GCE A/L, en biologie, chimie et physique.$t$,
  "ProgrammeEn" = $t$Baccalauréat C or D, or GCE A/L, syllabus in biology, chemistry and physics.$t$,
  "ExamDate" = coalesce("ExamDate", '2026-09-04 12:00:00+00'),
  "SourceUrl" = $t$https://www.minesup.gov.cm/wp-content/uploads/2026/07/Examen_Nationale_Aptitude_Formation_Medicale_Pharmaceutique_Odontostomatologique_Cameroun.pdf
https://www.minesup.gov.cm/wp-content/uploads/2026/07/National-aptitude-examination-for-medical-training-2026-2027.pdf
https://concoursdauq.com$t$,
  "SourceCheckedAt" = '2026-10-20 12:00:00+00',
  "UpdatedAt" = now()
WHERE "Id" = 4 AND "Slug" = 'fmsb' AND "Year" = 2026
  AND "SourceUrl" IS NULL;

-- ── ESSEC Douala, concours 2026 (décisions 18260271 et 18260272/MINESUP du 3 juin 2026) ──
UPDATE "ConcoursEvents" SET
  "AdmissionConditions" = $t$**Filière ESC (après le BAC)**
- BAC A, B, C, D, TI ou G, ou GCE A/L avec mathématiques ou des matières d'économie et de gestion.
- Formation à plein temps, réservée aux candidats sans activité professionnelle ; autorisation du MINFOPRA pour les agents de l'État.
- Élèves de terminale admis à concourir sous réserve.
- Frais : 20 000 FCFA. 10 % des places réservées aux ressortissants CEMAC sur titre.

**Filière EPA**
- Licence (ou Bachelor) ou équivalent ; étudiants de 3e année de licence admis sous réserve.
- Plein temps, sans emploi ; frais : 25 000 FCFA.$t$,
  "AdmissionConditionsEn" = $t$**ESC programme (after the Baccalauréat)**
- Baccalauréat A, B, C, D, TI or G, or GCE A/L with mathematics or economics and management subjects.
- Full-time study, only for candidates without employment; MINFOPRA authorisation for State employees.
- Final-year students may apply provisionally.
- Fee: 20,000 FCFA. 10% of places reserved for CEMAC nationals, admitted on qualifications.

**EPA programme**
- Licence (or Bachelor's degree) or equivalent; third-year undergraduates may apply provisionally.
- Full-time, not employed; fee: 25,000 FCFA.$t$,
  "Tracks" = $t$- ESC : 450 places en 1re année.
- EPA : 300 places : entrepreneuriat (30), vente et négociation (35), finance et comptabilité (65), politique sociale de l'entreprise (50), QHSE (50), projets (40), communication (30).$t$,
  "TracksEn" = $t$- ESC: 450 first-year places.
- EPA: 300 places: entrepreneurship (30), sales and negotiation (35), finance and accounting (65), corporate social policy (50), QHSE (50), project management (40), communication (30).$t$,
  "Coefficients" = $t$- ESC : écrit seul, en français ou en anglais ; trois épreuves d'égale importance de 2 h : mathématiques générales, mathématiques spécialisées, dissertation économique. Concours le 25 août 2026.
- EPA : deux épreuves d'égale importance de 2 h : dissertation économique et épreuve de spécialité de l'option. Concours le 26 août 2026.$t$,
  "CoefficientsEn" = $t$- ESC: written only, in French or English; three papers of equal weight, 2 hours each: general mathematics, specialised mathematics, economics essay. Examination on 25 August 2026.
- EPA: two papers of equal weight, 2 hours each: economics essay and a specialty paper for the option. Examination on 26 August 2026.$t$,
  "Programme" = $t$- Mathématiques générales : programmes des terminales scientifiques.
- Mathématiques spécialisées : probabilités et mathématiques financières (terminales générales et technique commerciale).
- Dissertation économique : niveau terminale, sur l'actualité économique des entreprises, l'actualité nationale et internationale, l'histoire économique récente ou la géographie économique.$t$,
  "ProgrammeEn" = $t$- General mathematics: science Terminale syllabuses.
- Specialised mathematics: probability and financial mathematics (general and commercial Terminale).
- Economics essay: Terminale level, on business news, national and international affairs, recent economic history or economic geography.$t$,
  "SourceUrl" = $t$https://www.minesup.gov.cm/wp-content/uploads/2026/06/ESC_L1_ESSEC_Douala.pdf
https://www.minesup.gov.cm/wp-content/uploads/2026/06/EPA_L1_ESSEC_Douala.pdf
https://www.essec-dla.com/concours/$t$,
  "SourceCheckedAt" = '2026-10-20 12:00:00+00',
  "UpdatedAt" = now()
WHERE "Id" = 5 AND "Slug" = 'essec' AND "Year" = 2026
  AND "SourceUrl" IS NULL;

-- ── ENSET Douala, session 2026 (arrêté 26/00036/MINESUP du 13 janvier 2026) ──
-- Omis : nombre de places par option et liste détaillée des séries de BAC par division
-- (les versions française et anglaise de l'arrêté diffèrent) ; le site enset-douala.cm
-- n'est pas cité (contenu sans rapport affiché, site probablement compromis).
UPDATE "ConcoursEvents" SET
  "AdmissionConditions" = $t$- Camerounais des deux sexes ; étrangers admis sur titre ou sur concours, dans la limite des places.
- 1re année du 1er cycle : 29 ans au plus au 1er janvier 2026 ; BAC, GCE A Level ou BT des séries prévues par l'arrêté pour chaque division (les brevets professionnels ne sont pas acceptés).
- 1re année du 2nd cycle : 32 ans au plus au 1er janvier 2026 ; licence correspondant à la filière (licences professionnelles correspondantes acceptées).
- Les fonctionnaires autorisés par leur administration ne sont pas concernés par la limite d'âge.
- Frais : 20 000 FCFA (BICEC) ; certificat médical d'aptitude à l'enseignement exigé.$t$,
  "AdmissionConditionsEn" = $t$- Cameroonians of both sexes; foreigners admitted on qualifications or by examination, within the places available.
- First year, first cycle: at most 29 years old on 1 January 2026; Baccalauréat, GCE A Level or BT in the series set by the order for each division (professional certificates not accepted).
- First year, second cycle: at most 32 years old on 1 January 2026; degree matching the programme (matching professional degrees accepted).
- Civil servants authorised by their administration are exempt from the age limit.
- Fee: 20,000 FCFA (BICEC); medical certificate of fitness to teach required.$t$,
  "Tracks" = $t$Génie civil (bâtiments et travaux publics) ; génie électrique (électrotechnique) ; génie mécanique (construction et fabrication mécaniques) ; génie forestier (métier du bois, exploitation forestière) ; génie informatique (informatique industrielle, technologies de l'information et de la communication) ; sciences de l'éducation (conseillers d'orientation, 2nd cycle) ; techniques administratives (communication administrative) ; division des sciences et technologies du tertiaire.$t$,
  "TracksEn" = $t$Civil engineering (building and public works); electrical engineering (electrotechnics); mechanical engineering (mechanical construction and manufacturing); forestry engineering (woodwork, forest exploitation); computer engineering (industrial computing, information and communication technology); education sciences (guidance counsellors, second cycle); administrative techniques (administrative communication); tertiary sciences and technologies division.$t$,
  "Coefficients" = $t$- 1re épreuve (épreuve de spécialité ou étude de cas selon la série) : 4 h, coefficient 5.
- 2e épreuve (mathématiques, mathématiques financières ou langue selon la série) : 3 h, coefficient 4.
- Même structure au 2nd cycle (spécialité 4 h coef. 5 ; mathématiques ou culture économique 3 h coef. 4).
- Écrits le samedi 14 mars 2026 (niveau BAC/GCE et BT) et le dimanche 15 mars 2026 (niveau licence).$t$,
  "CoefficientsEn" = $t$- Paper 1 (specialty paper or case study depending on the series): 4 hours, weighting 5.
- Paper 2 (mathematics, financial mathematics or language depending on the series): 3 hours, weighting 4.
- Same structure for the second cycle (specialty 4 h weighting 5; mathematics or economic culture 3 h weighting 4).
- Written papers on Saturday 14 March 2026 (Baccalauréat/GCE and BT level) and Sunday 15 March 2026 (degree level).$t$,
  "Programme" = $t$- 1er cycle : programmes de Terminale de la série du candidat (par exemple mathématiques de Terminale C ou F, physique de Terminale C, spécialité de la série technique).
- 2nd cycle : programmes de la licence de la spécialité.$t$,
  "ProgrammeEn" = $t$- First cycle: Terminale syllabus of the candidate's series (for example Terminale C or F mathematics, Terminale C physics, the technical series specialty).
- Second cycle: degree syllabus of the specialty.$t$,
  "SourceUrl" = $t$https://www.minesup.gov.cm/wp-content/uploads/2026/01/Concours-ENSET-Douala-2026_1er-et-2nd-cycle.pdf
https://www.minesup.gov.cm/index.php/2026/01/13/concours-dentree-dans-les-ens-et-enset-au-titre-de-lannee-academique-2025-2026/$t$,
  "SourceCheckedAt" = '2026-10-20 12:00:00+00',
  "UpdatedAt" = now()
WHERE "Id" = 6 AND "Slug" = 'enset' AND "Year" = 2026
  AND "SourceUrl" IS NULL;

-- ── Baccalauréat ESG, session 2026 (OBC, horaires, durées et coefficients du 18 mars 2026) ──
-- Saisies : séries C et D (lues dans le document) ; autres séries : voir le document OBC.
UPDATE "ConcoursEvents" SET
  "Tracks" = $t$Séries de l'enseignement secondaire général : A1, A2, A3, A4, A5, ABI, AC, C, D, E, TI.$t$,
  "TracksEn" = $t$General secondary education series: A1, A2, A3, A4, A5, ABI, AC, C, D, E, TI.$t$,
  "Coefficients" = $t$**Série C** : mathématiques 4 h coef. 7 ; physique théorique 4 h coef. 3 ; physique pratique 1 h coef. 1 ; chimie théorique 3 h coef. 1,5 ; chimie pratique 1 h coef. 0,5 ; SVTEEHB 2 h coef. 2 ; anglais 2 h coef. 3 ; littérature ou culture générale 3 h coef. 2 ; philosophie 2 h coef. 2 ; langue française 2 h coef. 1 ; informatique 2 h coef. 2 ; éducation à la citoyenneté et à la morale 2 h coef. 2 ; géographie 2 h coef. 1 ; EPS théorique (inaptes) 2 h coef. 2.

**Série D** : mathématiques 4 h coef. 4 ; SVTEEHB théorique 4 h coef. 4 ; SVTEEHB pratique 2 h coef. 2 ; physique théorique 3 h coef. 2 ; chimie théorique 3 h coef. 1,5 ; chimie pratique 1 h coef. 0,5 ; anglais 2 h coef. 3 ; littérature ou culture générale 3 h coef. 2 ; philosophie 2 h coef. 2 ; langue française 2 h coef. 1 ; informatique 2 h coef. 2 ; éducation à la citoyenneté et à la morale 2 h coef. 2 ; géographie 2 h coef. 1 ; EPS théorique (inaptes) 2 h coef. 2.

Le candidat compose en français (littérature ou culture générale et langue française) ou en philosophie. Un candidat sans note d'EPS est déclaré refusé. Écrits à partir du 25 mai 2026. Autres séries : document de l'OBC.$t$,
  "CoefficientsEn" = $t$**Series C**: mathematics 4 h weighting 7; theoretical physics 4 h weighting 3; practical physics 1 h weighting 1; theoretical chemistry 3 h weighting 1.5; practical chemistry 1 h weighting 0.5; life and earth sciences (SVTEEHB) 2 h weighting 2; English 2 h weighting 3; literature or general knowledge 3 h weighting 2; philosophy 2 h weighting 2; French language 2 h weighting 1; computer science 2 h weighting 2; citizenship and moral education 2 h weighting 2; geography 2 h weighting 1; theoretical PE (unfit candidates) 2 h weighting 2.

**Series D**: mathematics 4 h weighting 4; theoretical SVTEEHB 4 h weighting 4; practical SVTEEHB 2 h weighting 2; theoretical physics 3 h weighting 2; theoretical chemistry 3 h weighting 1.5; practical chemistry 1 h weighting 0.5; English 2 h weighting 3; literature or general knowledge 3 h weighting 2; philosophy 2 h weighting 2; French language 2 h weighting 1; computer science 2 h weighting 2; citizenship and moral education 2 h weighting 2; geography 2 h weighting 1; theoretical PE (unfit candidates) 2 h weighting 2.

Candidates sit French (literature or general knowledge and French language) or philosophy. A candidate with no PE mark fails. Written papers from 25 May 2026. Other series: OBC document.$t$,
  "SourceUrl" = $t$https://officedubac.cm/wp-content/uploads/2026/04/HORAIRES-DE-PASSAGE-BAGN-2026.pdf
https://officedubac.cm/examen-esg/$t$,
  "SourceCheckedAt" = '2026-10-20 12:00:00+00',
  "UpdatedAt" = now()
WHERE "Id" = 7 AND "Slug" = 'bac' AND "Year" = 2026
  AND "SourceUrl" IS NULL;

-- ── Probatoire ESG, session 2026 (OBC, horaires, durées et coefficients du 18 mars 2026) ──
-- Saisies : séries C, D et TI ; autres séries : voir le document OBC.
UPDATE "ConcoursEvents" SET
  "Coefficients" = $t$**Série C** : mathématiques 3 h coef. 6 ; physique 3 h coef. 4 ; chimie 2 h coef. 2 ; SVTEEHB 2 h coef. 2 ; anglais 2 h coef. 3 ; littérature ou culture générale 3 h coef. 2 ; français 2 h coef. 1 ; histoire 2 h coef. 1 ; philosophie 2 h coef. 1 ; informatique 2 h coef. 2 ; ECM 2 h coef. 2 ; EPS théorique (inaptes) 2 h coef. 2.

**Série D** : comme la série C, sauf mathématiques 3 h coef. 4, physique 2 h coef. 2, SVTEEHB 4 h coef. 6.

**Série TI** : algorithme et programmation 3 h coef. 3 ; systèmes d'information 2 h coef. 3 ; maintenance et multimédia 2 h coef. 2 ; informatique pratique 1 h coef. 2 ; mathématiques 3 h coef. 4 ; physique 2 h coef. 2 ; chimie 1 h coef. 1 ; SVTEEHB 2 h coef. 2 ; anglais 2 h coef. 3 ; littérature 3 h coef. 2 ; français 2 h coef. 1 ; histoire 2 h coef. 1 ; philosophie 2 h coef. 1 ; ECM 2 h coef. 2 ; EPS 2 h coef. 2.

Un candidat sans note d'EPS est déclaré refusé. Écrits du 8 au 12 juin 2026. Autres séries : document de l'OBC.$t$,
  "CoefficientsEn" = $t$**Series C**: mathematics 3 h weighting 6; physics 3 h weighting 4; chemistry 2 h weighting 2; SVTEEHB 2 h weighting 2; English 2 h weighting 3; literature or general knowledge 3 h weighting 2; French 2 h weighting 1; history 2 h weighting 1; philosophy 2 h weighting 1; computer science 2 h weighting 2; citizenship (ECM) 2 h weighting 2; theoretical PE (unfit candidates) 2 h weighting 2.

**Series D**: as series C, except mathematics 3 h weighting 4, physics 2 h weighting 2, SVTEEHB 4 h weighting 6.

**Series TI**: algorithms and programming 3 h weighting 3; information systems 2 h weighting 3; maintenance and multimedia 2 h weighting 2; practical computing 1 h weighting 2; mathematics 3 h weighting 4; physics 2 h weighting 2; chemistry 1 h weighting 1; SVTEEHB 2 h weighting 2; English 2 h weighting 3; literature 3 h weighting 2; French 2 h weighting 1; history 2 h weighting 1; philosophy 2 h weighting 1; citizenship (ECM) 2 h weighting 2; PE 2 h weighting 2.

A candidate with no PE mark fails. Written papers from 8 to 12 June 2026. Other series: OBC document.$t$,
  "ExamDate" = coalesce("ExamDate", '2026-06-08 12:00:00+00'),
  "SourceUrl" = $t$https://officedubac.cm/wp-content/uploads/2026/04/HORAIRE-DE-PASSAGE-PBGN-2026.pdf
https://officedubac.cm/examen-esg/$t$,
  "SourceCheckedAt" = '2026-10-20 12:00:00+00',
  "UpdatedAt" = now()
WHERE "Id" = 9 AND "Slug" = 'probatoire' AND "Year" = 2026
  AND "SourceUrl" IS NULL;

-- ── Fiches 2027 (BAC, Probatoire) : dates d'inscription déjà saisies, source ajoutée ──
UPDATE "ConcoursEvents" SET
  "SourceUrl" = $t$https://officedubac.cm
https://epim-exam.cm$t$,
  "SourceCheckedAt" = '2026-10-20 12:00:00+00',
  "UpdatedAt" = now()
WHERE "Slug" IN ('bac', 'probatoire') AND "Year" = 2027 AND "SourceUrl" IS NULL;

-- ── FASA, IBAF, IUT : concours 2026 sans fiche jusqu'ici (INSERT gardés) ──
-- FASA (décision 18260292/MINESUP du 9 juin 2026). Omis : date du concours (28 août en
-- français, 26 août en anglais dans le même texte) ; aucun coefficient n'est publié.
INSERT INTO "ConcoursEvents" ("Slug","Name","Year","RegistrationStartDate","RegistrationEndDate","ExamDate","ResultsDate","Location","EnrollmentFeeXaf","OfficialRegistrationUrl","Notes",
  "AdmissionConditions","AdmissionConditionsEn","Tracks","TracksEn","Coefficients","CoefficientsEn","Programme","ProgrammeEn","SourceUrl","SourceCheckedAt","IsPublished","CreatedAt","UpdatedAt")
SELECT 'fasa', 'FASA, Université de Dschang (cycle des ingénieurs agronomes)', 2026, NULL, '2026-08-25 12:00:00+00', NULL, NULL,
  'Bafia, Bamenda, Douala, Dschang, Maroua, Ngaoundéré, Yaoundé', NULL, 'http://sigesonline.univ-dschang.org',
  'Pré-inscription sur sigesonline, dossier au plus tard le 25 août 2026 à 15 h 30. Seule la décision du MINESUP fait foi.',
  $t$- BAC A, C ou D, GCE A Level dans au moins deux matières scientifiques, BAC agricole ou diplôme équivalent.
- Camerounais des deux sexes et ressortissants des pays CEMAC concernés par le traitement national des étudiants.
- Aucune limite d'âge n'est fixée par la décision.
- Frais : 20 000 FCFA, non remboursables.$t$,
  $t$- Baccalauréat A, C or D, GCE A Level in at least two science subjects, agricultural Baccalauréat or equivalent.
- Cameroonians of both sexes and nationals of the CEMAC countries covered by national treatment of students.
- The decision sets no age limit.
- Fee: 20,000 FCFA, non-refundable.$t$,
  $t$Agronomie et foresterie, 1re année du cycle des ingénieurs : 230 places à Dschang, 100 à Bafia (concours commun avec les IPES sous tutelle de la FASA).$t$,
  $t$Agronomy and forestry, first year of the engineering cycle: 230 places in Dschang, 100 in Bafia (joint examination with the private institutions supervised by FASA).$t$,
  $t$Quatre écrits d'une heure chacun : sciences naturelles, mathématiques, chimie, culture générale. Pas d'oral. Aucun coefficient n'est publié par la décision.$t$,
  $t$Four written papers of one hour each: natural sciences, mathematics, chemistry, general knowledge. No oral. The decision publishes no weightings.$t$,
  $t$Programmes des classes de terminale et du GCE A Level.$t$,
  $t$Terminale and GCE A Level syllabuses.$t$,
  $t$https://www.minesup.gov.cm/wp-content/uploads/2026/06/FASA_L1_Dschang_Bafia.pdf
https://www.minesup.gov.cm/index.php/2026/06/10/decisions-portant-ouverture-des-concours-a-la-fasa-dschangbafia-et-isabee-bertoua/$t$,
  '2026-10-20 12:00:00+00', true, now(), now()
WHERE NOT EXISTS (SELECT 1 FROM "ConcoursEvents" e WHERE e."Slug" = 'fasa' AND e."Year" = 2026);

-- IBAF (Institut des Beaux-Arts de Foumban, décision 18260360/MINESUP du 3 juillet 2026).
-- Omis : durée de l'épreuve pratique des arts du spectacle (3 h en français, 1 h en anglais).
INSERT INTO "ConcoursEvents" ("Slug","Name","Year","RegistrationStartDate","RegistrationEndDate","ExamDate","ResultsDate","Location","EnrollmentFeeXaf","OfficialRegistrationUrl","Notes",
  "AdmissionConditions","AdmissionConditionsEn","Tracks","TracksEn","Coefficients","CoefficientsEn","Programme","ProgrammeEn","SourceUrl","SourceCheckedAt","IsPublished","CreatedAt","UpdatedAt")
SELECT 'ibaf', 'Institut des Beaux-Arts de Foumban, Université de Dschang (1re année)', 2026, NULL, '2026-09-04 12:00:00+00', '2026-09-08 12:00:00+00', NULL,
  'Foumban, Bamenda-Bambui, Yaoundé-Nkolbisson, Dschang, Douala', NULL, 'http://sigesonline.univ-dschang.org',
  'Dossier au plus tard le 4 septembre 2026 ; épreuves les 8 et 9 septembre 2026. Seule la décision du MINESUP fait foi.',
  $t$- BAC de toutes séries, GCE A Level en au moins deux matières (hors Religious Knowledge), BT, BP ou diplôme reconnu équivalent.
- Camerounais des deux sexes ; candidats CEMAC aux mêmes conditions ; autres étrangers sur étude de dossier, dans la limite des places.
- Élèves de terminale admis sous réserve. Aucune limite d'âge n'est fixée par la décision.
- Frais : 20 000 FCFA.$t$,
  $t$- Baccalauréat in any series, GCE A Level in at least two subjects (Religious Knowledge excluded), BT, BP or recognised equivalent.
- Cameroonians of both sexes; CEMAC candidates on the same terms; other foreigners on file review, within the places available.
- Final-year students may apply provisionally. The decision sets no age limit.
- Fee: 20,000 FCFA.$t$,
  $t$260 places :
- Arts plastiques (25) : peinture et dessin, sculpture, gravure, céramique, histoire de l'art et art contemporain.
- Arts décoratifs (80) : stylisme, modélisme, accessoires de mode, arts textiles.
- Arts, technologie et patrimoine (50) : bois et fibres végétales, argiles, métaux et alliages, patrimoine et muséologie.
- Arts du spectacle (25) : danse, musique, cinéma et audiovisuel, théâtre.
- Architecture et art de l'ingénieur (80).$t$,
  $t$260 places:
- Visual arts (25): painting and drawing, sculpture, printmaking, ceramics, art history and contemporary art.
- Decorative arts (80): fashion design, pattern making, fashion accessories, textile arts.
- Arts, technology and heritage (50): wood and plant fibres, clays, metals and alloys, heritage and museology.
- Performing arts (25): dance, music, film and audiovisual, theatre.
- Architecture and engineering art (80).$t$,
  $t$Étude de dossier 20 %, écrits 60 %, pratique 20 % ; chaque épreuve est notée sur 20.
- Arts plastiques et arts décoratifs : culture générale 2 h coef. 3 ; langue vivante 2 h coef. 2 ; dessin 3 h coef. 2.
- Arts du spectacle : culture générale 2 h coef. 3 ; langue vivante 2 h coef. 2 ; épreuve pratique coef. 2.
- Arts, technologie et patrimoine : culture générale 2 h coef. 2 ; langue vivante 2 h coef. 2 ; mathématiques ou physique (QCM) 2 h coef. 1 ; dessin 3 h coef. 2.
- Architecture et art de l'ingénieur : culture générale 2 h coef. 2 ; langue vivante 2 h coef. 1 ; mathématiques (QCM) 2 h coef. 1 ; physique (QCM) 2 h coef. 2 ; dessin 3 h coef. 2.$t$,
  $t$File review 20%, written papers 60%, practical 20%; each paper is marked out of 20.
- Visual and decorative arts: general knowledge 2 h weighting 3; modern language 2 h weighting 2; drawing 3 h weighting 2.
- Performing arts: general knowledge 2 h weighting 3; modern language 2 h weighting 2; practical test weighting 2.
- Arts, technology and heritage: general knowledge 2 h weighting 2; modern language 2 h weighting 2; mathematics or physics (MCQ) 2 h weighting 1; drawing 3 h weighting 2.
- Architecture and engineering art: general knowledge 2 h weighting 2; modern language 2 h weighting 1; mathematics (MCQ) 2 h weighting 1; physics (MCQ) 2 h weighting 2; drawing 3 h weighting 2.$t$,
  $t$Programmes des BAC A, B, C, D, F et G pour les francophones, et du GCE A Level pour les anglophones.$t$,
  $t$Baccalauréat A, B, C, D, F and G syllabuses for French-speaking candidates, GCE A Level for English-speaking candidates.$t$,
  $t$https://www.minesup.gov.cm/wp-content/uploads/2026/07/Universite_Dschang_IBA_UDs_Foumban_L1.pdf
https://www.minesup.gov.cm/index.php/2026/07/03/decisions-portant-ouverture-des-concours-a-la-fsmb-au-departement-dagriculture-durable-et-gestion-des-catastrophes-de-luniversite-de-garoua-et-a-liba-uds-de-foumban/$t$,
  '2026-10-20 12:00:00+00', true, now(), now()
WHERE NOT EXISTS (SELECT 1 FROM "ConcoursEvents" e WHERE e."Slug" = 'ibaf' AND e."Year" = 2026);

-- IUT de Douala, 1re année DUT (décision 18260151/MINESUP du 16 avril 2026).
INSERT INTO "ConcoursEvents" ("Slug","Name","Year","RegistrationStartDate","RegistrationEndDate","ExamDate","ResultsDate","Location","EnrollmentFeeXaf","OfficialRegistrationUrl","Notes",
  "AdmissionConditions","AdmissionConditionsEn","Tracks","TracksEn","Coefficients","CoefficientsEn","Programme","ProgrammeEn","SourceUrl","SourceCheckedAt","IsPublished","CreatedAt","UpdatedAt")
SELECT 'iut', 'IUT de Douala (1re année, DUT)', 2026, NULL, '2026-07-03 12:00:00+00', '2026-07-07 12:00:00+00', NULL,
  'Bafoussam, Bamenda, Bertoua, Buéa, Douala, Ebolowa, Edéa, Nkongsamba, Yaoundé', NULL, 'https://www.iut-douala.cm',
  'Fiche à remplir en ligne sur iut-douala.cm, dossier au plus tard le 3 juillet 2026 ; concours le 7 juillet 2026. Seule la décision du MINESUP fait foi.',
  $t$- Camerounais des deux sexes et étrangers, 29 ans au plus au 1er janvier 2026.
- Diplômes selon la spécialité : GAPMO : BAC A, C, D, E, CG, FIG, ACC, SES ou GCE A/L ; GLT et HT : BAC C, D, E, CG, FIG, SES ou GCE A/L ; OGA : BAC A, ACA, ACC ou GCE A/L ; mention MTIN : BAC C, D, E, F2, F3, MHB, TI ou GCE A/L ; mention MTI : BAC C, D, E, F1 à F7, BT ou GCE A/L.
- Frais : 20 000 FCFA.$t$,
  $t$- Cameroonians of both sexes and foreigners, at most 29 years old on 1 January 2026.
- Qualifications by specialty: GAPMO: Baccalauréat A, C, D, E, CG, FIG, ACC, SES or GCE A/L; GLT and HT: Baccalauréat C, D, E, CG, FIG, SES or GCE A/L; OGA: Baccalauréat A, ACA, ACC or GCE A/L; MTIN: Baccalauréat C, D, E, F2, F3, MHB, TI or GCE A/L; MTI: Baccalauréat C, D, E, F1 to F7, BT or GCE A/L.
- Fee: 20,000 FCFA.$t$,
  $t$1 163 places (Douala 863, site d'Edéa 300).
- Douala, MTT : GAPMO 100, GLT 100, OGA 30 ; MTIN : GEII 90, GBM 48, GI 100, GRT 60 ; MTI : GIM 60, GMP 60, GTE 30, GME 20, GMI 25, GC 120, MET 20.
- Edéa, MTT : GAPMO 40, GLT 40, HT 20 ; MTIN : GEII 30, GI 50 ; MTI : GMP 30, GC 70, TP 20.$t$,
  $t$1,163 places (Douala 863, Edéa site 300).
- Douala, MTT: GAPMO 100, GLT 100, OGA 30; MTIN: GEII 90, GBM 48, GI 100, GRT 60; MTI: GIM 60, GMP 60, GTE 30, GME 20, GMI 25, GC 120, MET 20.
- Edéa, MTT: GAPMO 40, GLT 40, HT 20; MTIN: GEII 30, GI 50; MTI: GMP 30, GC 70, TP 20.$t$,
  $t$Pour chaque spécialité : culture générale 2 h coef. 2 ; mathématiques 2 h coef. 3 ; épreuve de spécialité 2 h coef. 3.$t$,
  $t$For each specialty: general knowledge 2 h weighting 2; mathematics 2 h weighting 3; specialty paper 2 h weighting 3.$t$,
  $t$Programmes des classes de terminale préparant aux diplômes exigés.$t$,
  $t$Terminale syllabuses leading to the required qualifications.$t$,
  $t$https://www.minesup.gov.cm/wp-content/uploads/2026/04/IUT_UIT_EDEA_Douala.pdf
https://www.minesup.gov.cm/index.php/2026/04/20/decision-portant-ouverture-de-differents-concours-et-recrutements-au-titre-de-lannee-academique-2026-2027/$t$,
  '2026-10-20 12:00:00+00', true, now(), now()
WHERE NOT EXISTS (SELECT 1 FROM "ConcoursEvents" e WHERE e."Slug" = 'iut' AND e."Year" = 2026);

COMMIT;

-- Contrôle : remplissage des contenus par fiche.
SELECT "Id" || ' | ' || "Slug" || ' ' || "Year" || ' | publié=' || "IsPublished"
       || ' | conditions=' || ("AdmissionConditions" IS NOT NULL) || ' filières=' || ("Tracks" IS NOT NULL)
       || ' coefficients=' || ("Coefficients" IS NOT NULL) || ' programme=' || ("Programme" IS NOT NULL)
       || ' | en=' || ("AdmissionConditionsEn" IS NOT NULL OR "CoefficientsEn" IS NOT NULL)
       || ' | source=' || coalesce(split_part("SourceUrl", E'\n', 1), 'MANQUANTE') || ' (' || coalesce("SourceCheckedAt"::date::text, '-') || ')'
FROM "ConcoursEvents" ORDER BY "Slug", "Year";
