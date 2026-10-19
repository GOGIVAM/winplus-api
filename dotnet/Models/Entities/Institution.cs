namespace Backend.Models.Entities;

/// <summary>
/// Institution éducative (université, école, etc.)
/// </summary>
public class Institution
{
    public int Id { get; set; }
    
    /// <summary>
    /// Nom de l'institution
    /// </summary>
    public string Name { get; set; } = string.Empty;
    
    /// <summary>
    /// Code de l'institution
    /// </summary>
    public string? Code { get; set; }
    
    /// <summary>
    /// Pays de l'institution
    /// </summary>
    public string Country { get; set; } = string.Empty;
    
    /// <summary>
    /// Ville
    /// </summary>
    public string? City { get; set; }
    
    /// <summary>
    /// Adresse
    /// </summary>
    public string? Address { get; set; }
    
    /// <summary>
    /// Email de contact
    /// </summary>
    public string? Email { get; set; }
    
    /// <summary>
    /// Téléphone de contact
    /// </summary>
    public string? Phone { get; set; }
    
    /// <summary>
    /// Région
    /// </summary>
    public string? Region { get; set; }
    
    /// <summary>
    /// Type d'institution (University, School, College, etc.)
    /// </summary>
    public string? Type { get; set; }
    
    /// <summary>
    /// Indicateur d'activité
    /// </summary>
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Portefeuille institution (lot 9.3, suivi 17.4) : compte institution
    /// titulaire des écritures du registre (WalletTransactions, OwnerType
    /// « institution »). Fixé une fois, au premier usage, sur le premier compte
    /// institution qui l'ouvre ; tous les comptes de l'institution voient et
    /// utilisent ce même portefeuille. Suivi 17.14 : seul un administrateur
    /// WinPlus peut changer de titulaire (<see cref="InstitutionWalletHolderChange"/>),
    /// le solde étant transféré par deux écritures du registre ; le compte
    /// titulaire ne peut pas être supprimé tant qu'il porte le portefeuille.
    /// </summary>
    public int? WalletOwnerUserId { get; set; }

    // ── Profil pédagogique (onboarding, Migrations/SQL_InstitutionOnboarding.sql) ──

    /// <summary>Cycles enseignés : primaire, secondaire, superieur, concours (séparés par des virgules).</summary>
    [System.ComponentModel.DataAnnotations.MaxLength(100)]
    public string? Cycles { get; set; }

    /// <summary>Sous-système : fr, en ou bilingue.</summary>
    [System.ComponentModel.DataAnnotations.MaxLength(20)]
    public string? Subsystem { get; set; }

    /// <summary>Enseignement : general, technique ou both.</summary>
    [System.ComponentModel.DataAnnotations.MaxLength(20)]
    public string? Teaching { get; set; }

    public int? StudentsEstimate { get; set; }

    /// <summary>Priorités choisies à l'onboarding (séparées par des virgules).</summary>
    [System.ComponentModel.DataAnnotations.MaxLength(200)]
    public string? Goals { get; set; }

    public DateTime? OnboardedAt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
    public bool IsDeleted { get; set; } = false;
}
