using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TransactionLedger.Data;
using TransactionLedger.Domain;
using TransactionLedger.DTOs;
using TransactionLedger.Tests.Infrastructure;

namespace TransactionLedger.Tests.Integration;

/// <summary>
/// Tests I1-I6 from docs/07-testing-strategy.md §4.7.
/// </summary>
public sealed class IdempotencyTests : IntegrationTestBase
{
    public IdempotencyTests(DatabaseFixture database) : base(database)
    {
    }

    // I1 - BR-32. Required, not optional: an optional safety mechanism is one
    // that is absent in production.
    [Fact]
    public async Task Missing_idempotency_key_returns_400()
    {
        var source = await FundedAccountAsync("I1 source", 500m);
        var destination = await CreateAccountAsync("I1 destination");

        var transfer = await AuthedClient.PostAsJsonAsync("/api/transfers", new
        {
            sourceAccountId = source.Id,
            destinationAccountId = destination.Id,
            amount = 100m
        });

        Assert.Equal(HttpStatusCode.BadRequest, transfer.StatusCode);
        Assert.Equal("IDEMPOTENCY_KEY_MISSING", await CodeOfAsync(transfer));

        var transaction = await AuthedClient.PostAsJsonAsync(
            $"/api/accounts/{source.Id}/transactions",
            new { type = "Credit", amount = 10m, category = "Salary" });

        Assert.Equal(HttpStatusCode.BadRequest, transaction.StatusCode);
        Assert.Equal("IDEMPOTENCY_KEY_MISSING", await CodeOfAsync(transaction));

        // Nothing moved on the way to being rejected.
        Assert.Equal(500m, await BalanceOfAsync(source.Id));
    }

    // BR-33: 8-128 characters.
    [Theory]
    [InlineData("short")]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Malformed_idempotency_key_returns_400(string key)
    {
        var account = await FundedAccountAsync("Malformed key", 100m);

        var response = await AuthedClient.PostWithKeyAsync(
            $"/api/accounts/{account.Id}/transactions",
            new { type = "Credit", amount = 10m, category = "Salary" },
            key);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("IDEMPOTENCY_KEY_MISSING", await CodeOfAsync(response));
    }

    /// <summary>
    /// I2 - the headline behaviour. Same key twice with the same body: exactly
    /// ONE financial effect, and the second call answers 200 with the replay
    /// header rather than the original 201 (contract §5).
    /// </summary>
    [Fact]
    public async Task Same_key_twice_produces_one_effect_and_a_replay()
    {
        var source = await FundedAccountAsync("I2 source", 1000m);
        var destination = await CreateAccountAsync("I2 destination");
        var key = TestClient.NewIdempotencyKey();

        var first = await TransferAsync(source.Id, destination.Id, 250m, key);
        var second = await TransferAsync(source.Id, destination.Id, 250m, key);

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.False(first.Headers.Contains(IdempotencyHeader.ReplayName));

        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Equal("true", second.Headers.GetValues(IdempotencyHeader.ReplayName).Single());

        // ONE effect: 250 moved, not 500.
        Assert.Equal(750m, await BalanceOfAsync(source.Id));
        Assert.Equal(250m, await BalanceOfAsync(destination.Id));
        Assert.Equal(1, await CountTransfersAsync());
    }

    // I3 - the replayed body is byte-identical to the original.
    [Fact]
    public async Task Replayed_body_is_byte_identical_to_the_original()
    {
        var source = await FundedAccountAsync("I3 source", 1000m);
        var destination = await CreateAccountAsync("I3 destination");
        var key = TestClient.NewIdempotencyKey();

        var first = await TransferAsync(source.Id, destination.Id, 123.45m, key);
        var second = await TransferAsync(source.Id, destination.Id, 123.45m, key);

        var firstBody = await first.Content.ReadAsStringAsync();
        var secondBody = await second.Content.ReadAsStringAsync();

        Assert.Equal(firstBody, secondBody);

        // Including the generated id: a replay must not mint a new transfer.
        var replayed = JsonSerializer.Deserialize<TransferResponse>(secondBody, TestClient.JsonOptions)!;
        var original = JsonSerializer.Deserialize<TransferResponse>(firstBody, TestClient.JsonOptions)!;
        Assert.Equal(original.Id, replayed.Id);
        Assert.Equal(original.DebitTransactionId, replayed.DebitTransactionId);
    }

