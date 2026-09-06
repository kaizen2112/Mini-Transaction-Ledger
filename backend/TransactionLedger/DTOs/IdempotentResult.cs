namespace TransactionLedger.DTOs;

/// <summary>
/// A response plus whether it was produced now or replayed from a previous
/// identical request (BR-34).
///
/// The flag exists because the two cases differ on the wire: a fresh result is
/// 201 with a Location header, a replay is 200 with Idempotent-Replay: true
/// (contract §5 — "the replay returns 200, not the original 201"). The body is
/// byte-identical either way, so only the controller needs to care.
/// </summary>
public sealed record IdempotentResult<T>(T Value, bool IsReplay);
