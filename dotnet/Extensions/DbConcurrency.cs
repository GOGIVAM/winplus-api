namespace Backend.Extensions;

/// <summary>
/// Aide partagée pour les transactions Serializable (Module 19, passe de
/// clôture du lot 0). Extraite de OrdersController pour être réutilisée par
/// l'achat parent pour un enfant (ParentCreditsController) au lieu d'être
/// recopiée.
/// </summary>
public static class DbConcurrency
{
    /// <summary>
    /// Vrai si l'exception (ou l'une de ses causes) est un échec de
    /// sérialisation ou un interblocage Postgres, les deux verdicts que rend
    /// une transaction Serializable en conflit.
    ///
    /// La chaîne est parcourue parce que la même erreur arrive sous deux
    /// formes : nue depuis le COMMIT, ou enveloppée dans une
    /// DbUpdateException quand elle survient sur un SaveChanges.
    /// </summary>
    public static bool IsSerializationFailure(Exception exception)
    {
        for (Exception? current = exception; current != null; current = current.InnerException)
        {
            if (current is Npgsql.PostgresException pg &&
                (pg.SqlState == Npgsql.PostgresErrorCodes.SerializationFailure ||
                 pg.SqlState == Npgsql.PostgresErrorCodes.DeadlockDetected))
            {
                return true;
            }
        }

        return false;
    }
}
