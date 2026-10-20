using Microsoft.EntityFrameworkCore;
using Backend.Models.Entities;

namespace Backend.Data;

/// <summary>Rendez-vous parent-professeur. Schéma posé par Migrations/SQL_ParentTeacherMeetings.sql.</summary>
public partial class ApplicationDbContext
{
    public DbSet<TeacherMeetingSlot> TeacherMeetingSlots => Set<TeacherMeetingSlot>();
    public DbSet<ParentTeacherMeeting> ParentTeacherMeetings => Set<ParentTeacherMeeting>();

    private void OnModelCreatingParentTeacherMeetings(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<TeacherMeetingSlot>(entity =>
        {
            entity.ToTable("TeacherMeetingSlots");
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.TeacherUserId, e.StartsAt });
            entity.HasOne(e => e.Teacher).WithMany().HasForeignKey(e => e.TeacherUserId).OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<ParentTeacherMeeting>(entity =>
        {
            entity.ToTable("ParentTeacherMeetings");
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.ParentUserId);
            entity.HasOne(e => e.Slot).WithMany().HasForeignKey(e => e.SlotId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.Parent).WithMany().HasForeignKey(e => e.ParentUserId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.Child).WithMany().HasForeignKey(e => e.ChildUserId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.Teacher).WithMany().HasForeignKey(e => e.TeacherUserId).OnDelete(DeleteBehavior.Cascade);
        });
    }
}
