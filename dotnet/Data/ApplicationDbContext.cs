using Microsoft.EntityFrameworkCore;
using Backend.Models.Entities;

namespace Backend.Data;

/// <summary>
/// ApplicationDbContext - Entity Framework Core DbContext for the application
/// </summary>
public class ApplicationDbContext : DbContext
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options)
    {
    }

    // DbSets for all entities
    public DbSet<User> Users => Set<User>();
    public DbSet<Subject> Subjects => Set<Subject>();
    public DbSet<CourseContent> CourseContents => Set<CourseContent>();
    public DbSet<Enrollment> Enrollments => Set<Enrollment>();
    public DbSet<CartItem> CartItems => Set<CartItem>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderItem> OrderItems => Set<OrderItem>();
    public DbSet<Favorite> Favorites => Set<Favorite>();
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<LearningHistory> LearningHistories => Set<LearningHistory>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<AnalyticsEvent> AnalyticsEvents => Set<AnalyticsEvent>();
    public DbSet<Review> Reviews => Set<Review>();
    public DbSet<PromoCode> PromoCodes => Set<PromoCode>();
    public DbSet<PromoCodeUsage> PromoCodeUsages => Set<PromoCodeUsage>();
    public DbSet<FavoriteCollection> FavoriteCollections => Set<FavoriteCollection>();
    public DbSet<Certificate> Certificates => Set<Certificate>();
    
    // Learning Resource entities (Exams, Quizzes, Revisions)
    public DbSet<Exam> Exams => Set<Exam>();
    public DbSet<Quiz> Quizzes => Set<Quiz>();
    public DbSet<QuizAttempt> QuizAttempts => Set<QuizAttempt>();
    public DbSet<Revision> Revisions => Set<Revision>();
    public DbSet<RevisionEnrollment> RevisionEnrollments => Set<RevisionEnrollment>();
    
    // Chatbot entities
    public DbSet<Conversation> Conversations => Set<Conversation>();
    public DbSet<Message> Messages => Set<Message>();
    public DbSet<ChatbotContext> ChatbotContexts => Set<ChatbotContext>();

    // Authentication & Security entities
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<DeviceInfo> DeviceInfos => Set<DeviceInfo>();
    public DbSet<EmailVerificationToken> EmailVerificationTokens => Set<EmailVerificationToken>();
    public DbSet<PasswordResetToken> PasswordResetTokens => Set<PasswordResetToken>();
    public DbSet<TwoFactorToken> TwoFactorTokens => Set<TwoFactorToken>();
    public DbSet<BackupCode> BackupCodes => Set<BackupCode>();
    public DbSet<OAuthAccount> OAuthAccounts => Set<OAuthAccount>();

    // User Settings entities
    public DbSet<UserNotificationSettings> UserNotificationSettings => Set<UserNotificationSettings>();
    public DbSet<UserPrivacySettings> UserPrivacySettings => Set<UserPrivacySettings>();
    public DbSet<UserTwoFactorAuthentication> UserTwoFactorAuthentications => Set<UserTwoFactorAuthentication>();
    public DbSet<UserSession> UserSessions => Set<UserSession>();

    // Backend Alignment entities (PricingPlans, Institutions, etc.)
    public DbSet<PricingPlan> PricingPlans => Set<PricingPlan>();
    public DbSet<Institution> Institutions => Set<Institution>();
    public DbSet<Announcement> Announcements => Set<Announcement>();
    public DbSet<Event> Events => Set<Event>();
    public DbSet<Session> Sessions => Set<Session>();
    public DbSet<Subscription> Subscriptions => Set<Subscription>();

    // Category and Learning entities
    public DbSet<Level> Levels => Set<Level>();
    public DbSet<Goal> Goals => Set<Goal>();
    public DbSet<HomePage> HomePages => Set<HomePage>();
    public DbSet<HomePageFeature> HomePageFeatures => Set<HomePageFeature>();
    public DbSet<Page> Pages => Set<Page>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Configure User entity
        modelBuilder.Entity<User>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.CognitoId).IsRequired(false).HasMaxLength(255);
            entity.Property(e => e.Email).IsRequired().HasMaxLength(255);
            entity.Property(e => e.FirstName).HasMaxLength(100);
            entity.Property(e => e.LastName).HasMaxLength(100);
            entity.Property(e => e.Bio).HasMaxLength(1000);
            entity.HasIndex(e => e.CognitoId).IsUnique().HasFilter("\"CognitoId\" IS NOT NULL");
            entity.HasIndex(e => e.Email).IsUnique();
        });

        // Configure Subject entity
        modelBuilder.Entity<Subject>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Title).IsRequired().HasMaxLength(255);
            entity.Property(e => e.Description).HasMaxLength(2000);
            entity.Property(e => e.Category).HasMaxLength(100);
            entity.Property(e => e.Price).HasPrecision(10, 2);
            entity.Property(e => e.AverageRating).HasPrecision(3, 2);
            entity.HasMany(e => e.Contents)
                .WithOne(c => c.Subject)
                .HasForeignKey(c => c.SubjectId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // Configure CourseContent entity
        modelBuilder.Entity<CourseContent>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Title).IsRequired().HasMaxLength(255);
            entity.Property(e => e.Description).HasMaxLength(2000);
            entity.HasOne(e => e.Subject)
                .WithMany(s => s.Contents)
                .HasForeignKey(e => e.SubjectId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // Configure Enrollment entity
        modelBuilder.Entity<Enrollment>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.ProgressPercentage).HasPrecision(5, 2);
            entity.HasOne(e => e.User)
                .WithMany(u => u.Enrollments)
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.Subject)
                .WithMany(s => s.Enrollments)
                .HasForeignKey(e => e.SubjectId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(e => new { e.UserId, e.SubjectId }).IsUnique();
        });

        // Configure CartItem entity
        modelBuilder.Entity<CartItem>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Price).HasPrecision(10, 2);
            entity.HasOne(e => e.User)
                .WithMany(u => u.CartItems)
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.Subject)
                .WithMany(s => s.CartItems)
                .HasForeignKey(e => e.SubjectId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(e => new { e.UserId, e.SubjectId }).IsUnique();
        });

        // Configure Order entity
        modelBuilder.Entity<Order>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.OrderNumber).IsRequired().HasMaxLength(50);
            entity.Property(e => e.TotalAmount).HasPrecision(12, 2);
            entity.Property(e => e.Status).HasMaxLength(50);
            entity.Property(e => e.PaymentMethod).HasMaxLength(50);
            // ✅ CORRECTION: Mapper CreatedAt vers la colonne PostreSQL existante OrderDate
            entity.Property(e => e.CreatedAt).HasColumnName("OrderDate");
            entity.HasOne(e => e.User)
                .WithMany(u => u.Orders)
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(e => e.OrderNumber).IsUnique();
        });

        // Configure OrderItem entity
        modelBuilder.Entity<OrderItem>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.PriceAtPurchase).HasPrecision(10, 2);
            entity.HasOne(e => e.Order)
                .WithMany(o => o.Items)
                .HasForeignKey(e => e.OrderId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.Subject)
                .WithMany()
                .HasForeignKey(e => e.SubjectId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // Configure Favorite entity
        modelBuilder.Entity<Favorite>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasOne(e => e.User)
                .WithMany(u => u.Favorites)
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.Subject)
                .WithMany(s => s.Favorites)
                .HasForeignKey(e => e.SubjectId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(e => new { e.UserId, e.SubjectId }).IsUnique();
        });

        // Configure LearningHistory entity
        modelBuilder.Entity<LearningHistory>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.ActivityType).HasMaxLength(50);
            entity.Property(e => e.QuizScore).HasPrecision(5, 2);
            entity.HasOne(e => e.User)
                .WithMany(u => u.LearningHistories)
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.Subject)
                .WithMany(s => s.LearningHistories)
                .HasForeignKey(e => e.SubjectId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.Content)
                .WithMany(c => c.LearningHistories)
                .HasForeignKey(e => e.ContentId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        // Configure Notification entity
        modelBuilder.Entity<Notification>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Title).IsRequired().HasMaxLength(255);
            entity.Property(e => e.Message).IsRequired().HasMaxLength(2000);
            entity.Property(e => e.Type).HasMaxLength(50);
            entity.Property(e => e.RelatedEntityType).HasMaxLength(50);
            entity.HasOne(e => e.User)
                .WithMany(u => u.Notifications)
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // Configure Payment entity
        modelBuilder.Entity<Payment>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.OrderId).IsRequired();
            entity.Property(e => e.Amount).HasPrecision(12, 2).IsRequired();
            entity.Property(e => e.Currency).HasMaxLength(3).IsRequired();
            entity.Property(e => e.Status).HasMaxLength(50).IsRequired();
            entity.Property(e => e.PaymentMethod).HasMaxLength(100);
            entity.Property(e => e.TransactionId).HasMaxLength(255);
            entity.HasOne(e => e.User)
                .WithMany(u => u.Payments)
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.Order)
                .WithMany(o => o.Payments)
                .HasForeignKey(e => e.OrderId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(e => e.TransactionId).IsUnique().IsUnique(false);
        });

        // Configure AnalyticsEvent entity
        modelBuilder.Entity<AnalyticsEvent>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.EventType).IsRequired().HasMaxLength(100);
            entity.Property(e => e.EventName).IsRequired().HasMaxLength(255);
            entity.Property(e => e.EventCategory).HasMaxLength(100);
            entity.Property(e => e.IpAddress).HasMaxLength(45);
            entity.Property(e => e.EventData).HasColumnType("jsonb");
            entity.HasOne(e => e.User)
                .WithMany(u => u.AnalyticsEvents)
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        // Configure PromoCode entity
        modelBuilder.Entity<PromoCode>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Code).IsRequired().HasMaxLength(50);
            entity.Property(e => e.DiscountType).IsRequired().HasMaxLength(50);
            entity.Property(e => e.DiscountValue).IsRequired().HasPrecision(10, 2);
            entity.Property(e => e.MinimumPurchase).HasPrecision(10, 2);
            entity.Property(e => e.MaximumDiscount).HasPrecision(10, 2);
            entity.Property(e => e.ApplicableSubjectIds).HasColumnType("jsonb");
            entity.Property(e => e.IsActive).IsRequired().HasDefaultValue(true);
            entity.HasIndex(e => e.Code).IsUnique();
            entity.HasIndex(e => e.IsActive);
            entity.HasIndex(e => new { e.ValidFrom, e.ValidUntil });
            entity.HasOne(e => e.Creator)
                .WithMany(u => u.CreatedPromoCodes)
                .HasForeignKey(e => e.CreatedBy)
                .OnDelete(DeleteBehavior.SetNull);
            entity.HasMany(e => e.Usages)
                .WithOne(u => u.PromoCode)
                .HasForeignKey(u => u.PromoCodeId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // Configure PromoCodeUsage entity
        modelBuilder.Entity<PromoCodeUsage>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.DiscountAmount).HasPrecision(10, 2);
            entity.HasIndex(e => e.PromoCodeId);
            entity.HasIndex(e => e.UserId);
            entity.HasIndex(e => e.OrderId);
            entity.HasOne(e => e.PromoCode)
                .WithMany(p => p.Usages)
                .HasForeignKey(e => e.PromoCodeId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.User)
                .WithMany(u => u.PromoCodeUsages)
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // Configure FavoriteCollection entity
        modelBuilder.Entity<FavoriteCollection>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).IsRequired().HasMaxLength(100);
            entity.Property(e => e.Description).HasMaxLength(500);
            entity.Property(e => e.Color).HasMaxLength(20);
            entity.Property(e => e.Icon).HasMaxLength(50);
            entity.HasIndex(e => e.UserId);
            entity.HasIndex(e => new { e.Name, e.UserId }).IsUnique();
            entity.HasOne(e => e.User)
                .WithMany(u => u.FavoriteCollections)
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasMany(e => e.Favorites)
                .WithOne(f => f.Collection)
                .HasForeignKey(f => f.CollectionId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        // Configure Certificate entity
        modelBuilder.Entity<Certificate>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.CertificateNumber).IsRequired().HasMaxLength(100);
            entity.Property(e => e.VerificationCode).IsRequired().HasMaxLength(50);
            entity.Property(e => e.FileUrl).HasMaxLength(500);
            entity.Property(e => e.Grade).HasPrecision(5, 2);
            entity.HasIndex(e => e.CertificateNumber).IsUnique();
            entity.HasIndex(e => e.VerificationCode).IsUnique();
            entity.HasIndex(e => e.UserId);
            entity.HasIndex(e => e.SubjectId);
            entity.HasIndex(e => e.EnrollmentId).IsUnique();
            entity.HasOne(e => e.User)
                .WithMany(u => u.Certificates)
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.Subject)
                .WithMany(s => s.Certificates)
                .HasForeignKey(e => e.SubjectId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.Enrollment)
                .WithOne(en => en.Certificate)
                .HasForeignKey<Certificate>(e => e.EnrollmentId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // Configure Conversation entity (Chatbot)
        modelBuilder.Entity<Conversation>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Title).IsRequired().HasMaxLength(255);
            entity.Property(e => e.Tags).HasColumnType("jsonb");
            entity.Property(e => e.Metadata).HasColumnType("jsonb");
            entity.HasIndex(e => e.UserId);
            entity.HasIndex(e => e.IsActive);
            entity.HasIndex(e => e.LastMessageAt);
            entity.HasIndex(e => e.IsDeleted);
            entity.HasOne(e => e.User)
                .WithMany()
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // Configure Message entity (Chatbot)
        modelBuilder.Entity<Message>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Role).IsRequired().HasMaxLength(20);
            entity.Property(e => e.Content).IsRequired();
            entity.Property(e => e.Attachments).HasColumnType("jsonb");
            entity.Property(e => e.FeedbackComment).HasMaxLength(1000);
            entity.HasIndex(e => e.ConversationId);
            entity.HasIndex(e => e.CreatedAt);
            entity.HasIndex(e => e.IsDeleted);
            entity.HasOne(e => e.Conversation)
                .WithMany(c => c.Messages)
                .HasForeignKey(e => e.ConversationId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // Configure ChatbotContext entity
        modelBuilder.Entity<ChatbotContext>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.EducationLevel).HasMaxLength(50);
            entity.Property(e => e.Grade).HasMaxLength(50);
            entity.Property(e => e.LearningStyle).HasMaxLength(50);
            entity.Property(e => e.UserObjectives).HasColumnType("jsonb");
            entity.Property(e => e.EnrolledSubjects).HasColumnType("jsonb");
            entity.Property(e => e.RecentActivity).HasColumnType("jsonb");
            entity.Property(e => e.NavigationHistory).HasColumnType("jsonb");
            entity.Property(e => e.Preferences).HasColumnType("jsonb");
            entity.Property(e => e.Strengths).HasColumnType("jsonb");
            entity.Property(e => e.Weaknesses).HasColumnType("jsonb");
            entity.HasIndex(e => e.UserId).IsUnique();
            entity.HasOne(e => e.User)
                .WithOne()
                .HasForeignKey<ChatbotContext>(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // Configure RefreshToken entity
        modelBuilder.Entity<RefreshToken>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Token).IsRequired().HasMaxLength(500);
            entity.Property(e => e.ExpiresAt).IsRequired();
            entity.HasIndex(e => e.UserId);
            entity.HasIndex(e => e.Token).IsUnique();
            entity.HasOne(e => e.User)
                .WithMany(u => u.RefreshTokens)
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // Configure DeviceInfo entity
        modelBuilder.Entity<DeviceInfo>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.UserAgent).IsRequired().HasMaxLength(500);
            entity.Property(e => e.IpAddress).IsRequired().HasMaxLength(45);
            entity.Property(e => e.OsName).HasColumnName("OSName");
            entity.Property(e => e.OsVersion).HasColumnName("OSVersion");
            entity.Property(e => e.BrowserVersion).HasColumnName("BrowserVersion");
            entity.HasIndex(e => e.UserId);
            entity.HasIndex(e => new { e.UserId, e.DeviceFingerprint });
            entity.HasOne(e => e.User)
                .WithMany(u => u.DeviceInfos)
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // Configure EmailVerificationToken entity
        modelBuilder.Entity<EmailVerificationToken>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.VerificationCode).IsRequired().HasMaxLength(6);
            entity.HasIndex(e => e.UserId);
            entity.HasIndex(e => new { e.UserId, e.VerificationCode });
            entity.HasOne(e => e.User)
                .WithMany(u => u.EmailVerificationTokens)
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // Configure PasswordResetToken entity
        modelBuilder.Entity<PasswordResetToken>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Token).IsRequired().HasMaxLength(500);
            entity.HasIndex(e => e.Token).IsUnique();
            entity.HasIndex(e => e.UserId);
            entity.HasOne(e => e.User)
                .WithMany(u => u.PasswordResetTokens)
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // Configure TwoFactorToken entity
        modelBuilder.Entity<TwoFactorToken>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.UserId).IsUnique();
            entity.Property(e => e.TotpSecret).HasMaxLength(255);
            entity.HasOne(e => e.User)
                .WithOne(u => u.TwoFactorToken)
                .HasForeignKey<TwoFactorToken>(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasMany(e => e.BackupCodes)
                .WithOne(b => b.TwoFactorToken)
                .HasForeignKey(b => b.TwoFactorTokenId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // Configure BackupCode entity
        modelBuilder.Entity<BackupCode>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Code).IsRequired().HasMaxLength(100);
            entity.HasIndex(e => e.Code).IsUnique();
            entity.HasOne(e => e.TwoFactorToken)
                .WithMany(t => t.BackupCodes)
                .HasForeignKey(e => e.TwoFactorTokenId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // Configure OAuthAccount entity
        modelBuilder.Entity<OAuthAccount>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Provider).IsRequired().HasMaxLength(50);
            entity.Property(e => e.ProviderUserId).IsRequired().HasMaxLength(255);
            entity.HasIndex(e => new { e.Provider, e.ProviderUserId }).IsUnique();
            entity.HasIndex(e => e.UserId);
            entity.HasOne(e => e.User)
                .WithMany(u => u.OAuthAccounts)
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // Configure Exam entity
        // Configure Exam entity
modelBuilder.Entity<Exam>(entity =>
{
    entity.ToTable("Exams");
    entity.HasKey(e => e.Id);
    entity.Property(e => e.Title).IsRequired().HasMaxLength(255);
    entity.Property(e => e.Description).HasMaxLength(2000);
    entity.Property(e => e.ExamType).IsRequired().HasMaxLength(100);
    entity.Property(e => e.Year).IsRequired();
    entity.Property(e => e.Category).HasColumnName("Category").HasMaxLength(100);
    entity.Property(e => e.DurationMinutes).HasColumnName("Duration");
    entity.Property(e => e.DocumentUrl).HasColumnName("DocumentUrl").HasMaxLength(500);
    entity.Property(e => e.DownloadCount).HasColumnName("DownloadCount");
    entity.Property(e => e.Difficulty).HasMaxLength(50);
    entity.Property(e => e.Session).HasMaxLength(50);
    entity.Property(e => e.Level).HasMaxLength(100);
    entity.Property(e => e.CorrectionUrl).HasMaxLength(500);
    entity.HasIndex(e => e.ExamType);
    entity.HasIndex(e => e.Category);
    entity.HasIndex(e => e.Year);
    entity.HasIndex(e => new { e.ExamType, e.Category, e.Year });
    entity.HasOne(e => e.SubjectReference)
        .WithMany(s => s.Exams)
        .HasForeignKey(e => e.SubjectId)
        .OnDelete(DeleteBehavior.SetNull);
});

        // Configure Quiz entity
        modelBuilder.Entity<Quiz>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Title).IsRequired().HasMaxLength(255);
            entity.Property(e => e.Description).HasMaxLength(2000);
            entity.Property(e => e.Subject).IsRequired().HasMaxLength(100);
            entity.Property(e => e.QuestionsJson).IsRequired().HasColumnType("jsonb");
            entity.Property(e => e.AverageScore).HasPrecision(5, 2);
            entity.HasIndex(e => e.Subject);
            entity.HasIndex(e => e.IsAIGenerated);
            entity.HasOne(e => e.Subject_Reference)
                .WithMany(s => s.Quizzes)
                .HasForeignKey(e => e.SubjectId)
                .OnDelete(DeleteBehavior.SetNull);
            entity.HasOne(e => e.Exam_Reference)
                .WithMany(ex => ex.Quizzes)
                .HasForeignKey(e => e.ExamId)
                .OnDelete(DeleteBehavior.SetNull);
            entity.HasMany(e => e.Attempts_Collection)
                .WithOne(a => a.Quiz)
                .HasForeignKey(a => a.QuizId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // Configure QuizAttempt entity
        modelBuilder.Entity<QuizAttempt>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.UserAnswersJson).IsRequired().HasColumnType("jsonb");
            entity.Property(e => e.Score).HasPrecision(5, 2);
            entity.HasIndex(e => e.UserId);
            entity.HasIndex(e => e.QuizId);
            entity.HasIndex(e => new { e.UserId, e.QuizId, e.AttemptNumber });
            entity.HasOne(e => e.User)
                .WithMany(u => u.QuizAttempts)
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.Quiz)
                .WithMany(q => q.Attempts_Collection)
                .HasForeignKey(e => e.QuizId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // Configure Revision entity
        modelBuilder.Entity<Revision>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Title).IsRequired().HasMaxLength(255);
            entity.Property(e => e.Description).HasMaxLength(2000);
            entity.Property(e => e.Subject).IsRequired().HasMaxLength(100);
            entity.Property(e => e.Type).IsRequired().HasMaxLength(50);
            entity.Property(e => e.ImprovedScore).HasPrecision(5, 2);
            entity.HasIndex(e => e.Subject);
            entity.HasIndex(e => e.Type);
            entity.HasIndex(e => new { e.SubjectId, e.Status });
            entity.HasOne(e => e.Subject_Reference)
                .WithMany(s => s.Revisions)
                .HasForeignKey(e => e.SubjectId)
                .OnDelete(DeleteBehavior.SetNull);
            entity.HasOne(e => e.Exam_Reference)
                .WithMany(ex => ex.Revisions)
                .HasForeignKey(e => e.ExamId)
                .OnDelete(DeleteBehavior.SetNull);
            entity.HasOne(e => e.CreatedByUser)
                .WithMany() // No back-reference
                .HasForeignKey(e => e.CreatedByUserId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        // Configure RevisionEnrollment entity
        modelBuilder.Entity<RevisionEnrollment>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.OriginalScore).HasPrecision(5, 2);
            entity.Property(e => e.ProgressPercentage).HasPrecision(5, 2);
            entity.Property(e => e.FinalScore).HasPrecision(5, 2);
            entity.Property(e => e.ScoreImprovement).HasPrecision(5, 2);
            entity.HasIndex(e => e.UserId);
            entity.HasIndex(e => e.RevisionId);
            entity.HasIndex(e => new { e.UserId, e.RevisionId }).IsUnique();
            entity.HasIndex(e => new { e.UserId, e.Status });
            entity.HasOne(e => e.User)
                .WithMany(u => u.RevisionEnrollments)
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.Revision)
                .WithMany(r => r.UserEnrollments)
                .HasForeignKey(e => e.RevisionId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.AssociatedLearningHistory)
                .WithMany() // No back-reference
                .HasForeignKey(e => e.AssociatedLearningHistoryId)
                .OnDelete(DeleteBehavior.SetNull);
        });
        // Configure UserNotificationSettings entity
        modelBuilder.Entity<UserNotificationSettings>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.UserId).IsUnique();
            entity.HasOne(e => e.User)
                .WithOne()
                .HasForeignKey<UserNotificationSettings>(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // Configure UserPrivacySettings entity
        modelBuilder.Entity<UserPrivacySettings>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.UserId).IsUnique();
            entity.HasOne(e => e.User)
                .WithOne()
                .HasForeignKey<UserPrivacySettings>(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // Configure UserTwoFactorAuthentication entity
        modelBuilder.Entity<UserTwoFactorAuthentication>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.UserId).IsUnique();
            entity.HasOne(e => e.User)
                .WithOne()
                .HasForeignKey<UserTwoFactorAuthentication>(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // Configure UserSession entity
        modelBuilder.Entity<UserSession>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.UserId);
            entity.HasIndex(e => e.DeviceName);
            entity.HasOne(e => e.User)
                .WithMany(u => u.Sessions)
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // Configure PricingPlan entity
        modelBuilder.Entity<PricingPlan>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Price).HasPrecision(10, 2);
            entity.Property(e => e.Name).IsRequired().HasMaxLength(100);
            entity.Property(e => e.Category).HasMaxLength(50);
        });

        // Configure Institution entity
        modelBuilder.Entity<Institution>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).IsRequired().HasMaxLength(255);
            entity.Property(e => e.Country).HasMaxLength(100);
        });

        // Configure Announcement entity
        modelBuilder.Entity<Announcement>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Title).IsRequired().HasMaxLength(255);
            entity.Property(e => e.Content).IsRequired();
        });

        // Configure Event entity
        modelBuilder.Entity<Event>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Title).IsRequired().HasMaxLength(255);
        });

        // Configure Session entity
        modelBuilder.Entity<Session>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Title).IsRequired().HasMaxLength(255);
        });

        // Configure Subscription entity
        modelBuilder.Entity<Subscription>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.UserId);
            entity.HasOne(e => e.User)
                .WithMany(u => u.Subscriptions)
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.PricingPlan)
                .WithMany()
                .HasForeignKey(e => e.PricingPlanId)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }
}