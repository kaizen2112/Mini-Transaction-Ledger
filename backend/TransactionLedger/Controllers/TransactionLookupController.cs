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
}
