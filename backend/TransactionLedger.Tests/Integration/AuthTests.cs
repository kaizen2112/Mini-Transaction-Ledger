using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TransactionLedger.Data;
using TransactionLedger.Tests.Infrastructure;

namespace TransactionLedger.Tests.Integration;

/// <summary>Tests A1-A6 from docs/07-testing-strategy.md §4.1.</summary>
public sealed class AuthTests : IntegrationTestBase
{
    public AuthTests(DatabaseFixture database) : base(database)
    {
    }

    // A1
    [Fact]
    public async Task Register_with_valid_payload_returns_201_and_no_token()
    {
        var email = TestClient.UniqueEmail();

        var response = await Client.PostAsJsonAsync("/api/auth/register", new
        {
            email,
            password = TestClient.DefaultPassword,
            displayName = "Turzo"
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(body).RootElement;

        Assert.Equal(email, json.GetProperty("email").GetString());
        Assert.Equal("Turzo", json.GetProperty("displayName").GetString());
        Assert.NotEqual(Guid.Empty, json.GetProperty("id").GetGuid());

        Assert.DoesNotContain("token", body, StringComparison.OrdinalIgnoreCase);
    }

    // A2
    [Fact]
    public async Task Register_with_duplicate_email_returns_409_email_already_registered()
    {
        var email = TestClient.UniqueEmail();
        await TestClient.RegisterAsync(Client, email);

        var response = await Client.PostAsJsonAsync("/api/auth/register", new
        {
            email,
            password = TestClient.DefaultPassword,
            displayName = "Second"
        });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal("EMAIL_ALREADY_REGISTERED", json.GetProperty("code").GetString());
    }

    // A2 (BR-10): uniqueness is case-insensitive because the email is normalised.
    [Fact]
    public async Task Register_with_same_email_in_different_case_returns_409()
    {
        var email = TestClient.UniqueEmail();
        await TestClient.RegisterAsync(Client, email);

        var response = await Client.PostAsJsonAsync("/api/auth/register", new
        {
            email = email.ToUpperInvariant(),
            password = TestClient.DefaultPassword,
            displayName = "Second"
        });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    // A3
    [Fact]
    public async Task Register_response_body_never_contains_the_password()
    {
        const string password = "Unmistakable!Password9";

        var response = await Client.PostAsJsonAsync("/api/auth/register", new
        {
            email = TestClient.UniqueEmail(),
            password,
            displayName = "Turzo"
        });

        var body = await response.Content.ReadAsStringAsync();

        Assert.DoesNotContain(password, body);
        Assert.DoesNotContain("passwordHash", body, StringComparison.OrdinalIgnoreCase);
    }

    // A4
    [Fact]
    public async Task Stored_hash_differs_from_plaintext_and_from_another_users_hash()
    {
        var firstEmail = TestClient.UniqueEmail();
        var secondEmail = TestClient.UniqueEmail();

        await TestClient.RegisterAsync(Client, firstEmail);
        await TestClient.RegisterAsync(Client, secondEmail);

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var first = await db.Users.AsNoTracking().SingleAsync(u => u.Email == firstEmail);
        var second = await db.Users.AsNoTracking().SingleAsync(u => u.Email == secondEmail);

        Assert.NotEqual(TestClient.DefaultPassword, first.PasswordHash);
        Assert.DoesNotContain(TestClient.DefaultPassword, first.PasswordHash);

        // Same plaintext, different stored hash: proof of a per-password salt.
        Assert.NotEqual(first.PasswordHash, second.PasswordHash);
    }

    // A5
    [Fact]
    public async Task Login_with_correct_credentials_returns_a_token_whose_sub_is_the_user_id()
    {
        var email = TestClient.UniqueEmail();
        var registered = await TestClient.RegisterAsync(Client, email);

        var login = await TestClient.LoginAsync(Client, email);

        Assert.Equal(registered.Id, login.User.Id);
        Assert.False(string.IsNullOrWhiteSpace(login.AccessToken));

        var sub = JwtTestTokens.ReadClaim(login.AccessToken, "sub");
        Assert.Equal(registered.Id.ToString(), sub);

        // 60-minute lifetime per contract §3.
        var lifetime = login.ExpiresAt - DateTime.UtcNow;
        Assert.InRange(lifetime.TotalMinutes, 55, 60);
    }

    // A6
    [Fact]
    public async Task Wrong_password_and_unknown_email_return_identical_401_bodies()
    {
        var email = TestClient.UniqueEmail();
        await TestClient.RegisterAsync(Client, email);

        var wrongPassword = await Client.PostAsJsonAsync("/api/auth/login", new
        {
            email,
            password = "Definitely!TheWrongOne1"
        });

        var unknownEmail = await Client.PostAsJsonAsync("/api/auth/login", new
        {
            email = TestClient.UniqueEmail(),
            password = TestClient.DefaultPassword
        });

        Assert.Equal(HttpStatusCode.Unauthorized, wrongPassword.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, unknownEmail.StatusCode);

        // traceId is per-request by design (contract §1.1), so it is excluded
        // from the comparison. Every other member must match exactly, or the
        // API leaks which emails are registered (BR-12).
        Assert.Equal(
            await WithoutTraceIdAsync(wrongPassword),
            await WithoutTraceIdAsync(unknownEmail));
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
