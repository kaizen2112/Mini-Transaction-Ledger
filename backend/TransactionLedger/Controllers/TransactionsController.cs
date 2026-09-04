using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TransactionLedger.DTOs;
using TransactionLedger.Extensions;
using TransactionLedger.Services;

namespace TransactionLedger.Controllers;

/// <summary>
/// POST /api/accounts/{accountId}/transactions per docs/05-api-contract.md §5.
///
/// There is deliberately no PUT and no DELETE: transactions are immutable and
/// a correction is a compensating entry, not an edit (BR-21, BR-22). The
/// absence of those verbs IS the enforcement.
///
/// The Idempotency-Key header is NOT required yet; it becomes mandatory in B10
/// (contract §9 sequencing note).
/// </summary>
[ApiController]
[Route("api/accounts/{accountId:guid}/transactions")]
[Authorize]
public sealed class TransactionsController : ControllerBase
{
    private readonly ITransactionService _transactionService;

    public TransactionsController(ITransactionService transactionService)
    {
        _transactionService = transactionService;
    }

    [HttpPost]
    [ProducesResponseType(typeof(TransactionResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create(
        Guid accountId,
        [FromBody] CreateTransactionRequest request,
        CancellationToken cancellationToken)
    {
        var transaction = await _transactionService.CreateAsync(
            User.GetUserId(),
            accountId,
            request,
            cancellationToken);

        // No Location header yet: it must point at GET /api/transactions/{id},
        // which arrives in B7. Emitting a header that resolves to 404 would be
        // worse than omitting it; B7 turns this into CreatedAtAction.
        return StatusCode(StatusCodes.Status201Created, transaction);
    }
}
