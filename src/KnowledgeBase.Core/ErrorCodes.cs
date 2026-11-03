namespace KnowledgeBase.Core;

/// <summary>The stable error codes returned in problem responses.</summary>
public static class ErrorCodes
{
    public const string ValidationFailed = "validation_failed";
    public const string NotFound = "not_found";
    public const string Forbidden = "forbidden";
    public const string InvalidState = "invalid_state";
    public const string Conflict = "conflict";
    public const string EmailTaken = "email_taken";
    public const string InvalidCredentials = "invalid_credentials";
    public const string RateLimited = "rate_limited";
    public const string UnsupportedFile = "unsupported_file";
    public const string FileTooLarge = "file_too_large";
    public const string DuplicateDocument = "duplicate_document";
    public const string StorageLimitReached = "storage_limit_reached";
    public const string ProviderUnavailable = "provider_unavailable";
}
