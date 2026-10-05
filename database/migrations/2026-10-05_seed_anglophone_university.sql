-- WinPlus: seed niveaux et contenus pour le système anglophone et universitaire
-- 2026-10-05. Idempotent (ré-exécutable). Ne supprime ni ne modifie rien d'existant.
-- Les contenus sont créés NON PUBLIÉS (IsPublished = FALSE) : ce sont des fiches
-- à compléter par l'admin avec les vrais documents avant publication.
-- psql -U winplus -d winplus -f 2026-10-05_seed_anglophone_university.sql

BEGIN;

-- ─── Niveaux ─────────────────────────────────────────────────────────────────
-- Ordre à partir de 100 pour ne pas interférer avec les niveaux francophones existants.
INSERT INTO public."Levels" ("Name","DisplayName","Description","Order","IsActive","CreatedAt","UpdatedAt")
SELECT v.name, v.display, v.descr, v.ord, TRUE, NOW(), NOW()
FROM (VALUES
  ('form_1', 'Form 1', 'Anglophone secondary: Form 1 (Year 7)', 100),
  ('form_2', 'Form 2', 'Anglophone secondary: Form 2', 101),
  ('form_3', 'Form 3', 'Anglophone secondary: Form 3', 102),
  ('form_4', 'Form 4', 'Anglophone secondary: Form 4', 103),
  ('form_5', 'Form 5 (GCE O Level)', 'Final year of the first cycle: GCE Ordinary Level', 104),
  ('lower_sixth', 'Lower Sixth', 'Anglophone high school: Lower Sixth (A Level year 1)', 105),
  ('upper_sixth', 'Upper Sixth (GCE A Level)', 'Final year of the second cycle: GCE Advanced Level', 106),
  ('technical_form_5', 'Technical Form 5 (GCE O Level Tech)', 'Technical education: first cycle', 107),
  ('technical_upper_sixth', 'Technical Upper Sixth (GCE A Level Tech)', 'Technical education: second cycle', 108),
  ('university_year_1', 'University Year 1 (Level 100)', 'Bachelor''s degree: first year', 109),
  ('university_year_2', 'University Year 2 (Level 200)', 'Bachelor''s degree: second year', 110),
  ('university_year_3', 'University Year 3 (Level 300)', 'Bachelor''s degree: third year', 111),
  ('university_year_4', 'University Year 4 (Level 400)', 'Bachelor''s degree: fourth year / engineering and medical programmes', 112),
  ('bachelor', 'Bachelor''s Degree', 'Licence (3-4 years)', 113),
  ('master', 'Master''s Degree', 'Master (Bac+5)', 114),
  ('phd', 'PhD / Doctorate', 'Doctorate (Bac+8)', 115),
  ('hnd', 'HND / BTS', 'Higher National Diploma (equivalent of BTS)', 116)
) AS v(name, display, descr, ord)
WHERE NOT EXISTS (SELECT 1 FROM public."Levels" l WHERE l."Name" = v.name);

-- ─── Contenus (brouillons non publiés) ───────────────────────────────────────
INSERT INTO public."Subjects" ("Title","Description","Category","Level","Price","IsPublished","EnrollmentCount","AverageRating","TotalRatings","CreatedAt","IsDeleted","IsFeatured","DownloadCount")
SELECT v.title, v.descr, v.category, v.level, 0, FALSE, 0, 0, 0, NOW(), FALSE, FALSE, 0
FROM (VALUES
  ('English Language: GCE O Level Revision', 'Comprehension, essay writing, grammar and summary skills for the GCE O Level.', 'English Language', 'Form 5 (GCE O Level)'),
  ('Literature in English: GCE A Level Set Texts', 'Set texts analysis and essay technique for the GCE A Level.', 'Literature in English', 'Upper Sixth (GCE A Level)'),
  ('Mathematics: GCE O Level Past Questions', 'Algebra, geometry, statistics and trigonometry with worked solutions.', 'Mathematics', 'Form 5 (GCE O Level)'),
  ('Pure Mathematics: GCE A Level', 'Calculus, sequences, vectors and complex numbers.', 'Mathematics', 'Upper Sixth (GCE A Level)'),
  ('Further Mathematics: GCE A Level', 'Matrices, differential equations and proof.', 'Mathematics', 'Upper Sixth (GCE A Level)'),
  ('Physics: GCE O Level', 'Mechanics, waves, electricity and modern physics.', 'Physics', 'Form 5 (GCE O Level)'),
  ('Chemistry: GCE A Level', 'Organic, inorganic and physical chemistry.', 'Chemistry', 'Upper Sixth (GCE A Level)'),
  ('Biology: GCE O Level', 'Cell biology, genetics, ecology and human physiology.', 'Biology', 'Form 5 (GCE O Level)'),
  ('Geography: GCE O Level', 'Physical and human geography, map work.', 'Geography', 'Form 5 (GCE O Level)'),
  ('History: GCE A Level (Cameroon and the World)', 'Cameroon, Africa and world history.', 'History', 'Upper Sixth (GCE A Level)'),
  ('Economics: GCE A Level', 'Microeconomics, macroeconomics and development.', 'Economics', 'Upper Sixth (GCE A Level)'),
  ('Computer Science / ICT: GCE O Level', 'Programming basics, networks and databases.', 'ICT', 'Form 5 (GCE O Level)'),
  ('Government and Citizenship: GCE', 'Political systems, institutions and civics.', 'Government', 'Lower Sixth'),
  ('Accounting and Commerce: GCE', 'Bookkeeping, financial statements and trade.', 'Accounting', 'Upper Sixth (GCE A Level)'),
  ('Introduction to Calculus: University Year 1', 'Limits, derivatives and integrals for first-year students.', 'Mathematics', 'University Year 1 (Level 100)'),
  ('Linear Algebra: University Year 1', 'Vector spaces, matrices and linear maps.', 'Mathematics', 'University Year 1 (Level 100)'),
  ('Introduction to Programming: University Year 1', 'Algorithms and programming fundamentals.', 'Computer Science', 'University Year 1 (Level 100)'),
  ('Data Structures and Algorithms: University Year 2', 'Lists, trees, graphs and complexity.', 'Computer Science', 'University Year 2 (Level 200)'),
  ('Microeconomics: University Year 2', 'Consumer theory, firms and market structures.', 'Economics', 'University Year 2 (Level 200)'),
  ('Constitutional Law: University Year 2', 'Constitutional law and institutions (bilingual common law and civil law).', 'Law', 'University Year 2 (Level 200)'),
  ('Financial Accounting: University Year 2', 'Principles of financial reporting.', 'Accounting', 'University Year 2 (Level 200)'),
  ('Research Methods: Master''s Degree', 'Thesis design, methodology and academic writing.', 'Research Methods', 'Master''s Degree')
) AS v(title, descr, category, level)
WHERE NOT EXISTS (SELECT 1 FROM public."Subjects" s WHERE s."Title" = v.title AND s."IsDeleted" = FALSE);

COMMIT;
