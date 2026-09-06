using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TransactionLedger.Data;
using TransactionLedger.DTOs;
using TransactionLedger.Tests.Infrastructure;

namespace TransactionLedger.Tests.Integration;

/// <summary>
/// Tests T1-T6 and O5 from docs/07-testing-strategy.md §4.5.
/// </summary>
public sealed class TransferTests : IntegrationTestBase
{
    public TransferTests(DatabaseFixture database) : base(database)
    {
    }

    // T1 - the headline behaviour: exactly 500 leaves one and arrives at the other.
    [Fact]
    public async Task Transfer_of_500_moves_exactly_500()
    {
        var source = await FundedAccountAsync("T1 source", 1000m);
        var destination = await CreateAccountAsync("T1 destination");

        var response = await TransferAsync(source.Id, destination.Id, 500m);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(500m, await BalanceOfAsync(source.Id));
        Assert.Equal(500m, await BalanceOfAsync(destination.Id));
    }

    // T2 - BR-27: both legs are REAL ledger entries, linked and categorised.
    // Without this the destination's history could not explain its balance.
    [Fact]
    public async Task Both_legs_exist_as_transactions_with_TransferId_and_category_Transfer()
    {
        var source = await FundedAccountAsync("T2 source", 1000m);
        var destination = await CreateAccountAsync("T2 destination");

        var transfer = await CreatedTransferAsync(source.Id, destination.Id, 250m, "Moving savings");

        var debit = await GetTransactionAsync(transfer.DebitTransactionId);
        var credit = await GetTransactionAsync(transfer.CreditTransactionId);

        Assert.Equal("Debit", debit.Type.ToString());
        Assert.Equal(source.Id, debit.AccountId);
        Assert.Equal(250m, debit.Amount);
        Assert.Equal("Transfer", debit.Category.ToString());
        Assert.Equal(transfer.Id, debit.TransferId);

        Assert.Equal("Credit", credit.Type.ToString());
        Assert.Equal(destination.Id, credit.AccountId);
        Assert.Equal(250m, credit.Amount);
        Assert.Equal("Transfer", credit.Category.ToString());
        Assert.Equal(transfer.Id, credit.TransferId);

        // BR-41: one timestamp for both legs, so they cannot sort apart.
        Assert.Equal(debit.OccurredAt, credit.OccurredAt);

        // Both legs appear in their own account's history.
        Assert.Contains(debit.Id, await HistoryIdsAsync(source.Id));
        Assert.Contains(credit.Id, await HistoryIdsAsync(destination.Id));
    }

    // T3 - BR-27.
    [Fact]
    public async Task Transfer_to_the_same_account_returns_400()
    {
        var account = await FundedAccountAsync("T3 self", 1000m);

        var response = await TransferAsync(account.Id, account.Id, 100m);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("SAME_ACCOUNT_TRANSFER", await CodeOfAsync(response));

        // And nothing was written on the way to rejecting it.
        Assert.Equal(1000m, await BalanceOfAsync(account.Id));
        Assert.Equal(1, await CountTransactionsAsync(account.Id));
    }

    // T4 - BR-20. The overdraft rule applies to a transfer exactly as it does
    // to a plain debit, and BOTH balances must be untouched.
    [Fact]
    public async Task Transfer_exceeding_source_balance_returns_409_and_changes_nothing()
    {
        var source = await FundedAccountAsync("T4 source", 100m);
        var destination = await FundedAccountAsync("T4 destination", 50m);

        var response = await TransferAsync(source.Id, destination.Id, 500m);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("INSUFFICIENT_FUNDS", await CodeOfAsync(response));

        Assert.Equal(100m, await BalanceOfAsync(source.Id));
        Assert.Equal(50m, await BalanceOfAsync(destination.Id));
        Assert.Equal(0, await CountTransfersAsync());
    }

    /// <summary>
    /// T5 - BR-28, the atomicity proof and the most important test in this file.
    ///
    /// A temporary CHECK constraint makes any balance increase on the
    /// destination illegal, so the credit leg fails DURING the transfer, after
    /// the debit leg has already been written. If the operation were two
    /// independent steps rather than one transaction, the source would come out
    /// of this permanently poorer and the money would be gone.
    ///
    /// The constraint is scoped by account ID, so rows belonging to other test
    /// classes running in parallel satisfy it trivially.
    /// </summary>
    [Fact]
    public async Task Forced_failure_on_the_credit_leg_leaves_the_source_untouched()
    {
        var source = await FundedAccountAsync("T5 source", 1000m);
        var destination = await CreateAccountAsync("T5 destination");

        var balanceBefore = await BalanceOfAsync(source.Id);
        var countBefore = await CountTransactionsAsync(source.Id);
        var transfersBefore = await CountTransfersAsync();

        await ExecuteSqlAsync($"""
            ALTER TABLE "Accounts" ADD CONSTRAINT "CK_T5_ForcedFailure"
            CHECK ("Id" <> '{destination.Id}'::uuid OR "Balance" = 0)
            """);

        HttpResponseMessage response;
        try
        {
            response = await TransferAsync(source.Id, destination.Id, 400m);
        }
        finally
        {
            await ExecuteSqlAsync("""
                ALTER TABLE "Accounts" DROP CONSTRAINT IF EXISTS "CK_T5_ForcedFailure"
                """);
        }

        // The constraint violation surfaces as a 500: it is an infrastructure
        // failure, not a business rule the API models. What matters is not the
        // status code but that NOTHING survived it.
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);

