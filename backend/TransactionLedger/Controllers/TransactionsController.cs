using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TransactionLedger.Domain;
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
/// The Idempotency-Key header IS required on POST (BR-32): this is one of the
/// two endpoints where an accidental retry duplicates money movement.
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

    /// <summary>
    /// The key is bound as nullable and validated in the service. A
    /// non-nullable [FromHeader] would make [ApiController] emit its own 400
    /// with code VALIDATION_FAILED, but the contract requires
    /// IDEMPOTENCY_KEY_MISSING specifically (§1.2).
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(TransactionResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(TransactionResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Create(
        Guid accountId,
        [FromBody] CreateTransactionRequest request,
        [FromHeader(Name = IdempotencyHeader.Name)] string? idempotencyKey,
        CancellationToken cancellationToken)
    {
        var result = await _transactionService.CreateAsync(
            User.GetUserId(),
            accountId,
            request,
            idempotencyKey,
            cancellationToken);

        // Contract §5: "the replay returns 200, not the original 201". A 201
        // would claim something was created, and on a replay nothing was.
        if (result.IsReplay)
        {
            Response.Headers[IdempotencyHeader.ReplayName] = "true";

            return Ok(result.Value);
        }

        // B7 note: the Location header now has a real target, so this points
        // at GET /api/transactions/{id} on the sibling controller.
        return Created($"/api/transactions/{result.Value.Id}", result.Value);
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
