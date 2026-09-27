using MedMateAI.Domain.Entities;
using MedMateAI.Domain.Enums;
using MedMateAI.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MedMateAI.Infrastructure.Persistence.FluentAPiConfiguration;

public sealed class SymptomAnalysisSessionConfiguration : IEntityTypeConfiguration<SymptomAnalysisSession>
{
    public void Configure(EntityTypeBuilder<SymptomAnalysisSession> builder)
    {
        builder.ToTable("SymptomAnalysisSession", table =>
            table.HasCheckConstraint(
                "CK_SymptomAnalysisSession_ServiceCreditLinkage",
                "(\"UserSubscriptionId\" IS NULL AND \"UserSubscriptionUsageId\" IS NULL) OR "
                + "(\"UserSubscriptionId\" IS NOT NULL AND \"UserSubscriptionUsageId\" IS NOT NULL)"));

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("SymptomAnalysisSessionId").ValueGeneratedOnAdd();

        builder.Property(x => x.Status)
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired()
            .HasDefaultValue(SymptomAnalysisSessionStatus.Processing);

        builder.Property(x => x.SessionType)
            .IsRequired()
            .HasDefaultValue(SymptomAnalysisSessionType.None);

        builder.Property(x => x.QuotaSource)
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired()
            .HasDefaultValue(QuotaSource.None);

        builder.HasOne(x => x.FreeQuotaUsage)
            .WithMany()
            .HasForeignKey(x => x.FreeQuotaUsageId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<ApplicationUser>()
            .WithMany(x => x.SymptomAnalysisSessions)
            .HasForeignKey(x => x.UserId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.UserSubscription)
            .WithMany()
            .HasForeignKey(x => x.UserSubscriptionId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.UserSubscriptionUsage)
            .WithMany()
            .HasForeignKey(x => x.UserSubscriptionUsageId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => x.UserId);
        builder.HasIndex(x => new { x.UserId, x.SessionType });
        builder.HasIndex(x => x.UserSubscriptionId);
        builder.HasIndex(x => x.UserSubscriptionUsageId);
        builder.HasIndex(x => x.FreeQuotaUsageId);
        builder.HasIndex(x => new { x.Status, x.SubmittedAt, x.CreatedAt });

        builder.HasMany(x => x.SessionSymptoms)
            .WithOne(x => x.SymptomAnalysisSession)
            .HasForeignKey(x => x.SymptomAnalysisSessionId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(x => x.DepartmentRecommendations)
            .WithOne(x => x.SymptomAnalysisSession)
            .HasForeignKey(x => x.SymptomAnalysisSessionId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
