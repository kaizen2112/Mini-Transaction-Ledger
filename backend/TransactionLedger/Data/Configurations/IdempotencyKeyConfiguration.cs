using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TransactionLedger.Domain;

namespace TransactionLedger.Data.Configurations;

/// <summary>
/// IdempotencyKeys table per docs/04-database-design.md §3.5.
/// </summary>
public sealed class IdempotencyKeyConfiguration : IEntityTypeConfiguration<IdempotencyKey>
{
    public void Configure(EntityTypeBuilder<IdempotencyKey> builder)
    {
        builder.ToTable("IdempotencyKeys");

        builder.HasKey(k => k.Id)
            .HasName("PK_IdempotencyKeys");

        builder.Property(k => k.Id)
            .HasColumnType("uuid")
            .ValueGeneratedNever();

        builder.Property(k => k.UserId)
            .HasColumnType("uuid")
            .IsRequired();

        builder.Property(k => k.Endpoint)
            .HasMaxLength(IdempotencyKey.MaxEndpointLength)
            .IsRequired();

        builder.Property(k => k.Key)
            .HasMaxLength(IdempotencyKey.MaxKeyLength)
            .IsRequired();

        // char(64), not varchar: a SHA-256 hex string is always exactly this
        // long, so the fixed width documents the invariant in the schema.
        builder.Property(k => k.RequestHash)
            .HasColumnType($"char({IdempotencyKey.HashLength})")
            .IsRequired();

        builder.Property(k => k.ResponseStatusCode)
            .IsRequired();

        builder.Property(k => k.ResponseBody)
            .HasColumnType("jsonb")
            .IsRequired();

        builder.Property(k => k.CreatedAt)
            .HasColumnType("timestamptz")
            .IsRequired();

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(k => k.UserId)
            .HasConstraintName("FK_IdempotencyKeys_Users")
            .OnDelete(DeleteBehavior.Restrict);

        // THE MECHANISM (BR-34). Everything else in this file is bookkeeping.
        //
        // Scoped by UserId as well as Key (BR-33), so one user's key can never
        // collide with another's — clients generate opaque strings and two of
        // them picking the same one must not make one user replay the other's
        // response.
        builder.HasIndex(k => new { k.UserId, k.Endpoint, k.Key })
            .IsUnique()
            .HasDatabaseName("UX_IdempotencyKeys_User_Endpoint_Key");

        // BR-35: supports the 24-hour retention sweep. The sweep job itself is
        // explicitly out of scope; the index it would need is here so the
        // answer to "does this table grow forever?" is "yes, and here is the
        // one-line job that fixes it".
        builder.HasIndex(k => k.CreatedAt)
            .HasDatabaseName("IX_IdempotencyKeys_CreatedAt");
    }
}
