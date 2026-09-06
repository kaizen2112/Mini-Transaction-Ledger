using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using TransactionLedger.DTOs;
using TransactionLedger.Tests.Infrastructure;

namespace TransactionLedger.Tests.Integration;

/// <summary>
/// O7 from docs/07-testing-strategy.md §4.2, plus the BR-43 aggregation rules.
///
/// O6 and K5 belong to the CSV export, which is deliberately out of scope.
/// </summary>
public sealed class ReportTests : IntegrationTestBase
{
    // O7 - BR-43. The report boundary is the same ownership boundary as
    // everything else: user B's money must not appear in user A's totals, not
    // even aggregated into a figure where it would be invisible.
    [Fact]
    public async Task User_As_reports_contain_no_trace_of_user_Bs_amounts()
    {
        var mine = await CreateAccountAsync("O7 mine");
        await PostAsync(mine.Id, "Credit", 100m, "Salary");

        var (userB, _) = await TestClient.AuthedClientAsync(Factory);
        using var b = userB;

        var theirs = await CreateAccountAsync("O7 theirs", userB);
        await PostAsync(theirs.Id, "Credit", 9_999m, "Salary", client: userB);
        await PostAsync(theirs.Id, "Debit", 7_777m, "Food", client: userB);

        var summary = await SummaryAsync();

        // Exact figures, not "less than": an off-by-one leak would still pass
        // a range assertion.
        Assert.Equal(100m, summary.TotalBalance);
        Assert.Equal(100m, summary.TotalCredits);
        Assert.Equal(decimal.Zero, summary.TotalDebits);
        Assert.Equal(1, summary.TransactionCount);
        Assert.Equal(1, summary.AccountCount);

        var categories = await CategoriesAsync();
        Assert.Empty(categories.Categories);
        Assert.Equal(decimal.Zero, categories.TotalAmount);

        var monthly = await MonthlyAsync();
        Assert.DoesNotContain(monthly.Months, m => m.Debits == 7_777m || m.Credits == 9_999m);
    }

    // BR-43: summary respects from/to for activity, but NOT for balance.
    [Fact]
    public async Task Summary_totals_are_correct_and_balance_ignores_the_date_filter()
    {
        var account = await CreateAccountAsync("Summary");
        await PostAsync(account.Id, "Credit", 1_000m, "Salary");
        await PostAsync(account.Id, "Debit", 250m, "Food");
        await PostAsync(account.Id, "Debit", 150m, "Bills");

        var summary = await SummaryAsync();

        Assert.Equal(600m, summary.TotalBalance);
        Assert.Equal(1_000m, summary.TotalCredits);
        Assert.Equal(400m, summary.TotalDebits);
        Assert.Equal(600m, summary.NetChange);
        Assert.Equal(3, summary.TransactionCount);

        // A window containing nothing: activity is zero, but the balance is
        // still what the account holds right now (contract §7).
        var past = await SummaryAsync("?from=2000-01-01T00:00:00Z&to=2000-12-31T23:59:59Z");

        Assert.Equal(decimal.Zero, past.TotalCredits);
        Assert.Equal(decimal.Zero, past.TotalDebits);
        Assert.Equal(0, past.TransactionCount);
        Assert.Equal(600m, past.TotalBalance);
    }

    [Fact]
    public async Task Summary_for_a_user_with_no_data_is_zeroed_not_an_error()
    {
        var summary = await SummaryAsync();

        Assert.Equal(decimal.Zero, summary.TotalBalance);
        Assert.Equal(decimal.Zero, summary.NetChange);
        Assert.Equal(0, summary.TransactionCount);
        Assert.Equal(0, summary.AccountCount);
    }

    // BR-43: debits only. A credit is income, not spending.
    [Fact]
    public async Task Category_report_groups_debits_and_ignores_credits()
    {
        var account = await CreateAccountAsync("Categories");
        await PostAsync(account.Id, "Credit", 1_000m, "Salary");
        await PostAsync(account.Id, "Debit", 30m, "Food");
        await PostAsync(account.Id, "Debit", 20m, "Food");
        await PostAsync(account.Id, "Debit", 200m, "Bills");

        var report = await CategoriesAsync();

        Assert.Equal(250m, report.TotalAmount);

        // Ordered by amount, biggest first — that is what a report is read for.
        Assert.Equal("Bills", report.Categories[0].Category.ToString());
        Assert.Equal(200m, report.Categories[0].TotalAmount);
        Assert.Equal(1, report.Categories[0].TransactionCount);

        Assert.Equal("Food", report.Categories[1].Category.ToString());
        Assert.Equal(50m, report.Categories[1].TotalAmount);
        Assert.Equal(2, report.Categories[1].TransactionCount);

        // Salary was a credit, so it is not spending and not in the report.
        Assert.DoesNotContain(report.Categories, c => c.Category.ToString() == "Salary");
    }

