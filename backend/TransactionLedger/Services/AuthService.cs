using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using TransactionLedger.Data;
using TransactionLedger.Domain;
using TransactionLedger.DTOs;

namespace TransactionLedger.Services;

public sealed class AuthService : IAuthService
{
    private const string UniqueViolationSqlState = "23505";
    private const string EmailUniqueIndexName = "UX_Users_Email";

    /// <summary>
    /// A real PBKDF2 hash, verified against on the unknown-email path so that
    /// path costs the same as a wrong-password path (BR-12). Computed once.
    /// </summary>
    private static readonly string DummyHash =
        new PasswordHasher<User>().HashPassword(
            User.Register("dummy@example.invalid", "dummy"),
            "not-a-real-password");

    private readonly AppDbContext _dbContext;
    private readonly IPasswordHasher<User> _passwordHasher;
    private readonly ITokenService _tokenService;

    public AuthService(
        AppDbContext dbContext,
        IPasswordHasher<User> passwordHasher,
        ITokenService tokenService)
    {
        _dbContext = dbContext;
        _passwordHasher = passwordHasher;
        _tokenService = tokenService;
    }

    public async Task<RegisterResponse> RegisterAsync(
        RegisterRequest request,
        CancellationToken cancellationToken)
    {
        var user = User.Register(request.Email, request.DisplayName);
        user.SetPasswordHash(_passwordHasher.HashPassword(user, request.Password));

        _dbContext.Users.Add(user);

        try
        {
            // BR-11: no SELECT-then-INSERT pre-check. The unique index decides,
            // and we translate its violation. A pre-check is a TOCTOU race:
            // two concurrent registrations both see "free" and both insert.
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (IsEmailUniqueViolation(ex))
        {
            throw new DomainException(
                ErrorCodes.EmailAlreadyRegistered,
                StatusCodes.Status409Conflict,
                "That email address is already registered.");
        }

        return new RegisterResponse(user.Id, user.Email, user.DisplayName, user.CreatedAt);
    }

    public async Task<LoginResponse> LoginAsync(
        LoginRequest request,
        CancellationToken cancellationToken)
    {
        var email = User.NormaliseEmail(request.Email);

        var user = await _dbContext.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Email == email, cancellationToken);

        if (user is null)
        {
            // BR-12: burn the same PBKDF2 work an existing user would cost,
            // so response time does not reveal whether the email exists.
            _passwordHasher.VerifyHashedPassword(
                User.Register("dummy@example.invalid", "dummy"),
                DummyHash,
                request.Password);

            throw InvalidCredentials();
        }

        var verification = _passwordHasher.VerifyHashedPassword(user, user.PasswordHash, request.Password);
        if (verification == PasswordVerificationResult.Failed)
        {
            throw InvalidCredentials();
        }

        var (accessToken, expiresAt) = _tokenService.CreateToken(user);

        return new LoginResponse(
            accessToken,
            expiresAt,
            new UserSummary(user.Id, user.Email, user.DisplayName));
    }

    /// <summary>
    /// Identical for unknown email and wrong password, byte for byte (BR-12).
    /// </summary>
    private static DomainException InvalidCredentials() => new(
        ErrorCodes.InvalidCredentials,
        StatusCodes.Status401Unauthorized,
        "Email or password is incorrect.");

    private static bool IsEmailUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is PostgresException postgres
        && postgres.SqlState == UniqueViolationSqlState
        && postgres.ConstraintName == EmailUniqueIndexName;
}
