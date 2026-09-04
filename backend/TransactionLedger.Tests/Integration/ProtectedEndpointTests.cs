using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using TransactionLedger.Tests.Infrastructure;

namespace TransactionLedger.Tests.Integration;

/// <summary>
/// Tests A7-A9 from docs/07-testing-strategy.md §4.1, against the test-only
/// protected endpoint described in TestProtectedController.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class ProtectedEndpointTests : IAsyncLifetime
{
    private const string ProtectedRoute = "/test/protected";

    private readonly DatabaseFixture _database;
    private ApiFactory _factory = null!;

    public ProtectedEndpointTests(DatabaseFixture database)
    {
        _database = database;
    }

    public Task InitializeAsync()
    {
        _factory = new ApiFactory(_database.ConnectionString);
        return Task.CompletedTask;
    }

    public Task DisposeAsync() => _factory.DisposeAsync().AsTask();

    // A7
    [Fact]
    public async Task Protected_endpoint_without_a_token_returns_401()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync(ProtectedRoute);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal("UNAUTHENTICATED", json.GetProperty("code").GetString());
    }

    // A8
    [Fact]
    public async Task Protected_endpoint_with_an_expired_token_returns_401()
    {
        var expired = JwtTestTokens.Create(Guid.CreateVersion7(), lifetime: TimeSpan.FromMinutes(-5));

        var response = await GetWithTokenAsync(expired);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // A9
    [Fact]
    public async Task Protected_endpoint_with_a_token_signed_by_another_key_returns_401()
    {
        var foreign = JwtTestTokens.Create(
            Guid.CreateVersion7(),
            key: "a-completely-different-signing-key-32+chars");

        var response = await GetWithTokenAsync(foreign);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Protected_endpoint_with_a_valid_token_returns_the_caller_id_from_sub()
    {
        var (client, user) = await TestClient.AuthedClientAsync(_factory);
        using var _ = client;

        var response = await client.GetAsync(ProtectedRoute);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal(user.Id, json.GetProperty("userId").GetGuid());
    }

    private async Task<HttpResponseMessage> GetWithTokenAsync(string token)
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await client.GetAsync(ProtectedRoute);
    }
}
