using Backend.Models.DTOs;

namespace Backend.Services;

/// <summary>
/// Interface pour les services HomePage
/// </summary>
public interface IHomeService
{
    /// <summary>
    /// Récupère les statistiques globales de la plateforme
    /// </summary>
    Task<HomeStatsDto> GetHomeStatsAsync();

    /// <summary>
    /// Récupère les fonctionnalités de la page d'accueil
    /// </summary>
    Task<IEnumerable<HomeFeatureDto>> GetHomeFeaturesAsync();

    /// <summary>
    /// Récupère les informations de contact
    /// </summary>
    Task<IEnumerable<ContactInfoDto>> GetContactInfoAsync();

    /// <summary>
    /// Récupère le contenu "À propos"
    /// </summary>
    Task<PageContentDto> GetAboutContentAsync();

    /// <summary>
    /// Récupère les données du footer
    /// </summary>
    Task<FooterDto> GetFooterAsync();

    /// <summary>
    /// Compte les épreuves publiées par type d'examen (vitrine)
    /// </summary>
    Task<IEnumerable<ExamCountDto>> GetExamCountsAsync();
}
