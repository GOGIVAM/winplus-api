namespace Backend.Services;

/// <summary>
/// Point d'extension de la <b>recharge de quota WinAI</b> (décision 8.5 du
/// suivi : « blocage 402 une fois le quota épuisé, avec option de recharge via
/// le wallet plutôt qu'une attente jusqu'au mois suivant »).
///
/// ⚠ <b>Dépendance non satisfaite, assumée et documentée.</b> La recharge
/// réelle suppose le ledger <c>WalletTransaction</c> décrit au Module 1/14
/// (PROMPTS_IMPLEMENTATION_VERSEMENT_ET_CORRECTIONS.md). Ce ledger
/// <b>n'existe pas encore dans le code</b> : c'est un chantier séparé, non
/// entamé. Construire ici un second système de solde en parallèle aurait
/// produit exactement ce que la Partie 8 cherche à éviter (une définition de
/// plus du « solde actif », en plus de celles déjà recensées).
///
/// Cette implémentation minimale se contente donc de :
///   - exposer le contrat que l'implémentation wallet devra remplir ;
///   - annoncer honnêtement au client que la recharge arrive, au lieu de
///     laisser croire à un débit qui n'a pas lieu.
///
/// Le jour où le Module 1/14 est livré, il suffit d'enregistrer une
/// implémentation qui débite <c>WalletTransaction</c> et crédite le quota
/// (une ligne <see cref="Backend.Models.Entities.AiTokenUsage"/> négative, ou
/// une table de crédits dédiée) : aucun appelant n'a à changer.
/// </summary>
public interface ITokenTopUpService
{
    /// <summary>
    /// Vrai si la recharge est réellement disponible. Tant que le wallet n'est
    /// pas livré, c'est faux, et l'API ne propose donc pas une action qu'elle
    /// ne sait pas tenir.
    /// </summary>
    bool IsAvailable { get; }

    /// <summary>
    /// Message à afficher à l'utilisateur au moment du blocage 402.
    /// </summary>
    string UnavailableReason { get; }

    /// <summary>
    /// Recharge le quota WinAI de <paramref name="userId"/> de
    /// <paramref name="tokens"/> tokens, débités du wallet.
    /// </summary>
    Task<TokenTopUpResult> TopUpAsync(int userId, int tokens, CancellationToken cancellationToken = default);
}

public readonly record struct TokenTopUpResult(bool Succeeded, string Message);

/// <summary>
/// Implémentation d'attente : ne débite rien, ne crédite rien, et le dit.
/// </summary>
public class UnavailableTokenTopUpService : ITokenTopUpService
{
    public bool IsAvailable => false;

    public string UnavailableReason =>
        "La recharge de quota WinAI arrive prochainement. En attendant, un plan supérieur augmente immédiatement votre usage.";

    public Task<TokenTopUpResult> TopUpAsync(int userId, int tokens, CancellationToken cancellationToken = default) =>
        Task.FromResult(new TokenTopUpResult(false, UnavailableReason));
}