    /// <summary>
    /// BR-43, the exclusion that matters most: a transfer between your own
    /// accounts is not spending. Without this, everyone who moves money between
    /// their own pockets sees an inflated spending figure.
    /// </summary>
    [Fact]
    public async Task Category_report_excludes_transfer_legs()
    {
        var source = await CreateAccountAsync("Transfer source");
        var destination = await CreateAccountAsync("Transfer destination");
        await PostAsync(source.Id, "Credit", 1_000m, "Salary");
        await PostAsync(source.Id, "Debit", 40m, "Food");

        await TransferAsync(source.Id, destination.Id, 500m);

        var report = await CategoriesAsync();

        // The 500 moved, but it was not spent.
        Assert.Equal(40m, report.TotalAmount);
        Assert.DoesNotContain(report.Categories, c => c.Category.ToString() == "Transfer");

        // ...and the summary DOES include it, because the ledger really did
        // debit one account and credit the other (BR-43).
        var summary = await SummaryAsync();
        Assert.Equal(540m, summary.TotalDebits);
        Assert.Equal(1_500m, summary.TotalCredits);

        // Money did not leave the user: balance is unchanged by the transfer.
        Assert.Equal(960m, summary.TotalBalance);
    }

    /// <summary>
    /// BR-43: a refunded purchase did not happen. BOTH halves disappear — the
    /// original (which would overstate spending) and the reversal itself.
    /// </summary>
    [Fact]
    public async Task Category_report_excludes_reversed_transactions_and_their_reversals()
    {
        var account = await CreateAccountAsync("Reversal categories");
        await PostAsync(account.Id, "Credit", 1_000m, "Salary");

        var kept = await PostAsync(account.Id, "Debit", 60m, "Food");
        var refunded = await PostAsync(account.Id, "Debit", 500m, "Shopping");

        var before = await CategoriesAsync();
        Assert.Equal(560m, before.TotalAmount);

        await ReverseAsync(refunded.Id);

        var after = await CategoriesAsync();

        // Only the genuine purchase survives.
        Assert.Equal(60m, after.TotalAmount);
        Assert.Equal("Food", Assert.Single(after.Categories).Category.ToString());
        Assert.DoesNotContain(after.Categories, c => c.Category.ToString() == "Shopping");
        Assert.DoesNotContain(after.Categories, c => c.Category.ToString() == "Reversal");

        Assert.NotEqual(kept.Id, refunded.Id);
    }

