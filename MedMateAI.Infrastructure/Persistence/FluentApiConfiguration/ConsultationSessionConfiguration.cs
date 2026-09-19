using MedMateAI.Domain.Entities;
using MedMateAI.Domain.Enums;
using MedMateAI.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MedMateAI.Infrastructure.Persistence.FluentAPiConfiguration;

public sealed class ConsultationSessionConfiguration : IEntityTypeConfiguration<ConsultationSession>
{
    public void Configure(EntityTypeBuilder<ConsultationSession> builder)
    {
        builder.ToTable("ConsultationSession", table =>
            table.HasCheckConstraint(
                "CK_ConsultationSession_ServiceCreditLinkage",
                "(\"UserSubscriptionId\" IS NULL AND \"UserSubscriptionUsageId\" IS NULL) OR "
                + "(\"UserSubscriptionId\" IS NOT NULL AND \"UserSubscriptionUsageId\" IS NOT NULL)"));

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("ConsultationSessionId").ValueGeneratedOnAdd();

        builder.Property(x => x.UserSymptoms)
            .HasColumnType("text");

        builder.Property(x => x.AppointmentTime)
            .HasColumnType("timestamp with time zone");

        builder.Property(x => x.Status)
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired()
            .HasDefaultValue(ConsultationSessionStatus.Processing);

        builder.Property(x => x.IsReminderEnabled)
            .HasDefaultValue(false);

        builder.Property(x => x.ReminderScheduledAt)
            .HasColumnType("timestamp with time zone");

        builder.Property(x => x.ReminderEmailSentAt)
            .HasColumnType("timestamp with time zone");

        builder.Property(x => x.ReminderPushSentAt)
            .HasColumnType("timestamp with time zone");

        builder.Property(x => x.ReminderSmsSentAt)
            .HasColumnType("timestamp with time zone");

        builder.HasIndex(x => x.FacilityId);
        builder.HasIndex(x => x.UserSubscriptionId);
        builder.HasIndex(x => x.UserSubscriptionUsageId);

        builder.HasOne<ApplicationUser>()
            .WithMany(x => x.ConsultationSessions)
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.Department)
            .WithMany(x => x.ConsultationSessions)
            .HasForeignKey(x => x.DepartmentId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.Facility)
            .WithMany(x => x.ConsultationSessions)
            .HasForeignKey(x => x.FacilityId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(x => x.UserSubscription)
            .WithMany()
            .HasForeignKey(x => x.UserSubscriptionId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.UserSubscriptionUsage)
            .WithMany()
            .HasForeignKey(x => x.UserSubscriptionUsageId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(x => x.ConsultationQuestions)
            .WithOne(x => x.ConsultationSession)
            .HasForeignKey(x => x.ConsultationSessionId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
