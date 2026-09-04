using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using TransactionLedger.DTOs;

namespace TransactionLedger.Tests.Infrastructure;

/// <summary>
/// Each test class registers its own users through the real endpoints, so no
/// two classes share a user, so no class needs the tables truncated and they
/// can run in parallel (docs/07-testing-strategy.md §3).
/// </summary>
public static class TestClient
{
    public const string DefaultPassword = "S3cure!passphrase";

    /// <summary>
    /// Mirrors the API's own serialisation: enums travel as NAMES in both
    /// directions (contract §1.3). Without the converter the client cannot
    /// read "type":"Cash" back into AccountType, which is a client-side
    /// limitation, not an API one.
    /// </summary>
    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    public static string UniqueEmail(string prefix = "user") =>
        $"{prefix}-{Guid.NewGuid():N}@example.com";

    public static async Task<RegisterResponse> RegisterAsync(
        HttpClient client,
        string? email = null,
        string password = DefaultPassword,
        string displayName = "Test User")
    {
        var response = await client.PostAsJsonAsync("/api/auth/register", new
        {
            email = email ?? UniqueEmail(),
            password,
            displayName
        });

        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<RegisterResponse>(JsonOptions))!;
    }

    public static async Task<LoginResponse> LoginAsync(
        HttpClient client,
        string email,
        string password = DefaultPassword)
    {
        var response = await client.PostAsJsonAsync("/api/auth/login", new { email, password });

        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<LoginResponse>(JsonOptions))!;
    }

    /// <summary>Registers a fresh user and returns a client carrying its token.</summary>
    public static async Task<(HttpClient Client, RegisterResponse User)> AuthedClientAsync(
        ApiFactory factory)
    {
        var client = factory.CreateClient();
        var email = UniqueEmail();
        var user = await RegisterAsync(client, email);
        var login = await LoginAsync(client, email);

        var authed = factory.CreateClient();
        authed.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", login.AccessToken);

        return (authed, user);
    }
}