    // BR-08: narrowing to a foreign account is a 404, not an empty report.
    [Fact]
    public async Task Category_report_for_a_foreign_account_returns_404()
    {
        var (userB, _) = await TestClient.AuthedClientAsync(Factory);
        using var b = userB;

        var theirs = await CreateAccountAsync("Foreign", userB);

        var foreign = await AuthedClient.GetAsync($"/api/reports/categories?accountId={theirs.Id}");
        var ghost = await AuthedClient.GetAsync($"/api/reports/categories?accountId={Guid.CreateVersion7()}");

        Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, ghost.StatusCode);
        Assert.Equal("NOT_FOUND", await CodeOfAsync(foreign));
    }

    [Fact]
    public async Task Category_report_can_be_narrowed_to_one_owned_account()
    {
        var first = await CreateAccountAsync("Narrow first");
        var second = await CreateAccountAsync("Narrow second");
        await PostAsync(first.Id, "Credit", 500m, "Salary");
        await PostAsync(second.Id, "Credit", 500m, "Salary");
        await PostAsync(first.Id, "Debit", 70m, "Food");
        await PostAsync(second.Id, "Debit", 90m, "Food");

        var all = await CategoriesAsync();
        var narrowed = await CategoriesAsync($"?accountId={first.Id}");

        Assert.Equal(160m, all.TotalAmount);
        Assert.Equal(70m, narrowed.TotalAmount);
    }

    [Fact]
    public async Task Monthly_report_groups_the_current_month()
    {
        var account = await CreateAccountAsync("Monthly");
        await PostAsync(account.Id, "Credit", 900m, "Salary");
        await PostAsync(account.Id, "Debit", 300m, "Food");

        var report = await MonthlyAsync();
        var now = DateTime.UtcNow;

        var current = Assert.Single(
            report.Months, m => m.Year == now.Year && m.Month == now.Month);

        Assert.Equal(900m, current.Credits);
        Assert.Equal(300m, current.Debits);
        Assert.Equal(600m, current.Net);
    }

    [Fact]
    public async Task Monthly_report_for_a_year_with_no_activity_is_empty()
    {
        var account = await CreateAccountAsync("Monthly empty");
        await PostAsync(account.Id, "Credit", 100m, "Salary");

        var report = await MonthlyAsync("?year=2001");

        Assert.Empty(report.Months);
    }

    [Theory]
    [InlineData("/api/reports/summary?from=2026-12-01T00:00:00Z&to=2026-01-01T00:00:00Z")]
    [InlineData("/api/reports/categories?from=2026-12-01T00:00:00Z&to=2026-01-01T00:00:00Z")]
    [InlineData("/api/reports/monthly?year=0")]
    public async Task Inverted_or_nonsense_ranges_return_400(string route)
    {
        var response = await AuthedClient.GetAsync(route);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("FILTER_RANGE_INVALID", await CodeOfAsync(response));
    }

    [Theory]
    [InlineData("/api/reports/summary")]
    [InlineData("/api/reports/categories")]
    [InlineData("/api/reports/monthly")]
    public async Task Reports_without_a_token_return_401(string route)
    {
        var response = await Client.GetAsync(route);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>
    /// BR-40/BR-43: the aggregation happens in PostgreSQL. Same reasoning as
    /// H8 — an in-memory implementation returns byte-identical JSON while
    /// dragging every row across the wire, so only the generated SQL can tell
    /// the two apart.
    /// </summary>
    [Fact]
    public async Task Aggregation_runs_in_sql_not_in_memory()
    {
        var account = await CreateAccountAsync("SQL proof");
        await PostAsync(account.Id, "Credit", 500m, "Salary");
        await PostAsync(account.Id, "Debit", 20m, "Food");

        Factory.SqlCapture.Clear();
        await SummaryAsync();
        AssertAggregatedInSql("summary", "SUM(");

        Factory.SqlCapture.Clear();
        await CategoriesAsync();
        AssertAggregatedInSql("categories", "GROUP BY");

        Factory.SqlCapture.Clear();
        await MonthlyAsync();
        AssertAggregatedInSql("monthly", "GROUP BY");
    }

    private void AssertAggregatedInSql(string report, string expected)
    {
        var commands = Factory.SqlCapture.Commands;

        Assert.True(
            commands.Any(c => c.Contains(expected, StringComparison.OrdinalIgnoreCase)),
            $"The {report} report issued no SQL containing '{expected}'. Commands:\n"
            + string.Join("\n---\n", commands));
    }

    // ---- helpers ----------------------------------------------------------

    private async Task<AccountResponse> CreateAccountAsync(string name, HttpClient? client = null)
    {
        var response = await (client ?? AuthedClient)
            .PostAsJsonAsync("/api/accounts", new { name, type = "Cash" });
        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<AccountResponse>(TestClient.JsonOptions))!;
    }

    private async Task<TransactionResponse> PostAsync(
        Guid accountId,
        string type,
        decimal amount,
        string category,
        HttpClient? client = null)
    {
        var response = await (client ?? AuthedClient).PostWithKeyAsync(
            $"/api/accounts/{accountId}/transactions",
            new { type, amount, category });

        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<TransactionResponse>(TestClient.JsonOptions))!;
    }

    private async Task TransferAsync(Guid sourceAccountId, Guid destinationAccountId, decimal amount)
    {
        var response = await AuthedClient.PostWithKeyAsync("/api/transfers", new
        {
            sourceAccountId,
            destinationAccountId,
            amount
        });

        response.EnsureSuccessStatusCode();
    }

    private async Task ReverseAsync(Guid transactionId)
    {
        var response = await AuthedClient.PostAsJsonAsync(
            $"/api/transactions/{transactionId}/reverse", new { description = (string?)null });

        response.EnsureSuccessStatusCode();
    }

    private async Task<SummaryReportResponse> SummaryAsync(string queryString = "") =>
        (await AuthedClient.GetFromJsonAsync<SummaryReportResponse>(
            $"/api/reports/summary{queryString}", TestClient.JsonOptions))!;

    private async Task<CategoryReportResponse> CategoriesAsync(string queryString = "") =>
        (await AuthedClient.GetFromJsonAsync<CategoryReportResponse>(
            $"/api/reports/categories{queryString}", TestClient.JsonOptions))!;

    private async Task<MonthlyReportResponse> MonthlyAsync(string queryString = "") =>
        (await AuthedClient.GetFromJsonAsync<MonthlyReportResponse>(
            $"/api/reports/monthly{queryString}", TestClient.JsonOptions))!;

    private static async Task<string?> CodeOfAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync())
            .RootElement.GetProperty("code").GetString();

    public ReportTests(DatabaseFixture database) : base(database)
    {
    }
}
