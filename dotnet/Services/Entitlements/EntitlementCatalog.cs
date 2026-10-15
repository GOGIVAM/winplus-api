using System.Globalization;

namespace Backend.Services.Entitlements;

/// <summary>Type d'un droit (étude 9.7b, section 1.2).</summary>
public enum EntitlementType
{
    /// <summary>oui / non. Fusion : OU logique.</summary>
    Boolean,
    /// <summary>Entier positif, -1 = illimité. Fusion : maximum.</summary>
    Integer,
    /// <summary>Pourcentage entier de 0 à 100. Fusion : maximum.</summary>
    Percent,
}

/// <summary>Définition d'une clé de droit : seul le code lit ces clés, jamais un nom de plan.</summary>
public sealed record EntitlementDefinition(
    string Key,
    EntitlementType Type,
    string Unit,
    string DefaultValue,
    string Label,
    string Description,
    string[] Audiences,
    bool AllowUnlimited = false,
    bool ShowOnPricing = true,
    string? Group = null);

/// <summary>
/// Catalogue des droits typés (étude 9.7b section 1.2, décisions 18.17 et
/// 18.21). <b>Seuls les droits réellement appliqués par le serveur y figurent</b>
/// (18.18) : chaque clé a au moins un point d'application, documenté sur sa
/// ligne. Les valeurs par défaut sont celles du plan gratuit ; la base peut
/// les remplacer par la version publiée du plan gratuit de chaque public.
/// </summary>
public static class EntitlementCatalog
{
    public const int Unlimited = -1;

    // Catalogue
    public const string CatalogAccess = "catalog.access";

    // WinAI
    public const string AiReferenceTokens = "ai.reference_tokens";
    public const string AiTopup = "ai.topup";
    public const string AiChat = "ai.feature.chat";
    public const string AiAttachments = "ai.feature.attachments";
    public const string AiErrorExplain = "ai.feature.error_explain";
    public const string AiAdaptiveQuiz = "ai.feature.adaptive_quiz";
    public const string AiStudyPlan = "ai.feature.study_plan";
    public const string AiStudySession = "ai.feature.study_session";
    public const string AiExamCoach = "ai.feature.exam_coach";
    public const string AiParentAdvisor = "ai.feature.parent_advisor";
    public const string AiTeacherAssist = "ai.feature.teacher_assist";
    public const string AiQuizGeneration = "ai.feature.quiz_generation";
    public const string AiCorrectionAssist = "ai.feature.correction_assist";
    public const string AiCourseBuilder = "ai.feature.course_builder";
    public const string AiParentBonusPerChild = "ai.parent_bonus_per_child";
    public const string AiParentBonusShares = "ai.parent_bonus_shares";

    // Parent
    public const string ChildrenMax = "children.max";
    public const string ChildrenCatalog = "children.catalog";
    public const string ParentMonthlyCreditXaf = "parent.monthly_credit_xaf";
    public const string ParentRealtimeAlerts = "parent.realtime_alerts";

    // Professeur
    public const string TeacherRevenueShare = "teacher.revenue_share";
    public const string TeacherMaxClasses = "teacher.max_classes";
    public const string TeacherMaxStudentsPerClass = "teacher.max_students_per_class";
    public const string TeacherMaxPublishedCourses = "teacher.max_published_courses";

    // Communauté
    public const string GroupsMaxOwned = "groups.max_owned";
    public const string FavoritesMax = "favorites.max";

    private static readonly string[] S = { "student" };
    private static readonly string[] P = { "parent" };
    private static readonly string[] T = { "teacher" };
    private static readonly string[] SPT = { "student", "parent", "teacher" };
    private static readonly string[] ST = { "student", "teacher" };

