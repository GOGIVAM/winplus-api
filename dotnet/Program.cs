using QuestPDF.Infrastructure;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using System.Text.Json;
using Backend.Data;
using Backend.Repositories;
using Backend.Services;
using Backend.Utilities;
using Backend.Middlewares;
using Backend.Extensions;
using Backend.Models.Entities;
using Microsoft.AspNetCore.HttpOverrides;
using Serilog;
using Serilog.Events;

QuestPDF.Settings.License = LicenseType.Community;

// ── Serilog : configuration avant builder.Build() ──────────────────────────
var isDevelopment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") == "Development";

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Is(isDevelopment ? LogEventLevel.Debug : LogEventLevel.Information)
    .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
    .MinimumLevel.Override("Microsoft.Hosting.Lifetime", LogEventLevel.Information)
    .MinimumLevel.Override("Microsoft.EntityFrameworkCore.Database.Command",
        isDevelopment ? LogEventLevel.Information : LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .WriteTo.Console(outputTemplate:
        "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
    .WriteTo.File(
        path: "logs/winplus-.txt",
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 30,
        outputTemplate:
            "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
    .CreateLogger();

var builder = WebApplication.CreateBuilder(args);

// Remplace le logging ASP.NET Core par Serilog
builder.Host.UseSerilog();

// ── Taille des requêtes ─────────────────────────────────────────────────────
// Les gros fichiers (vidéos de cours) ne passent plus par l'API : le navigateur
// les envoie par parties directement à S3 (AdminUploadsController). Kestrel n'a
// donc à accepter que les envois directs  images de vignette, PDF courts 
// plafonnés à 32 Mio.
builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MaxRequestBodySize = 32L * 1024 * 1024;
    // Un téléphone en 3G doit pouvoir finir son envoi : le débit minimal par
    // défaut (240 octets/s) coupe des connexions honnêtes.
    options.Limits.MinRequestBodyDataRate = new Microsoft.AspNetCore.Server.Kestrel.Core.MinDataRate(
        bytesPerSecond: 100, gracePeriod: TimeSpan.FromSeconds(30));
    options.Limits.RequestHeadersTimeout = TimeSpan.FromSeconds(60);
});

builder.Services.Configure<Microsoft.AspNetCore.Http.Features.FormOptions>(o =>
{
    o.MultipartBodyLengthLimit = 32L * 1024 * 1024;
    o.ValueLengthLimit         = int.MaxValue;
    o.MemoryBufferThreshold    = 1024 * 1024;
});

// Add services to the container
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        // ✅ Utiliser CamelCase pour la sérialisation JSON
        options.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        options.JsonSerializerOptions.WriteIndented = false;
        options.JsonSerializerOptions.DefaultIgnoreCondition = 
            System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull;
        // ❌ RETIRÉ: ReferenceHandler.Preserve crée des structures circulaires que le frontend ne peut pas parser
        // options.JsonSerializerOptions.ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.Preserve;
        // ✅ IgnoreCycles (différent de Preserve : ne change pas la forme du JSON,
        // n'ajoute pas de $id/$ref)  filet de sécurité pour tout endpoint qui
        // renvoie encore une entité EF brute avec ses navigations peuplées dans
        // les deux sens (ex. FavoritesController → Favorite.User.Favorites...
        // "A possible object cycle was detected", 500 systématique). Le vrai
        // correctif reste de projeter vers un DTO ; ceci évite un crash total si
        // un futur endpoint oublie de le faire.
        options.JsonSerializerOptions.ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles;
    });
// ❌ NE PAS ajouter AddNewtonsoftJson() - causes duplication

builder.Services.AddEndpointsApiExplorer();

// Configure Swagger with JWT authentication
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "WinPlus Educational API",
        Version = "v4.0",
        Description = "WinPlus Educational API"
    });
    
    // Add JWT Authentication to Swagger
    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Description = "JWT Authorization header using the Bearer scheme. Example: \"Bearer {token}\"",
        Name = "Authorization",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT"
    });
    
    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            Array.Empty<string>()
        }
    });
});