    // The same guarantee for the other protected endpoint.
    [Fact]
    public async Task Same_key_twice_on_a_transaction_produces_one_effect()
    {
        var account = await FundedAccountAsync("I2 transaction", 100m);
        var key = TestClient.NewIdempotencyKey();

        var first = await PostTransactionAsync(account.Id, "Credit", 500m, key);
        var second = await PostTransactionAsync(account.Id, "Credit", 500m, key);

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Equal("true", second.Headers.GetValues(IdempotencyHeader.ReplayName).Single());

        // 100 + 500, not 100 + 500 + 500.
        Assert.Equal(600m, await BalanceOfAsync(account.Id));
        Assert.Equal(await first.Content.ReadAsStringAsync(), await second.Content.ReadAsStringAsync());
    }

    /// <summary>
    /// I4 - BR-34. Same key, different body means the CLIENT has a bug.
    /// Returning the stored response would hide it; performing the new
    /// operation would defeat the key. So: 422, and no second effect.
    /// </summary>
    [Fact]
    public async Task Same_key_different_body_returns_422_and_no_second_effect()
    {
        var source = await FundedAccountAsync("I4 source", 1000m);
        var destination = await CreateAccountAsync("I4 destination");
        var key = TestClient.NewIdempotencyKey();

        await TransferAsync(source.Id, destination.Id, 100m, key);

        var mismatched = await TransferAsync(source.Id, destination.Id, 900m, key);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, mismatched.StatusCode);
        Assert.Equal("IDEMPOTENCY_KEY_REUSED", await CodeOfAsync(mismatched));

