using Hangfire;
using Hangfire.PostgreSql;
using MedMateAI.Application.IService;
using MedMateAI.Application.Options;
using MedMateAI.Application.Service;
using MedMateAI.Infrastructure.Payments.PayOS;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace MedMateAI.Infrastructure.BackgroundJobs;

public static class HangfireDependencyInjection
{
    public static IServiceCollection AddHangfireBackgroundJobs(
        this IServiceCollection services,
        IConfiguration configuration)
    {
       
        var connectionString = configuration.GetConnectionString("HangfireConnection")
            ?? configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("Connection string for Hangfire not found.");

        services.AddHangfire(config => config
            .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
            .UseSimpleAssemblyNameTypeSerializer()
            .UseRecommendedSerializerSettings()
            .UsePostgreSqlStorage(options => options.UseNpgsqlConnection(connectionString)));

        services.AddHangfireServer(options =>
        {
            options.WorkerCount = 20;
            options.SchedulePollingInterval = TimeSpan.FromSeconds(5);
        });

        services.AddScoped<ILabTestOcrProcessor, LabTestOcrProcessor>();
        services.AddScoped<LabTestOcrJob>();
        services.AddScoped<LabTestSummaryJob>();
        services.AddSingleton<ILabTestJobScheduler, HangfireLabTestJobScheduler>();

        services.AddScoped<ConsultationDoctorQuestionsJob>();
        services.AddScoped<ConsultationReminderSmsJob>();
        services.AddSingleton<IConsultationSessionJobScheduler, HangfireConsultationSessionJobScheduler>();

        services.AddScoped<SymptomAnalysisMedGemmaJob>();
        services.AddScoped<SymptomAnalysisQuotaFinalizeJob>();
        services.AddScoped<SymptomAnalysisAbandonedSessionJob>();
        services.AddSingleton<ISymptomAnalysisJobScheduler, HangfireSymptomAnalysisJobScheduler>();

        services.AddScoped<PayOSPendingPaymentReconciliationJob>();

        return services;
    }

    public static IApplicationBuilder UsePayOSPendingPaymentMaintenance(
        this IApplicationBuilder app)
    {
        var options = app.ApplicationServices
            .GetRequiredService<IOptions<PayOSOptions>>()
            .Value;
        var recurringJobManager = app.ApplicationServices
            .GetRequiredService<IRecurringJobManager>();
        var cronExpression = options.PendingReconciliationIntervalMinutes == 60
            ? Cron.Hourly()
            : $"*/{options.PendingReconciliationIntervalMinutes} * * * *";

        recurringJobManager.AddOrUpdate<PayOSPendingPaymentReconciliationJob>(
            "payos-pending-payment-maintenance",
            job => job.ExecuteAsync(CancellationToken.None),
            cronExpression,
            new RecurringJobOptions
            {
                TimeZone = TimeZoneInfo.Utc,
            });

        return app;
    }

    public static IApplicationBuilder UseSymptomAnalysisMaintenance(
        this IApplicationBuilder app)
    {
        var options = app.ApplicationServices
            .GetRequiredService<IOptions<SymptomAnalysisOptions>>()
            .Value;
        var recurringJobManager = app.ApplicationServices
            .GetRequiredService<IRecurringJobManager>();
        var cronExpression = options.AbandonedSessionCleanupIntervalMinutes == 60
            ? Cron.Hourly()
            : $"*/{options.AbandonedSessionCleanupIntervalMinutes} * * * *";

        recurringJobManager.AddOrUpdate<SymptomAnalysisAbandonedSessionJob>(
            "symptom-analysis-abandoned-session-cleanup",
            job => job.ExecuteAsync(CancellationToken.None),
            cronExpression,
            new RecurringJobOptions
            {
                TimeZone = TimeZoneInfo.Utc,
            });

        return app;
    }
}