// Configure CORS
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowFrontend", policyBuilder =>
    {
        var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins")
            .Get<string[]>() ?? new[]
            {
                "http://localhost:3000",
                "http://localhost:5173",
                "https://winplus.cm",
                "https://www.winplus.cm"
            };
        
        policyBuilder
            .WithOrigins(allowedOrigins)
            .AllowCredentials()
            .WithMethods("GET", "POST", "PUT", "DELETE", "PATCH")
                .WithHeaders("Content-Type", "Authorization", "X-Requested-With", "Accept")
            .WithExposedHeaders("Content-Disposition");
    });
});

// Configure Entity Framework Core with PostgreSQL
builder.Services.AddDbContext<ApplicationDbContext>(options =>
{
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection"),
        pgOptions =>
        {
            pgOptions.MigrationsAssembly("backend");
            // Plusieurs Include de collections dans une même requête : en
            // SingleQuery, PostgreSQL renvoie un produit cartésien (lenteur du
            // dashboard admin). SplitQuery = une requête par collection.
            pgOptions.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery);
        });
    // Les entités avec soft-delete (HasQueryFilter !IsDeleted) sont l'extrémité
    // required de nombreuses relations  comportement voulu et maîtrisé : les
    // requêtes admin utilisent .IgnoreQueryFilters() quand elles ont besoin des
    // enregistrements supprimés. Supprimer les 49 avertissements parasites.
    options.ConfigureWarnings(w =>
        w.Ignore(CoreEventId.PossibleIncorrectRequiredNavigationWithQueryFilterInteractionWarning));
});

// Configure Authentication - Custom Auth
var jwtSecretKey = builder.Configuration["JWT:SecretKey"] 
    ?? throw new InvalidOperationException("JWT:SecretKey not configured");
var jwtIssuer = builder.Configuration["JWT:Issuer"] ?? "WinPlusApp";
var jwtAudience = builder.Configuration["JWT:Audience"] ?? "WinPlusUsers";
var useCustomAuth = builder.Configuration.GetValue<bool>("Auth:UseCustomAuth", true);

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        var key = System.Text.Encoding.UTF8.GetBytes(jwtSecretKey);
        
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwtIssuer,
            ValidateAudience = true,
            ValidAudience = jwtAudience,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(key),
            ClockSkew = TimeSpan.FromSeconds(60)
        };
        
        // ✅ IMPORTANT: Valide les tokens Bearer même sans [Authorize]
        options.SaveToken = true;
        options.IncludeErrorDetails = true;
        
        // Configure events for better logging
        options.Events = new JwtBearerEvents
        {
            OnAuthenticationFailed = context =>
            {
                // Uniquement logger  ne jamais écrire la réponse ici.
                // Pour les routes [Authorize], OnChallenge gère le 401.
                // Pour les routes [AllowAnonymous], le pipeline continue normalement.
                var logger = context.HttpContext.RequestServices
                    .GetRequiredService<ILogger<Program>>();

                logger.LogWarning(
                    "[JWT Auth Failed] Token invalide ou expiré  URL: {Url}  Error: {Error}",
                    context.Request.Path,
                    context.Exception?.Message
                );
                return Task.CompletedTask;
            },
            OnTokenValidated = context =>
            {
                var logger = context.HttpContext.RequestServices
                    .GetRequiredService<ILogger<Program>>();
                
                // Log successful validation
                var userId = context.Principal?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
                var email = context.Principal?.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value;
                var role = context.Principal?.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value;
                var authHeader = context.Request.Headers.Authorization.ToString();
                var tokenPreview = authHeader.Length > 20 ? authHeader.Substring(7, Math.Min(20, authHeader.Length - 7)) + "..." : "N/A";
                
                logger.LogInformation(
                    "[JWT Auth Success] ✅ Token validé avec succès\n" +
                    "URL: {Url}\n" +
                    "Method: {Method}\n" +
                    "UserId: {UserId}\n" +
                    "Email: {Email}\n" +
                    "Role: {Role}\n" +
                    "Token Preview: Bearer {TokenPreview}",
                    context.Request.Path,
                    context.Request.Method,
                    userId,
                    email,
                    role,
                    tokenPreview
                );
                return Task.CompletedTask;
            },
            OnChallenge = context =>
            {
                var logger = context.HttpContext.RequestServices
                    .GetRequiredService<ILogger<Program>>();
                var authHeader = context.Request.Headers.Authorization.ToString();
                var tokenPreview = authHeader.Length > 20 ? authHeader.Substring(7, Math.Min(20, authHeader.Length - 7)) + "..." : "MISSING";
                
                logger.LogWarning(
                    "[JWT Challenge] ⚠️ Authentification requise\n" +
                    "URL: {Url}\n" +
                    "Method: {Method}\n" +
                    "Authorization Header: Bearer {TokenPreview}\n" +
                    "Error: {Error}",
                    context.Request.Path,
                    context.Request.Method,
                    tokenPreview,
                    context.ErrorDescription
                );
                return Task.CompletedTask;
            }
        };
    });

