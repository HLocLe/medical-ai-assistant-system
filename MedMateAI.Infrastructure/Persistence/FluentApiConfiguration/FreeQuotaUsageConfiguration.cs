using MedMateAI.Domain.Entities;
using MedMateAI.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MedMateAI.Infrastructure.Persistence.FluentAPiConfiguration;

public sealed class FreeQuotaUsageConfiguration : IEntityTypeConfiguration<FreeQuotaUsage>
{
    public void Configure(EntityTypeBuilder<FreeQuotaUsage> builder)
    {
        builder.ToTable("FreeQuotaUsage", t =>
        {
            t.HasCheckConstraint("CK_FreeQuotaUsage_LimitValue", "\"LimitValue\" >= 0");
            t.HasCheckConstraint(
                "CK_FreeQuotaUsage_Counts",
                "\"UsedCount\" >= 0 AND \"ReservedCount\" >= 0 AND \"UsedCount\" + \"ReservedCount\" <= \"LimitValue\"");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("FreeQuotaUsageId").ValueGeneratedOnAdd();
        builder.Property(x => x.Feature).HasMaxLength(64).IsRequired();
        builder.Property(x => x.BusinessDate).HasColumnType("date");
        builder.HasIndex(x => new { x.UserId, x.Feature, x.BusinessDate })
            .IsUnique()
            .HasFilter("\"IsDeleted\" = false");
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
    }
}
