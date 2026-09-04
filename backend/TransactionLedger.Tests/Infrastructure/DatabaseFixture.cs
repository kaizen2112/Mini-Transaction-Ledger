using Testcontainers.PostgreSql;

namespace TransactionLedger.Tests.Infrastructure;

/// <summary>
/// One real Postgres container per test run (docs/07-testing-strategy.md §3).
/// Not the EF InMemory provider: it has no transactions, no FOR UPDATE, no
/// check constraints and no unique-index enforcement, so A2 and A4 would pass
/// against it while being false in production (§2.2).
/// </summary>
public sealed class DatabaseFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .Build();

    public string ConnectionString => _container.GetConnectionString();

    public Task InitializeAsync() => _container.StartAsync();

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();
}

[CollectionDefinition(DatabaseCollection.Name)]
public sealed class DatabaseCollection : ICollectionFixture<DatabaseFixture>
{
    public const string Name = "database";
}
