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
/// Tests L2-L10 and O2 from docs/07-testing-strategy.md §4.2/§4.3.
/// </summary>
public sealed class TransactionTests : IntegrationTestBase
{
    public TransactionTests(DatabaseFixture database) : base(database)
    {
    }

    // L2
    [Fact]
    public async Task Credit_of_200_on_balance_500_gives_700()
    {
        var account = await CreateAccountAsync("L2 credit");
        await PostAsync(account.Id, "Credit", 500m);

        var response = await PostAsync(account.Id, "Credit", 200m);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(700m, await BalanceOfAsync(account.Id));
    }

    // L3
    [Fact]
    public async Task Debit_of_200_on_balance_500_gives_300()
    {
        var account = await CreateAccountAsync("L3 debit");
        await PostAsync(account.Id, "Credit", 500m);

        var response = await PostAsync(account.Id, "Debit", 200m);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(300m, await BalanceOfAsync(account.Id));
    }

    // L4 - BR-03 and BR-05, each with its own contract error code.
    [Theory]
    [InlineData(0, "AMOUNT_NOT_POSITIVE")]
    [InlineData(-1, "AMOUNT_NOT_POSITIVE")]
    [InlineData(0.001, "AMOUNT_SCALE_INVALID")]
    [InlineData(1000000000.01, "AMOUNT_TOO_LARGE")]
    public async Task Invalid_amounts_are_rejected_with_400(decimal amount, string expectedCode)
    {
        var account = await CreateAccountAsync($"L4 amount {amount}");

        var response = await PostAsync(account.Id, "Credit", amount);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(expectedCode, await CodeOfAsync(response));
    }

    // L5 - the one that matters. 409, and NOTHING moved.
    [Fact]
    public async Task Debit_exceeding_balance_returns_409_and_changes_nothing()
    {
        var account = await CreateAccountAsync("L5 overdraft");
        await PostAsync(account.Id, "Credit", 500m);

        var before = await CountTransactionsAsync(account.Id);

        var response = await PostAsync(account.Id, "Debit", 1000m);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("INSUFFICIENT_FUNDS", await CodeOfAsync(response));

        // The contract requires both figures in the detail so the UI needs no
        // second call.
        var detail = await DetailOfAsync(response);
        Assert.Contains("500.00", detail);
        Assert.Contains("1000.00", detail);

        Assert.Equal(500m, await BalanceOfAsync(account.Id));
        Assert.Equal(before, await CountTransactionsAsync(account.Id));
    }

    // L6 - BR-24: Transfer and Reversal are system-only.
    [Theory]
    [InlineData("Transfer")]
    [InlineData("Reversal")]
    public async Task System_only_categories_supplied_by_a_client_are_rejected(string category)
    {
        var account = await CreateAccountAsync($"L6 {category}");

        var response = await PostAsync(account.Id, "Credit", 10m, category);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("CATEGORY_SYSTEM_ONLY", await CodeOfAsync(response));
    }

    // L7 - BR-26: the server clock wins; a client value is ignored.
    [Fact]
    public async Task OccurredAt_is_server_set_and_a_client_value_is_ignored()
    {
        var account = await CreateAccountAsync("L7 clock");
        var backdated = new DateTime(1999, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        var before = DateTime.UtcNow.AddSeconds(-5);
        var response = await AuthedClient.PostWithKeyAsync(
            $"/api/accounts/{account.Id}/transactions",
            new
            {
                type = "Credit",
                amount = 10m,
                category = "Salary",
                occurredAt = backdated
            });
        var after = DateTime.UtcNow.AddSeconds(5);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var created = (await response.Content.ReadFromJsonAsync<TransactionResponse>(
            TestClient.JsonOptions))!;

        Assert.NotEqual(backdated, created.OccurredAt);
        Assert.InRange(created.OccurredAt, before, after);
    }

    // L8 - BR-21: no verb exists to update or delete a transaction.
    [Fact]
    public async Task No_verb_exists_to_update_or_delete_a_transaction()
    {
        var account = await CreateAccountAsync("L8 immutable");
        var created = await CreatedTransactionAsync(account.Id, "Credit", 50m);

        var route = $"/api/accounts/{account.Id}/transactions/{created.Id}";

        var put = await AuthedClient.PutAsJsonAsync(route, new { amount = 999m });
        var delete = await AuthedClient.DeleteAsync(route);

        Assert.Contains(put.StatusCode, new[] { HttpStatusCode.NotFound, HttpStatusCode.MethodNotAllowed });
        Assert.Contains(delete.StatusCode, new[] { HttpStatusCode.NotFound, HttpStatusCode.MethodNotAllowed });

        // And the row is untouched.
        Assert.Equal(50m, await BalanceOfAsync(account.Id));
    }

    // L9 - tests the DATABASE, not the application. Proves the constraint is
    // actually deployed rather than merely described in a document.
    [Fact]
    public async Task Direct_db_write_violating_the_amount_check_throws()
    {
        var account = await CreateAccountAsync("L9 check");

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var ex = await Assert.ThrowsAnyAsync<Exception>(() =>
            db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO "Transactions"
                    ("Id", "AccountId", "Type", "Amount", "Category", "OccurredAt")
                VALUES
                    ({Guid.CreateVersion7()}, {account.Id}, 0, {-5m}, 0, {DateTime.UtcNow})
                """));

        Assert.Contains("CK_Transactions_AmountPositive", ex.ToString());
    }

    // L10
    [Fact]
    public async Task Direct_db_write_violating_the_balance_check_throws()
    {
        var account = await CreateAccountAsync("L10 check");

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var ex = await Assert.ThrowsAnyAsync<Exception>(() =>
            db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE "Accounts" SET "Balance" = {-1m} WHERE "Id" = {account.Id}
                """));

        Assert.Contains("CK_Accounts_BalanceNonNegative", ex.ToString());
    }

