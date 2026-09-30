using System.Security.Cryptography;
using System.Text;
using System.Threading.RateLimiting;
using IELTop_Content_Server;
using IELTop_Content_Server.Data;
using IELTop_Content_Server.Options;
using IELTop_Content_Server.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<ServerOptions>(builder.Configuration.GetSection(ServerOptions.Section));
builder.Services.Configure<DatabaseOptions>(builder.Configuration.GetSection(DatabaseOptions.Section));
builder.Services.Configure<CacheOptions>(builder.Configuration.GetSection(CacheOptions.Section));
builder.Services.Configure<StorageOptions>(builder.Configuration.GetSection(StorageOptions.Section));
builder.Services.Configure<RateLimitOptions>(builder.Configuration.GetSection(RateLimitOptions.Section));
builder.Services.Configure<CaptchaOptions>(builder.Configuration.GetSection(CaptchaOptions.Section));
builder.Services.Configure<SmtpOptions>(builder.Configuration.GetSection(SmtpOptions.Section));
builder.Services.Configure<LlmOptions>(builder.Configuration.GetSection(LlmOptions.Section));
builder.Services.Configure<ContributeOptions>(builder.Configuration.GetSection(ContributeOptions.Section));

// Kestrel is tuned for many small, mostly cached requests. HTTP/2 and
// HTTP/3 stay on. A single request body is capped so one client cannot
// exhaust host memory, and connections are capped as a safety net.
var serverOptions = builder.Configuration
    .GetSection(ServerOptions.Section).Get<ServerOptions>() ?? new ServerOptions();
long maxBodyBytes = Math.Max(1, serverOptions.MaxRequestBodyMb) * 1024L * 1024L;
int maxConnections = Math.Max(100, serverOptions.MaxConcurrentConnections);

builder.WebHost.ConfigureKestrel(options =>
{
    options.AddServerHeader = false;
    options.Limits.MaxConcurrentConnections = maxConnections;
    options.Limits.MaxConcurrentUpgradedConnections = maxConnections;
    options.Limits.MaxRequestBodySize = maxBodyBytes;
    options.Limits.KeepAliveTimeout = TimeSpan.FromMinutes(2);
    options.Limits.RequestHeadersTimeout = TimeSpan.FromSeconds(30);
});

var storageOptions = builder.Configuration
    .GetSection(StorageOptions.Section).Get<StorageOptions>() ?? new StorageOptions();
string storageRoot = StoragePaths.Root(storageOptions);
Directory.CreateDirectory(storageRoot);
Directory.CreateDirectory(StoragePaths.Audio(storageOptions));
Directory.CreateDirectory(StoragePaths.DataProtection(storageOptions));
Directory.CreateDirectory(SubmissionPaths.Root(storageOptions));

// Data protection keys live with the store and survive a restart or a
// redeploy, so signed in users are not dropped every time.
builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(StoragePaths.DataProtection(storageOptions)))
    .SetApplicationName("IELTop.ContentServer");

// Uploads arrive as multipart. Match the form limit to the storage
// limit so the reader and the writer agree, and keep the submit limit
// for the public flow.
long maxUploadBytes = Math.Max(1, storageOptions.MaxUploadMb) * 1024L * 1024L;
var contribute = builder.Configuration
    .GetSection(ContributeOptions.Section).Get<ContributeOptions>() ?? new ContributeOptions();
long maxSubmissionBytes = Math.Max(1, contribute.MaxSubmissionMb) * 1024L * 1024L;
long formLimit = Math.Max(maxUploadBytes, maxSubmissionBytes);
builder.Services.Configure<Microsoft.AspNetCore.Http.Features.FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = formLimit;
    options.ValueLengthLimit = (int)Math.Min(formLimit, int.MaxValue);
    options.MultipartHeadersLengthLimit = 64 * 1024;
});

// Database. Sqlite is the local default, Postgres is the Linux server.
var database = builder.Configuration
    .GetSection(DatabaseOptions.Section).Get<DatabaseOptions>() ?? new DatabaseOptions();

builder.Services.AddDbContextFactory<AppDbContext>(options =>
{
    if (string.Equals(database.Provider, "Postgres", StringComparison.OrdinalIgnoreCase)
        || string.Equals(database.Provider, "PostgreSQL", StringComparison.OrdinalIgnoreCase))
    {
        string connection = string.IsNullOrWhiteSpace(database.ConnectionString)
            ? "Host=localhost;Database=ieltop;Username=ieltop;Password=ieltop"
            : database.ConnectionString;
        options.UseNpgsql(connection);
    }
    else
    {
        string connection = string.IsNullOrWhiteSpace(database.ConnectionString)
            ? $"Data Source={Path.Combine(storageRoot, "ieltop.db")}"
            : database.ConnectionString;
        options.UseSqlite(connection);
    }
});

// Cache. One instance uses memory, many instances share Redis.
var cache = builder.Configuration
    .GetSection(CacheOptions.Section).Get<CacheOptions>() ?? new CacheOptions();

