using Microsoft.EntityFrameworkCore;
using Backend.Models.Entities;

namespace Backend.Data;

/// <summary>
/// Entités ajoutées par les sprints S1→S7 (groupes d'étude, notes de révision,
/// questions ratées, classes enseignant, crédits parent, annuaire institution).
///
/// Cette partie est isolée pour ne pas toucher au fichier historique :
/// ApplicationDbContext doit être déclaré "partial" et appeler
/// OnModelCreatingSprints(modelBuilder) à la fin de son OnModelCreating.
/// Voir README-PATCH.md § 2.
/// </summary>
public partial class ApplicationDbContext
{
    public DbSet<StudyGroup> StudyGroups => Set<StudyGroup>();
    public DbSet<StudyGroupMember> StudyGroupMembers => Set<StudyGroupMember>();
    public DbSet<RevisionNote> RevisionNotes => Set<RevisionNote>();
    public DbSet<RevisionTag> RevisionTags => Set<RevisionTag>();
    public DbSet<TeacherClassStudent> TeacherClassStudents => Set<TeacherClassStudent>();
    public DbSet<ParentCreditLedger> ParentCreditLedgers => Set<ParentCreditLedger>();
    public DbSet<InstitutionStudent> InstitutionStudents => Set<InstitutionStudent>();

    private void OnModelCreatingSprints(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<StudyGroup>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.JoinCode).IsUnique();
            entity.HasIndex(e => e.OwnerId);
            entity.HasOne(e => e.Owner)
                  .WithMany()
                  .HasForeignKey(e => e.OwnerId)
                  .OnDelete(DeleteBehavior.Cascade);
            entity.HasMany(e => e.Members)
                  .WithOne(m => m.StudyGroup)
                  .HasForeignKey(m => m.StudyGroupId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<StudyGroupMember>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.StudyGroupId, e.UserId }).IsUnique();
            entity.HasOne(e => e.User)
                  .WithMany()
                  .HasForeignKey(e => e.UserId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<RevisionNote>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.UserId, e.SubjectId });
            entity.HasOne(e => e.User)
                  .WithMany()
                  .HasForeignKey(e => e.UserId)
                  .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.Subject)
                  .WithMany()
                  .HasForeignKey(e => e.SubjectId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<RevisionTag>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.UserId, e.SubjectId, e.Label }).IsUnique();
            entity.HasOne(e => e.User)
                  .WithMany()
                  .HasForeignKey(e => e.UserId)
                  .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.Subject)
                  .WithMany()
                  .HasForeignKey(e => e.SubjectId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<QuizMistake>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.UserId, e.IsResolved });
            entity.HasIndex(e => e.CreatedAt);
            entity.HasOne(e => e.User)
                  .WithMany()
                  .HasForeignKey(e => e.UserId)
                  .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.Quiz)
                  .WithMany()
                  .HasForeignKey(e => e.QuizId)
                  .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<TeacherClassStudent>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.TeacherClassId, e.StudentId }).IsUnique();
            entity.HasOne(e => e.TeacherClass)
                  .WithMany()
                  .HasForeignKey(e => e.TeacherClassId)
                  .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.Student)
                  .WithMany()
                  .HasForeignKey(e => e.StudentId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ParentCreditLedger>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Amount).HasPrecision(12, 2);
            entity.HasIndex(e => new { e.ParentId, e.PeriodStart });
            entity.HasOne(e => e.Parent)
                  .WithMany()
                  .HasForeignKey(e => e.ParentId)
                  .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.Child)
                  .WithMany()
                  .HasForeignKey(e => e.ChildId)
                  .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<InstitutionStudent>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.InstitutionId, e.StudentId }).IsUnique();
            entity.HasIndex(e => e.InstitutionId);
            entity.HasOne(e => e.Institution)
                  .WithMany()
                  .HasForeignKey(e => e.InstitutionId)
                  .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.Student)
                  .WithMany()
                  .HasForeignKey(e => e.StudentId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<CourseContent>(entity =>
        {
            entity.HasIndex(e => e.CreatedByUserId);
            entity.HasIndex(e => e.Status);
        });
    }
}
