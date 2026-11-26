using System.Globalization;
using System.Text;

namespace Backend.Services;

/// <summary>
/// Référentiel commun des matières et des niveaux du répétiteur (26/11/2026), exposé par
/// GET /api/tutor-profile/referential pour que le site et le mobile n'aient plus chacun leur liste.
/// Repris de la liste du site (frontend/src/lib/constants.ts : MATIERES, SUPERIOR_SUBJECTS,
/// SECONDARY_LEVELS, SUPERIOR_LEVELS), la plus complète ; les libellés propres au mobile sont des alias.
/// Canonical() ramène un libellé saisi (casse, accents, alias) au libellé du référentiel, ou le garde tel
/// quel s'il n'y figure pas (« Autre » saisi librement).
/// </summary>
public static class TutorReferential
{
    public static readonly IReadOnlyList<string> SecondarySubjects = new[]
    {
        "Mathématiques",
        "Physique",
        "Chimie",
        "SVT",
        "Français",
        "Philosophie",
        "Anglais",
        "Histoire-Géo",
        "Informatique",
        "Économie",
        "English Language",
        "Literature in English",
        "Geography",
        "Government & Citizenship",
        "Accounting & Commerce",
        "Computer Science / ICT",
        "Droit / Law",
        "Médecine / Medicine",
        "Pharmacie / Pharmacy",
        "Génie Civil / Civil Engineering",
        "Génie Électrique / Electrical Engineering",
        "Génie Informatique / Software Engineering",
        "Gestion / Management",
        "Sciences Économiques / Economics",
        "Sciences Politiques / Political Science",
        "Architecture",
        "Agronomie / Agricultural Sciences",
        "Sociologie / Sociology",
        "Psychologie / Psychology",
        "Communication / Journalisme",
        "Relations Internationales / International Relations",
    };

    public static readonly IReadOnlyList<string> SuperiorSubjects = new[]
    {
        "Droit",
        "Sciences politiques",
        "Sciences économiques & de gestion",
        "Lettres & sciences humaines",
        "Sciences de l'éducation",
        "Sciences (Maths/Physique/Chimie/Bio)",
        "Médecine & sciences biomédicales",
        "Sciences pharmaceutiques",
        "Sciences infirmières",
        "Agronomie & sciences agricoles",
        "Génie industriel",
        "Communication & journalisme",
        "Relations internationales",
        "Comptabilité & gestion des entreprises",
        "Marketing, commerce & vente",
        "Gestion des ressources humaines",
        "Banque & finance",
        "Assurance",
        "Commerce international",
        "Logistique & transport",
        "Génie logiciel",
        "Maintenance des systèmes informatiques",
        "Informatique industrielle & automatisme",
        "Réseaux & sécurité informatique",
        "Télécommunications",
        "E-commerce & marketing digital",
        "Génie civil",
        "Génie mécanique",
        "Électrotechnique",
        "Génie chimique",
        "Maintenance industrielle",
        "Mines & géologie appliquée",
    };

    public static readonly IReadOnlyList<string> SecondaryLevels = new[]
    {
        "6ème",
        "5ème",
        "4ème",
        "3ème",
        "Seconde",
        "Première",
        "Terminale",
        "BEPC",
        "Probatoire",
        "BAC",
        "Form 1",
        "Form 2",
        "Form 3",
        "Form 4",
        "Form 5 (GCE O Level)",
        "Lower Sixth",
        "Upper Sixth (GCE A Level)",
        "BTS / DUT / DTS",
        "HND (Higher National Diploma)",
        "Prépa",
        "Licence (Université)",
        "Master (Université)",
        "Doctorat (Université)",
        "Bachelor's Degree (University)",
        "Master's Degree (University)",
        "PhD (University)",
        "University Year 1 (Level 100)",
        "University Year 2 (Level 200)",
        "University Year 3 (Level 300)",
        "University Year 4 (Level 400)",
        "ENSP Yaoundé (ENSPY)",
        "ENSP Douala (ENSPD)",
        "ENSP Maroua (ENSPM)",
        "ENSP Bamenda",
        "ENS École Normale",
        "ESSTIC Communication",
        "IRIC Relations Internationales",
        "ESSEC Commerce",
        "ENAM Administration",
        "ENSET",
        "UCAC-ICAM Ingénieur",
        "IAI Informatique",
        "FMSB Médecine (Yaoundé I)",
        "FMSP Médecine (Douala)",
        "FHS Médecine (Buea)",
        "FHS Médecine (Bamenda)",
        "FMSP Médecine (Dschang)",
        "FMSB Médecine (Garoua)",
        "Université des Montagnes (Médecine, privée)",
    };