if (string.Equals(cache.Provider, "Redis", StringComparison.OrdinalIgnoreCase)
    || string.Equals(cache.Provider, "RedisDistributed", StringComparison.OrdinalIgnoreCase))
{
    builder.Services.AddStackExchangeRedisCache(options =>
    {
        options.Configuration = cache.RedisConnection;
        options.InstanceName = "ieltop:";
    });
}
else
{
    builder.Services.AddDistributedMemoryCache();
}

builder.Services.AddMemoryCache();
builder.Services.AddSingleton<IWriteThrottle, WriteThrottle>();
builder.Services.AddHttpClient("turnstile")
    .ConfigureHttpClient(c => c.Timeout = TimeSpan.FromSeconds(10));
builder.Services.AddHttpClient("llm")
    .ConfigureHttpClient(c => c.Timeout = TimeSpan.FromMinutes(5));

builder.Services.AddSingleton<IContentCache, ContentCache>();
builder.Services.AddSingleton<ITokenService, TokenService>();
builder.Services.AddSingleton<IStatsService, StatsService>();
builder.Services.AddSingleton<IPaperService, PaperService>();
builder.Services.AddSingleton<IAudioService, AudioService>();
builder.Services.AddSingleton<ICaptchaService, CaptchaService>();
builder.Services.AddSingleton<ILlmReviewService, LlmReviewService>();
builder.Services.AddSingleton<INotificationService, NotificationService>();
builder.Services.AddScoped<IAuditService, AuditService>();
builder.Services.AddScoped<ISettingsService, SettingsService>();
builder.Services.AddScoped<IProtocolService, ProtocolService>();
builder.Services.AddScoped<IAdminAuthService, AdminAuthService>();
builder.Services.AddScoped<IContributorService, ContributorService>();
builder.Services.AddScoped<ISubmissionService, SubmissionService>();
builder.Services.AddScoped<IEditorService, EditorService>();
builder.Services.AddScoped<DbInitializer>();

builder.Services.AddHostedService<NotificationWorker>();
builder.Services.AddHostedService<StatsFlushService>();

builder.Services
    .AddAuthentication(options =>
    {
        options.DefaultScheme = "admin";
        options.DefaultChallengeScheme = "admin";
    })
    .AddCookie("admin", options =>
    {
        options.Cookie.Name = "ieltop.admin";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        options.LoginPath = "/Account/Login";
        options.LogoutPath = "/Account/Logout";
        options.AccessDeniedPath = "/Account/Login";
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
        options.SlidingExpiration = true;
    })
    .AddCookie("contrib", options =>
    {
        options.Cookie.Name = "ieltop.contrib";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        options.LoginPath = "/Contrib/SignIn";
        options.LogoutPath = "/Contrib/SignOut";
        options.AccessDeniedPath = "/Contrib/SignIn";
        options.ExpireTimeSpan = TimeSpan.FromDays(14);
        options.SlidingExpiration = true;
    });

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("Admin", policy => policy
        .AddAuthenticationSchemes("admin")
        .RequireAuthenticatedUser()
        .RequireClaim(System.Security.Claims.ClaimTypes.Role, "Admin"));

    options.AddPolicy("Contributor", policy => policy
        .AddAuthenticationSchemes("contrib")
        .RequireAuthenticatedUser()
        .RequireClaim(System.Security.Claims.ClaimTypes.Role, "Contributor"));

    // The review queue is open to admins and to approved editors. Both
    // cookie schemes are accepted; the handler decides who qualifies.
    options.AddPolicy("Editor", policy => policy
        .AddAuthenticationSchemes("admin", "contrib")
        .RequireAuthenticatedUser()
        .AddRequirements(new IELTop_Content_Server.Auth.EditorRequirement()));
});

builder.Services.AddScoped<
    Microsoft.AspNetCore.Authorization.IAuthorizationHandler,
    IELTop_Content_Server.Auth.EditorAuthorizationHandler>();

builder.Services.AddRazorPages(options =>
{
    // Admin area: every page under these folders needs an admin.
    options.Conventions.AuthorizePage("/Index", "Admin");
    options.Conventions.AuthorizeFolder("/Papers", "Admin");
    options.Conventions.AuthorizeFolder("/Audio", "Admin");
    options.Conventions.AuthorizeFolder("/Credentials", "Admin");
    options.Conventions.AuthorizeFolder("/Settings", "Admin");
    options.Conventions.AuthorizeFolder("/Audit", "Admin");
    options.Conventions.AuthorizeFolder("/Submissions", "Editor");
    options.Conventions.AuthorizeFolder("/Editors", "Admin");
    options.Conventions.AuthorizeFolder("/Contributors", "Admin");
    options.Conventions.AuthorizeFolder("/Catalog", "Admin");
    options.Conventions.AuthorizeFolder("/Notifications", "Admin");

    // Contributor area: signed in contributors only, with the public
    // pages in the same folder opened back up.
    options.Conventions.AuthorizeFolder("/Contrib", "Contributor");
    options.Conventions.AllowAnonymousToPage("/Contrib/Register");
    options.Conventions.AllowAnonymousToPage("/Contrib/SignIn");
    options.Conventions.AllowAnonymousToPage("/Contrib/ApplyEditor");

    options.Conventions.AllowAnonymousToPage("/About");
    options.Conventions.AllowAnonymousToPage("/Error");
    options.Conventions.AllowAnonymousToPage("/Health");
    options.Conventions.AllowAnonymousToPage("/Legal");
});

