using System.Net.Mail;
using System.Security.Claims;
using KnowledgeBase.Api.Common;
using KnowledgeBase.Api.Features.Workspaces;
using KnowledgeBase.Api.Logging;
using KnowledgeBase.Core;
using KnowledgeBase.Core.Users;
using KnowledgeBase.Core.Workspaces;
using KnowledgeBase.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace KnowledgeBase.Api.Features.Auth;

public sealed record RegisterRequest(string Email, string DisplayName, string Password, string? WorkspaceName = null);

public sealed record LoginRequest(string Email, string Password);

public sealed record UserResponse(Guid Id, string Email, string DisplayName);

public sealed record SessionResponse(string AccessToken, DateTimeOffset ExpiresAt, UserResponse User);

public sealed record MeResponse(UserResponse User, IReadOnlyList<WorkspaceResponse> Workspaces);

internal static partial class AuthEndpoints
{
    public const string RateLimit = "auth";
    public const int PasswordMinLength = 10;
    private const int PasswordMaxLength = 128;
    private const string DefaultWorkspaceName = "My workspace";

    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/auth").WithTags("Auth");
        group.MapPost("/register", Register).RequireRateLimiting(RateLimit)
            .WithSummary("Create an account")
            .WithDescription("The account comes with a workspace of its own, named `workspaceName` or \"My workspace\".");
        group.MapPost("/login", Login).RequireRateLimiting(RateLimit).WithSummary("Sign in");
        app.MapGet("/api/me", Me).WithTags("Auth").RequireAuthorization().WithSummary("You and your workspaces");
        return app;
    }

    private static async Task<Results<Created<SessionResponse>, ValidationProblem>> Register(
        RegisterRequest request, KnowledgeBaseDbContext db, PasswordVerifier passwords, TokenService tokens, TimeProvider clock,
        ILoggerFactory loggers, CancellationToken ct)
    {
        var validation = new Validation()
            .Require(IsEmail(request.Email), "email", "A valid email address is required.")
            .Require(request.DisplayName?.Trim().Length is >= User.DisplayNameMinLength and <= User.DisplayNameMaxLength,
                "displayName", $"A display name has {User.DisplayNameMinLength} to {User.DisplayNameMaxLength} characters.")
            .Require(request.Password is { Length: >= PasswordMinLength and <= PasswordMaxLength },
                "password", $"A password has {PasswordMinLength} to {PasswordMaxLength} characters.")
            .Require(request.WorkspaceName is null || request.WorkspaceName.Trim().Length is >= Workspace.NameMinLength and <= Workspace.NameMaxLength,
                "workspaceName", $"A workspace name has {Workspace.NameMinLength} to {Workspace.NameMaxLength} characters.");
        if (!validation.IsValid)
        {
            return validation.ToProblem();
        }

        var email = User.NormalizeEmail(request.Email);
        if (await db.Users.AnyAsync(u => u.Email == email, ct))
        {
            throw new DomainException(ErrorCodes.EmailTaken, "An account with this email already exists. Sign in instead.");
        }

        var now = clock.GetUtcNow();
        var user = User.Register(email, request.DisplayName!, now);
        user.SetPasswordHash(passwords.Hash(user, request.Password));
        var (workspace, owner) = Workspace.Create(request.WorkspaceName ?? DefaultWorkspaceName, user.Id, now);
        db.Users.Add(user);
        db.Workspaces.Add(workspace);
        db.Memberships.Add(owner);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.IsUniqueViolation("ix_users_email"))
        {
            throw new DomainException(ErrorCodes.EmailTaken, "An account with this email already exists. Sign in instead.");
        }

        LogRegistered(loggers.CreateLogger(typeof(AuthEndpoints)), user.Id);
        return TypedResults.Created("/api/me", Session(tokens, user));
    }

    private static async Task<Results<Ok<SessionResponse>, ValidationProblem>> Login(
        LoginRequest request, KnowledgeBaseDbContext db, PasswordVerifier passwords, TokenService tokens, ILoggerFactory loggers, CancellationToken ct)
    {
        var validation = new Validation()
            .Require(!string.IsNullOrWhiteSpace(request.Email), "email", "Email is required.")
            .Require(!string.IsNullOrEmpty(request.Password), "password", "Password is required.");
        if (!validation.IsValid)
        {
            return validation.ToProblem();
        }

        var email = User.NormalizeEmail(request.Email);
        var user = await db.Users.SingleOrDefaultAsync(u => u.Email == email, ct);
        if (!passwords.Verify(user, request.Password, out var rehash) || user is null)
        {
            LogFailed(loggers.CreateLogger(typeof(AuthEndpoints)), Masking.Email(email));
            throw new DomainException(ErrorCodes.InvalidCredentials, "Email or password is incorrect.");
        }

        if (rehash is not null)
        {
            user.SetPasswordHash(rehash);
            await db.SaveChangesAsync(ct);
        }

        return TypedResults.Ok(Session(tokens, user));
    }

    private static async Task<Ok<MeResponse>> Me(ClaimsPrincipal principal, KnowledgeBaseDbContext db, CancellationToken ct)
    {
        var userId = principal.UserId();
        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Id == userId, ct)
            ?? throw new DomainException(ErrorCodes.NotFound, "The account no longer exists.");
        var workspaces = await WorkspaceEndpoints.ListForAsync(db, userId, ct);
        return TypedResults.Ok(new MeResponse(new UserResponse(user.Id, user.Email, user.DisplayName), workspaces));
    }

    private static SessionResponse Session(TokenService tokens, User user)
    {
        var (token, expiresAt) = tokens.Issue(user);
        return new SessionResponse(token, expiresAt, new UserResponse(user.Id, user.Email, user.DisplayName));
    }

    internal static bool IsEmail(string? value) =>
        !string.IsNullOrWhiteSpace(value)
        && value.Trim().Length <= User.EmailMaxLength
        && MailAddress.TryCreate(value.Trim(), out var address)
        && string.Equals(address.Address, value.Trim(), StringComparison.OrdinalIgnoreCase)
        && address.Host.Contains('.', StringComparison.Ordinal);

    [LoggerMessage(Level = LogLevel.Information, Message = "Account {UserId} registered")]
    private static partial void LogRegistered(ILogger logger, Guid userId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed sign-in for {MaskedEmail}")]
    private static partial void LogFailed(ILogger logger, string maskedEmail);
}