    public static readonly IReadOnlyList<EntitlementDefinition> All = new List<EntitlementDefinition>
    {
        // ContentAccessService.HasPaidContentAccessAsync
        new(CatalogAccess, EntitlementType.Boolean, "", "false", "Catalogue complet",
            "Lecture dans la visionneuse de tout le catalogue payant (épreuves, corrigés, livres), sans téléchargement.", ST, Group: "Catalogue"),

        // AiQuotaService (référence ; semaine = ÷4, session = ÷3 de la semaine)
        new(AiReferenceTokens, EntitlementType.Integer, "tokens", "200000", "Usage WinAI",
            "Quota de référence WinAI. Affiché en multiplicateur du plan gratuit, jamais en tokens.", SPT, Group: "WinAI"),
        // TokenTopUpService
        new(AiTopup, EntitlementType.Boolean, "", "true", "Recharge WinAI",
            "Achat de tokens en plus depuis le portefeuille, au prix fixé par l'administration.", SPT, Group: "WinAI"),
        // ChatbotController (stream, message)
        new(AiChat, EntitlementType.Boolean, "", "true", "Chat WinAI", "Discussion avec WinAI.", SPT, Group: "WinAI"),
        // ChatbotController (pièces jointes)
        new(AiAttachments, EntitlementType.Boolean, "", "false", "Pièces jointes WinAI",
            "Photos, documents et audio envoyés à WinAI.", SPT, Group: "WinAI"),
        // AIController explain-error
        new(AiErrorExplain, EntitlementType.Boolean, "", "true", "Explication des erreurs",
            "WinAI explique une erreur de quiz.", S, Group: "WinAI"),
        // AIController adaptive-quiz
        new(AiAdaptiveQuiz, EntitlementType.Boolean, "", "true", "Quiz adaptatif", "Quiz généré par WinAI selon le niveau.", S, Group: "WinAI"),
        // AIController study-plan
        new(AiStudyPlan, EntitlementType.Boolean, "", "false", "Plan d'étude", "Plan d'étude personnalisé.", S, Group: "WinAI"),
        // AIController study-session/*
        new(AiStudySession, EntitlementType.Boolean, "", "false", "Session d'étude guidée",
            "Session guidée en trois phases, avec historique.", S, Group: "WinAI"),
        // AIController exam-coach/*, ExamCoachController
        new(AiExamCoach, EntitlementType.Boolean, "", "false", "Exam Coach",
            "Plan jusqu'à l'examen, recalibrage et séance du jour.", S, Group: "WinAI"),
        // ParentController (conseiller), chat parent
        new(AiParentAdvisor, EntitlementType.Boolean, "", "true", "Conseiller WinAI", "Conseils WinAI au parent.", P, Group: "WinAI"),
        // AIController optimize-title, generate-description, tarif de tutorat
        new(AiTeacherAssist, EntitlementType.Boolean, "", "true", "Aides de publication",
            "Titre, description et tarif suggérés par WinAI.", T, Group: "WinAI"),
        // AIController generate-quiz-questions
        new(AiQuizGeneration, EntitlementType.Boolean, "", "false", "Génération de quiz",
            "Quiz et révisions générés par WinAI pour vos élèves.", T, Group: "WinAI"),
        // CorrectionsController (correction assistée)
        new(AiCorrectionAssist, EntitlementType.Boolean, "", "false", "Correction assistée",
            "WinAI propose une correction des devoirs.", T, Group: "WinAI"),
        // Exposé au chantier B (syllabus de formation) : AiFeatureGate.EnsureAsync(course_builder)
        new(AiCourseBuilder, EntitlementType.Boolean, "", "false", "Création de formation assistée",
            "Génération et import de syllabus de formation.", T, Group: "WinAI"),
        // EntitlementService (bonus parent → enfants couverts)
        new(AiParentBonusPerChild, EntitlementType.Integer, "tokens", "0", "Bonus WinAI par enfant",
            "Usage WinAI ajouté à chaque enfant couvert.", P, ShowOnPricing: false, Group: "WinAI"),
        new(AiParentBonusShares, EntitlementType.Integer, "parts", "0", "Parts de bonus",
            "Nombre de bonus complets que le plan peut distribuer (au-delà, partagés).", P, ShowOnPricing: false, Group: "WinAI"),

        // ParentLinkLimits (liaison), EntitlementService (enfants couverts)
        new(ChildrenMax, EntitlementType.Integer, "enfants", "1", "Enfants suivis",
            "Enfants liés couverts par le plan.", P, AllowUnlimited: true, Group: "Famille"),
        // EntitlementService (catalogue ouvert aux enfants couverts)
        new(ChildrenCatalog, EntitlementType.Boolean, "", "false", "Catalogue pour les enfants",
            "Le plan ouvre le catalogue complet aux enfants couverts.", P, Group: "Famille"),
        // ParentAllowanceService (dotation à chaque période payée)
        new(ParentMonthlyCreditXaf, EntitlementType.Integer, "XAF", "0", "Dotation du portefeuille",
            "Montant versé au portefeuille parent à chaque période payée.", P, Group: "Famille"),
        // ParentAdvisorThresholdService (alertes immédiates)
        new(ParentRealtimeAlerts, EntitlementType.Boolean, "", "false", "Alertes en temps réel",
            "Alertes poussées dès qu'un seuil est franchi (sinon synthèse hebdomadaire).", P, Group: "Famille"),

        // TeacherShareService (figée au paiement des séances)
        new(TeacherRevenueShare, EntitlementType.Percent, "%", "70", "Part sur le tutorat",
            "Part reversée au professeur sur le tutorat et les sessions, figée au paiement.", T, Group: "Professeur"),
        // TeacherClassesController
        new(TeacherMaxClasses, EntitlementType.Integer, "classes", "1", "Classes actives",
            "Classes actives simultanées.", T, AllowUnlimited: true, Group: "Professeur"),
        new(TeacherMaxStudentsPerClass, EntitlementType.Integer, "élèves", "30", "Élèves par classe",
            "Élèves par classe.", T, AllowUnlimited: true, Group: "Professeur"),
        // Exposé au chantier B (publication des formations)
        new(TeacherMaxPublishedCourses, EntitlementType.Integer, "formations", "1", "Formations publiées",
            "Formations publiées simultanément (le catalogue reste illimité).", T, AllowUnlimited: true, Group: "Professeur"),

        // StudyGroupsController
        new(GroupsMaxOwned, EntitlementType.Integer, "groupes", "1", "Groupes d'étude",
            "Groupes d'étude créés.", S, AllowUnlimited: true, Group: "Communauté"),
        // FavoriteService
        new(FavoritesMax, EntitlementType.Integer, "favoris", "20", "Favoris",
            "Favoris enregistrés.", S, AllowUnlimited: true, Group: "Communauté"),
    };

