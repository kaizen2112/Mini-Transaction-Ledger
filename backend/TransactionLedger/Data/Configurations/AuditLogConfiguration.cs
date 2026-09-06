using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TransactionLedger.Domain;

namespace TransactionLedger.Data.Configurations;

/// <summary>
/// AuditLogs table per docs/04-database-design.md §3.6.
///
/// No CHECK constraints here, unlike every other table: an audit row asserts
/// nothing about money, so there is no financial invariant for the database to
/// defend (BR-37).
/// </summary>
public sealed class AuditLogConfiguration : IEntityTypeConfiguration<AuditLog>
{
    public void Configure(EntityTypeBuilder<AuditLog> builder)
    {
        builder.ToTable("AuditLogs");

        builder.HasKey(a => a.Id)
            .HasName("PK_AuditLogs");

        builder.Property(a => a.Id)
            .HasColumnType("uuid")
            .ValueGeneratedNever();

        builder.Property(a => a.UserId)
            .HasColumnType("uuid")
            .IsRequired();

        // Stored as its integer value (docs/04 §1.4). The name is an API
        // concern, and this table has no API.
        builder.Property(a => a.Action)
            .HasConversion<int>()
            .IsRequired();

        builder.Property(a => a.EntityType)
            .HasMaxLength(AuditLog.MaxEntityTypeLength)
            .IsRequired();

        builder.Property(a => a.EntityId)
            .HasColumnType("uuid")
            .IsRequired();

        // jsonb, not text: it is queryable with the -> operators when someone
        // is actually investigating an incident, which is the only time this
        // table gets read.
        builder.Property(a => a.Metadata)
            .HasColumnType("jsonb");

        builder.Property(a => a.OccurredAt)
            .HasColumnType("timestamptz")
            .IsRequired();

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(a => a.UserId)
            .HasConstraintName("FK_AuditLogs_Users")
            .OnDelete(DeleteBehavior.Restrict);

        // "What did this user do, most recent first" — the incident-response
        // query, so the sort order is baked into the index.
        builder.HasIndex(a => new { a.UserId, a.OccurredAt })
            .IsDescending(false, true)
            .HasDatabaseName("IX_AuditLogs_User_Occurred");

        // "What happened to this row" — starting from a suspicious account or
        // transaction rather than from a user.
        builder.HasIndex(a => new { a.EntityType, a.EntityId })
            .HasDatabaseName("IX_AuditLogs_Entity");
    }
}
