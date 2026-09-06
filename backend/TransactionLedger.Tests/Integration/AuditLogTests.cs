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
/// BR-36, BR-37 and BR-38. There is no read API by design, so every assertion
/// here queries the table directly — which is also exactly how the audit log is
/// meant to be inspected in production.
/// </summary>
public sealed class AuditLogTests : IntegrationTestBase
{
    public AuditLogTests(DatabaseFixture database) : base(database)
    {
    }

    // BR-36: registration is audited. The base class already registered this
    // class's user, so the row must exist before any test body runs.
    [Fact]
    public async Task Registering_a_user_writes_a_UserRegistered_entry()
    {
        var entries = await EntriesAsync(AuditAction.UserRegistered);

        var entry = Assert.Single(entries);
        Assert.Equal(nameof(User), entry.EntityType);
        Assert.Equal(User.Id, entry.EntityId);
        Assert.Equal(User.Id, entry.UserId);
    }

    // BR-36: all five actions, end to end, in one user's lifetime.
    [Fact]
    public async Task All_five_audited_actions_write_exactly_one_entry_each()
    {
        var source = await CreateAccountAsync("Audit source");
        var destination = await CreateAccountAsync("Audit destination");

        var credit = await PostTransactionAsync(source.Id, "Credit", 500m);
        await ReverseAsync(credit.Id);
        await PostTransactionAsync(source.Id, "Credit", 500m);
        await TransferAsync(source.Id, destination.Id, 100m);

        Assert.Equal(1, await CountAsync(AuditAction.UserRegistered));
        Assert.Equal(2, await CountAsync(AuditAction.AccountCreated));
        Assert.Equal(2, await CountAsync(AuditAction.TransactionCreated));
        Assert.Equal(1, await CountAsync(AuditAction.TransactionReversed));
        Assert.Equal(1, await CountAsync(AuditAction.TransferCreated));
    }

    // BR-36: the entry points at the entity the action created.
    [Fact]
    public async Task Each_entry_identifies_the_entity_it_records()
    {
        var account = await CreateAccountAsync("Entity identity");
        var transaction = await PostTransactionAsync(account.Id, "Credit", 250m);

        var accountEntry = Assert.Single(
            await EntriesAsync(AuditAction.AccountCreated, e => e.EntityId == account.Id));
        Assert.Equal(nameof(Account), accountEntry.EntityType);

        var transactionEntry = Assert.Single(
            await EntriesAsync(AuditAction.TransactionCreated, e => e.EntityId == transaction.Id));
        Assert.Equal(nameof(Transaction), transactionEntry.EntityType);
    }

