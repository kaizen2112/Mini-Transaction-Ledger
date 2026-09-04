namespace TransactionLedger.Services;

/// <summary>
/// BR-36: an audit record is written in the SAME database transaction as the
/// action it records.
///
/// STUB. The AuditLogs table and its real implementation arrive in Step 19.
/// The interface and its call sites exist now so that the write is already
/// inside the transaction boundary — retrofitting the call site later is
/// exactly how an audit entry ends up outside the transaction it was meant to
/// be atomic with.
/// </summary>
public interface IAuditService
{
    Task RecordAsync(
        Guid userId,
        string action,
        string entityType,
        Guid entityId,
        object? metadata,
        CancellationToken cancellationToken);
}
