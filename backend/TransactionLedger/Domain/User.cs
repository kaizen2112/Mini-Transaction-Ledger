namespace TransactionLedger.Domain;

/// <summary>
/// A registered user. Email is the identity field, stored trimmed and
/// lowercased (BR-10); normalisation lives here so no caller can bypass it.
/// There is no plaintext password property anywhere on this type or in the
/// schema — that absence is the enforcement of BR-09.
/// </summary>
public sealed class User
{
    // EF Core materialises through this; application code uses Register.
    private User()
    {
        Email = string.Empty;
        PasswordHash = string.Empty;
        DisplayName = string.Empty;
    }

    private User(Guid id, string email, string displayName, DateTime createdAt)
    {
        Id = id;
        Email = email;
        DisplayName = displayName;
        CreatedAt = createdAt;
        PasswordHash = string.Empty;
    }

    public Guid Id { get; private set; }
    public string Email { get; private set; }
    public string PasswordHash { get; private set; }
    public string DisplayName { get; private set; }
    public DateTime CreatedAt { get; private set; }

    /// <summary>
    /// UUIDv7 so inserts land at the right-hand edge of the B-tree while the
    /// ID stays non-guessable (docs/04-database-design.md §1.1).
    /// </summary>
    public static User Register(string email, string displayName) =>
        new(Guid.CreateVersion7(), NormaliseEmail(email), displayName.Trim(), DateTime.UtcNow);

    /// <summary>BR-10: trimmed and lowercased, so uniqueness is case-insensitive.</summary>
    public static string NormaliseEmail(string email) => email.Trim().ToLowerInvariant();

    public void SetPasswordHash(string passwordHash) => PasswordHash = passwordHash;
}