builder.Services.AddAuthorization(options =>
{
    // Politique pour les administrateurs
    // RequireRole uses ClaimTypes.Role which matches the JWT "role" claim after MapInboundClaims mapping
    options.AddPolicy("AdminOnly", policy =>
    {
        policy.RequireAuthenticatedUser();
        policy.RequireRole("admin");
    });

    // Politique pour les instructeurs
    options.AddPolicy("InstructorOnly", policy =>
    {
        policy.RequireAuthenticatedUser();
        policy.RequireRole("teacher", "admin");
    });

    // Politique pour les parents
    options.AddPolicy("ParentOnly", policy =>
    {
        policy.RequireAuthenticatedUser();
        policy.RequireRole("parent", "admin");
    });

    // Module 20 : la politique "StudentOnly" est retirée plutôt qu'appliquée.
    // Elle autorisait student, teacher, parent ET admin, c'est-à-dire tous les
    // rôles que la plateforme crée : elle était donc strictement équivalente à
    // "AuthenticatedUser" ci-dessous, sous un nom trompeur qui suggérait un
    // filtrage de rôle inexistant. Aucun endpoint ne l'utilisait.

    // Politique pour les utilisateurs authentifiés
    options.AddPolicy("AuthenticatedUser", policy => 
        policy.RequireAuthenticatedUser());
    
    // Politique pour les utilisateurs avec email vérifié.
    //
    // Module 20 : elle n'exigeait pas explicitement un utilisateur
    // authentifié (l'assertion seule suffisait à refuser un anonyme, mais le
    // contrat était implicite), et elle ne prévoyait aucune sortie pour
    // l'administrateur. Appliquée à un flux d'administration, elle aurait
    // verrouillé l'administrateur lui-même si son adresse n'est pas marquée
    // vérifiée en base exactement le type de fonctionnalité morte que ce
    // projet a déjà produit plusieurs fois.
    options.AddPolicy("VerifiedEmailOnly", policy =>
    {
        policy.RequireAuthenticatedUser();
        policy.RequireAssertion(context =>
        {
            if (context.User.IsAdmin()) return true;

            var emailVerified = context.User.FindFirst("email_verified")?.Value;
            return string.Equals(emailVerified, "true", StringComparison.OrdinalIgnoreCase);
        });
    });
});

// ============ CUSTOM AUTH SERVICES ============
// Register custom authentication services (MAIN AUTH SYSTEM)
builder.Services.AddScoped<IJwtService, JwtService>();
// Modules 23/36 : jeton technique des tâches de fond vers FastAPI (audience et
// périmètre dédiés, même secret que les jetons utilisateurs).
builder.Services.AddSingleton<IServiceTokenProvider, ServiceTokenProvider>();
builder.Services.AddScoped<IEmailService, EmailService>();
builder.Services.AddScoped<IDeviceTrackingService, DeviceTrackingService>();
builder.Services.AddScoped<ICustomAuthService, CustomAuthService>();

builder.Services.AddMemoryCache();

