using Microsoft.EntityFrameworkCore;
using Backend.Models.Entities;

namespace Backend.Data;

/// <summary>Cahier de textes des classes. Schéma posé par Migrations/SQL_ClassLogbook.sql.</summary>
public partial class ApplicationDbContext
{
    public DbSet<ClassLogbookEntry> ClassLogbookEntries => Set<ClassLogbookEntry>();

    private void OnModelCreatingClassLogbook(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ClassLogbookEntry>(entity =>
        {
            entity.ToTable("ClassLogbookEntries");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.LessonDate).HasColumnType("date");
            entity.Property(e => e.HomeworkDue).HasColumnType("date");
            entity.HasIndex(e => new { e.StudyGroupId, e.LessonDate });
            entity.HasOne(e => e.StudyGroup).WithMany().HasForeignKey(e => e.StudyGroupId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.Author).WithMany().HasForeignKey(e => e.AuthorUserId).OnDelete(DeleteBehavior.Cascade);
        });
    }
}
