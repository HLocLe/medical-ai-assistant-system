using MedMateAI.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MedMateAI.Infrastructure.Persistence.FluentAPiConfiguration;

public sealed class SessionSymptomConfiguration : IEntityTypeConfiguration<SessionSymptom>
{
    public void Configure(EntityTypeBuilder<SessionSymptom> builder)
    {
        builder.ToTable("SessionSymptom");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("SessionSymptomId").ValueGeneratedOnAdd();

        builder.Property(x => x.Icd10Code)
            .HasMaxLength(32);
    }
}