// Register Repositories
builder.Services.AddScoped<IUserRepository, UserRepository>();
builder.Services.AddScoped<ISubjectRepository, SubjectRepository>();
builder.Services.AddScoped<IEnrollmentRepository, EnrollmentRepository>();
builder.Services.AddScoped<ICartRepository, CartRepository>();
builder.Services.AddScoped<IOrderRepository, OrderRepository>();
builder.Services.AddScoped<IFavoriteRepository, FavoriteRepository>();
builder.Services.AddScoped<IPaymentRepository, PaymentRepository>();
builder.Services.AddScoped<IHistoryRepository, HistoryRepository>();
builder.Services.AddScoped<IAnalyticsRepository, AnalyticsRepository>();

// Register Services
builder.Services.AddScoped<IUserService, UserService>();
builder.Services.AddScoped<ISubjectService, SubjectService>();
builder.Services.AddScoped<ICartService, CartService>();
// Pochette de document générée par WinAI à la demande (CoverController).
builder.Services.AddScoped<ICoverGenerationService, CoverGenerationService>();
builder.Services.AddScoped<IOrderService, OrderService>();
builder.Services.AddScoped<IEnrollmentService, EnrollmentService>();
// Règle d'accès unique aux contenus payants (Module 17) : partagée par la
// consultation, le téléchargement présigné et l'inscription.
builder.Services.AddScoped<IContentAccessService, ContentAccessService>();

// Règle unique du quota mensuel WinAI : partagée par l'affichage
// (GET /api/subscriptions/me) et par l'application réelle du quota
// (POST /api/chatbot/message et POST /api/chatbot/stream).
builder.Services.AddScoped<IAiQuotaService, AiQuotaService>();
// Recharge de quota WinAI (décision 8.5) : point d'extension seulement la
// recharge réelle dépend du wallet du Module 1/14, pas encore construit.
builder.Services.AddScoped<ITokenTopUpService, UnavailableTokenTopUpService>();

// Création réelle de l'abonnement à la confirmation du paiement (Module 18,
// correction §7.3) : sans elle, un client qui payait un abonnement n'obtenait
// aucune ligne Subscriptions, et le mur payant du Module 17 le bloquait.
builder.Services.AddScoped<ISubscriptionActivationService, SubscriptionActivationService>();
builder.Services.AddScoped<IPaymentService, PaymentService>();
builder.Services.AddScoped<IHistoryService, HistoryService>();
builder.Services.AddScoped<IAnalyticsService, AnalyticsService>();
builder.Services.AddScoped<IAdminService, AdminService>();
// Stockage fichiers : bucket lu dans la configuration, vérification d'existence,
// repli disque local. Singleton pour ne sonder le bucket qu'une seule fois.
builder.Services.AddSingleton<IStorageService, StorageService>();
// Filigrane nominatif incrusté dans les épreuves, corrigés et livres servis à
// la visionneuse (Module 44) : sans état, réutilise QuestPDF/qpdf.
builder.Services.AddSingleton<IDocumentWatermarkService, DocumentWatermarkService>();
builder.Services.AddScoped<IFileUploadService, FileUploadService>();
builder.Services.AddScoped<IReviewRepository, ReviewRepository>();
builder.Services.AddScoped<IReviewService, ReviewService>();
builder.Services.AddScoped<IPromoCodeService, PromoCodeService>();
builder.Services.AddScoped<IFavoriteCollectionService, FavoriteCollectionService>();
builder.Services.AddScoped<IFavoriteService, FavoriteService>();
builder.Services.AddScoped<IFavoriteRepository, FavoriteRepository>();
builder.Services.AddScoped<ICertificateService, CertificateService>();
builder.Services.AddScoped<IPdfService, PdfService>();

// User Settings Services (Notifications, Privacy, Sessions, 2FA)
builder.Services.AddScoped<ISettingsService, SettingsService>();
builder.Services.AddScoped<ISessionService, SessionService>();
builder.Services.AddScoped<ITwoFactorService, TwoFactorService>();

// Chatbot Services
builder.Services.AddScoped<IChatbotRepository, ChatbotRepository>();
builder.Services.AddScoped<IChatbotService, ChatbotService>();