    // O2 - BR-08. Ownership is in the FOR UPDATE predicate, so a foreign
    // account simply yields no row.
    [Fact]
    public async Task User_B_posting_a_transaction_to_user_As_account_returns_404()
    {
        var account = await CreateAccountAsync("O2 foreign");

        var (userB, _) = await TestClient.AuthedClientAsync(Factory);
        using var b = userB;

        var response = await userB.PostWithKeyAsync(
            $"/api/accounts/{account.Id}/transactions",
            new { type = "Credit", amount = 100m, category = "Salary" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("NOT_FOUND", await CodeOfAsync(response));

        // And user A's balance is untouched.
        Assert.Equal(decimal.Zero, await BalanceOfAsync(account.Id));
    }

    [Fact]
    public async Task Posting_to_a_nonexistent_account_returns_404()
    {
        var response = await PostAsync(Guid.CreateVersion7(), "Credit", 100m);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Transactions_endpoint_without_a_token_returns_401()
    {
        var account = await CreateAccountAsync("Anonymous");

        var response = await Client.PostWithKeyAsync(
            $"/api/accounts/{account.Id}/transactions",
            new { type = "Credit", amount = 10m, category = "Salary" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // Contract §1.3 plus BR-25.
    [Fact]
    public async Task Created_transaction_echoes_the_contracted_shape()
    {
        var account = await CreateAccountAsync("Shape");

        var response = await AuthedClient.PostWithKeyAsync(
            $"/api/accounts/{account.Id}/transactions",
            new
            {
                type = "Debit",
                amount = 0m + 120.5m,
                category = "Food",
                description = "  Lunch with colleagues  "
            });

        // Balance is zero, so a debit must fail; credit first instead.
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        await PostAsync(account.Id, "Credit", 500m);

        var raw = await AuthedClient.PostWithKeyAsync(
            $"/api/accounts/{account.Id}/transactions",
            new
            {
                type = "Debit",
                amount = 120.5m,
                category = "Food",
                description = "  Lunch with colleagues  "
            });

        var body = await raw.Content.ReadAsStringAsync();
        var created = (await raw.Content.ReadFromJsonAsync<TransactionResponse>(
            TestClient.JsonOptions))!;

        Assert.Equal(HttpStatusCode.Created, raw.StatusCode);
        Assert.Contains("\"type\":\"Debit\"", body);
        Assert.Contains("\"category\":\"Food\"", body);
        Assert.Contains("\"amount\":120.50", body);
        Assert.Equal("Lunch with colleagues", created.Description);
        Assert.Null(created.ReversesTransactionId);
        Assert.Null(created.TransferId);
        Assert.False(created.IsReversed);
        Assert.Equal(379.5m, await BalanceOfAsync(account.Id));
    }

    private async Task<AccountResponse> CreateAccountAsync(string name)
    {
        var response = await AuthedClient.PostAsJsonAsync("/api/accounts", new { name, type = "Cash" });
        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<AccountResponse>(TestClient.JsonOptions))!;
    }

    private Task<HttpResponseMessage> PostAsync(
        Guid accountId,
        string type,
        decimal amount,
        string category = "Salary") =>
        AuthedClient.PostWithKeyAsync(
            $"/api/accounts/{accountId}/transactions",
            new { type, amount, category });

    private async Task<TransactionResponse> CreatedTransactionAsync(
        Guid accountId,
        string type,
        decimal amount)
    {
        var response = await PostAsync(accountId, type, amount);
        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<TransactionResponse>(
            TestClient.JsonOptions))!;
    }

    private async Task<decimal> BalanceOfAsync(Guid accountId)
    {
        var balance = await AuthedClient.GetFromJsonAsync<AccountBalanceResponse>(
            $"/api/accounts/{accountId}/balance", TestClient.JsonOptions);

        return balance!.Balance;
    }

    private async Task<int> CountTransactionsAsync(Guid accountId)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        return await db.Transactions.CountAsync(t => t.AccountId == accountId);
    }

    private static async Task<string?> CodeOfAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync())
            .RootElement.GetProperty("code").GetString();

    private static async Task<string> DetailOfAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync())
            .RootElement.GetProperty("detail").GetString() ?? string.Empty;
}
