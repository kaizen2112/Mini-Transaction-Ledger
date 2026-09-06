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
/// Tests H1-H8 and O3 from docs/07-testing-strategy.md §4.2/§4.4.
/// </summary>
public sealed class TransactionHistoryTests : IntegrationTestBase
{
    private AccountResponse _account = null!;

    public TransactionHistoryTests(DatabaseFixture database) : base(database)
    {
    }

    protected override async Task OnInitializedAsync()
    {
        _account = await CreateAccountAsync("History");
        await PostAsync("Credit", 100_000m, "Salary");
    }

    // H1
    [Fact]
    public async Task Twenty_five_transactions_at_page_size_ten_gives_three_pages()
    {
        var account = await CreateAccountAsync("H1");
        await SeedAsync(account.Id, 25);

        var page = await GetPageAsync(account.Id, "?page=1&pageSize=10");

        Assert.Equal(25, page.TotalItems);
        Assert.Equal(3, page.TotalPages);
        Assert.Equal(10, page.Items.Count);
        Assert.Equal(1, page.Page);
        Assert.Equal(10, page.PageSize);

        var last = await GetPageAsync(account.Id, "?page=3&pageSize=10");
        Assert.Equal(5, last.Items.Count);
    }

    // H2
    [Fact]
    public async Task Union_of_all_pages_equals_the_full_set_with_no_gaps_or_duplicates()
    {
        var account = await CreateAccountAsync("H2");
        await SeedAsync(account.Id, 25);

        var collected = new List<Guid>();
        for (var page = 1; page <= 3; page++)
        {
            var result = await GetPageAsync(account.Id, $"?page={page}&pageSize=10");
            collected.AddRange(result.Items.Select(i => i.Id));
        }

        var everything = await GetPageAsync(account.Id, "?page=1&pageSize=100");

        Assert.Equal(25, collected.Count);
        Assert.Equal(25, collected.Distinct().Count());
        Assert.Equal(
            everything.Items.Select(i => i.Id).ToList(),
            collected);
    }

    // H2 continued: a page past the end is a valid state, not an error.
    [Fact]
    public async Task A_page_past_the_last_returns_200_with_an_empty_array()
    {
        var account = await CreateAccountAsync("H2 overflow");
        await SeedAsync(account.Id, 3);

        var page = await GetPageAsync(account.Id, "?page=50&pageSize=10");

        Assert.Empty(page.Items);
        Assert.Equal(3, page.TotalItems);
        Assert.Equal(1, page.TotalPages);
        Assert.Equal(50, page.Page);
    }

    // H3 - BR-41. Identical OccurredAt must still paginate deterministically,
    // which is what the Id tiebreak exists for.
    [Fact]
    public async Task Identical_timestamps_still_paginate_deterministically()
    {
        var account = await CreateAccountAsync("H3");

        // Written with raw SQL because OccurredAt is server-set by the domain
        // factory (BR-26) and cannot be forced through the API.
        var sharedInstant = new DateTime(2026, 3, 1, 12, 0, 0, DateTimeKind.Utc);
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            for (var i = 0; i < 6; i++)
            {
                await db.Database.ExecuteSqlInterpolatedAsync($"""
                    INSERT INTO "Transactions"
                        ("Id", "AccountId", "Type", "Amount", "Category", "OccurredAt")
                    VALUES
                        ({Guid.CreateVersion7()}, {account.Id}, 0, {10m}, 0, {sharedInstant})
                    """);
            }
        }

        var first = await GetPageAsync(account.Id, "?page=1&pageSize=3");
        var second = await GetPageAsync(account.Id, "?page=2&pageSize=3");

        // No row appears on both pages, and none is missing from both.
        Assert.Empty(first.Items.Select(i => i.Id).Intersect(second.Items.Select(i => i.Id)));
        Assert.Equal(6, first.Items.Count + second.Items.Count);