    /// <summary>
    /// BR-36, the rule that makes this feature worth having. A failed action
    /// leaves NO audit entry, because the entry shares the action's transaction
    /// and rolls back with it. An audit log that records things which never
    /// happened is worse than none.
    /// </summary>
    [Fact]
    public async Task A_rolled_back_action_leaves_no_audit_entry()
    {
        var account = await CreateAccountAsync("Rollback");
        await PostTransactionAsync(account.Id, "Credit", 100m);

        var before = await CountAsync(AuditAction.TransactionCreated);

        // Overdraft: rejected with 409 after the transaction has been opened.
        var response = await AuthedClient.PostWithKeyAsync(
            $"/api/accounts/{account.Id}/transactions",
            new { type = "Debit", amount = 5000m, category = "Food" });

        Assert.Equal(System.Net.HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(before, await CountAsync(AuditAction.TransactionCreated));
    }

    /// <summary>
    /// The same property for a transfer, which is the harder case: several rows
    /// are already written before the failure point (BR-28 + BR-36 together).
    /// </summary>
    [Fact]
    public async Task A_failed_transfer_leaves_no_audit_entry()
    {
        var source = await CreateAccountAsync("Failed transfer source");
        var destination = await CreateAccountAsync("Failed transfer destination");
        await PostTransactionAsync(source.Id, "Credit", 100m);

        var before = await CountAsync(AuditAction.TransferCreated);

        var response = await AuthedClient.PostWithKeyAsync("/api/transfers", new
        {
            sourceAccountId = source.Id,
            destinationAccountId = destination.Id,
            amount = 9999m
        });

        Assert.Equal(System.Net.HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(before, await CountAsync(AuditAction.TransferCreated));
    }

    // BR-37: the audit log is not a second ledger. If an Amount leaked into
    // metadata, someone could compute a balance from it and get a second,
    // disagreeing answer.
    [Fact]
    public async Task Metadata_carries_no_financial_payload()
    {
        var source = await CreateAccountAsync("Metadata source");
        var destination = await CreateAccountAsync("Metadata destination");
        await PostTransactionAsync(source.Id, "Credit", 1234.56m);
        await TransferAsync(source.Id, destination.Id, 777.77m);

        var entries = await EntriesAsync();

        Assert.NotEmpty(entries);

        foreach (var entry in entries.Where(e => e.Metadata is not null))
        {
            using var json = JsonDocument.Parse(entry.Metadata!);

            foreach (var property in json.RootElement.EnumerateObject())
            {
                Assert.False(
                    property.Name.Contains("amount", StringComparison.OrdinalIgnoreCase),
                    $"{entry.Action} metadata leaks an amount: {entry.Metadata}");

                Assert.False(
                    property.Name.Contains("balance", StringComparison.OrdinalIgnoreCase),
                    $"{entry.Action} metadata leaks a balance: {entry.Metadata}");
            }

            // And no bare number that could be money.
            Assert.DoesNotContain("1234.56", entry.Metadata);
            Assert.DoesNotContain("777.77", entry.Metadata);
        }
    }

    // BR-37: metadata is valid jsonb, so the column type is doing real work
    // rather than storing opaque text.
    [Fact]
    public async Task Metadata_is_queryable_json_in_the_database()
    {
        var account = await CreateAccountAsync("Json metadata");

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // ->> is a jsonb operator. It would fail outright on a text column.
        var types = await db.Database
            .SqlQuery<string>($"""
                SELECT "Metadata" ->> 'type' AS "Value"
                FROM "AuditLogs"
                WHERE "EntityId" = {account.Id} AND "Action" = 1
                """)
            .ToListAsync();

        Assert.Equal("Cash", Assert.Single(types));
    }

    // BR-38: append-only. No endpoint reads, updates or deletes audit logs.
    [Theory]
    [InlineData("/api/audit-logs")]
    [InlineData("/api/auditlogs")]
    [InlineData("/api/audit")]
    public async Task No_endpoint_exposes_audit_logs(string route)
    {
        var response = await AuthedClient.GetAsync(route);

        Assert.Equal(System.Net.HttpStatusCode.NotFound, response.StatusCode);
    }

    // BR-07/BR-43: one user's actions never appear under another's id.
    [Fact]
    public async Task Entries_are_attributed_to_the_user_who_acted()
    {
        await CreateAccountAsync("Attribution");

        var (userB, _) = await TestClient.AuthedClientAsync(Factory);
        using var b = userB;

        var response = await userB.PostAsJsonAsync(
            "/api/accounts", new { name = "User B account", type = "Cash" });
        response.EnsureSuccessStatusCode();
        var theirs = (await response.Content.ReadFromJsonAsync<AccountResponse>(TestClient.JsonOptions))!;

        var mine = await EntriesAsync();

        Assert.All(mine, entry => Assert.Equal(User.Id, entry.UserId));
        Assert.DoesNotContain(mine, entry => entry.EntityId == theirs.Id);
    }

    // ---- helpers ----------------------------------------------------------

    private async Task<List<AuditLog>> EntriesAsync(
        AuditAction? action = null,
        Func<AuditLog, bool>? predicate = null)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var query = db.AuditLogs.AsNoTracking().Where(a => a.UserId == User.Id);

        if (action.HasValue)
        {
            query = query.Where(a => a.Action == action.Value);
        }

        var entries = await query.OrderBy(a => a.OccurredAt).ToListAsync();

        return predicate is null ? entries : entries.Where(predicate).ToList();
    }

    private async Task<int> CountAsync(AuditAction action) =>
        (await EntriesAsync(action)).Count;

    private async Task<AccountResponse> CreateAccountAsync(string name)
    {
        var response = await AuthedClient.PostAsJsonAsync("/api/accounts", new { name, type = "Cash" });
        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<AccountResponse>(TestClient.JsonOptions))!;
    }

    private async Task<TransactionResponse> PostTransactionAsync(
        Guid accountId,
        string type,
        decimal amount)
    {
        var response = await AuthedClient.PostWithKeyAsync(
            $"/api/accounts/{accountId}/transactions",
            new { type, amount, category = "Salary" });

        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<TransactionResponse>(TestClient.JsonOptions))!;
    }

    private async Task ReverseAsync(Guid transactionId)
    {
        var response = await AuthedClient.PostAsJsonAsync(
            $"/api/transactions/{transactionId}/reverse", new { description = (string?)null });

        response.EnsureSuccessStatusCode();
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
}
