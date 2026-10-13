using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Backend.Models.DTOs;
using Backend.Models.Entities;
using Backend.Services;

namespace Backend.Controllers;

/// <summary>
/// Politique de tutorat publique (lot 9.2, suivi 17.10).
///
/// GET /api/tutor-policy  (anonyme) : texte de la politique commune, limites
/// plateforme des réglages d'annulation du tuteur, délai de contestation et
/// taux de frais Mobile Money en vigueur. Source unique : le web et le mobile
/// affichent ce qu'il renvoie, sans copie locale des bornes ni du texte.
/// </summary>
[ApiController]
[Route("api/tutor-policy")]
[AllowAnonymous]
[Produces("application/json")]
public class TutorPolicyController : ControllerBase
{
    private readonly TutorPolicySettings _settings;

    public TutorPolicyController(IConfiguration configuration, IPlatformSettings? platformSettings = null)
    {
        _settings = TutorPolicySettings.From(configuration, platformSettings);
    }

    [HttpGet]
    [ResponseCache(Duration = 300)]
    public ActionResult<TutorPolicyDto> Get() => Ok(TutorPolicyDocument.Build(_settings));
}

/// <summary>Texte de la politique de tutorat, construit à partir des réglages et des bornes serveur.</summary>
public static class TutorPolicyDocument
{
    public static TutorPolicyDto Build(TutorPolicySettings s)
    {
        var fee = s.NotchPayFeePercent.ToString("0.##", System.Globalization.CultureInfo.GetCultureInfo("fr-FR"));
        var h = s.DisputeWindowHours;
        return new TutorPolicyDto
        {
            Version = TutorPolicy.Version,
            DisputeWindowHours = h,
            NotchPayFeePercent = s.NotchPayFeePercent,
            Bounds = new TutorPolicyBoundsDto
            {
                FullRefundHoursMin = TutorPolicy.FullRefundHoursMin,
                FullRefundHoursMax = TutorPolicy.FullRefundHoursMax,
                PartialRefundPercentMin = TutorPolicy.PartialRefundPercentMin,
                PartialRefundPercentMax = TutorPolicy.PartialRefundPercentMax,
                NoRefundHoursMin = TutorPolicy.NoRefundHoursMin,
                NoRefundHoursMax = TutorPolicy.NoRefundHoursMax,
                DefaultFullRefundHours = TutorPolicy.DefaultFullRefundHours,
                DefaultPartialRefundPercent = TutorPolicy.DefaultPartialRefundPercent,
                DefaultNoRefundHours = TutorPolicy.DefaultNoRefundHours,
            },
            Sections = new List<TutorPolicySectionDto>
            {
                new()
                {
                    Id = "reservation-paiement",
                    Title = "Réservation et paiement",
                    Paragraphs =
                    {
                        "Une séance se réserve par tranches de 30 minutes dans les disponibilités du tuteur. Le prix est calculé par WinPlus à partir de la grille du tuteur et affiché avant tout paiement.",
                        "Tu paies soit par Mobile Money (MTN MoMo ou Orange Money), soit avec le solde de ton portefeuille WinPlus. Le paiement par solde est immédiat et sans frais.",
                        "Une fois payée, la demande est envoyée au tuteur, qui l'accepte ou la refuse dans son délai de réponse. Ton argent reste bloqué par WinPlus : le tuteur ne le reçoit qu'après la séance.",
                    },
                },
                new()
                {
                    Id = "annulation-remboursement",
                    Title = "Annulation et remboursement",
                    Paragraphs =
                    {
                        "Tout remboursement est versé immédiatement sur le portefeuille WinPlus de la personne qui a payé, quel que soit le moyen de paiement utilisé. Le solde peut servir à réserver d'autres séances.",
                        "Si tu annules toi-même, la politique d'annulation du tuteur s'applique : remboursement intégral si tu annules assez tôt, partiel entre ses deux seuils, aucun remboursement en dessous de son dernier seuil. Ses réglages sont affichés sur son profil et avant le paiement.",
                        $"Quand tu annules une séance payée par Mobile Money, les frais de l'opérateur de paiement ({fee} % du montant payé) sont déduits de ton remboursement. Une séance payée avec ton solde n'a aucun frais.",
                        "Si le tuteur annule, refuse ta demande ou ne répond pas à temps, tu es remboursé intégralement, frais compris. Les frais Mobile Money sont alors retenus sur le solde du tuteur.",
                    },
                },
                new()
                {
                    Id = "engagement-mensuel",
                    Title = "Engagement mensuel",
                    Paragraphs =
                    {
                        $"Un engagement mensuel réserve les mêmes créneaux chaque semaine pendant {TutorMonthlyPlan.Weeks} semaines. Toutes les séances sont créées d'un coup et bloquées dans l'agenda du tuteur ; si une seule n'est pas disponible, l'engagement n'est pas créé.",
                        $"Chaque séance est chiffrée par la grille du tuteur, puis sa remise mensuelle s'applique (au plus {TutorPackageKinds.MaxMonthlyDiscountPercent} %). Le total du mois est payé d'avance, par solde ou par Mobile Money.",
                        "Le tuteur accepte ou refuse l'engagement entier dans son délai de réponse. S'il refuse ou ne répond pas, tout est remboursé intégralement sur le portefeuille du payeur.",
                        $"L'argent reste bloqué par WinPlus et n'est versé au tuteur que séance par séance, {h} h après chaque séance effectuée et non contestée. Une contestation ne porte que sur sa séance et ne bloque pas les autres.",
                        "En cas d'annulation en cours de mois, les séances déjà passées restent dues. Les séances à venir sont remboursées selon la politique d'annulation du tuteur appliquée à chacune (selon son heure de début) si le payeur annule, et intégralement si le tuteur annule. Une séance peut aussi être annulée seule, selon les mêmes règles.",
                        $"Le renouvellement est manuel par défaut : un rappel est envoyé {TutorMonthlyPlan.RenewalReminderDaysBefore} jours avant la fin. Si le mois est payé par solde, le renouvellement automatique peut être activé : il est tenté {TutorMonthlyPlan.AutoRenewDaysBefore} jours avant la fin, pour les mêmes créneaux s'ils sont encore libres et si le solde suffit ; sinon rien n'est débité et le payeur est prévenu.",
                    },
                },
                new()
                {
                    Id = "limites-tuteur",
                    Title = "Ce que chaque tuteur peut régler",
                    Paragraphs =
                    {
                        $"Remboursement intégral pour une annulation faite au moins N heures avant la séance, avec N entre {TutorPolicy.FullRefundHoursMin} h et {TutorPolicy.FullRefundHoursMax} h.",
                        $"Part remboursée entre les deux seuils : de {TutorPolicy.PartialRefundPercentMin} % à {TutorPolicy.PartialRefundPercentMax} %.",
                        $"Aucun remboursement pour une annulation faite moins de M heures avant la séance, avec M entre {TutorPolicy.NoRefundHoursMin} h et {TutorPolicy.NoRefundHoursMax} h.",
                        $"Sans réglage particulier : remboursement intégral jusqu'à {TutorPolicy.DefaultFullRefundHours} h avant, {TutorPolicy.DefaultPartialRefundPercent} % ensuite, rien à moins de {TutorPolicy.DefaultNoRefundHours} h.",
                    },
                },
                new()
                {
                    Id = "contestation",
                    Title = $"Contestation pendant {h} h",
                    Paragraphs =
                    {
                        $"Après la séance, tu as {h} heures pour la contester depuis « Mes réservations » si elle n'a pas eu lieu ou s'est mal passée. Passé ce délai sans contestation, l'argent est versé au tuteur, qui en dispose librement.",
                        "Une contestation bloque l'argent jusqu'à la décision du support WinPlus, qui peut rembourser tout ou partie de la séance ou la payer au tuteur. Si tu obtiens gain de cause, tu es remboursé intégralement sur ton portefeuille, frais Mobile Money compris : WinPlus les prend à sa charge.",
                    },
                },
                new()
                {
                    Id = "comportement",
                    Title = "Comportement attendu",
                    Paragraphs =
                    {
                        "Élèves, parents et tuteurs se doivent respect et ponctualité. Prévenez le plus tôt possible en cas d'empêchement : une annulation précoce est toujours mieux remboursée.",
                        "Les échanges et les paiements passent par WinPlus. Ne demandez ni n'acceptez de paiement en dehors de la plateforme : il ne serait ni protégé ni remboursable.",
                        "Tout propos ou comportement déplacé peut être signalé au support WinPlus, qui peut suspendre un compte.",
                    },
                },
                new()
                {
                    Id = "securite-presentiel",
                    Title = "Sécurité des séances en présentiel",
                    Paragraphs =
                    {
                        "Pour une première rencontre, préférez un lieu public ou la présence d'un adulte au domicile de l'élève. Les parents d'un élève mineur doivent être informés de chaque séance en présentiel.",
                        "Ne communiquez pas d'informations personnelles inutiles. En cas de doute ou d'incident, interrompez la séance et prévenez le support WinPlus.",
                        "WinPlus renforce progressivement la vérification des tuteurs qui proposent le présentiel ; les badges affichés sur les profils indiquent les vérifications déjà faites.",
                    },
                },
            },
        };
    }
}
