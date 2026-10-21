namespace Backend.Services.Translation;

/// <summary>
/// Vocabulaire imposé à la traduction automatique : celui que le traducteur du
/// propriétaire a validé dans frontend/src/i18n/locales/en/*.json (lot 10.6).
/// Les libellés de taxonomie (table ContentTranslations) s'y ajoutent à chaque
/// lot : seuls les termes présents dans les textes du lot sont envoyés.
///
/// Changer une entrée change <see cref="Version"/> : le cache des traductions
/// est alors ignoré pour les nouveaux textes (les traductions déjà enregistrées
/// ne sont pas refaites d'office).
/// </summary>
public static class ContentGlossary
{
    public const string Version = "g2";

    public static readonly IReadOnlyList<(string Fr, string En)> Terms = new List<(string, string)>
    {
        ("épreuve", "paper"),
        ("épreuves", "papers"),
        ("annales", "past papers"),
        ("ancienne épreuve", "past paper"),
        ("corrigé", "worked solution"),
        ("corrigés", "worked solutions"),
        ("corrigé type", "answer key"),
        ("barème", "marking scheme"),
        ("examen blanc", "mock exam"),
        ("concours", "entrance exam"),
        ("concours d'entrée", "entrance exam"),
        ("bulletin", "report card"),
        ("bulletins", "report cards"),
        ("cahier de textes", "Class logbook"),
        ("formation", "course"),
        ("formations", "courses"),
        ("leçon", "lesson"),
        ("chapitre", "chapter"),
        ("répétiteur", "tutor"),
        ("livre", "book"),
        ("manuel", "textbook"),
        ("matière", "subject"),
        ("série", "series"),
        ("Série A", "Series A (Arts)"),
        ("Série C", "Series C (Maths and Physics)"),
        ("Série D", "Series D (Life Sciences and Maths)"),
        ("sous-système", "subsystem"),
        ("cycle", "cycle"),
        ("Baccalauréat", "Baccalaureate"),
        ("Probatoire", "Probatoire"),
        ("BEPC", "BEPC"),
        // Lot 10.6 : classes francophones rendues par leur équivalent du
        // sous-système anglophone, à année d'études égale (MINESEC : 4 + 3
        // ans côté francophone, 5 + 2 ans côté anglophone).
        ("Terminale", "Upper Sixth"),
        ("Première", "Lower Sixth"),
        ("Seconde", "Form 5"),
        ("Troisième", "Form 4"),
        ("Quatrième", "Form 3"),
        ("Cinquième", "Form 2"),
        ("Sixième", "Form 1"),
        ("6ème", "Form 1"),
        ("5ème", "Form 2"),
        ("4ème", "Form 3"),
        ("3ème", "Form 4"),
        ("SIL", "Class 1"),
        ("CP", "Class 2"),
        ("CE1", "Class 3"),
        ("CE2", "Class 4"),
        ("CM1", "Class 5"),
        ("CM2", "Class 6"),
        ("collège", "lower secondary school"),
        ("lycée", "secondary school"),
        ("enseignement supérieur", "higher education"),
        ("Culture générale", "General knowledge"),
        ("SVT", "Life and earth sciences"),
        ("Sciences naturelles", "Natural sciences"),
        ("Génie civil", "Civil engineering"),
        ("Informatique", "Computer science"),
        ("WinPlus", "WinPlus"),
        ("WinAI", "WinAI"),
    };

    /// <summary>Instructions fixes envoyées avec chaque lot (le service Python les complète).</summary>
    public const string Rules =
        "Keep proper nouns, school names, acronyms (BEPC, ENS, ENSET, FMSB, IUT, ENSP, GCE…), " +
        "numbers, years, codes, formulas and URLs exactly as they are. Render the Cameroonian francophone class names " +
        "by their anglophone equivalents from the glossary (Terminale = Upper Sixth, Première = Lower Sixth, Seconde = Form 5…), " +
        "keep series letters and exam names (BEPC, Probatoire, BAC). British spelling. " +
        "Do not add or remove information.";
}
