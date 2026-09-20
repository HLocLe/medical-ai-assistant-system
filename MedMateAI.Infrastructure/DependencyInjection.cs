using System.Text;
using AutoMapper;
using MedMateAI.Application.Service;
using MedMateAI.Application.IService;
using MedMateAI.Infrastructure.Identity;
using MedMateAI.Domain.Persistence;
using MedMateAI.Domain.Repository;
using MedMateAI.Application.Options;
using MedMateAI.Infrastructure.Auth.Options;
using MedMateAI.Infrastructure.Auth.Providers;
using MedMateAI.Infrastructure.Auth.Services;
using MedMateAI.Application.Mapping;
using MedMateAI.Application.Common;
using MedMateAI.Application.IRepository;
using MedMateAI.Infrastructure.Mapping;
using MedMateAI.Infrastructure.Persistence.Seeder;
using MedMateAI.Infrastructure.Repositories;
using MedMateAI.Infrastructure.Email.Brevo;
using MedMateAI.Infrastructure.Email.Brevo.Options;
using MedMateAI.Infrastructure.SMS.Stringee;
using MedMateAI.Infrastructure.SMS.Stringee.Options;
using MedMateAI.Infrastructure.AI;
using MedMateAI.Infrastructure.AI.Options;
using MedMateAI.Infrastructure.BackgroundJobs;
using MedMateAI.Infrastructure.BackgroundJobs.RecoveryPlans;
using MedMateAI.Infrastructure.BackgroundJobs.Sales;
using MedMateAI.Infrastructure.ComputerVision;
using MedMateAI.Infrastructure.ComputerVision.Options;
using MedMateAI.Infrastructure.Payments.PayOS;
using MedMateAI.Infrastructure.Push.Expo;
using MedMateAI.Infrastructure.Push.Expo.Options;
using MedMateAI.Infrastructure.Realtime.RecoveryPlans;
using MedMateAI.Infrastructure.NationalInstitutesofHealth;
using MedMateAI.Infrastructure.NationalInstitutesofHealth.Options;
using MedMateAI.Infrastructure.Translation;
using MedMateAI.Infrastructure.Translation.Options;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace MedMateAI.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");

        var jwt = configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>()
        ?? throw new InvalidOperationException($"Configuration section '{JwtOptions.SectionName}' is missing or invalid.");
        
        services.AddDbContext<ApplicationDbContext>(options =>
        options.UseNpgsql(connectionString));

        services.AddRecoveryPlanRealtime(configuration);
        
        //
        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddScoped(typeof(IGenericRepository<>), typeof(GenericRepository<>));
        
        //
        services.AddScoped<IJwtTokenGenerator, JwtTokenGenerator>();
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<IUserService, UserService>();
        services.AddScoped<IMedicalDepartmentService, MedicalDepartmentService>();
        services.AddScoped<IIcdChapterService, IcdChapterService>();
        services.AddScoped<IDiseasePriorProbabilityService, DiseasePriorProbabilityService>();
        services.AddScoped<ILabIndicatorService, LabIndicatorService>();
        services.AddScoped<ILabTestService, LabTestService>();
        services.AddScoped<ILabTestAnalyticsRepository, LabTestAnalyticsRepository>();
        services.AddScoped<ILabTestAnalyticsService, LabTestAnalyticsService>();
        services.AddScoped<ILabTestQuotaService, LabTestQuotaService>();
        services.AddScoped<ILabTestResultAnalyzer, LabTestResultAnalyzer>();
        services.AddScoped<ILabTestOcrStructurer, LabTestOcrStructurer>();
        services.AddScoped<IClinicalQuestionService, ClinicalQuestionService>();
        services.AddScoped<IDepartmentConsultationQuestionService, DepartmentConsultationQuestionService>();
        services.AddScoped<IChecklistItemService, ChecklistItemService>();
        services.AddScoped<IMedicalFacilityService, MedicalFacilityService>();
        services.AddScoped<IFacilityDepartmentService, FacilityDepartmentService>();
        services.AddScoped<IDoctorService, DoctorService>();
        services.AddScoped<IDoctorInvitationService, DoctorInvitationService>();
        services.AddScoped<IInvitationTokenService, InvitationTokenService>();
        services.AddScoped<IDoctorAccountRegistrationService, DoctorAccountRegistrationService>();
        services.AddScoped<IFeedbackReviewService, FeedbackReviewService>();
        services.AddScoped<IPatientProfileService, PatientProfileService>();
        services.AddScoped<ISubscriptionPlanQuotaRepository, SubscriptionPlanQuotaRepository>();
        services.AddScoped<ISubscriptionPlanQuotaService, SubscriptionPlanQuotaService>();
        services.AddScoped<ISubscriptionPlanCacheInvalidator, SubscriptionPlanCacheInvalidator>();
        services.AddScoped<ISubscriptionPlanService, SubscriptionPlanService>();
        services.AddScoped<ISaleCampaignRepository, SaleCampaignRepository>();
        services.AddScoped<ISaleRedemptionRepository, SaleRedemptionRepository>();
        services.AddScoped<
            ISaleCampaignAnalyticsRepository,
            SaleCampaignAnalyticsRepository>();
        services.AddScoped<
            ISaleCampaignAnnouncementRepository,
            SaleCampaignAnnouncementRepository>();
        services.AddScoped<ISaleCampaignService, SaleCampaignService>();
        services.AddScoped<
            ISaleCampaignAnalyticsService,
            SaleCampaignAnalyticsService>();
        services.AddScoped<ISaleRedemptionService, SaleRedemptionService>();
        services.AddScoped<
            ISaleCampaignAnnouncementContextService,
            SaleCampaignAnnouncementContextService>();
        services.AddScoped<
            ISaleCampaignNotificationContentBuilder,
            SaleCampaignNotificationContentBuilder>();
        services.AddScoped<
            ISaleCampaignNotificationScheduler,
            SaleCampaignNotificationScheduler>();
        services.AddScoped<IAIConfigService, AIConfigService>();
        services.AddScoped<IWebChatbotService, WebChatbotService>();
        services.AddScoped<ISymptomAnalysisService, SymptomAnalysisService>();
        services.AddScoped<ISymptomAnalysisQuotaService, SymptomAnalysisQuotaService>();
        services.AddScoped<IConsultationSessionService, ConsultationSessionService>();
        services.AddScoped<IConsultationSessionQuotaService, ConsultationSessionQuotaService>();
        services.AddScoped<IPayOSService, PayOSService>();
        services.AddScoped<IUserSubscriptionService, UserSubscriptionService>();
        services.AddScoped<IPaymentService, PaymentService>();
        services.AddScoped<IServiceCreditService, ServiceCreditService>();
        services.AddScoped<IRecoveryPlanQuotaService, RecoveryPlanQuotaService>();
        services.AddScoped<IRecoveryPlanRequestService, RecoveryPlanRequestService>();
        services.AddScoped<IRecoveryPlanClinicalContextService, RecoveryPlanClinicalContextService>();
        services.AddScoped<IRecoveryPlanService, RecoveryPlanService>();
        services.AddScoped<IRecoveryPlanTemplateService, RecoveryPlanTemplateService>();
        services.AddScoped<
            IRecoveryPlanFeedbackAnalyticsRepository,
            RecoveryPlanFeedbackAnalyticsRepository>();
        services.AddScoped<
            IRecoveryPlanFeedbackAnalyticsService,
            RecoveryPlanFeedbackAnalyticsService>();
        services.AddScoped<IUserMedicationService, UserMedicationService>();
        services.AddScoped<IRecoveryPlanRealtimeAccessService, RecoveryPlanRealtimeAccessService>();
        services.AddScoped<IOutboxMessageRepository, OutboxMessageRepository>();
        services.AddScoped<INotificationRepository, NotificationRepository>();
        services.AddScoped<IUserPushDeviceRepository, UserPushDeviceRepository>();
        services.AddScoped<IUserMedicationRepository, UserMedicationRepository>();
        services.AddScoped<IOutboxMessageProcessor, RecoveryPlanOutboxProcessor>();
        services.AddScoped<INotificationEmailProcessor, NotificationEmailProcessor>();
        services.AddScoped<INotificationPushProcessor, NotificationPushProcessor>();
        services.AddScoped<
            INotificationPushReceiptProcessor,
            NotificationPushReceiptProcessor>();
        services.AddScoped<INotificationEmailRenderer, NotificationEmailRenderer>();
        services.AddScoped<
            IRecoveryPlanAssignmentTimeoutProcessor,
            RecoveryPlanAssignmentTimeoutProcessor>();
        services.AddScoped<
            IRecoveryPlanCompletionProcessor,
            RecoveryPlanCompletionProcessor>();
        services.AddScoped<IMedicationReminderScheduler, MedicationReminderScheduler>();
        services.AddScoped<IUserPushDeviceService, UserPushDeviceService>();
        services.AddSingleton<TimeProvider>(TimeProvider.System);

        //
        services.AddOptions<PayOSOptions>()
            .Bind(configuration.GetSection(PayOSOptions.SectionName))
            .Validate(
                options => options.PaymentLinkExpirationMinutes is >= 5 and <= 120,
                "PayOS PaymentLinkExpirationMinutes must be between 5 and 120.")
            .Validate(
                options => options.PendingReconciliationIntervalMinutes is >= 1 and <= 60,
                "PayOS PendingReconciliationIntervalMinutes must be between 1 and 60.")
            .Validate(
                options => options.PendingReconciliationMinimumAgeMinutes is >= 0 and <= 30,
                "PayOS PendingReconciliationMinimumAgeMinutes must be between 0 and 30.")
            .Validate(
                options => options.PendingCleanupGraceMinutes is >= 0 and <= 30,
                "PayOS PendingCleanupGraceMinutes must be between 0 and 30.")
            .Validate(
                options => options.PendingReconciliationBatchSize is >= 1 and <= 500,
                "PayOS PendingReconciliationBatchSize must be between 1 and 500.")
            .ValidateOnStart();
        services.Configure<MobilePaymentOptions>(
            configuration.GetSection(MobilePaymentOptions.SectionName));
       
        //
        services.Configure<AzureTranslatorOptions>(configuration.GetSection(AzureTranslatorOptions.SectionName));
        services.Configure<AzureOptions>(configuration.GetSection(AzureOptions.SectionName));

        services.Configure<BrevoOptions>(configuration.GetSection(BrevoOptions.SectionName));
        services.Configure<StringeeOptions>(configuration.GetSection(StringeeOptions.SectionName));
        services.Configure<FrontendOptions>(configuration.GetSection(FrontendOptions.SectionName));

        services.AddOptions<RecoveryPlanOptions>()
            .Bind(configuration.GetSection(RecoveryPlanOptions.SectionName))
            .Validate(x => x.AssignmentTimeoutMinutes is > 0 and <= 120,
                "RecoveryPlan AssignmentTimeoutMinutes must be between 1 and 120.")
            .ValidateOnStart();
        services.AddOptions<BackgroundJobRetryOptions>()
            .Bind(configuration.GetSection(BackgroundJobRetryOptions.SectionName))
            .Validate(
                options => options.MaxAttempts is >= 1 and <= 20,
                "BackgroundJobRetry MaxAttempts must be between 1 and 20.")
            .Validate(
                options =>
                    options.RetryBaseSeconds is >= 1 and <= 3600
                    && options.RetryMaxSeconds >= options.RetryBaseSeconds
                    && options.RetryMaxSeconds <= 86400,
                "BackgroundJobRetry retry settings are invalid.")
            .ValidateOnStart();

        services.AddOptions<RecoveryPlanJobOptions>()
            .Bind(configuration.GetSection(RecoveryPlanJobOptions.SectionName))
            .Validate(
                options =>
                    options.OutboxPollingSeconds is >= 1 and <= 300
                    && options.NotificationPollingSeconds is >= 1 and <= 300,
                "RecoveryPlanJobs polling intervals must be between 1 and 300 seconds.")
            .Validate(
                options => options.BatchSize is >= 1 and <= 200,
                "RecoveryPlanJobs BatchSize must be between 1 and 200.")
            .Validate(
                options => options.MaxAttempts is >= 1 and <= 20,
                "RecoveryPlanJobs MaxAttempts must be between 1 and 20.")
            .Validate(
                options => options.ProcessingLeaseSeconds is >= 15 and <= 1800,
                "RecoveryPlanJobs ProcessingLeaseSeconds must be between 15 and 1800.")
            .Validate(
                options =>
                    options.RetryBaseSeconds is >= 1 and <= 3600
                    && options.RetryMaxSeconds >= options.RetryBaseSeconds
                    && options.RetryMaxSeconds <= 86400,
                "RecoveryPlanJobs retry settings are invalid.")
            .Validate(
                options =>
                    options.MedicationMaxLatenessMinutes is >= 1 and <= 1440,
                "RecoveryPlanJobs MedicationMaxLatenessMinutes must be between 1 and 1440.")
            .Validate(
                options => options.LifecyclePollingSeconds is >= 5 and <= 300,
                "RecoveryPlanJobs LifecyclePollingSeconds must be between 5 and 300.")
            .Validate(
                options => options.LifecycleBatchSize is >= 1 and <= 200,
                "RecoveryPlanJobs LifecycleBatchSize must be between 1 and 200.")
            .Validate(
                options =>
                    options.MedicationSchedulerPollingSeconds is >= 5 and <= 3600,
                "RecoveryPlanJobs MedicationSchedulerPollingSeconds must be between 5 and 3600.")
            .Validate(
                options =>
                    options.MedicationScheduleHorizonHours is >= 1 and <= 168,
                "RecoveryPlanJobs MedicationScheduleHorizonHours must be between 1 and 168.")
            .Validate(
                options =>
                    options.MedicationScheduleLookbackMinutes is >= 0 and <= 1440,
                "RecoveryPlanJobs MedicationScheduleLookbackMinutes must be between 0 and 1440.")
            .Validate(
                options =>
                    options.MedicationSchedulerBatchSize is >= 1 and <= 1000,
                "RecoveryPlanJobs MedicationSchedulerBatchSize must be between 1 and 1000.")
            .ValidateOnStart();

        services.AddOptions<SaleCampaignNotificationOptions>()
            .Bind(configuration.GetSection(
                SaleCampaignNotificationOptions.SectionName))
            .Validate(
                options => options.PollingSeconds is >= 10 and <= 3600,
                "SaleCampaignNotifications PollingSeconds must be between 10 and 3600.")
            .Validate(
                options => options.UserBatchSize is >= 1 and <= 1000,
                "SaleCampaignNotifications UserBatchSize must be between 1 and 1000.")
            .Validate(
                options => options.CampaignBatchSize is >= 1 and <= 100,
                "SaleCampaignNotifications CampaignBatchSize must be between 1 and 100.")
            .Validate(
                options => options.MaxOffersInEmail is >= 1 and <= 10,
                "SaleCampaignNotifications MaxOffersInEmail must be between 1 and 10.")
            .ValidateOnStart();

        services.AddOptions<ExpoPushOptions>()
            .Bind(configuration.GetSection(ExpoPushOptions.SectionName))
            .Validate(
                options => options.RequestTimeoutSeconds is >= 1 and <= 120,
                "ExpoPush RequestTimeoutSeconds must be between 1 and 120.")
            .Validate(
                options => options.ReceiptDelayMinutes is >= 1 and <= 60,
                "ExpoPush ReceiptDelayMinutes must be between 1 and 60.")
            .Validate(
                options => options.ReceiptRetryMinutes is >= 1 and <= 60,
                "ExpoPush ReceiptRetryMinutes must be between 1 and 60.")
            .Validate(
                options => options.ReceiptMaxAttempts is >= 1 and <= 20,
                "ExpoPush ReceiptMaxAttempts must be between 1 and 20.")
            .Validate(
                options => options.ReceiptBatchSize is >= 1 and <= 1000,
                "ExpoPush ReceiptBatchSize must be between 1 and 1000.")
            .Validate(
                options => IsHttpsEndpoint(options.SendEndpoint)
                           && IsHttpsEndpoint(options.ReceiptEndpoint),
                "ExpoPush endpoints must be absolute HTTPS URLs.")
            .ValidateOnStart();
      
        
        //
        services.Configure<MedGemmaOptions>(configuration.GetSection(MedGemmaOptions.SectionName));
        services.Configure<CloudFlareAIOptions>(configuration.GetSection(CloudFlareAIOptions.SectionName));
        services.Configure<NihClinicalTablesOptions>(configuration.GetSection(NihClinicalTablesOptions.SectionName));
        //
        services.AddHttpContextAccessor();

        //
        services.AddMemoryCache();
        services.AddStackExchangeRedisCache(options =>
        {
            options.Configuration = configuration["Redis:ConnectionString"]
                ?? throw new InvalidOperationException("Redis connection string is missing.");
        });

        //
        services.AddHttpClient<BrevoEmailSender>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(30);
        });

        services.AddHttpClient<IPushNotificationGateway, ExpoPushGateway>(
            (serviceProvider, client) =>
            {
                var options = serviceProvider
                    .GetRequiredService<IOptions<ExpoPushOptions>>()
                    .Value;
                client.Timeout = TimeSpan.FromSeconds(
                    options.RequestTimeoutSeconds);
            });

        services.AddHttpClient<StringeeSmsSender>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(30);
        });

        //
        services.AddScoped<IEmailSender>(sp => sp.GetRequiredService<BrevoEmailSender>());
        services.AddScoped<IEmailOtpSender>(sp => sp.GetRequiredService<BrevoEmailSender>());
        services.AddScoped<ISmsSender>(sp => sp.GetRequiredService<StringeeSmsSender>());
        services.AddHttpClient<IAIChatProvider, OpenRouterChatProvider>();

        //
        services.AddHttpClient<IMedGemmaChatService, MedGemmaChatService>(client =>
        {
            client.Timeout = TimeSpan.FromMinutes(5);
        });

        services.AddHttpClient<ICloudFlareAIChatService, CloudFlareAIChatService>(client =>
        {
            client.Timeout = TimeSpan.FromMinutes(5);
        });

        //
        services.AddHttpClient<ITranslationService, AzureTranslationService>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(15);
        });

        services.AddHttpClient<IDocumentIntelligenceService, AzureDocumentIntelligenceService>(client =>
        {
            client.Timeout = TimeSpan.FromMinutes(3);
        });

        services.AddHttpClient<IIcdLookupService, NihIcdLookupService>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(15);
        });

        // 
        services.AddIdentityCore<ApplicationUser>(options =>
            {
                options.User.RequireUniqueEmail = true;
                options.Password.RequireDigit = true;
                options.Password.RequireLowercase = true;
                options.Password.RequireUppercase = true;
                options.Password.RequireNonAlphanumeric = true;
                options.Password.RequiredLength = 8;

                options.Lockout.AllowedForNewUsers = true;
                options.Lockout.MaxFailedAccessAttempts = 5;
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
            })
            .AddRoles<IdentityRole<Guid>>()
            .AddEntityFrameworkStores<ApplicationDbContext>()
            .AddDefaultTokenProviders();
        
        //
        services.AddHostedService<IdentitySeedHostedService>();
        services.AddHostedService<OutboxBackgroundService>();
        services.AddHostedService<NotificationBackgroundService>();
        services.AddHostedService<NotificationPushBackgroundService>();
        services.AddHostedService<NotificationPushReceiptBackgroundService>();
        services.AddHostedService<RecoveryPlanLifecycleBackgroundService>();
        services.AddHostedService<MedicationReminderBackgroundService>();
        services.AddHostedService<SaleCampaignNotificationBackgroundService>();
        
        //
        services.AddOptions<JwtOptions>()
        .Bind(configuration.GetSection(JwtOptions.SectionName))
        .Validate(x =>
           !string.IsNullOrWhiteSpace(x.Secret) &&
            x.Secret.Length >= 32,
           "JWT secret must be at least 32 chars.")
         .ValidateOnStart();
        
        services.AddAutoMapper(cfg => { }, typeof(UserMappingProfile), typeof(PatientProfileMappingProfile), typeof(IcdChapterMappingProfile), typeof(DiseasePriorProbabilityMappingProfile), typeof(ClinicalQuestionMappingProfile), typeof(MedicalFacilityMappingProfile), typeof(SymptomAnalysisMappingProfile), typeof(LabIndicatorMappingProfile), typeof(DepartmentConsultationQuestionMappingProfile), typeof(ChecklistItemMappingProfile));

        services.AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
                options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
            })
            .AddJwtBearer(options =>
            {
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    ValidIssuer = jwt.Issuer,
                    ValidAudience = jwt.Audience,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.Secret)),
                    ClockSkew = TimeSpan.FromMinutes(1),
                };
                options.Events = new JwtBearerEvents
                {
                    OnMessageReceived = context =>
                    {
                        var accessToken = context.Request.Query["access_token"];
                        if (!string.IsNullOrWhiteSpace(accessToken)
                            && context.HttpContext.Request.Path.StartsWithSegments(
                                RecoveryPlanRealtimeConstants.HubPath))
                        {
                            context.Token = accessToken;
                        }

                        return Task.CompletedTask;
                    }
                };
            });

        services.AddAuthorization(options =>
        {
            options.FallbackPolicy = new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .Build();
        });

        services.AddHangfireBackgroundJobs(configuration);

        return services;
    }

    private static bool IsHttpsEndpoint(string? value)
    {
        return Uri.TryCreate(value, UriKind.Absolute, out var uri)
               && uri.Scheme == Uri.UriSchemeHttps;
    }
}
