using Microsoft.EntityFrameworkCore;
using Backend.Models.Entities;

namespace Backend.Data;

/// <summary>Emploi du temps des classes. Schéma posé par Migrations/SQL_ClassTimetable.sql.</summary>
public partial class ApplicationDbContext
{
    public DbSet<ClassTimetableSlot> ClassTimetableSlots => Set<ClassTimetableSlot>();

    private void OnModelCreatingClassTimetable(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ClassTimetableSlot>(entity =>
        {
            entity.ToTable("ClassTimetableSlots");
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.StudyGroupId);
            entity.HasIndex(e => e.DayOfWeek);
            entity.HasOne(e => e.StudyGroup).WithMany().HasForeignKey(e => e.StudyGroupId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.Teacher).WithMany().HasForeignKey(e => e.TeacherUserId).OnDelete(DeleteBehavior.SetNull);
        });
    }
}
