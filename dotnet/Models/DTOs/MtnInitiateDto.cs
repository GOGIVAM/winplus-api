using System.ComponentModel.DataAnnotations;

namespace Backend.Models.DTOs;

public class MtnInitiateDto
{
    // FIX 1 : [Required] sur les champs obligatoires.
    // Sans ces attributs, ModelState.IsValid retournait toujours true même avec
    // un OrderId vide ou un numéro absent — la requête MTN échouait alors en 400
    // sans message d'erreur utile côté client.
    [Required(ErrorMessage = "L'identifiant de commande est requis.")]
    public string OrderId { get; set; } = "";

    // FIX 2 : [Range] pour garantir un montant strictement positif.
    // Un montant à 0 ou négatif passait la validation et MTN renvoyait un 400 opaque.
    [Required]
    [Range(1, double.MaxValue, ErrorMessage = "Le montant doit être supérieur à 0.")]
    public decimal Amount { get; set; }

    public string Currency { get; set; } = "XAF";

    // FIX 1 (suite) : [Required] + [Phone] sur le numéro.
    // FIX 3 : [RegularExpression] pour s'assurer que le numéro est bien un MSISDN
    // (chiffres uniquement, sans +) avant même d'appeler le service MTN.
    // Le frontend envoie déjà "237650XXXXXX" mais cette validation côté serveur
    // empêche tout appel MTN avec un numéro malformé.
    [Required(ErrorMessage = "Le numéro de téléphone est requis.")]
    [RegularExpression(@"^\d{8,15}$",
        ErrorMessage = "Le numéro doit contenir entre 8 et 15 chiffres sans espaces ni +.")]
    public string CustomerPhone { get; set; } = "";

    // FIX 4 : [EmailAddress] pour valider le format de l'email.
    // L'email est utilisé pour les notifications et les reçus — un format invalide
    // peut provoquer des erreurs silencieuses dans les services de notification.
    [EmailAddress(ErrorMessage = "Format d'email invalide.")]
    public string CustomerEmail { get; set; } = "";

    // FIX 5 : longueur max sur la description.
    // L'API MTN tronque ou rejette les payerMessage/payeeNote au-delà de 160 caractères.
    [MaxLength(160, ErrorMessage = "La description ne peut pas dépasser 160 caractères.")]
    public string? Description { get; set; }
}