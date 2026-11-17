using Microsoft.EntityFrameworkCore;
using Backend.Data;

namespace Backend.Services;

/// <summary>
/// Classe suivie dans « Mon programme » (CurriculumSelections), mise en forme
/// pour les consignes de WinAI : quiz et fiches générés, discussion.
/// Le référentiel des chapitres vit côté site : le site envoie lui-même, avec
/// la demande, le résumé de ce que l'élève étudie (ProgrammeContext).
/// </summary>
public static class CurriculumContext
{
    private static readonly Dictionary<string, string> Labels = new()
    {
        ["6e"] = "6ème", ["5e"] = "5ème", ["4e"] = "4ème", ["3e"] = "3ème",
        ["2nde"] = "Seconde", ["1ere"] = "Première", ["tle"] = "Terminale",
        ["tc1"] = "1ère année", ["tc2"] = "2ème année", ["tc3"] = "3ème année", ["tc4"] = "4ème année",
        ["tc2nde"] = "Seconde", ["tc1ere"] = "Première", ["tctle"] = "Terminale",
        ["f1"] = "Form 1", ["f2"] = "Form 2", ["f3"] = "Form 3", ["f4"] = "Form 4", ["f5"] = "Form 5",
        ["ls"] = "Lower Sixth", ["us"] = "Upper Sixth",
    };

    /// <summary>Sous-système de la classe, pour que WinAI suive le bon programme et la bonne langue.</summary>
    private static string SystemOf(string level) =>
        level.StartsWith("tc") ? "enseignement technique francophone"
        : level is "f1" or "f2" or "f3" or "f4" or "f5" or "ls" or "us" ? "sous-système anglophone (GCE)"
        : "enseignement général francophone";

    /// <summary>« Terminale C », « 4ème »… ou null si l'élève n'a pas choisi de classe.</summary>
    public static async Task<string?> LevelLabelAsync(ApplicationDbContext db, int userId, CancellationToken ct = default)
    {
        var sel = await db.CurriculumSelections.AsNoTracking()
            .Where(s => s.UserId == userId)
            .Select(s => new { s.Level, s.Series })
            .FirstOrDefaultAsync(ct);
        if (sel == null || !Labels.TryGetValue(sel.Level, out var label)) return null;
        return string.IsNullOrWhiteSpace(sel.Series) ? label : $"{label} {sel.Series}";
    }

    /// <summary>Niveau à donner à WinAI : la classe du programme si elle est connue, sinon celle du profil.</summary>
    public static async Task<string?> GradeAsync(ApplicationDbContext db, int userId, string? profileLevel, CancellationToken ct = default)
    {
        var label = await LevelLabelAsync(db, userId, ct);
        if (label == null) return profileLevel;
        var level = await db.CurriculumSelections.AsNoTracking().Where(s => s.UserId == userId).Select(s => s.Level).FirstOrDefaultAsync(ct) ?? "";
        return $"{label} ({SystemOf(level)}, programme officiel camerounais, MINESEC)";
    }

    /// <summary>
    /// Nettoie le résumé envoyé par le site (texte libre, borné) avant de le
    /// joindre aux objectifs transmis à WinAI.
    /// </summary>
    public static string? Sanitize(string? programmeContext)
    {
        if (string.IsNullOrWhiteSpace(programmeContext)) return null;
        var s = new string(programmeContext.Where(c => !char.IsControl(c) || c == ' ').ToArray()).Trim();
        if (s.Length > 1200) s = s[..1200];
        return s.Length == 0 ? null : $"Suivre le programme officiel : {s}";
    }
}
