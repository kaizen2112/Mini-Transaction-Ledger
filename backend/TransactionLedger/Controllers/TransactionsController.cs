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

        // B7 note: the Location header now has a real target, so this points
        // at GET /api/transactions/{id} on the sibling controller.
        return Created($"/api/transactions/{transaction.Id}", transaction);
    }

    /// <summary>
    /// GET /api/accounts/{accountId}/transactions (contract §5).
    ///
    /// [FromQuery] binds page, pageSize and every BR-42 filter. An
    /// out-of-range page is not an error: a page past the last one returns 200
    /// with an empty items array and correct metadata.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(PagedResponse<TransactionResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> List(
        Guid accountId,
        [FromQuery] TransactionQuery query,
        CancellationToken cancellationToken)
    {
        var page = await _transactionService.ListAsync(
            User.GetUserId(),
            accountId,
            query,
            cancellationToken);

        return Ok(page);
    }
}