// New Backend-Alignment Services (PricingPlans, Institutions, Announcements, etc.)
builder.Services.AddScoped<IPricingService, PricingService>();
builder.Services.AddScoped<IInstitutionService, InstitutionService>();
builder.Services.AddScoped<IAnnouncementService, AnnouncementService>();
builder.Services.AddScoped<ITeacherService, TeacherService>();
// Lot 2, Module 1 : journal de portefeuille unique (source de vérité des soldes).
builder.Services.AddScoped<IWalletService, WalletService>();
builder.Services.AddScoped<ICatalogCommissionService, CatalogCommissionService>();
builder.Services.AddScoped<IWalletBackfillService, WalletBackfillService>();
// Lot 2, Module 2 : retrait Mobile Money automatisé par l'API de transfert NotchPay.
builder.Services.AddScoped<IWithdrawalService, WithdrawalService>();
// Lot 2, Module 3 : recharge du portefeuille par le parcours d'encaissement existant.
builder.Services.AddScoped<IWalletTopUpService, WalletTopUpService>();
builder.Services.AddScoped<IAffiliateService, AffiliateService>();
builder.Services.AddScoped<IParentService, ParentService>();
builder.Services.AddScoped<IStudentService, StudentService>();
builder.Services.AddScoped<IYearlyAlbumService, YearlyAlbumService>();
builder.Services.AddScoped<IHomeService, HomeService>();
builder.Services.AddScoped<ICategoryService, CategoryService>();

// Exam, Quiz, Revision Services (Latest addition for content management)
builder.Services.AddScoped<IExamService, ExamService>();
builder.Services.AddScoped<IQuizService, QuizService>();
builder.Services.AddScoped<ITutorProfileService, TutorProfileService>();
builder.Services.AddScoped<IAssignmentService, AssignmentService>();
builder.Services.AddScoped<ITeachingSessionService, TeachingSessionService>();
builder.Services.AddScoped<ITutorBookingService, TutorBookingService>();
builder.Services.AddScoped<ITutorReviewService, TutorReviewService>();
builder.Services.AddScoped<ITutorInsightsService, TutorInsightsService>();
builder.Services.AddScoped<ICourseAccessService, CourseAccessService>();
builder.Services.AddScoped<ICourseGamificationService, CourseGamificationService>();
builder.Services.AddScoped<ICourseCertificateService, CourseCertificateService>();
builder.Services.AddScoped<IDailyScoreService, DailyScoreService>();
builder.Services.AddScoped<IRevisionService, RevisionService>();

// New Repository Services
builder.Services.AddScoped<IPricingPlanRepository, PricingPlanRepository>();
builder.Services.AddScoped<IInstitutionRepository, InstitutionRepository>();
builder.Services.AddScoped<IAnnouncementRepository, AnnouncementRepository>();
builder.Services.AddScoped<IEventRepository, EventRepository>();
builder.Services.AddScoped<ISessionRepository, SessionRepository>();
builder.Services.AddScoped<ISubscriptionRepository, SubscriptionRepository>();

// Generic Repository registrations for services that use IRepository<T>
builder.Services.AddScoped<IRepository<Announcement>, GenericRepository<Announcement>>();
builder.Services.AddScoped<IRepository<Institution>, GenericRepository<Institution>>();
builder.Services.AddScoped<IRepository<PricingPlan>, GenericRepository<PricingPlan>>();

// Configure HttpClient for FastAPI AI Service
// Note: FastApiClient service reads AIService:TimeoutSeconds from config and overrides this value.
// The circuit breaker is implemented in FastApiClient.cs and activated via AIService:EnableCircuitBreaker.
builder.Services.AddHttpClient("FastApiClient", client =>
{
    var fastapiBaseUrl = builder.Configuration["AIService:BaseUrl"]
        ?? builder.Configuration["FastApi:BaseUrl"]
        ?? "http://172.31.1.71:5000";
    client.BaseAddress = new Uri(fastapiBaseUrl);
    // Le défaut était 5 (+2 de marge) : SEPT secondes pour tout appel IA.
    // Or la génération d'un quiz par le modèle prend 8-20 s (commentaire de
    // app.py), et l'analyse d'une image davantage. Chaque appel expirait donc
    // avant la première réponse, et le front basculait sur son message
    // « WinAI est momentanément indisponible ».
    // 180 s couvre un stream complet ; le circuit breaker de FastApiClient
    // protège toujours contre un service réellement mort.
    client.Timeout = TimeSpan.FromSeconds(
        builder.Configuration.GetValue<int>("AIService:TimeoutSeconds", 180));
    client.DefaultRequestHeaders.Add("Accept", "application/json");
});

