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
using System.Text.Json;
using Backend.Data;
using Backend.Repositories;
using Backend.Services;
using Backend.Utilities;

using Backend.Middlewares;
using Backend.Models.Entities;

QuestPDF.Settings.License = LicenseType.Community;

var builder = WebApplication.CreateBuilder(args);

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
                "http://localhost:5173"
            };
        
        policyBuilder
            .WithOrigins(allowedOrigins)
            .AllowCredentials()
            .WithMethods("GET", "POST", "PUT", "DELETE", "PATCH")
                .WithHeaders("Content-Type", "Authorization", "X-Requested-With")
            .WithExposedHeaders("Content-Disposition");
    });
});

// Configure Entity Framework Core with PostgreSQL
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection"),
        pgOptions => pgOptions.MigrationsAssembly("backend")));

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
                var logger = context.HttpContext.RequestServices
                    .GetRequiredService<ILogger<Program>>();
                
                logger.LogError(
                    "[JWT Auth Failed] ❌ Token invalide ou expiré\n" +
                    "URL: {Url}\n" +
                    "Method: {Method}\n" +
                    "Authorization Header: {AuthHeader}\n" +
                    "Error: {Error}",
                    context.Request.Path,
                    context.Request.Method,
                    context.Request.Headers.Authorization.ToString().Length > 0 ? $"Bearer {context.Request.Headers.Authorization.ToString().Substring(7, Math.Min(20, context.Request.Headers.Authorization.ToString().Length - 7))}..." : "MISSING",
                    context.Exception?.Message
                );
                
                // ✅ Laisser passer les requêtes anonymes aussi
                if (!context.Request.Path.StartsWithSegments("/api/cart") && 
                    !context.Request.Path.StartsWithSegments("/api/subjects"))
                {
                    context.Response.StatusCode = 401;
                    context.Response.ContentType = "application/json";
                    context.Response.WriteAsJsonAsync(new 
                    { 
                        error = "Authentication failed",
                        message = context.Exception?.Message,
                        timestamp = DateTime.UtcNow
                    }).Wait();
                }
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

    // Politique pour les étudiants
    options.AddPolicy("StudentOnly", policy =>
    {
        policy.RequireAuthenticatedUser();
        policy.RequireRole("student", "teacher", "parent", "admin");
    });
    
    // Politique pour les utilisateurs authentifiés
    options.AddPolicy("AuthenticatedUser", policy => 
        policy.RequireAuthenticatedUser());
    
    // Politique pour les utilisateurs avec email vérifié
    options.AddPolicy("VerifiedEmailOnly", policy => 
        policy.RequireAssertion(context =>
        {
            var emailVerified = context.User.FindFirst("email_verified")?.Value;
            return emailVerified == "True" || emailVerified == "true";
        }));
});

// ============ CUSTOM AUTH SERVICES ============
// Register custom authentication services (MAIN AUTH SYSTEM)
builder.Services.AddScoped<IJwtService, JwtService>();
builder.Services.AddScoped<IEmailService, EmailService>();
builder.Services.AddScoped<IDeviceTrackingService, DeviceTrackingService>();
builder.Services.AddScoped<ICustomAuthService, CustomAuthService>();

builder.Services.AddMemoryCache();

// Register Repositories
builder.Services.AddScoped<IUserRepository, UserRepository>();
builder.Services.AddScoped<ISubjectRepository, SubjectRepository>();
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
builder.Services.AddSingleton<IAnonymousCartService, AnonymousCartService>(); // ✅ Singleton for in-memory cart
builder.Services.AddScoped<IOrderService, OrderService>();
builder.Services.AddScoped<IEnrollmentService, EnrollmentService>();
builder.Services.AddScoped<IPaymentService, PaymentService>();
builder.Services.AddScoped<IHistoryService, HistoryService>();
builder.Services.AddScoped<IAnalyticsService, AnalyticsService>();
builder.Services.AddScoped<IAdminService, AdminService>();
builder.Services.AddScoped<IFileUploadService, FileUploadService>();
builder.Services.AddScoped<IReviewRepository, ReviewRepository>();
builder.Services.AddScoped<IReviewService, ReviewService>();
builder.Services.AddScoped<IPromoCodeService, PromoCodeService>();
builder.Services.AddScoped<IFavoriteCollectionService, FavoriteCollectionService>();
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
builder.Services.AddScoped<IParentService, ParentService>();
builder.Services.AddScoped<IStudentService, StudentService>();
builder.Services.AddScoped<IHomeService, HomeService>();
builder.Services.AddScoped<ICategoryService, CategoryService>();

// Exam, Quiz, Revision Services (Latest addition for content management)
builder.Services.AddScoped<IExamService, ExamService>();
builder.Services.AddScoped<IQuizService, QuizService>();
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
    client.Timeout = TimeSpan.FromSeconds(
        builder.Configuration.GetValue<int>("AIService:TimeoutSeconds", 5) + 2); // grace margin
    client.DefaultRequestHeaders.Add("Accept", "application/json");
});

builder.Services.AddScoped<IFastApiClient, FastApiClient>();

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
    client.DefaultRequestHeaders.Add("Authorization", config.SecretKey);
    client.DefaultRequestHeaders.Add("Accept", "application/json");
    client.Timeout = TimeSpan.FromSeconds(30);
});

builder.Services.AddScoped<INotchPayService, NotchPayService>();

// Forum
builder.Services.AddScoped<IForumService, ForumService>();
builder.Services.AddScoped<INtfyService, NtfyService>();

// Background services for payment lifecycle
builder.Services.AddHostedService<PaymentReconciliationService>();
builder.Services.AddHostedService<PaymentExpirationService>();

// Background services for subscriptions
builder.Services.AddHostedService<SubscriptionExpirationService>();
builder.Services.AddHostedService<SubscriptionReminderService>();

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

// app.UseHttpsRedirection();
app.UseStaticFiles(); // Serve static files from wwwroot/uploads
app.UseCors("AllowFrontend");

// IMPORTANT: ErrorHandlingMiddleware doit être très tôt dans le pipeline
app.UseErrorHandling();

// Add rate limiting middleware for auth endpoints
app.UseRateLimiting();

// IMPORTANT: L'ordre est crucial !
app.UseAuthentication();  // Doit être avant UseAuthorization
app.UseAuthorization();

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