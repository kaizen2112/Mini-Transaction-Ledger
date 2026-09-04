using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TransactionLedger.DTOs;
using TransactionLedger.Extensions;
using TransactionLedger.Services;

namespace TransactionLedger.Controllers;

/// <summary>
/// The four account endpoints from docs/05-api-contract.md §4. Thin by design:
/// model binding validates, one service call does the work, the result is
/// mapped. The caller's identity comes from the token on every action and is
/// never accepted from the client (BR-06).
/// </summary>
[ApiController]
[Route("api/accounts")]
[Authorize]
public sealed class AccountsController : ControllerBase
{
    private readonly IAccountService _accountService;

    public AccountsController(IAccountService accountService)
    {
        _accountService = accountService;
    }

    [HttpPost]
    [ProducesResponseType(typeof(AccountResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create(
        [FromBody] CreateAccountRequest request,
        CancellationToken cancellationToken)
    {
        var account = await _accountService.CreateAsync(User.GetUserId(), request, cancellationToken);

        return CreatedAtAction(nameof(Get), new { accountId = account.Id }, account);
    }

    [HttpGet]
    [ProducesResponseType(typeof(AccountListResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> List(CancellationToken cancellationToken)
    {
        var accounts = await _accountService.ListAsync(User.GetUserId(), cancellationToken);

        return Ok(accounts);
    }

    [HttpGet("{accountId:guid}")]
    [ProducesResponseType(typeof(AccountResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(Guid accountId, CancellationToken cancellationToken)
    {
        var account = await _accountService.GetAsync(User.GetUserId(), accountId, cancellationToken);

        return Ok(account);
    }

    [HttpGet("{accountId:guid}/balance")]
    [ProducesResponseType(typeof(AccountBalanceResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetBalance(Guid accountId, CancellationToken cancellationToken)
    {
        var balance = await _accountService.GetBalanceAsync(User.GetUserId(), accountId, cancellationToken);

        return Ok(balance);
    }
}