// IHttpContextAccessor : indispensable à FastApiClient, qui relaie le jeton
// de l'utilisateur courant vers FastAPI. Tous les endpoints IA du service
// Python sont protégés par Depends(verify_token) ; sans ce relais, chaque
// appel recevait 401. Enregistrement idempotent côté ASP.NET Core.
builder.Services.AddHttpContextAccessor();

builder.Services.AddScoped<IFastApiClient, FastApiClient>();
builder.Services.AddScoped<IAIService, AIService>();

// Resend email client
builder.Services.AddHttpClient("ResendClient", client =>
{
    client.BaseAddress = new Uri("https://api.resend.com");
    client.DefaultRequestHeaders.Add("Accept", "application/json");
    client.Timeout = TimeSpan.FromSeconds(15);
});

// ============ NOTCHPAY PAYMENT SERVICE ============
builder.Services.AddHttpClient();

builder.Services.AddSingleton(new NotchPayConfig
{
    PublicKey = builder.Configuration["NotchPay:PublicKey"] ?? "",
    SecretKey = builder.Configuration["NotchPay:SecretKey"] ?? "",
    WebhookSecret = builder.Configuration["NotchPay:WebhookSecret"] ?? "",
    BaseUrl = builder.Configuration["NotchPay:BaseUrl"] ?? "https://api.notchpay.co",
    CallbackUrl = builder.Configuration["NotchPay:CallbackUrl"] ?? "https://api.winplus.cm/api/payments/webhook/notchpay",
    Currency = builder.Configuration["NotchPay:Currency"] ?? "XAF"
});

builder.Services.AddHttpClient("NotchPayClient", (sp, client) =>
{
    var config = sp.GetRequiredService<NotchPayConfig>();
    client.BaseAddress = new Uri(config.BaseUrl);
    client.DefaultRequestHeaders.Add("Authorization", config.PublicKey);
    client.DefaultRequestHeaders.Add("Accept", "application/json");
    client.Timeout = TimeSpan.FromSeconds(30);
});

builder.Services.AddScoped<INotchPayService, NotchPayService>();

// Forum
builder.Services.AddScoped<IForumService, ForumService>();
// Module 22 : point unique de lecture des préférences de notification
// (contexte de données propre, jamais celui de l'appelant).
builder.Services.AddSingleton<INotificationPreferenceService, NotificationPreferenceService>();
builder.Services.AddScoped<INtfyService, NtfyService>();
builder.Services.AddScoped<ISmartNotificationService, SmartNotificationService>();

// Background services for payment lifecycle
builder.Services.AddHostedService<PaymentReconciliationService>();
builder.Services.AddHostedService<PaymentExpirationService>();
builder.Services.AddHostedService<TutorBookingLifecycleService>();
builder.Services.AddHostedService<ScheduledMessageDeliveryService>();

// Background services for subscriptions
builder.Services.AddHostedService<SubscriptionExpirationService>();
builder.Services.AddHostedService<SubscriptionReminderService>();

// Background services for parent features
builder.Services.AddHostedService<WeeklyParentReportService>();
builder.Services.AddHostedService<ExamWatchModeExpirationService>();
builder.Services.AddHostedService<MonthlyPortfolioService>();

// Background services for institution features
builder.Services.AddHostedService<MonthlyInstitutionReportService>();

// Background services for tutor (Répétiteur) features
builder.Services.AddHostedService<TutorCoachingReportService>();