        Assert.Equal(900m, await BalanceOfAsync(source.Id));
        Assert.Equal(100m, await BalanceOfAsync(destination.Id));
        Assert.Equal(1, await CountTransfersAsync());
    }

    // I4, the subtler half: a different DESTINATION with the same key and
    // amount is still a different request.
    [Fact]
    public async Task Same_key_different_destination_returns_422()
    {
        var source = await FundedAccountAsync("I4 alt source", 1000m);
        var first = await CreateAccountAsync("I4 alt first");
        var second = await CreateAccountAsync("I4 alt second");
        var key = TestClient.NewIdempotencyKey();

        await TransferAsync(source.Id, first.Id, 100m, key);

        var response = await TransferAsync(source.Id, second.Id, 100m, key);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal(decimal.Zero, await BalanceOfAsync(second.Id));
    }

    // BR-34: the hash is over MEANING, not bytes. A client that reformats its
    // JSON is retrying, not sending a different request.
    [Fact]
    public async Task Reformatted_but_equivalent_body_still_replays()
    {
        var account = await FundedAccountAsync("Canonical", 100m);
        var key = TestClient.NewIdempotencyKey();

        var first = await AuthedClient.PostWithKeyAsync(
            $"/api/accounts/{account.Id}/transactions",
            new { type = "Credit", amount = 50m, category = "Salary", description = (string?)null },
            key);

        // Same meaning, different property order and an omitted optional field.
        var second = await AuthedClient.PostWithKeyAsync(
            $"/api/accounts/{account.Id}/transactions",
            new { category = "Salary", amount = 50m, type = "Credit" },
            key);

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Equal(150m, await BalanceOfAsync(account.Id));
    }

    // I5 - BR-33. Keys are scoped to (UserId, Endpoint, Key), so one user's
    // key can never collide with another's.
    [Fact]
    public async Task The_same_key_used_by_two_users_succeeds_independently()
    {
        var mine = await FundedAccountAsync("I5 mine", 500m);
        var sharedKey = TestClient.NewIdempotencyKey();

        var (userB, _) = await TestClient.AuthedClientAsync(Factory);
        using var b = userB;

        var theirAccount = await CreateAccountAsync("I5 theirs", userB);

        var first = await PostTransactionAsync(mine.Id, "Credit", 100m, sharedKey);
        var second = await userB.PostWithKeyAsync(
            $"/api/accounts/{theirAccount.Id}/transactions",
            new { type = "Credit", amount = 100m, category = "Salary" },
            sharedKey);

        // BOTH created. Neither replayed the other.
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Created, second.StatusCode);
        Assert.Equal(600m, await BalanceOfAsync(mine.Id));
    }

    // BR-32: the two endpoints are separate scopes, so the same key works on
    // each without colliding.
    [Fact]
    public async Task The_same_key_on_two_different_endpoints_does_not_collide()
    {
        var source = await FundedAccountAsync("I5 endpoints", 500m);
        var destination = await CreateAccountAsync("I5 endpoints destination");
        var key = TestClient.NewIdempotencyKey();

        var transaction = await PostTransactionAsync(source.Id, "Credit", 100m, key);
        var transfer = await TransferAsync(source.Id, destination.Id, 50m, key);

        Assert.Equal(HttpStatusCode.Created, transaction.StatusCode);
        Assert.Equal(HttpStatusCode.Created, transfer.StatusCode);
    }

    /// <summary>
    /// I6 - BR-34, and the reason the key INSERT lives INSIDE the financial
    /// transaction rather than before it. A request that fails does not burn
    /// the key: the failure rolls the key row back along with everything else,
    /// so the client can fix the problem and retry with the same key.
    ///
    /// If the insert were committed separately first, this retry would replay a
    /// 409 forever and the user could never complete the transfer.
    /// </summary>
    [Fact]
    public async Task A_failed_request_does_not_burn_the_key()
    {
        var account = await CreateAccountAsync("I6");
        await PostTransactionAsync(account.Id, "Credit", 50m);
        var key = TestClient.NewIdempotencyKey();

        // Overdraft: 409.
        var failed = await PostTransactionAsync(account.Id, "Debit", 500m, key);

        Assert.Equal(HttpStatusCode.Conflict, failed.StatusCode);
        Assert.Equal("INSUFFICIENT_FUNDS", await CodeOfAsync(failed));
        Assert.Equal(0, await CountKeysAsync(key));

        // Fund the account, then retry the SAME request with the SAME key.
        await PostTransactionAsync(account.Id, "Credit", 1000m);

        var retried = await PostTransactionAsync(account.Id, "Debit", 500m, key);

        Assert.Equal(HttpStatusCode.Created, retried.StatusCode);
        Assert.False(retried.Headers.Contains(IdempotencyHeader.ReplayName));
        Assert.Equal(550m, await BalanceOfAsync(account.Id));
    }

    // The key row is written in the same transaction as the money, so a
    // successful request leaves exactly one.
    [Fact]
    public async Task A_successful_request_stores_exactly_one_key_with_its_response()
    {
        var account = await FundedAccountAsync("Key row", 100m);
        var key = TestClient.NewIdempotencyKey();

        await PostTransactionAsync(account.Id, "Credit", 25m, key);

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var stored = await db.IdempotencyKeys
            .AsNoTracking()
            .SingleAsync(k => k.UserId == User.Id && k.Key == key);

        Assert.Equal(201, stored.ResponseStatusCode);
        Assert.Equal(64, stored.RequestHash.Length);
        Assert.Contains("\"amount\"", stored.ResponseBody);

        // The placeholder from Claim is never visible after commit.
        Assert.NotEqual("null", stored.ResponseBody);
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
        var response = await PostTransactionAsync(account.Id, "Credit", amount);
        response.EnsureSuccessStatusCode();

        return account;
    }

    private Task<HttpResponseMessage> PostTransactionAsync(
        Guid accountId,
        string type,
        decimal amount,
        string? key = null) =>
        AuthedClient.PostWithKeyAsync(
            $"/api/accounts/{accountId}/transactions",
            new { type, amount, category = "Salary" },
            key);

    private Task<HttpResponseMessage> TransferAsync(
        Guid sourceAccountId,
        Guid destinationAccountId,
        decimal amount,
        string? key = null) =>
        AuthedClient.PostWithKeyAsync(
            "/api/transfers",
            new { sourceAccountId, destinationAccountId, amount },
            key);

    private async Task<decimal> BalanceOfAsync(Guid accountId)
    {
        var balance = await AuthedClient.GetFromJsonAsync<AccountBalanceResponse>(
            $"/api/accounts/{accountId}/balance", TestClient.JsonOptions);

        return balance!.Balance;
    }

    private async Task<int> CountTransfersAsync()
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        return await db.Transfers.CountAsync(t => t.UserId == User.Id);
    }

    private async Task<int> CountKeysAsync(string key)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        return await db.IdempotencyKeys.CountAsync(k => k.UserId == User.Id && k.Key == key);
    }

    private static async Task<string?> CodeOfAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync())
            .RootElement.GetProperty("code").GetString();
}
