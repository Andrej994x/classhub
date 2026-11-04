using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ClassHub.Core.Infrastructure.Persistence.Technical;

/// <summary>
/// Technical key/value table that proves the migration pipeline (R0-P2). It holds no business or
/// personal data.
/// </summary>
public sealed class SchemaInfo
{
    public const int KeyMaxLength = 100;
    public const int ValueMaxLength = 400;

    public int Id { get; set; }

    public required string Key { get; set; }

    public required string Value { get; set; }

    public DateTime UpdatedAtUtc { get; set; }
}

internal sealed class SchemaInfoConfiguration : IEntityTypeConfiguration<SchemaInfo>
{
    public void Configure(EntityTypeBuilder<SchemaInfo> builder)
    {
        builder.ToTable("SchemaInfo");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Key).HasMaxLength(SchemaInfo.KeyMaxLength).IsRequired();
        builder.Property(x => x.Value).HasMaxLength(SchemaInfo.ValueMaxLength).IsRequired();
        builder.HasIndex(x => x.Key).IsUnique();

        builder.HasData(new SchemaInfo
        {
            Id = 1,
            Key = "schema.foundation",
            Value = "R0-P2",
            UpdatedAtUtc = new DateTime(2026, 11, 4, 0, 0, 0, DateTimeKind.Utc),
        });
    }
}
