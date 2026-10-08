namespace Backend.Models.DTOs;

/// <summary>
/// Module 15 (lot 6) : score de complétude de profil généralisé aux rôles
/// élève, professeur (catalogue) et parent, sur le modèle éprouvé de
/// TutorProfileService.ComputeCompletion (mode Répétiteur). Même forme que
/// TutorProfileCompletionDto, dans un fichier séparé pour ne pas mélanger un
/// DTO transverse avec les DTOs spécifiques au mode Répétiteur.
/// </summary>
public class ProfileCompletionDto
{
    public int Score { get; set; }
    public List<ProfileMissingItemDto> MissingItems { get; set; } = new();
}

public class ProfileMissingItemDto
{
    public string Field { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
}
