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
/// Tests R1-R8 and O4 from docs/07-testing-strategy.md §4.6.
/// </summary>
public sealed class ReversalTests : IntegrationTestBase
{
    public ReversalTests(DatabaseFixture database) : base(database)
    {
    }

    // R1 - BR-22. The reversal nets the original to exactly zero.
    [Fact]
    public async Task Reversing_a_debit_of_100_restores_the_balance_and_creates_a_credit_of_100()
    {
        var account = await FundedAccountAsync("R1", 500m);
        var debit = await PostAsync(account.Id, "Debit", 100m);

        Assert.Equal(400m, await BalanceOfAsync(account.Id));

        var response = await ReverseAsync(debit.Id);
        var reversal = await ReadAsync<TransactionResponse>(response);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal("Credit", reversal.Type.ToString());
        Assert.Equal(100m, reversal.Amount);
        Assert.Equal(account.Id, reversal.AccountId);
        Assert.Equal(500m, await BalanceOfAsync(account.Id));
    }

    /// <summary>
    /// R2 - BR-21, the load-bearing test of this whole block. Every column of
    /// the original is compared before and after, read straight from the
    /// database rather than through the API, so a stray UPDATE anywhere in the
    /// reversal path cannot hide behind a projection.
    /// </summary>
    [Fact]
    public async Task The_original_row_is_byte_identical_before_and_after_the_reversal()
    {
        var account = await FundedAccountAsync("R2", 500m);
        var debit = await PostAsync(account.Id, "Debit", 100m, description: "Original description");

        var before = await RawTransactionAsync(debit.Id);

        await ReverseAsync(debit.Id);

        var after = await RawTransactionAsync(debit.Id);

        Assert.Equal(before, after);
    }

    // R3 - BR-22. The link lives on the reversal and points backwards.
    [Fact]
    public async Task The_reversal_carries_ReversesTransactionId_and_category_Reversal()
    {
        var account = await FundedAccountAsync("R3", 500m);
        var debit = await PostAsync(account.Id, "Debit", 100m);

        var reversal = await ReadAsync<TransactionResponse>(await ReverseAsync(debit.Id));

        Assert.Equal(debit.Id, reversal.ReversesTransactionId);
        Assert.Equal("Reversal", reversal.Category.ToString());
        Assert.False(reversal.IsReversed);

        // The original still carries no forward link: it was never written to.
        var original = await GetTransactionAsync(debit.Id);
        Assert.Null(original.ReversesTransactionId);
    }

    // R4 - BR-23 check 3.
    [Fact]
    public async Task A_second_reversal_attempt_returns_409_already_reversed()
    {
        var account = await FundedAccountAsync("R4", 500m);
        var debit = await PostAsync(account.Id, "Debit", 100m);

        Assert.Equal(HttpStatusCode.Created, (await ReverseAsync(debit.Id)).StatusCode);

        var second = await ReverseAsync(debit.Id);

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Equal("ALREADY_REVERSED", await CodeOfAsync(second));

        // And the balance did not move a second time.
        Assert.Equal(500m, await BalanceOfAsync(account.Id));
        Assert.Equal(1, await CountReversalsOfAsync(debit.Id));
    }

