namespace IELTop_Content_Server.Options;

/// <summary>
/// How the public ieltop/1 protocol greets and guards clients.
/// </summary>
public sealed class ServerOptions
{
    public const string Section = "Server";

    public string Name { get; set; } = "IELTop Content Server";
    public bool AllowAnonymous { get; set; }
    public bool AllowAccessCode { get; set; } = true;
    public bool AllowLogin { get; set; } = true;

    /// <summary>Optional shared code that works without a stored row.</summary>
    public string MasterCode { get; set; } = string.Empty;

    public int TokenHours { get; set; } = 12;
    public bool UseHttps { get; set; }

    /// <summary>
    /// Trust X-Forwarded-For and X-Forwarded-Proto from any proxy. Turn
    /// this on only behind a proxy you control, so client addresses and
    /// the request scheme are correct for rate limiting and cookies.
    /// </summary>
    public bool TrustProxy { get; set; }

    /// <summary>Upper bound on one request body in bytes. Guards the host.</summary>
    public long MaxRequestBodyMb { get; set; } = 350;

    /// <summary>Upper bound on open connections. A safety net, not a queue.</summary>
    public int MaxConcurrentConnections { get; set; } = 20000;

    public string AdminUsername { get; set; } = "admin";
    public string AdminPassword { get; set; } = string.Empty;

    /// <summary>
    /// Empty by default: the configuration binder appends to a list,
    /// so a non empty default would double every configured entry.
    /// </summary>
    public List<string> Skills { get; set; } = new();

    public static readonly List<string> DefaultSkills = new()
    {
        "listening", "reading", "writing", "speaking"
    };
}

/// <summary>
/// Which database to talk to. Sqlite is the local default, Postgres is
/// for the Linux server.
/// </summary>
public sealed class DatabaseOptions
{
    public const string Section = "Database";

    public string Provider { get; set; } = "Sqlite";
    public string ConnectionString { get; set; } = string.Empty;
}

/// <summary>
/// Caching. Memory is one instance, Redis shares across instances.
/// </summary>
public sealed class CacheOptions
{
    public const string Section = "Cache";

    public string Provider { get; set; } = "Memory";
    public string RedisConnection { get; set; } = "localhost:6379";
    public int PaperListSeconds { get; set; } = 60;
    public int PaperDetailSeconds { get; set; } = 300;
    public int SettingsSeconds { get; set; } = 30;
}

/// <summary>
/// Where uploaded and seeded files live, and how big an upload may be.
/// </summary>
public sealed class StorageOptions
{
    public const string Section = "Storage";

    public string Root { get; set; } = "App_Data";
    public string SeedFolder { get; set; } = string.Empty;
    public string ImportFolder { get; set; } = string.Empty;
    public int MaxUploadMb { get; set; } = 300;
}

/// <summary>
/// Requests per minute per client. Anonymous callers are capped lower
/// than callers that present a code or a token.
/// </summary>
public sealed class RateLimitOptions
{
    public const string Section = "RateLimit";

    public bool Enabled { get; set; } = true;
    public int AnonymousPerMinute { get; set; } = 600;
    public int CredentialPerMinute { get; set; } = 3000;

    /// <summary>Login attempts per minute per address for the protocol login.</summary>
    public int LoginPerMinute { get; set; } = 30;

    /// <summary>Form saves per minute per address across the portal.</summary>
    public int FormPerMinute { get; set; } = 120;

    /// <summary>Register, sign in, and editor applications per minute per address.</summary>
    public int AuthPerMinute { get; set; } = 40;

    /// <summary>
    /// Cap on distinct credentials tracked by the protocol limiter. Keeps
    /// a flood of made up codes or tokens from growing the table forever.
    /// </summary>
    public int MaxTrackedCredentials { get; set; } = 20000;
}

/// <summary>
/// Cloudflare Turnstile on the public portal forms: contributor sign in,
/// register, and submit. Both secrets empty means the check is off so
/// the server still works offline.
/// </summary>
public sealed class CaptchaOptions
{
    public const string Section = "Captcha";

    public string SiteKey { get; set; } = string.Empty;
    public string SecretKey { get; set; } = string.Empty;

    /// <summary>Test keys from Cloudflare. Only honored in Development.</summary>
    public bool AllowTestKeys { get; set; } = true;

    /// <summary>
    /// When the verify call cannot reach Cloudflare, allow the form
    /// through (true) or refuse it (false). Default is refuse, so an
    /// outage cannot be used to bypass the bot check. Turn it on only if
    /// the form being briefly unavailable is worse than the risk.
    /// </summary>
    public bool FailOpen { get; set; }

    public bool Enabled => !string.IsNullOrWhiteSpace(SiteKey) && !string.IsNullOrWhiteSpace(SecretKey);
}

/// <summary>
/// SMTP for the notifications a contributor gets: welcome, accepted,
/// rejected, role approved or declined.
/// </summary>
public sealed class SmtpOptions
{
    public const string Section = "Smtp";

    public bool Enabled { get; set; }
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; } = 587;
    public bool UseStartTls { get; set; } = true;
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string FromAddress { get; set; } = "no-reply@example.com";
    public string FromName { get; set; } = "IELTop Content Server";
    public string BaseUrl { get; set; } = string.Empty;
}

/// <summary>
/// The OpenAI compatible endpoint used to review and tag submitted
/// papers. Empty base URL means review is skipped and the paper waits
/// for a human, so the server never depends on a model.
/// </summary>
public sealed class LlmOptions
{
    public const string Section = "Llm";

    public string BaseUrl { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public double Temperature { get; set; } = 0.2;
    public int MaxTokens { get; set; } = 2000;
    public int TimeoutSeconds { get; set; } = 120;

    public bool Enabled => !string.IsNullOrWhiteSpace(BaseUrl) && !string.IsNullOrWhiteSpace(Model);
}

/// <summary>
/// Limits for the public contribute flow.
/// </summary>
public sealed class ContributeOptions
{
    public const string Section = "Contribute";

    /// <summary>Review with the model on submit. Off means always human review.</summary>
    public bool AutoReview { get; set; } = true;

    /// <summary>Reject outright when the model score is below this. 0 disables auto reject.</summary>
    public int AutoRejectBelowScore { get; set; } = 0;

    public int MaxFilesPerSubmission { get; set; } = 10;
    public int MaxSubmissionMb { get; set; } = 60;

    /// <summary>Submissions allowed per account per hour.</summary>
    public int SubmissionsPerHour { get; set; } = 10;
}