    private static readonly Dictionary<string, EntitlementDefinition> ByKey =
        All.ToDictionary(d => d.Key, StringComparer.Ordinal);

    public static EntitlementDefinition? Find(string key) => ByKey.TryGetValue(key, out var d) ? d : null;

    public static bool IsKnown(string key) => ByKey.ContainsKey(key);

    public static IEnumerable<EntitlementDefinition> ForAudience(string audience) =>
        All.Where(d => d.Audiences.Contains(audience));

    /// <summary>
    /// Valide et met sous forme canonique une valeur saisie. Renvoie null et un
    /// message si la valeur ne respecte pas le type de la clé.
    /// </summary>
    public static string? Normalize(string key, string? raw, out string? error)
    {
        error = null;
        var def = Find(key);
        if (def == null) { error = $"Droit inconnu : {key}."; return null; }
        var v = (raw ?? string.Empty).Trim().ToLowerInvariant();
        switch (def.Type)
        {
            case EntitlementType.Boolean:
                if (v is "true" or "1" or "oui" or "yes") return "true";
                if (v is "false" or "0" or "non" or "no") return "false";
                error = $"{def.Label} : oui ou non attendu.";
                return null;
            case EntitlementType.Integer:
                if (v is "illimité" or "illimite" or "unlimited" or "-1")
                {
                    if (def.AllowUnlimited) return Unlimited.ToString(CultureInfo.InvariantCulture);
                    error = $"{def.Label} : la valeur illimitée n'est pas permise.";
                    return null;
                }
                if (long.TryParse(v.Replace(" ", ""), NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) && n >= 0 && n <= 1_000_000_000)
                    return n.ToString(CultureInfo.InvariantCulture);
                error = $"{def.Label} : entier positif attendu.";
                return null;
            case EntitlementType.Percent:
                if (int.TryParse(v.TrimEnd('%').Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var p) && p is >= 0 and <= 100)
                    return p.ToString(CultureInfo.InvariantCulture);
                error = $"{def.Label} : pourcentage entre 0 et 100 attendu.";
                return null;
        }
        error = $"Droit {key} : type non géré.";
        return null;
    }

    public static bool AsBool(string? v) => v == "true";

    public static long AsLong(string? v) =>
        long.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : 0;

    /// <summary>Fusion de deux valeurs d'une même clé : OU pour un booléen, maximum (illimité gagne) sinon.</summary>
    public static string Merge(string key, string a, string b)
    {
        var def = Find(key);
        if (def == null) return a;
        if (def.Type == EntitlementType.Boolean) return AsBool(a) || AsBool(b) ? "true" : "false";
        var x = AsLong(a);
        var y = AsLong(b);
        if (x == Unlimited || y == Unlimited) return Unlimited.ToString(CultureInfo.InvariantCulture);
        return Math.Max(x, y).ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Compare deux valeurs : +1 si <paramref name="candidate"/> est meilleure
    /// que <paramref name="current"/>, -1 si moins bonne, 0 si égale. Sert à
    /// décider si une nouvelle version est une amélioration (18.12).
    /// </summary>
    public static int Compare(string key, string? current, string? candidate)
    {
        var def = Find(key);
        if (def == null) return 0;
        current ??= def.DefaultValue;
        candidate ??= def.DefaultValue;
        if (def.Type == EntitlementType.Boolean)
            return AsBool(candidate) == AsBool(current) ? 0 : AsBool(candidate) ? 1 : -1;
        var c = AsLong(current);
        var n = AsLong(candidate);
        if (c == n) return 0;
        if (n == Unlimited) return 1;
        if (c == Unlimited) return -1;
        return n > c ? 1 : -1;
    }

    /// <summary>Valeur lisible pour l'affichage (page tarifs, aperçu admin).</summary>
    public static string Display(string key, string value, long freeReferenceTokens)
    {
        var def = Find(key);
        if (def == null) return value;
        if (key == AiReferenceTokens)
        {
            var mult = Math.Max(1, (int)Math.Round((double)AsLong(value) / Math.Max(1, freeReferenceTokens)));
            return mult <= 1 ? "Usage de référence" : $"{mult}x l'usage gratuit";
        }
        return def.Type switch
        {
            EntitlementType.Boolean => AsBool(value) ? "Oui" : "Non",
            EntitlementType.Percent => $"{AsLong(value)} %",
            _ => AsLong(value) == Unlimited ? "Illimité" : $"{AsLong(value):N0} {def.Unit}".Replace(',', ' ').Trim(),
        };
    }
}
