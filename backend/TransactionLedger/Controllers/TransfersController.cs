using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TransactionLedger.DTOs;
using TransactionLedger.Extensions;
using TransactionLedger.Services;

namespace TransactionLedger.Controllers;

/// <summary>
/// POST and GET /api/transfers per docs/05-api-contract.md §6.
///
/// Not nested under an account, unlike transactions: a transfer belongs to two
/// accounts, so nesting it under either one would imply an ownership the other
/// half does not have.
///
/// The contract marks Idempotency-Key as required. It is NOT enforced yet —
/// idempotency arrives in B10 (contract §9 sequencing note), the same staging
/// used for POST /api/accounts/{id}/transactions in B6. Until then a retried
/// transfer moves the money twice, which is exactly why B10 exists.
/// </summary>
[ApiController]
[Route("api/transfers")]
[Authorize]
public sealed class TransfersController : ControllerBase
{
    private readonly ITransferService _transferService;

    public TransfersController(ITransferService transferService)
    {
        _transferService = transferService;
    }

    [HttpPost]
    [ProducesResponseType(typeof(TransferResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create(
        [FromBody] CreateTransferRequest request,
        CancellationToken cancellationToken)
    {
        var transfer = await _transferService.CreateAsync(
            User.GetUserId(),
            request,
            cancellationToken);

        // No GET /api/transfers/{id} in the contract, so Location points at the
        // collection the new transfer is now the newest member of.
        return Created("/api/transfers", transfer);
    }

    [HttpGet]
    [ProducesResponseType(typeof(PagedResponse<TransferResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> List(
        [FromQuery] TransferQuery query,
        CancellationToken cancellationToken)
    {
        var page = await _transferService.ListAsync(
            User.GetUserId(),
            query,
            cancellationToken);

        return Ok(page);
    }
}
