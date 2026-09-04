namespace TransactionLedger.Services;

/// <summary>
/// Stub implementation: logs, and writes no row yet. Step 19 replaces the body
/// with an insert into AuditLogs using the ambient DbContext, so the write
/// joins the caller's open transaction automatically (BR-36).
/// </summary>
public sealed class AuditService : IAuditService
{
    private readonly ILogger<AuditService> _logger;

    public AuditService(ILogger<AuditService> logger)
    {
        _logger = logger;
    }

    public Task RecordAsync(
        Guid userId,
        string action,
        string entityType,
        Guid entityId,
        object? metadata,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "AUDIT {Action} {EntityType} {EntityId} by {UserId}",
            action,
            entityType,
            entityId,
            userId);

        return Task.CompletedTask;
    }
}