        Assert.Equal(balanceBefore, await BalanceOfAsync(source.Id));
        Assert.Equal(countBefore, await CountTransactionsAsync(source.Id));
        Assert.Equal(decimal.Zero, await BalanceOfAsync(destination.Id));
        Assert.Equal(0, await CountTransactionsAsync(destination.Id));

        // No orphaned debit leg, and no half-written transfer row.
        Assert.Equal(transfersBefore, await CountTransfersAsync());
    }

    // T6 - the conservation law. Whatever else a transfer does, it must not
    // create or destroy money. This is the single best sanity check in the
    // system (docs/07 §4.5).
    [Fact]
    public async Task Sum_of_all_account_balances_is_unchanged_by_a_transfer()
    {
        var a = await FundedAccountAsync("T6 a", 700m);
        var b = await FundedAccountAsync("T6 b", 300m);
        var c = await CreateAccountAsync("T6 c");

        var before = await SumOfMyBalancesAsync();

        await CreatedTransferAsync(a.Id, b.Id, 125.75m);
        await CreatedTransferAsync(b.Id, c.Id, 400m);
        await CreatedTransferAsync(c.Id, a.Id, 99.01m);

        Assert.Equal(before, await SumOfMyBalancesAsync());

        // And every individual balance still equals the sum of its own ledger.
        foreach (var accountId in new[] { a.Id, b.Id, c.Id })
        {
            Assert.Equal(await LedgerSumOfAsync(accountId), await BalanceOfAsync(accountId));
        }
    }

    // O5 - BR-08. Neither direction is allowed, and a foreign account is
    // indistinguishable from one that does not exist.
    [Fact]
    public async Task User_B_transferring_from_or_to_user_As_account_returns_404()
    {
        var mine = await FundedAccountAsync("O5 mine", 500m);

        var (userB, _) = await TestClient.AuthedClientAsync(Factory);
        using var b = userB;

        var theirs = await CreateAccountAsync("O5 theirs", userB);
        await FundAsync(theirs.Id, 500m, userB);

        // Pulling money out of my account into theirs.
        var pull = await TransferAsync(mine.Id, theirs.Id, 100m, client: userB);
        Assert.Equal(HttpStatusCode.NotFound, pull.StatusCode);
        Assert.Equal("NOT_FOUND", await CodeOfAsync(pull));

        // Pushing money from their account into mine.
        var push = await TransferAsync(theirs.Id, mine.Id, 100m, client: userB);
        Assert.Equal(HttpStatusCode.NotFound, push.StatusCode);

        // A nonexistent account produces the SAME response as a foreign one.
        var ghost = await TransferAsync(theirs.Id, Guid.CreateVersion7(), 100m, client: userB);
        Assert.Equal(HttpStatusCode.NotFound, ghost.StatusCode);
        Assert.Equal(await BodyOfAsync(push), await BodyOfAsync(ghost));

        Assert.Equal(500m, await BalanceOfAsync(mine.Id));
    }

    [Theory]
    [InlineData(0, "AMOUNT_NOT_POSITIVE")]
    [InlineData(-1, "AMOUNT_NOT_POSITIVE")]
    [InlineData(0.001, "AMOUNT_SCALE_INVALID")]
    [InlineData(1000000000.01, "AMOUNT_TOO_LARGE")]
    public async Task Invalid_transfer_amounts_are_rejected_with_400(decimal amount, string expectedCode)
    {
        var source = await FundedAccountAsync($"Amount {amount} source", 1000m);
        var destination = await CreateAccountAsync($"Amount {amount} destination");

        var response = await TransferAsync(source.Id, destination.Id, amount);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(expectedCode, await CodeOfAsync(response));
        Assert.Equal(1000m, await BalanceOfAsync(source.Id));
    }

    [Fact]
    public async Task Transfers_endpoint_without_a_token_returns_401()
    {
        var response = await Client.PostWithKeyAsync("/api/transfers", new
        {
            sourceAccountId = Guid.CreateVersion7(),
            destinationAccountId = Guid.CreateVersion7(),
            amount = 10m
        });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Client.GetAsync("/api/transfers")).StatusCode);
    }

    [Fact]
    public async Task Transfer_list_is_paginated_newest_first_and_scoped_to_the_caller()
    {
        var source = await FundedAccountAsync("List source", 1000m);
        var destination = await CreateAccountAsync("List destination");

        var created = new List<Guid>();
        for (var i = 0; i < 5; i++)
        {
            created.Add((await CreatedTransferAsync(source.Id, destination.Id, 10m)).Id);
        }

        var page = await AuthedClient.GetFromJsonAsync<PagedResponse<TransferResponse>>(
            "/api/transfers?page=1&pageSize=2", TestClient.JsonOptions);

        Assert.Equal(5, page!.TotalItems);
        Assert.Equal(3, page.TotalPages);
        Assert.Equal(2, page.Items.Count);

        // Newest first (BR-41).
        Assert.Equal(created[^1], page.Items[0].Id);

        // A second user sees none of them (BR-07).
        var (userB, _) = await TestClient.AuthedClientAsync(Factory);
        using var b = userB;

        var theirs = await userB.GetFromJsonAsync<PagedResponse<TransferResponse>>(
            "/api/transfers", TestClient.JsonOptions);

        Assert.Equal(0, theirs!.TotalItems);
    }

    [Theory]
    [InlineData("?page=0")]
    [InlineData("?pageSize=0")]
    [InlineData("?pageSize=101")]
    public async Task Out_of_range_transfer_pagination_returns_400(string queryString)
    {
        var response = await AuthedClient.GetAsync($"/api/transfers{queryString}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("PAGINATION_INVALID", await CodeOfAsync(response));
    }

    // ---- helpers ----------------------------------------------------------

    private async Task<AccountResponse> CreateAccountAsync(string name, HttpClient? client = null)
    {
        var response = await (client ?? AuthedClient)
            .PostAsJsonAsync("/api/accounts", new { name, type = "Cash" });
        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<AccountResponse>(TestClient.JsonOptions))!;
    }

    private async Task<AccountResponse> FundedAccountAsync(string name, decimal amount)
    {
        var account = await CreateAccountAsync(name);
        await FundAsync(account.Id, amount);
        return account;
    }

    private async Task FundAsync(Guid accountId, decimal amount, HttpClient? client = null)
    {
        var response = await (client ?? AuthedClient).PostWithKeyAsync(
            $"/api/accounts/{accountId}/transactions",
            new { type = "Credit", amount, category = "Salary" });

        response.EnsureSuccessStatusCode();
    }

    private Task<HttpResponseMessage> TransferAsync(
        Guid sourceAccountId,
        Guid destinationAccountId,
        decimal amount,
        string? description = null,
        HttpClient? client = null) =>
        (client ?? AuthedClient).PostWithKeyAsync("/api/transfers", new
        {
            sourceAccountId,
            destinationAccountId,
            amount,
            description
        });

    private async Task<TransferResponse> CreatedTransferAsync(
        Guid sourceAccountId,
        Guid destinationAccountId,
        decimal amount,
        string? description = null)
    {
        var response = await TransferAsync(sourceAccountId, destinationAccountId, amount, description);
        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<TransferResponse>(TestClient.JsonOptions))!;
    }

    private async Task<TransactionResponse> GetTransactionAsync(Guid transactionId) =>
        (await AuthedClient.GetFromJsonAsync<TransactionResponse>(
            $"/api/transactions/{transactionId}", TestClient.JsonOptions))!;

    private async Task<IReadOnlyList<Guid>> HistoryIdsAsync(Guid accountId)
    {
        var page = await AuthedClient.GetFromJsonAsync<PagedResponse<TransactionResponse>>(
            $"/api/accounts/{accountId}/transactions?pageSize=100", TestClient.JsonOptions);

        return page!.Items.Select(t => t.Id).ToList();
    }

    private async Task<decimal> BalanceOfAsync(Guid accountId)
    {
        var balance = await AuthedClient.GetFromJsonAsync<AccountBalanceResponse>(
            $"/api/accounts/{accountId}/balance", TestClient.JsonOptions);

        return balance!.Balance;
    }

    /// <summary>Sum over this class's own user only, so parallel classes cannot perturb it.</summary>
    private async Task<decimal> SumOfMyBalancesAsync()
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        return await db.Accounts.Where(a => a.UserId == User.Id).SumAsync(a => a.Balance);
    }

    /// <summary>BR-17/BR-18: recompute the balance from the ledger itself.</summary>
    private async Task<decimal> LedgerSumOfAsync(Guid accountId)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var rows = await db.Transactions
            .Where(t => t.AccountId == accountId)
            .Select(t => new { t.Type, t.Amount })
            .ToListAsync();

        return rows.Sum(r => r.Type == TransactionLedger.Domain.TransactionType.Credit ? r.Amount : -r.Amount);
    }

    private async Task<int> CountTransactionsAsync(Guid accountId)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        return await db.Transactions.CountAsync(t => t.AccountId == accountId);
    }

    private async Task<int> CountTransfersAsync()
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        return await db.Transfers.CountAsync(t => t.UserId == User.Id);
    }

    private async Task ExecuteSqlAsync(string sql)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        await db.Database.ExecuteSqlRawAsync(sql);
    }

    private static async Task<string?> CodeOfAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync())
            .RootElement.GetProperty("code").GetString();

    /// <summary>Body with traceId stripped, so two responses can be compared for BR-08.</summary>
    private static async Task<string> BodyOfAsync(HttpResponseMessage response)
    {
        var root = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

        return string.Join(
            '|',
            root.EnumerateObject()
                .Where(p => p.Name != "traceId")
                .OrderBy(p => p.Name)
                .Select(p => $"{p.Name}={p.Value}"));
    }
}