    public static readonly IReadOnlyList<string> SuperiorLevels = new[]
    {
        "BTS / DUT / DTS",
        "HND (Higher National Diploma)",
        "Prépa",
        "Licence (Université)",
        "Master (Université)",
        "Doctorat (Université)",
        "Bachelor's Degree (University)",
        "Master's Degree (University)",
        "PhD (University)",
        "University Year 1 (Level 100)",
        "University Year 2 (Level 200)",
        "University Year 3 (Level 300)",
        "University Year 4 (Level 400)",
        "ENSP Yaoundé (ENSPY)",
        "ENSP Douala (ENSPD)",
        "ENSP Maroua (ENSPM)",
        "ENSP Bamenda",
        "ENS École Normale",
        "ESSTIC Communication",
        "IRIC Relations Internationales",
        "ESSEC Commerce",
        "ENAM Administration",
        "ENSET",
        "UCAC-ICAM Ingénieur",
        "IAI Informatique",
        "FMSB Médecine (Yaoundé I)",
        "FMSP Médecine (Douala)",
        "FHS Médecine (Buea)",
        "FHS Médecine (Bamenda)",
        "FMSP Médecine (Dschang)",
        "FMSB Médecine (Garoua)",
        "Université des Montagnes (Médecine, privée)",
    };

    public static readonly IReadOnlyDictionary<string, string> Aliases = new Dictionary<string, string>
    {
        ["ENSP Polytechnique"] = "ENSP Yaoundé (ENSPY)",
        ["FMSB Médecine"] = "FMSB Médecine (Yaoundé I)",
        ["Maths"] = "Mathématiques",
        ["Mathematiques"] = "Mathématiques",
        ["Histoire-Géographie"] = "Histoire-Géo",
        ["Histoire Géographie"] = "Histoire-Géo",
        ["Economie"] = "Économie",
        ["Philo"] = "Philosophie",
        ["Terminale C"] = "Terminale",
        ["Terminale D"] = "Terminale",
    };

    private static readonly Dictionary<string, string> ByKey = BuildIndex();

    private static Dictionary<string, string> BuildIndex()
    {
        var index = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var v in SecondarySubjects.Concat(SuperiorSubjects).Concat(SecondaryLevels).Concat(SuperiorLevels))
            index.TryAdd(Key(v), v);
        foreach (var (alias, canonical) in Aliases)
            index.TryAdd(Key(alias), canonical);
        return index;
    }

    /// <summary>Clé de comparaison : minuscules, sans accents, espaces et ponctuation réduits.</summary>
    public static string Key(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var d = value.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(d.Length);
        var space = false;
        foreach (var c in d)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue;
            if (char.IsLetterOrDigit(c)) { sb.Append(c); space = false; }
            else if (!space && sb.Length > 0) { sb.Append(' '); space = true; }
        }
        return sb.ToString().TrimEnd();
    }

    /// <summary>Libellé du référentiel correspondant, ou la saisie nettoyée si elle n'y figure pas.</summary>
    public static string Canonical(string value) =>
        ByKey.TryGetValue(Key(value), out var canonical) ? canonical : value.Trim();

    public static object View() => new
    {
        subjects = new { secondary = SecondarySubjects, superior = SuperiorSubjects },
        levels = new { secondary = SecondaryLevels, superior = SuperiorLevels },
        aliases = Aliases,
    };
}
