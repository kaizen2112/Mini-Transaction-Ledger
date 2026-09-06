using System.Text.Json;
using TransactionLedger.Data;
using TransactionLedger.Domain;

namespace TransactionLedger.Services;

/// <summary>
/// Writes the audit row through the SAME scoped AppDbContext the caller is
/// using. That is what makes BR-36 work without any coordination: the context
/// already has the caller's transaction open, so this INSERT joins it
/// automatically and commits or rolls back with everything else.
///
/// SaveChangesAsync is called here rather than left to the caller, because
/// every call site invokes this AFTER its own SaveChangesAsync and immediately
/// before CommitAsync. An Add without a flush would be silently discarded — the
/// worst possible failure mode for an audit trail, since nothing would look
/// broken.
/// </summary>
public sealed class AuditService : IAuditService
{
    /// <summary>
    /// BR-37: the metadata column is small and non-financial. This cap is a
    /// backstop against a caller quietly turning the audit log into a second
    /// ledger by serialising an entire entity into it.
    /// </summary>
    private const int MaxMetadataLength = 2_000;

    private static readonly JsonSerializerOptions MetadataOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly AppDbContext _dbContext;
    private readonly ILogger<AuditService> _logger;

    public AuditService(AppDbContext dbContext, ILogger<AuditService> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task RecordAsync(
        Guid userId,
        AuditAction action,
        string entityType,
        Guid entityId,
        object? metadata,
        CancellationToken cancellationToken)
    {
        var entry = AuditLog.Record(userId, action, entityType, entityId, Serialise(action, metadata));

        _dbContext.AuditLogs.Add(entry);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    private string? Serialise(AuditAction action, object? metadata)
    {
        if (metadata is null)
        {
            return null;
        }

        var json = JsonSerializer.Serialize(metadata, MetadataOptions);

        if (json.Length <= MaxMetadataLength)
        {
            return json;
        }

        // Dropped rather than truncated: truncated JSON is invalid JSON, and
        // jsonb would reject it and fail the whole business transaction. Losing
        // the metadata is survivable; losing the user's transfer is not.
        _logger.LogWarning(
            "Audit metadata for {Action} exceeded {Max} characters and was dropped.",
            action,
            MaxMetadataLength);

        return null;
    }
}
