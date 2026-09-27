using MedMateAI.Domain.Entities;
using MedMateAI.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MedMateAI.Infrastructure.Persistence.FluentAPiConfiguration;

public sealed class FreeQuotaUsageLogConfiguration : IEntityTypeConfiguration<FreeQuotaUsageLog>
{
    public void Configure(EntityTypeBuilder<FreeQuotaUsageLog> builder)
    {
        builder.ToTable("FreeQuotaUsageLog", t =>
            t.HasCheckConstraint(
                "CK_FreeQuotaUsageLog_Counts",
                "\"UsedCountBefore\" >= 0 AND \"UsedCountAfter\" >= 0 AND \"ReservedCountBefore\" >= 0 AND \"ReservedCountAfter\" >= 0"));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("FreeQuotaUsageLogId").ValueGeneratedOnAdd();
        builder.Property(x => x.ActionType).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(x => x.ReferenceType).HasMaxLength(100);
        builder.Property(x => x.Reason).HasMaxLength(1000);
        builder.Property(x => x.IdempotencyKey).HasMaxLength(200).IsRequired();
        builder.Property(x => x.CreatedAt).HasColumnType("timestamp with time zone");
        builder.HasIndex(x => new { x.FreeQuotaUsageId, x.CreatedAt });
        builder.HasIndex(x => new { x.UserId, x.CreatedAt });
        builder.HasIndex(x => new { x.ReferenceType, x.ReferenceId });
        builder.HasIndex(x => x.IdempotencyKey).IsUnique();
        builder.HasOne(x => x.FreeQuotaUsage).WithMany(x => x.Logs).HasForeignKey(x => x.FreeQuotaUsageId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
    }
}
