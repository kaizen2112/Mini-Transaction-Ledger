using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;
using TransactionLedger.Data;

namespace TransactionLedger.Tests.Infrastructure;

/// <summary>
/// One real Postgres container per test RUN, not per test class and not per
/// test — container startup would otherwise dominate the runtime
/// (docs/07-testing-strategy.md §3).
///
/// It is a class fixture rather than a collection fixture on purpose. A
/// collection fixture would share the container correctly, but xUnit's unit of
/// parallelisation is the collection, so every class sharing it would run
/// serially — exactly what §3 says must not happen. The container therefore
/// lives in static state guarded by a semaphore: every class gets its own
/// fixture instance, they all resolve to the same container, and the classes
/// stay in separate collections and run in parallel.
///
/// Not the EF InMemory provider: it has no transactions, no FOR UPDATE, no
/// check constraints and no unique-index enforcement, so A2 and A4 would pass
/// against it while being false in production (§2.2).
/// </summary>
public sealed class DatabaseFixture : IAsyncLifetime
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static PostgreSqlContainer? _container;
    private static bool _migrated;

    public string ConnectionString { get; private set; } = string.Empty;

    public async Task InitializeAsync()
    {
        await Gate.WaitAsync();
        try
        {
            if (_container is null)
            {
                _container = new PostgreSqlBuilder()
                    .WithImage("postgres:16-alpine")
                    .Build();

                await _container.StartAsync();
            }

            ConnectionString = _container.GetConnectionString();

            // Applied exactly once, before any host starts. EF Core takes no
            // distributed lock (docs/08-docker.md §7), so letting several
            // parallel ApiFactory hosts each call MigrateAsync against an
            // empty database would be a genuine race. Pre-migrating here makes
            // each host's startup migration a no-op read of the history table.
            if (!_migrated)
            {
                await MigrateAsync(ConnectionString);
                _migrated = true;
            }
        }
        finally
        {
            Gate.Release();
        }
    }

    /// <summary>
    /// Deliberately does not stop the container: it is shared by every test
    /// class, so no single class may dispose it. Testcontainers' Ryuk sidecar
    /// removes it when the test session ends.
    /// </summary>
    public Task DisposeAsync() => Task.CompletedTask;

    private static async Task MigrateAsync(string connectionString)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        await using var dbContext = new AppDbContext(options);
        await dbContext.Database.MigrateAsync();
    }
}