        // And the order is stable across repeated identical requests.
        var firstAgain = await GetPageAsync(account.Id, "?page=1&pageSize=3");
        Assert.Equal(
            first.Items.Select(i => i.Id).ToList(),
            firstAgain.Items.Select(i => i.Id).ToList());
    }

    // H4 - BR-39: rejected, not clamped.
    [Theory]
    [InlineData("?pageSize=0")]
    [InlineData("?pageSize=101")]
    [InlineData("?page=0")]
    [InlineData("?page=-1")]
    public async Task Out_of_range_pagination_returns_400(string queryString)
    {
        var response = await AuthedClient.GetAsync(
            $"/api/accounts/{_account.Id}/transactions{queryString}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("PAGINATION_INVALID", await CodeOfAsync(response));
    }

    // H5 - BR-42, every filter.
    [Fact]
    public async Task Each_filter_returns_exactly_the_expected_subset()
    {
        var account = await CreateAccountAsync("H5");
        await PostToAsync(account.Id, "Credit", 5_000m, "Salary", "September salary");
        await PostToAsync(account.Id, "Debit", 120m, "Food", "Lunch with colleagues");
        await PostToAsync(account.Id, "Debit", 640m, "Bills", "Electricity");
        await PostToAsync(account.Id, "Debit", 45m, "Transport", "Bus fare");

        var credits = await GetPageAsync(account.Id, "?type=Credit");
        Assert.Single(credits.Items);
        Assert.Equal("September salary", credits.Items[0].Description);

        var food = await GetPageAsync(account.Id, "?category=Food");
        Assert.Single(food.Items);
        Assert.Equal(120m, food.Items[0].Amount);

        var midRange = await GetPageAsync(account.Id, "?minAmount=100&maxAmount=700");
        Assert.Equal(2, midRange.Items.Count);
        Assert.All(midRange.Items, i => Assert.InRange(i.Amount, 100m, 700m));

        var search = await GetPageAsync(account.Id, "?search=lunch");
        Assert.Single(search.Items);
        Assert.Equal("Lunch with colleagues", search.Items[0].Description);

        // from/to are inclusive bounds around "now".
        var windowed = await GetPageAsync(
            account.Id,
            $"?from={Uri.EscapeDataString(DateTime.UtcNow.AddMinutes(-5).ToString("O"))}");
        Assert.Equal(4, windowed.Items.Count);

        var past = await GetPageAsync(
            account.Id,
            $"?to={Uri.EscapeDataString(DateTime.UtcNow.AddYears(-1).ToString("O"))}");
        Assert.Empty(past.Items);

        // Filters compose.
        var combined = await GetPageAsync(account.Id, "?type=Debit&minAmount=100");
        Assert.Equal(2, combined.Items.Count);
    }

    // H6
    [Fact]
    public async Task From_later_than_to_returns_400_filter_range_invalid()
    {
        var from = Uri.EscapeDataString(new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc).ToString("O"));
        var to = Uri.EscapeDataString(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc).ToString("O"));

        var response = await AuthedClient.GetAsync(
            $"/api/accounts/{_account.Id}/transactions?from={from}&to={to}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("FILTER_RANGE_INVALID", await CodeOfAsync(response));
    }

    [Fact]
    public async Task MinAmount_greater_than_maxAmount_returns_400_filter_range_invalid()
    {
        var response = await AuthedClient.GetAsync(
            $"/api/accounts/{_account.Id}/transactions?minAmount=500&maxAmount=100");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("FILTER_RANGE_INVALID", await CodeOfAsync(response));
    }

    // H7
    [Fact]
    public async Task Search_is_case_insensitive()
    {
        var account = await CreateAccountAsync("H7");
        await PostToAsync(account.Id, "Credit", 10m, "Salary", "Quarterly BONUS payment");

        foreach (var term in new[] { "bonus", "BONUS", "BoNuS" })
        {
            var result = await GetPageAsync(account.Id, $"?search={term}");
            Assert.Single(result.Items);
        }
    }

    // H8 - the test that makes BR-40 real. Asserts on the SQL, because an
    // in-memory Skip would produce an identical HTTP response.
    [Fact]
    public async Task Generated_sql_contains_limit_and_offset()
    {
        var account = await CreateAccountAsync("H8");
        await SeedAsync(account.Id, 12);

        Factory.SqlCapture.Clear();
        var page = await GetPageAsync(account.Id, "?page=2&pageSize=5");
        Assert.Equal(5, page.Items.Count);

        var commands = Factory.SqlCapture.Commands;

        Assert.Contains(commands, c => c.Contains("LIMIT", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(commands, c => c.Contains("OFFSET", StringComparison.OrdinalIgnoreCase));

        // The count is aggregated in SQL too, not by loading rows and counting.
        Assert.Contains(commands, c => c.Contains("COUNT(*)", StringComparison.OrdinalIgnoreCase));

        // And the ordering the index was built for (BR-41).
        Assert.Contains(commands, c =>
            c.Contains("ORDER BY", StringComparison.OrdinalIgnoreCase)
            && c.Contains("DESC", StringComparison.OrdinalIgnoreCase));
    }

    // O3 - BR-08.
    [Fact]
    public async Task User_B_listing_user_As_transactions_returns_404()
    {
        var (userB, _) = await TestClient.AuthedClientAsync(Factory);
        using var b = userB;

        var response = await userB.GetAsync($"/api/accounts/{_account.Id}/transactions");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("NOT_FOUND", await CodeOfAsync(response));
    }

    [Fact]
    public async Task An_owned_account_with_no_transactions_returns_200_not_404()
    {
        var empty = await CreateAccountAsync("Empty");

        var page = await GetPageAsync(empty.Id, string.Empty);

        Assert.Empty(page.Items);
        Assert.Equal(0, page.TotalItems);
        Assert.Equal(0, page.TotalPages);
    }

    // GET /api/transactions/{id}
    [Fact]
    public async Task A_transaction_can_be_fetched_by_its_own_id()
    {
        var created = await PostToAsync(_account.Id, "Debit", 75m, "Food", "Single lookup");

        var fetched = await AuthedClient.GetFromJsonAsync<TransactionResponse>(
            $"/api/transactions/{created.Id}", TestClient.JsonOptions);

        Assert.Equal(created.Id, fetched!.Id);
        Assert.Equal(75m, fetched.Amount);
        Assert.Equal("Single lookup", fetched.Description);
        Assert.False(fetched.IsReversed);
    }

    [Fact]
    public async Task User_B_fetching_user_As_transaction_by_id_returns_404()
    {
        var created = await PostToAsync(_account.Id, "Debit", 30m, "Food", "Private");

        var (userB, _) = await TestClient.AuthedClientAsync(Factory);
        using var b = userB;

        var response = await userB.GetAsync($"/api/transactions/{created.Id}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task A_nonexistent_transaction_id_returns_404()
    {
        var response = await AuthedClient.GetAsync($"/api/transactions/{Guid.CreateVersion7()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task History_without_a_token_returns_401()
    {
        var response = await Client.GetAsync($"/api/accounts/{_account.Id}/transactions");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Default_page_and_page_size_are_applied_when_omitted()
    {
        var account = await CreateAccountAsync("Defaults");
        await SeedAsync(account.Id, 3);

        var page = await GetPageAsync(account.Id, string.Empty);

        Assert.Equal(1, page.Page);
        Assert.Equal(20, page.PageSize);
    }

    // ---- helpers ----

    private async Task SeedAsync(Guid accountId, int count)
    {
        await PostToAsync(accountId, "Credit", 50_000m, "Salary", "seed float");

        for (var i = 0; i < count - 1; i++)
        {
            await PostToAsync(accountId, "Debit", 10m + i, "Food", $"seed {i:D2}");
        }
    }

    private async Task<AccountResponse> CreateAccountAsync(string name)
    {
        var response = await AuthedClient.PostAsJsonAsync("/api/accounts", new { name, type = "Cash" });
        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<AccountResponse>(TestClient.JsonOptions))!;
    }

    private Task<TransactionResponse> PostAsync(string type, decimal amount, string category) =>
        PostToAsync(_account.Id, type, amount, category, null);

    private async Task<TransactionResponse> PostToAsync(
        Guid accountId,
        string type,
        decimal amount,
        string category,
        string? description)
    {
        var response = await AuthedClient.PostWithKeyAsync(
            $"/api/accounts/{accountId}/transactions",
            new { type, amount, category, description });

        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<TransactionResponse>(TestClient.JsonOptions))!;
    }

    private async Task<PagedResponse<TransactionResponse>> GetPageAsync(Guid accountId, string queryString)
    {
        var response = await AuthedClient.GetAsync(
            $"/api/accounts/{accountId}/transactions{queryString}");

        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<PagedResponse<TransactionResponse>>(
            TestClient.JsonOptions))!;
    }

    private static async Task<string?> CodeOfAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync())
            .RootElement.GetProperty("code").GetString();
}
