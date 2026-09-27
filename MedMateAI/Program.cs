using MedMateAI.Application.Common;
using MedMateAI.Infrastructure;
using MedMateAI.Infrastructure.BackgroundJobs;
using MedMateAI.Infrastructure.Realtime.RecoveryPlans;
using Hangfire;
using Microsoft.OpenApi.Models;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MedMateAI;

public class Program
{
    public static async Task Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        builder.Services.AddInfrastructure(builder.Configuration);

        builder.Services.AddCors(options =>
        {
        options.AddPolicy("AllowFrontend", policy =>
          {
        
        policy.WithOrigins(
                "http://localhost:3000",
                "http://localhost:8081",
                "http://localhost:19006",
                "https://sep-490-fe-medical-ai-assistant.vercel.app",
                "https://sep-490-fe-medical-ai-assista-git-9aca09-dhas-projects-901181f4.vercel.app")
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials(); 
          });
        });

        builder.Services.AddControllers()
        .AddJsonOptions(options =>
    {
       options.JsonSerializerOptions.Converters.Add(
            new JsonStringEnumConverter(JsonNamingPolicy.CamelCase)
        );
    });

        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddSwaggerGen(options =>
        {
            options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
            {
                Name = "Authorization",
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                BearerFormat = "JWT",
                In = ParameterLocation.Header,
                Description = "Nhập JWT: Bearer {token}",
            });

            options.AddSecurityRequirement(new OpenApiSecurityRequirement
            {
                {
                    new OpenApiSecurityScheme
                    {
                        Reference = new OpenApiReference
                        {
                            Type = ReferenceType.SecurityScheme,
                            Id = "Bearer",
                        },
                    },
                    Array.Empty<string>()
                },
            });
        });

        var app = builder.Build();

        app.UsePayOSPendingPaymentMaintenance();
        app.UseSymptomAnalysisMaintenance();

        app.UseSwagger();
        app.UseSwaggerUI(c =>
        {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "MedMateAI API V1");
        c.RoutePrefix = string.Empty; 
        });
        if (app.Environment.IsDevelopment())
        {
            app.UseSwagger();
            app.UseSwaggerUI();
            app.UseDeveloperExceptionPage();
        }

        app.UseCors("AllowFrontend");
        app.UseHttpsRedirection();

        if (app.Environment.IsDevelopment())
        {
            app.UseHangfireDashboard("/hangfire");
        }

        app.UseAuthentication();
        app.UseAuthorization();

        app.MapControllers();

        app.MapHub<RecoveryPlanHub>(
                RecoveryPlanRealtimeConstants.HubPath,
                options => options.CloseOnAuthenticationExpiration = true)
            .RequireAuthorization();

        await app.RunAsync();
    }
}
