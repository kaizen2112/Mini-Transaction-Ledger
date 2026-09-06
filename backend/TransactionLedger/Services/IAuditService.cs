using TransactionLedger.Domain;

namespace TransactionLedger.Services;

/// <summary>
/// BR-36: an audit record is written in the SAME database transaction as the
/// action it records.
///
/// That is the entire reason this is an interface with one method rather than
/// a line of code inside each service: the call site has to sit inside the
/// caller's open transaction, and a single shared method makes that placement
/// the same everywhere. If audit writes happened after the commit, a crash
/// between the two would leave money moved with no record of who moved it —
/// and a rollback would leave a record of something that never happened.
///
/// There is no read method. No API exposes audit logs, and no service reads
/// them (BR-38); they are inspected directly in the database.
/// </summary>
public interface IAuditService
{
    Task RecordAsync(
        Guid userId,
        AuditAction action,
        string entityType,
        Guid entityId,
        object? metadata,
        CancellationToken cancellationToken);
}
