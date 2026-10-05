using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace Rdw.Api;

/// <summary>
/// Per-user and global rate limits (ADR-005). Uses the built-in ASP.NET Core rate limiting
/// middleware. It must run after authentication and authorization, so requests answered with
/// 401 or 403 never reach it and use no quota.
/// </summary>
public static class RateLimiting
{
    public static IServiceCollection AddApiRateLimiting(this IServiceCollection services)
    {
        services.AddOptions<RateLimitingOptions>()
            .BindConfiguration(RateLimitingOptions.SectionName)
            // One rule per setting, so the error names the key that is wrong (never its value).
            .Validate(options => options.PerUser.PermitLimit > 0, MustBePositive("PerUser:PermitLimit"))
            .Validate(options => options.PerUser.WindowSeconds > 0, MustBePositive("PerUser:WindowSeconds"))
            .Validate(options => options.Global.PermitLimit > 0, MustBePositive("Global:PermitLimit"))
            .Validate(options => options.Global.WindowSeconds > 0, MustBePositive("Global:WindowSeconds"))
            .ValidateOnStart();

        services.AddRateLimiter(options =>
        {
            // Per user first, then global. A time-based limiter does not give a permit back when a
            // later limiter in the chain rejects the request. In this order a user who is over their
            // own limit cannot use up the global quota, which protects the RDW. The price: while the
            // global limit is reached, a user's retries still count against their own limit, so they
            // may wait up to one extra per-user window after the global window resets.
            options.GlobalLimiter = PartitionedRateLimiter.CreateChained(
                PartitionedRateLimiter.Create<HttpContext, string>(context =>
                    RateLimitPartition.GetFixedWindowLimiter(
                        $"user:{Subject(context)}",
                        _ => WindowOptions(Settings(context).PerUser))),
                PartitionedRateLimiter.Create<HttpContext, string>(context =>
                    RateLimitPartition.GetFixedWindowLimiter(
                        "global",
                        _ => WindowOptions(Settings(context).Global))));

            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = OnRejectedAsync;
        });

        return services;
    }

    private static string MustBePositive(string key) =>
        $"{RateLimitingOptions.SectionName}:{key} must be greater than zero.";

    // Only the Google sub claim identifies a user here: never the email, IP address or a raw header.
    // Every request that gets this far is authenticated; a token without sub (Google always sends
    // one) would share a single bucket rather than escape the per-user limit.
    private static string Subject(HttpContext context) => context.User.FindFirst("sub")?.Value ?? string.Empty;

    private static RateLimitingOptions Settings(HttpContext context) =>
        context.RequestServices.GetRequiredService<IOptions<RateLimitingOptions>>().Value;

    private static FixedWindowRateLimiterOptions WindowOptions(LimitOptions limit) => new()
    {
        PermitLimit = limit.PermitLimit,
        Window = TimeSpan.FromSeconds(limit.WindowSeconds),
        QueueLimit = 0,
        AutoReplenishment = true,
    };

    private static async ValueTask OnRejectedAsync(OnRejectedContext context, CancellationToken cancellationToken)
    {
        var httpContext = context.HttpContext;
        var settings = Settings(httpContext);

        // The fixed-window limiter reports its full window length, not the time left, so this is
        // an upper bound. If no value is reported, the longest window is always long enough.
        var retryAfter = context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var wait)
            ? wait
            : TimeSpan.FromSeconds(Math.Max(settings.PerUser.WindowSeconds, settings.Global.WindowSeconds));
        var retryAfterSeconds = Math.Max(1, (int)Math.Ceiling(retryAfter.TotalSeconds));
        httpContext.Response.Headers.RetryAfter = retryAfterSeconds.ToString(CultureInfo.InvariantCulture);

        // Logged per user identity (ADR-005); never the plate or the request path.
        httpContext.RequestServices.GetRequiredService<ILoggerFactory>()
            .CreateLogger(typeof(RateLimiting).FullName!)
            .LogWarning("Rate limit reached for user {Subject}.", Subject(httpContext));

        await TypedResults.Problem(
                title: "Too many requests",
                detail: $"The rate limit was reached. Try again in {retryAfterSeconds} seconds.",
                statusCode: StatusCodes.Status429TooManyRequests)
            .ExecuteAsync(httpContext);
    }
}

/// <summary>
/// Rate limit settings. The defaults are conservative guesses, not based on RDW limits: no official
/// RDW limits were found (ADR-005). To be verified before the euromaster.com domain is enabled.
/// </summary>
public sealed class RateLimitingOptions
{
    public const string SectionName = "RateLimiting";

    /// <summary>Requests per user (Google sub) per window. Default: 10 per 60 seconds.</summary>
    public LimitOptions PerUser { get; set; } = new() { PermitLimit = 10, WindowSeconds = 60 };

    /// <summary>Requests for all users together per window. Default: 60 per 60 seconds.</summary>
    public LimitOptions Global { get; set; } = new() { PermitLimit = 60, WindowSeconds = 60 };
}

public sealed class LimitOptions
{
    public int PermitLimit { get; set; }

    public int WindowSeconds { get; set; }
}