// Background services for the affiliate program
builder.Services.AddHostedService<AffiliateCommissionMaturityService>();
// Lot 2, Module 1 : filet de sécurité du journal (rejoue, sans doublon, les
// événements récents dont l'écriture aurait été perdue).
builder.Services.AddHostedService<WalletReconciliationService>();
// Module 14 : contre-passe le reliquat non consommé d'une dotation mensuelle
// de portefeuille parent exactement à son expiration (voir le commentaire du
// service pour le choix de conception).
builder.Services.AddHostedService<ParentWalletAllocationExpiryService>();
// Lot 2, Module 2 : suivi périodique des transferts de retrait en cours.
builder.Services.AddHostedService<WithdrawalTransferSyncService>();
builder.Services.AddHostedService<AffiliateRateRecalculationService>();

// Background services for Formations (drip content)
builder.Services.AddHostedService<SectionUnlockNotificationService>();
builder.Services.AddHostedService<CourseInactivityAlertService>();

// Add health checks
builder.Services.AddHealthChecks();

var app = builder.Build();

// Configure the HTTP request pipeline
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "WinPlus Educational API v4.0");
        c.RoutePrefix = string.Empty; // Swagger à la racine
    });
}

// Derrière nginx : on fait confiance aux en-têtes X-Forwarded-For / X-Real-IP
// pour récupérer la vraie IP du client au lieu de l'adresse de loopback nginx.
// Module 20 : les listes de confiance étaient vidées, ce qui faisait accepter
// l'en-tête X-Forwarded-For de n'importe quel appelant. Combiné à la
// limitation de débit indexée sur l'adresse du client, cela rendait la force
// brute sur la connexion illimitée : il suffisait de changer l'en-tête à
// chaque requête pour repartir d'un compteur neuf.
//
// On conserve la confiance dans le relais réel (nginx, sur la même machine :
// boucle locale) et on autorise en plus les adresses déclarées sous
// Security:TrustedProxies pour un déploiement où le relais est ailleurs.
// Aucune valeur de configuration n'est lue ici, seulement une clé.
var fwdOpts = new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
    // Le relais n'ajoute qu'un maillon : au-delà, c'est le client qui écrit.
    ForwardLimit = 1,
};
fwdOpts.KnownProxies.Clear();
fwdOpts.KnownNetworks.Clear();
fwdOpts.KnownProxies.Add(System.Net.IPAddress.Loopback);
fwdOpts.KnownProxies.Add(System.Net.IPAddress.IPv6Loopback);
foreach (var proxy in builder.Configuration.GetSection("Security:TrustedProxies").Get<string[]>() ?? Array.Empty<string>())
{
    if (System.Net.IPAddress.TryParse(proxy, out var parsed))
        fwdOpts.KnownProxies.Add(parsed);
}
app.UseForwardedHeaders(fwdOpts);

// app.UseHttpsRedirection();
app.UseStaticFiles(); // Serve static files from wwwroot/uploads
app.UseCors("AllowFrontend");

// IMPORTANT: ErrorHandlingMiddleware doit être très tôt dans le pipeline
app.UseErrorHandling();

// Add rate limiting middleware for auth endpoints
app.UseRateLimiting();

// IMPORTANT: L'ordre est crucial !
app.UseAuthentication();  // Doit être avant UseAuthorization

// Module 20 : revalidation de l'état du compte à chaque requête authentifiée.
// Placée juste après l'authentification (les revendications sont lues) et
// avant l'autorisation, pour qu'une suspension prenne effet dès la requête
// suivante au lieu d'attendre l'expiration naturelle du jeton d'accès.
app.UseAccountStatusRevalidation();

app.UseAuthorization();

// Suivi de présence : met à jour UserSessions.LastActivityAt à chaque requête
// authentifiée. C'est la source du « qui est en ligne » du dashboard admin.
app.UsePresenceTracking();

app.MapControllers();

// Health check endpoint
app.MapGet("/health", () => Results.Ok(new
{
    status = "healthy",
    service = "WinPlus Educational API (.NET)",
    timestamp = DateTime.UtcNow,
    authentication = "Custom JWT (Custom Auth Service)"
}));

// Mapped health checks with details
app.MapHealthChecks("/health/ready");

app.Run();