builder.Services.AddAntiforgery(options =>
{
    options.HeaderName = "X-CSRF-TOKEN";
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
});

builder.Services.AddResponseCompression(options =>
{
    options.EnableForHttps = true;
    options.MimeTypes = new[]
    {
        "application/json",
        "text/plain",
        "text/html",
        "text/css",
        "application/javascript",
        "image/svg+xml"
    };
});

// No output cache on the protocol API. Output caching is not auth
// aware, so it could serve a guarded reply to an unauthenticated
// caller. Caching lives in IContentCache, in front of the database,
// which is safe because it sits behind the auth check.

var rateLimit = builder.Configuration
    .GetSection(RateLimitOptions.Section).Get<RateLimitOptions>() ?? new RateLimitOptions();
RateLimitKeys.Configure(rateLimit.MaxTrackedCredentials);

// Always registered. The pages carry [EnableRateLimiting], so the named
// policies must exist even when limiting is turned off; disabled means a
// very high permit count rather than no limiter.
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = (context, ct) =>
    {
        context.HttpContext.Response.Headers.RetryAfter = "60";
        return ValueTask.CompletedTask;
    };

    int Scale(int value) => rateLimit.Enabled ? value : 1_000_000;

    // Callers that present a credential we have already accepted get a
    // higher budget and their own bucket, so one busy class cannot
    // exhaust a shared address. A made up value stays on the address.
    options.AddPolicy("protocol", httpContext =>
        Fixed(RateLimitKeys.ProtocolIdentity(httpContext),
            Scale(rateLimit.AnonymousPerMinute), Scale(rateLimit.CredentialPerMinute)));

    options.AddPolicy("auth", httpContext =>
        Fixed(RateLimitKeys.Address(httpContext),
            Scale(rateLimit.AuthPerMinute), Scale(rateLimit.AuthPerMinute)));

    options.AddPolicy("login", httpContext =>
        Fixed(RateLimitKeys.Address(httpContext),
            Scale(rateLimit.LoginPerMinute), Scale(rateLimit.LoginPerMinute)));

    options.AddPolicy("form", httpContext =>
        Fixed(RateLimitKeys.Address(httpContext),
            Scale(rateLimit.FormPerMinute), Scale(rateLimit.FormPerMinute)));
});

if (serverOptions.TrustProxy)
{
    // Only when a trusted proxy sits in front. This makes the client
    // address and the scheme correct for rate limiting and cookies.
    builder.Services.Configure<ForwardedHeadersOptions>(options =>
    {
        options.ForwardedHeaders =
            ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
        options.KnownNetworks.Clear();
        options.KnownProxies.Clear();
        options.ForwardLimit = 2;
    });
}

var app = builder.Build();

// Warm the store before the first client arrives.
using (var scope = app.Services.CreateScope())
{
    var initializer = scope.ServiceProvider.GetRequiredService<DbInitializer>();
    await initializer.RunAsync();
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

// Security headers on every response. A form page needs Turnstile, so
// the policy allows that one script and frame origin.
if (serverOptions.TrustProxy)
    app.UseForwardedHeaders();

app.Use(async (context, next) =>
{
    var headers = context.Response.Headers;
    headers["X-Content-Type-Options"] = "nosniff";
    headers["X-Frame-Options"] = "SAMEORIGIN";
    headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
    headers["Permissions-Policy"] = "geolocation=(), microphone=(), camera=()";
    headers["Cross-Origin-Opener-Policy"] = "same-origin";

    bool isForm = context.Request.Path.StartsWithSegments("/Contrib")
                  || context.Request.Path.StartsWithSegments("/Account");
    if (isForm && !context.Response.Headers.ContainsKey("Content-Security-Policy"))
    {
        headers["Content-Security-Policy"] =
            "default-src 'self'; "
            + "script-src 'self' https://challenges.cloudflare.com; "
            + "frame-src https://challenges.cloudflare.com; "
            + "style-src 'self' 'unsafe-inline'; "
            + "img-src 'self' data:; "
            + "connect-src 'self' https://challenges.cloudflare.com; "
            + "base-uri 'self'; form-action 'self'; frame-ancestors 'self'";
    }

    await next();
});

app.UseResponseCompression();
app.UseRouting();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.UseStaticFiles();
app.UseStatusCodePagesWithReExecute("/Error");

app.MapProtocolApi();
app.MapRazorPages();

app.Run();

static RateLimitPartition<string> Fixed(string key, int low, int high)
{
    int limit = key.StartsWith("key:", StringComparison.Ordinal) ? high : low;
    return RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions
    {
        PermitLimit = Math.Max(5, limit),
        Window = TimeSpan.FromMinutes(1),
        QueueLimit = 0,
        AutoReplenishment = true
    });
}

/// <summary>Visible for integration tests.</summary>
public partial class Program;