    /// <summary>
    /// R4, the part that matters. The service check is a friendly guard that
    /// two concurrent requests can both pass; UX_Transactions_Reverses is the
    /// one that actually holds (BR-22, BR-11). Proven by bypassing the API and
    /// inserting a second reversal row directly.
    /// </summary>
    [Fact]
    public async Task The_unique_index_refuses_a_second_reversal_even_when_the_service_check_is_bypassed()
    {
        var account = await FundedAccountAsync("R4 index", 500m);
        var debit = await PostAsync(account.Id, "Debit", 100m);

        await ReverseAsync(debit.Id);

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var ex = await Assert.ThrowsAnyAsync<Exception>(() =>
            db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO "Transactions"
                    ("Id", "AccountId", "Type", "Amount", "Category", "OccurredAt", "ReversesTransactionId")
                VALUES
                    ({Guid.CreateVersion7()}, {account.Id}, 0, {100m}, 9, {DateTime.UtcNow}, {debit.Id})
                """));

        Assert.Contains("UX_Transactions_Reverses", ex.ToString());
    }

    // R5 - BR-23 check 2.
    [Fact]
    public async Task Reversing_a_reversal_returns_409()
    {
        var account = await FundedAccountAsync("R5", 500m);
        var debit = await PostAsync(account.Id, "Debit", 100m);

        var reversal = await ReadAsync<TransactionResponse>(await ReverseAsync(debit.Id));

        var response = await ReverseAsync(reversal.Id);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("CANNOT_REVERSE_A_REVERSAL", await CodeOfAsync(response));
        Assert.Equal(500m, await BalanceOfAsync(account.Id));
    }

    // R6 - BR-23 check 4. Reversing one leg would create money on one side and
    // destroy it on the other.
    [Fact]
    public async Task Reversing_a_transfer_leg_returns_409()
    {
        var source = await FundedAccountAsync("R6 source", 500m);
        var destination = await CreateAccountAsync("R6 destination");

        var transferResponse = await AuthedClient.PostAsJsonAsync("/api/transfers", new
        {
            sourceAccountId = source.Id,
            destinationAccountId = destination.Id,
            amount = 200m
        });
        transferResponse.EnsureSuccessStatusCode();
        var transfer = await ReadAsync<TransferResponse>(transferResponse);

        foreach (var legId in new[] { transfer.DebitTransactionId, transfer.CreditTransactionId })
        {
            var response = await ReverseAsync(legId);

            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            Assert.Equal("TRANSFER_LEG_NOT_REVERSIBLE", await CodeOfAsync(response));
        }

        Assert.Equal(300m, await BalanceOfAsync(source.Id));
        Assert.Equal(200m, await BalanceOfAsync(destination.Id));
    }

    // R7 - BR-23 check 5. Reversing a credit removes money that may already
    // have been spent, and the overdraft rule is absolute (BR-20).
    [Fact]
    public async Task Reversing_a_credit_that_would_overdraw_returns_409_insufficient_funds()
    {
        var account = await CreateAccountAsync("R7");
        var credit = await PostAsync(account.Id, "Credit", 100m);
        await PostAsync(account.Id, "Debit", 80m);

        // Balance is 20; reversing the credit of 100 would drive it to -80.
        var response = await ReverseAsync(credit.Id);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("INSUFFICIENT_FUNDS", await CodeOfAsync(response));
        Assert.Equal(20m, await BalanceOfAsync(account.Id));

        // Funding the account makes the same reversal succeed - the refusal was
        // about the balance, not about the transaction being ineligible.
        await PostAsync(account.Id, "Credit", 500m);
        Assert.Equal(HttpStatusCode.Created, (await ReverseAsync(credit.Id)).StatusCode);
        Assert.Equal(420m, await BalanceOfAsync(account.Id));
    }

    // R8 - contract §5: the original's isReversed projection flips, without the
    // original row having been written to.
    [Fact]
    public async Task IsReversed_on_the_original_flips_to_true_after_reversal()
    {
        var account = await FundedAccountAsync("R8", 500m);
        var debit = await PostAsync(account.Id, "Debit", 100m);

        Assert.False((await GetTransactionAsync(debit.Id)).IsReversed);

        await ReverseAsync(debit.Id);

        Assert.True((await GetTransactionAsync(debit.Id)).IsReversed);

        // And through the history projection too, not just the by-id one.
        var history = await AuthedClient.GetFromJsonAsync<PagedResponse<TransactionResponse>>(
            $"/api/accounts/{account.Id}/transactions?pageSize=100", TestClient.JsonOptions);

        Assert.True(history!.Items.Single(t => t.Id == debit.Id).IsReversed);
    }

    // O4 - BR-08.
    [Fact]
    public async Task User_B_reversing_user_As_transaction_returns_404()
    {
        var account = await FundedAccountAsync("O4", 500m);
        var debit = await PostAsync(account.Id, "Debit", 100m);

        var (userB, _) = await TestClient.AuthedClientAsync(Factory);
        using var b = userB;

        var foreign = await userB.PostAsJsonAsync(
            $"/api/transactions/{debit.Id}/reverse", new { description = (string?)null });

        Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
        Assert.Equal("NOT_FOUND", await CodeOfAsync(foreign));

        // Identical to a transaction that does not exist (BR-08).
        var ghost = await userB.PostAsJsonAsync(
            $"/api/transactions/{Guid.CreateVersion7()}/reverse", new { description = (string?)null });

        Assert.Equal(await BodyOfAsync(foreign), await BodyOfAsync(ghost));

        // Nothing happened to user A's account.
        Assert.Equal(400m, await BalanceOfAsync(account.Id));
        Assert.False((await GetTransactionAsync(debit.Id)).IsReversed);
    }

    [Fact]
    public async Task Reversal_without_a_token_returns_401()
    {
        var account = await FundedAccountAsync("Anonymous", 500m);
        var debit = await PostAsync(account.Id, "Debit", 100m);

        var response = await Client.PostAsJsonAsync(
            $"/api/transactions/{debit.Id}/reverse", new { description = (string?)null });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Reversing_a_credit_is_a_debit_of_the_same_amount()
    {
        var account = await FundedAccountAsync("Credit reversal", 500m);
        var credit = await PostAsync(account.Id, "Credit", 250m);

        Assert.Equal(750m, await BalanceOfAsync(account.Id));

        var reversal = await ReadAsync<TransactionResponse>(await ReverseAsync(credit.Id));

        Assert.Equal("Debit", reversal.Type.ToString());
        Assert.Equal(250m, reversal.Amount);
        Assert.Equal(500m, await BalanceOfAsync(account.Id));
    }

    [Fact]
    public async Task A_supplied_description_is_used_and_a_default_is_generated_otherwise()
    {
        var account = await FundedAccountAsync("Descriptions", 500m);

        var withText = await PostAsync(account.Id, "Debit", 10m);
        var custom = await ReadAsync<TransactionResponse>(
            await ReverseAsync(withText.Id, "Refunded by the merchant"));
        Assert.Equal("Refunded by the merchant", custom.Description);

        var withoutText = await PostAsync(account.Id, "Debit", 20m);
        var generated = await ReadAsync<TransactionResponse>(await ReverseAsync(withoutText.Id));
        Assert.Contains("20.00", generated.Description);
    }

    // ---- helpers ----------------------------------------------------------

    private async Task<AccountResponse> CreateAccountAsync(string name)
    {
        var response = await AuthedClient.PostAsJsonAsync("/api/accounts", new { name, type = "Cash" });
        response.EnsureSuccessStatusCode();

        return await ReadAsync<AccountResponse>(response);
    }

    private async Task<AccountResponse> FundedAccountAsync(string name, decimal amount)
    {
        var account = await CreateAccountAsync(name);
        await PostAsync(account.Id, "Credit", amount);
        return account;
    }

    private async Task<TransactionResponse> PostAsync(
        Guid accountId,
        string type,
        decimal amount,
        string category = "Salary",
        string? description = null)
    {
        var response = await AuthedClient.PostAsJsonAsync(
            $"/api/accounts/{accountId}/transactions",
            new { type, amount, category, description });

        response.EnsureSuccessStatusCode();

        return await ReadAsync<TransactionResponse>(response);
    }

    private Task<HttpResponseMessage> ReverseAsync(Guid transactionId, string? description = null) =>
        AuthedClient.PostAsJsonAsync(
            $"/api/transactions/{transactionId}/reverse",
            new { description });

    private async Task<TransactionResponse> GetTransactionAsync(Guid transactionId) =>
        (await AuthedClient.GetFromJsonAsync<TransactionResponse>(
            $"/api/transactions/{transactionId}", TestClient.JsonOptions))!;

    private async Task<decimal> BalanceOfAsync(Guid accountId)
    {
        var balance = await AuthedClient.GetFromJsonAsync<AccountBalanceResponse>(
            $"/api/accounts/{accountId}/balance", TestClient.JsonOptions);

        return balance!.Balance;
    }

    /// <summary>Every column, straight from the database, for the R2 comparison.</summary>
    private async Task<string> RawTransactionAsync(Guid transactionId)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var row = await db.Transactions
            .AsNoTracking()
            .Where(t => t.Id == transactionId)
            .Select(t => new
            {
                t.Id,
                t.AccountId,
                t.Type,
                t.Amount,
                t.Category,
                t.Description,
                t.OccurredAt,
                t.ReversesTransactionId,
                t.TransferId
            })
            .SingleAsync();

        return JsonSerializer.Serialize(row);
    }

    private async Task<int> CountReversalsOfAsync(Guid transactionId)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        return await db.Transactions.CountAsync(t => t.ReversesTransactionId == transactionId);
    }

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<T>(TestClient.JsonOptions))!;

    private static async Task<string?> CodeOfAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync())
            .RootElement.GetProperty("code").GetString();

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
