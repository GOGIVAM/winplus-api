using Backend.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Backend.Utils;

/// <summary>
/// Décision du 2026-10-26 : un e-mail vérifié est obligatoire pour retirer de
/// l'argent (politique VerifiedEmailOnly) et pour publier des contenus en tant
/// que professeur (épreuves, livres, formations). Les achats restent ouverts.
/// Lu en base, pas dans le jeton : une vérification faite après la connexion
/// compte tout de suite.
/// </summary>
public static class VerifiedEmailRequirement
{
    public const string Code = "email_unverified";

    /// <summary>Réponse 403 si l'e-mail du compte n'est pas vérifié (administrateurs exemptés), sinon null.</summary>
    public static async Task<IActionResult?> CheckAsync(ApplicationDbContext db, int userId, bool isAdmin, bool english = false)
    {
        if (isAdmin) return null;
        var verified = await db.Users.AsNoTracking().Where(u => u.Id == userId).Select(u => u.IsEmailVerified).FirstOrDefaultAsync();
        if (verified) return null;
        return new ObjectResult(new
        {
            code = Code,
            error = english
                ? "Verify your email address before publishing content. The code can be sent again from your profile."
                : "Vérifiez votre adresse e-mail avant de publier un contenu. Le code peut être renvoyé depuis votre profil.",
        })
        { StatusCode = StatusCodes.Status403Forbidden };
    }
}
