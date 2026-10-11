using System.Globalization;
using System.Text;

namespace Backend.Services;

/// <summary>
/// Correspondance entre les sujets de séance saisis par l'élève (17.5, texte
/// libre : « équations du second degré », « dissertation ») et ce qu'un
/// répétiteur couvre. Volontairement simple et déterministe (pas d'appel IA
/// dans une page de recherche) :
///   - textes normalisés (minuscules, sans accents), découpés en mots de 3
///     lettres et plus, mots vides retirés, pluriel simple ramené au singulier ;
///   - un sujet correspond si l'un de ses mots apparaît dans le corpus du
///     répétiteur : matières, spécialités, titre, bio, et sujets des séances
///     qu'il a déjà données ;
///   - un petit lexique rattache les notions courantes du programme à leur
///     matière (« dérivée » → mathématiques), pour qu'un sujet précis trouve
///     un répétiteur qui n'a renseigné que sa matière.
/// </summary>
public static class TutorTopicMatcher
{
    private static readonly HashSet<string> StopWords = new(StringComparer.Ordinal)
    {
        "les", "des", "une", "pour", "avec", "sur", "aux", "dans", "est", "par", "que", "qui",
        "du", "de", "la", "le", "et", "en", "un", "au", "ses", "son", "mon", "mes", "cours",
        "the", "and", "for", "niveau", "chapitre", "exercice", "exercices", "revision", "revisions",
        "second", "premier", "premiere", "seconde", "terminale",
    };

    /// <summary>Racine de matière (normalisée) → notions du programme qui s'y rattachent.</summary>
    private static readonly (string SubjectRoot, string[] Notions)[] Lexicon =
    {
        ("math", new[] { "equation", "inequation", "derivee", "derivation", "integrale", "primitive", "fonction", "limite",
            "suite", "probabilite", "statistique", "geometrie", "algebre", "trigonometrie", "vecteur", "matrice",
            "polynome", "logarithme", "exponentielle", "complexe", "denombrement", "barycentre", "arithmetique", "fraction",
            "pythagore", "thales", "calcul" } ),
        ("physique", new[] { "mecanique", "electricite", "electrostatique", "electromagnetisme", "optique", "onde", "force",
            "energie", "circuit", "cinematique", "dynamique", "gravitation", "pendule", "oscillateur", "lentille", "radioactivite",
            "condensateur", "bobine", "tension", "puissance" } ),
        ("chimie", new[] { "molecule", "reaction", "acide", "base", "oxydoreduction", "oxydo", "atome", "solution", "mole",
            "dosage", "titrage", "ester", "alcool", "organique", "cinetique", "equilibre", "ion", "ph" } ),
        ("svt", new[] { "cellule", "genetique", "ecologie", "immunite", "immunologie", "photosynthese", "reproduction",
            "neurone", "geologie", "evolution", "adn", "mitose", "meiose", "digestion", "respiration", "biologie" } ),
        ("biolog", new[] { "cellule", "genetique", "ecologie", "immunite", "photosynthese", "adn", "mitose", "meiose" } ),
        ("francais", new[] { "dissertation", "commentaire", "grammaire", "conjugaison", "orthographe", "resume", "redaction",
            "poesie", "roman", "theatre", "argumentation", "lecture", "contraction", "litterature" } ),
        ("philo", new[] { "dissertation", "liberte", "conscience", "verite", "morale", "justice", "bonheur", "raison",
            "etat", "travail", "art", "religion", "texte" } ),
        ("anglais", new[] { "grammar", "essay", "vocabulary", "reading", "writing", "speaking", "tense", "translation", "comprehension" } ),
        ("histoire", new[] { "guerre", "colonisation", "decolonisation", "independance", "revolution", "mondialisation",
            "cartographie", "croquis", "geographie" } ),
        ("geograph", new[] { "cartographie", "croquis", "population", "climat", "relief", "mondialisation", "developpement" } ),
        ("informatique", new[] { "algorithme", "algorithmique", "programmation", "python", "java", "javascript", "reseau",
            "donnees", "sql", "tableur", "excel", "word", "html", "web" } ),
        ("econom", new[] { "marche", "monnaie", "croissance", "inflation", "comptabilite", "bilan", "gestion", "offre", "demande" } ),
    };

    /// <summary>Sujets (parmi <paramref name="topics"/>) couverts par ce corpus.</summary>
    public static List<string> MatchedTopics(IReadOnlyList<string> topics, IEnumerable<string?> tutorCorpus)
    {
        if (topics.Count == 0) return new List<string>();

        var corpusText = string.Join(" ", tutorCorpus.Where(s => !string.IsNullOrWhiteSpace(s)));
        var corpus = Tokens(corpusText).ToHashSet(StringComparer.Ordinal);

        // Notions du lexique rattachées aux matières présentes dans le corpus.
        var normalizedCorpus = Normalize(corpusText);
        foreach (var (root, notions) in Lexicon)
            if (normalizedCorpus.Contains(root, StringComparison.Ordinal))
                foreach (var n in notions) corpus.Add(Stem(n));

        var matched = new List<string>();
        foreach (var topic in topics)
        {
            var tokens = Tokens(topic).ToList();
            if (tokens.Count == 0) continue;
            if (tokens.Any(t => corpus.Contains(t) || corpus.Any(c => SharesPrefix(c, t))))
                matched.Add(topic);
        }
        return matched;
    }

    /// <summary>Découpe un paramètre de recherche « sujet1, sujet2 » en sujets nettoyés.</summary>
    public static List<string> ParseTopicsQuery(string? topics) =>
        TutorPricing.SanitizeTopics((topics ?? "").Split(new[] { ',', ';', '\n' }, StringSplitOptions.RemoveEmptyEntries));

    private static bool SharesPrefix(string a, string b)
    {
        // « equations » / « equation », « derivees » / « derivation » : préfixe
        // commun de 6 lettres au moins, pour rester prudent sur les mots courts.
        const int min = 6;
        if (a.Length < min || b.Length < min) return false;
        return string.CompareOrdinal(a, 0, b, 0, min) == 0;
    }

    private static IEnumerable<string> Tokens(string text)
    {
        var sb = new StringBuilder();
        foreach (var ch in Normalize(text))
        {
            if (char.IsLetterOrDigit(ch)) sb.Append(ch);
            else if (sb.Length > 0) { var w = sb.ToString(); sb.Clear(); if (Keep(w)) yield return Stem(w); }
        }
        if (sb.Length > 0) { var w = sb.ToString(); if (Keep(w)) yield return Stem(w); }
    }

    private static bool Keep(string w) => (w.Length >= 3 || w is "ph") && !StopWords.Contains(w);

    private static string Stem(string w) =>
        w.Length > 4 && (w.EndsWith('s') || w.EndsWith('x')) ? w[..^1] : w;

    private static string Normalize(string text)
    {
        var decomposed = text.ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposed.Length);
        foreach (var ch in decomposed)
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark) sb.Append(ch);
        return sb.ToString().Normalize(NormalizationForm.FormC);
    }
}
