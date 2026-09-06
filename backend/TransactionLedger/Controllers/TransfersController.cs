using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TransactionLedger.Domain;
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
/// The Idempotency-Key header IS required (BR-32). A transfer is the clearest
/// case for it: a user who retries after a network timeout would otherwise move
/// the money a second time.
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
    [ProducesResponseType(typeof(TransferResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Create(
        [FromBody] CreateTransferRequest request,
        [FromHeader(Name = IdempotencyHeader.Name)] string? idempotencyKey,
        CancellationToken cancellationToken)
    {
        var result = await _transferService.CreateAsync(
            User.GetUserId(),
            request,
            idempotencyKey,
            cancellationToken);

        // Contract §6: a replay is 200, never the original 201.
        if (result.IsReplay)
        {
            Response.Headers[IdempotencyHeader.ReplayName] = "true";

            return Ok(result.Value);
        }

        // No GET /api/transfers/{id} in the contract, so Location points at the
        // collection the new transfer is now the newest member of.
        return Created("/api/transfers", result.Value);
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
