using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TransactionLedger.DTOs;
using TransactionLedger.Extensions;
using TransactionLedger.Services;

namespace TransactionLedger.Controllers;

/// <summary>
/// GET /api/transactions/{transactionId} per docs/05-api-contract.md §5.
///
/// A separate controller because the route is NOT nested under an account:
/// the contract exposes a transaction by its own id, and ownership is resolved
/// through the join to Accounts rather than from the URL (BR-07). Still no PUT
/// and no DELETE anywhere — transactions are immutable (BR-21).
/// </summary>
[ApiController]
[Route("api/transactions")]
[Authorize]
public sealed class TransactionLookupController : ControllerBase
{
    private readonly ITransactionService _transactionService;

    public TransactionLookupController(ITransactionService transactionService)
    {
        _transactionService = transactionService;
    }

    [HttpGet("{transactionId:guid}")]
    [ProducesResponseType(typeof(TransactionResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(Guid transactionId, CancellationToken cancellationToken)
    {
        var transaction = await _transactionService.GetAsync(
            User.GetUserId(),
            transactionId,
            cancellationToken);

        return Ok(transaction);
    }

    /// <summary>
    /// POST /api/transactions/{transactionId}/reverse (contract §5).
    ///
    /// This is the ONLY way to correct a mistake, and it is a POST that creates
    /// a new row — not a PUT or a DELETE on the original (BR-21). The body is
    /// optional, hence the nullable binding: a reversal derives its amount,
    /// type and account from what it reverses, so there is nothing the caller
    /// must supply.
    ///
    /// No Idempotency-Key: the unique index on ReversesTransactionId makes a
    /// repeat impossible by construction, and it returns ALREADY_REVERSED.
    /// </summary>
    [HttpPost("{transactionId:guid}/reverse")]
    [ProducesResponseType(typeof(TransactionResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Reverse(
        Guid transactionId,
        [FromBody] ReverseTransactionRequest? request,
        CancellationToken cancellationToken)
    {
        var reversal = await _transactionService.ReverseAsync(
            User.GetUserId(),
            transactionId,
            request,
            cancellationToken);

        // 201 returns the REVERSAL row, which is what was created.
        return Created($"/api/transactions/{reversal.Id}", reversal);
    }
}
