using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using TransactionLedger.DTOs;
using TransactionLedger.Tests.Infrastructure;

namespace TransactionLedger.Tests.Integration;

/// <summary>
/// Tests O1, O8 and L1 from docs/07-testing-strategy.md §4.2/§4.3, plus
/// account name uniqueness (BR-14).
/// </summary>
public sealed class AccountTests : IntegrationTestBase
{
    public AccountTests(DatabaseFixture database) : base(database)
    {
    }

    // L1
    [Fact]
    public async Task New_account_balance_is_exactly_zero()
    {
        var response = await AuthedClient.PostAsJsonAsync("/api/accounts", new
        {
            name = "Cash",
            type = "Cash"
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var account = (await response.Content.ReadFromJsonAsync<AccountResponse>(TestClient.JsonOptions))!;
        Assert.Equal(decimal.Zero, account.Balance);
    }

    [Fact]
    public async Task Create_returns_201_with_a_resolvable_location_header()
    {
        var response = await AuthedClient.PostAsJsonAsync("/api/accounts", new
        {
            name = "Savings",
            type = "Savings"
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var location = response.Headers.Location;
        Assert.NotNull(location);

        var followed = await AuthedClient.GetAsync(location);
        Assert.Equal(HttpStatusCode.OK, followed.StatusCode);
    }

    // Contract §1.3: enums serialise as names, not integers, in both directions.
    [Fact]
    public async Task Account_type_round_trips_as_a_name_not_an_integer()
    {
        var response = await AuthedClient.PostAsJsonAsync("/api/accounts", new
        {
            name = "Business current",
            type = "Business"
        });

        var body = await response.Content.ReadAsStringAsync();

        Assert.Contains("\"type\":\"Business\"", body);
        Assert.DoesNotContain("\"type\":2", body);
    }

    // BR-14
    [Fact]
    public async Task Duplicate_account_name_returns_409_account_name_taken()
    {
        await CreateAccountAsync(AuthedClient, "Duplicate target");

        var response = await AuthedClient.PostAsJsonAsync("/api/accounts", new
        {
            name = "Duplicate target",
            type = "Savings"
        });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal("ACCOUNT_NAME_TAKEN", json.GetProperty("code").GetString());
    }

    // BR-14: unique case-insensitively, after trimming.
    [Theory]
    [InlineData("WALLET")]
    [InlineData("wallet")]
    [InlineData("  Wallet  ")]
    public async Task Account_name_uniqueness_ignores_case_and_surrounding_whitespace(string variant)
    {
        var (client, _) = await TestClient.AuthedClientAsync(Factory);
        using var owned = client;

        await CreateAccountAsync(owned, "Wallet");

        var response = await owned.PostAsJsonAsync("/api/accounts", new
        {
            name = variant,
            type = "Wallet"
        });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    // BR-14 is per USER, not global: two users may both have "Cash".
    [Fact]
    public async Task The_same_account_name_is_allowed_for_a_different_user()
    {
        await CreateAccountAsync(AuthedClient, "Shared name");

        var (otherClient, _) = await TestClient.AuthedClientAsync(Factory);
        using var other = otherClient;

        var response = await other.PostAsJsonAsync("/api/accounts", new
        {
            name = "Shared name",
            type = "Cash"
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    // O1
    [Fact]
    public async Task User_B_getting_user_As_account_returns_404()
    {
        var account = await CreateAccountAsync(AuthedClient, "User A private");

        var (userB, _) = await TestClient.AuthedClientAsync(Factory);
        using var b = userB;

        var response = await b.GetAsync($"/api/accounts/{account.Id}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal("NOT_FOUND", json.GetProperty("code").GetString());
    }

    // O1, applied to the balance endpoint as well.
    [Fact]
    public async Task User_B_getting_user_As_account_balance_returns_404()
    {
        var account = await CreateAccountAsync(AuthedClient, "User A balance");

        var (userB, _) = await TestClient.AuthedClientAsync(Factory);
        using var b = userB;

        var response = await b.GetAsync($"/api/accounts/{account.Id}/balance");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // O8 - the subtle one. If the two differ in ANY way, the API leaks which
    // account IDs are real.
    [Fact]
    public async Task Foreign_account_and_nonexistent_account_return_identical_responses()
    {
        var foreign = await CreateAccountAsync(AuthedClient, "User A hidden");
        var nonexistent = Guid.CreateVersion7();

        var (userB, _) = await TestClient.AuthedClientAsync(Factory);
        using var b = userB;

        var foreignResponse = await b.GetAsync($"/api/accounts/{foreign.Id}");
        var missingResponse = await b.GetAsync($"/api/accounts/{nonexistent}");

        Assert.Equal(foreignResponse.StatusCode, missingResponse.StatusCode);
        Assert.Equal(
            foreignResponse.Content.Headers.ContentType?.ToString(),
            missingResponse.Content.Headers.ContentType?.ToString());

        // traceId is per-request by design (contract §1.1), so it is excluded.
        // Every other member must match exactly - including detail, which is
        // why it never echoes the requested ID.
        Assert.Equal(
            await WithoutTraceIdAsync(foreignResponse),
            await WithoutTraceIdAsync(missingResponse));
    }

    // BR-07: the list is filtered by owner in the query predicate.
    [Fact]
    public async Task List_returns_only_the_callers_accounts_oldest_first_with_a_total()
    {
        var first = await CreateAccountAsync(AuthedClient, "Listing one", "Cash");
        var second = await CreateAccountAsync(AuthedClient, "Listing two", "Savings");

        var (userB, _) = await TestClient.AuthedClientAsync(Factory);
        using var b = userB;
        await CreateAccountAsync(b, "Not yours", "Wallet");

        var listing = (await AuthedClient.GetFromJsonAsync<AccountListResponse>("/api/accounts", TestClient.JsonOptions))!;

        var ids = listing.Accounts.Select(a => a.Id).ToList();
        Assert.Equal(new[] { first.Id, second.Id }, ids);
        Assert.DoesNotContain(listing.Accounts, a => a.Name == "Not yours");

        // Every account is new, so the total is still exactly zero (BR-15).
        Assert.Equal(decimal.Zero, listing.TotalBalance);
    }

    [Fact]
    public async Task Balance_endpoint_returns_the_account_id_balance_and_timestamp()
    {
        var account = await CreateAccountAsync(AuthedClient, "Balance probe");

        var balance = (await AuthedClient.GetFromJsonAsync<AccountBalanceResponse>(
            $"/api/accounts/{account.Id}/balance", TestClient.JsonOptions))!;

        Assert.Equal(account.Id, balance.AccountId);
        Assert.Equal(decimal.Zero, balance.Balance);
        Assert.InRange(balance.AsOf, DateTime.UtcNow.AddMinutes(-1), DateTime.UtcNow.AddMinutes(1));
    }

    [Fact]
    public async Task Account_endpoints_without_a_token_return_401()
    {
        var create = await Client.PostAsJsonAsync("/api/accounts", new { name = "Nope", type = "Cash" });
        var list = await Client.GetAsync("/api/accounts");

        Assert.Equal(HttpStatusCode.Unauthorized, create.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, list.StatusCode);
    }

    [Theory]
    [InlineData("", "Cash")]
    [InlineData("Valid name", "NotAType")]
    public async Task Invalid_create_payloads_return_400_validation_failed(string name, string type)
    {
        var response = await AuthedClient.PostAsJsonAsync("/api/accounts", new { name, type });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal("VALIDATION_FAILED", json.GetProperty("code").GetString());
    }

    // Contract Format header: money is a JSON number with TWO decimals.
    // The empty-list case is the one that regresses: EF translates a sum over
    // no rows to COALESCE(SUM(...), 0.0), which has scale 1.
    [Fact]
    public async Task Money_is_always_rendered_with_two_decimals()
    {
        var (client, _) = await TestClient.AuthedClientAsync(Factory);
        using var owned = client;

        var empty = await owned.GetStringAsync("/api/accounts");
        Assert.Contains("\"totalBalance\":0.00", empty);

        await CreateAccountAsync(owned, "Two decimals");

        var populated = await owned.GetStringAsync("/api/accounts");
        Assert.Contains("\"balance\":0.00", populated);
        Assert.Contains("\"totalBalance\":0.00", populated);
    }

    private static async Task<AccountResponse> CreateAccountAsync(
        HttpClient client,
        string name,
        string type = "Cash")
    {
        var response = await client.PostAsJsonAsync("/api/accounts", new { name, type });
        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<AccountResponse>(TestClient.JsonOptions))!;
    }

    private static async Task<string> WithoutTraceIdAsync(HttpResponseMessage response)
    {
        var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

        var members = json.EnumerateObject()
            .Where(member => member.Name != "traceId")
            .OrderBy(member => member.Name, StringComparer.Ordinal)
            .Select(member => $"{member.Name}={member.Value}");

        return string.Join("&", members);
    }
}
