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
    public const string Version = "g1";

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
        ("Terminale", "Terminale"),
        ("Première", "Première"),
        ("Seconde", "Seconde"),
        ("Troisième", "Troisième"),
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
        "numbers, years, codes, formulas and URLs exactly as they are. Keep the Cameroonian class names " +
        "(Terminale, Première, Seconde, Troisième) and series letters. British spelling. " +
        "Do not add or remove information.";
}
