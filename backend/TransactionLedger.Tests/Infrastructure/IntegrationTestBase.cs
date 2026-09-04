using System.Net.Http.Headers;
using TransactionLedger.DTOs;

namespace TransactionLedger.Tests.Infrastructure;

/// <summary>
/// Per docs/07-testing-strategy.md §3: each test class gets a freshly created
/// user via the real register and login endpoints. Classes never share a user,
/// so they never share that user's data, so they run in parallel without the
/// tables being truncated between them. Truncation would force serial
/// execution and hide ordering bugs.
/// </summary>
public abstract class IntegrationTestBase : IClassFixture<DatabaseFixture>, IAsyncLifetime
{
    private readonly DatabaseFixture _database;

    protected IntegrationTestBase(DatabaseFixture database)
    {
        _database = database;
    }

    protected ApiFactory Factory { get; private set; } = null!;

    /// <summary>Anonymous client: no Authorization header.</summary>
    protected HttpClient Client { get; private set; } = null!;

    /// <summary>Carries the bearer token of this class's own user.</summary>
    protected HttpClient AuthedClient { get; private set; } = null!;

    protected RegisterResponse User { get; private set; } = null!;

    protected string UserEmail { get; private set; } = string.Empty;

    public async Task InitializeAsync()
    {
        Factory = new ApiFactory(_database.ConnectionString);
        Client = Factory.CreateClient();

        UserEmail = TestClient.UniqueEmail();
        User = await TestClient.RegisterAsync(Client, UserEmail);
        var login = await TestClient.LoginAsync(Client, UserEmail);

        AuthedClient = Factory.CreateClient();
        AuthedClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", login.AccessToken);

        await OnInitializedAsync();
    }

    /// <summary>Hook for classes needing extra setup once the user exists.</summary>
    protected virtual Task OnInitializedAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        Client.Dispose();
        AuthedClient.Dispose();
        await Factory.DisposeAsync();
    }
}
