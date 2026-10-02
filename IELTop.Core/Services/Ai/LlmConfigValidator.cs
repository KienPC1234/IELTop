namespace IELTop.Services.Ai;

using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>One problem found while checking the language model settings.</summary>
public sealed record ConfigIssue(string Field, string Message, bool IsError);

/// <summary>Outcome of checking the settings before a call is attempted.</summary>
public sealed record LlmConfigCheck(bool IsValid, IReadOnlyList<ConfigIssue> Issues)
{
    public IEnumerable<ConfigIssue> Errors => Issues.Where(i => i.IsError);
    public IEnumerable<ConfigIssue> Warnings => Issues.Where(i => !i.IsError);
}

/// <summary>
/// Checks language model settings without calling the network, so the UI can
/// tell the user what is wrong before a request fails. Format checks live here;
/// reachability is a separate live test.
/// </summary>
public static class LlmConfigValidator
{
    public static LlmConfigCheck Validate(string baseUrl, string model, string apiKey)
    {
        var issues = new List<ConfigIssue>();

        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            issues.Add(new ConfigIssue("Base URL", "Enter the server address, for example https://api.openai.com/v1.", true));
        }
        else if (!Uri.TryCreate(baseUrl.Trim(), UriKind.Absolute, out var uri))
        {
            issues.Add(new ConfigIssue("Base URL", "That address is not a valid URL. It must start with http:// or https://.", true));
        }
        else
        {
            if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
                issues.Add(new ConfigIssue("Base URL", "Use http or https.", true));

            // OpenAI compatible servers expect the version segment in the base URL.
            if (!uri.AbsolutePath.TrimEnd('/').EndsWith("/v1", StringComparison.OrdinalIgnoreCase))
                issues.Add(new ConfigIssue("Base URL",
                    "Most servers expect the address to end with /v1, for example http://localhost:11434/v1.", false));

            if (uri.Scheme == Uri.UriSchemeHttp && !IsLocalHost(uri))
                issues.Add(new ConfigIssue("Base URL",
                    "This uses plain http on a remote host. The API key would be sent unencrypted.", false));
        }

        if (string.IsNullOrWhiteSpace(model))
            issues.Add(new ConfigIssue("Model", "Enter the model name exactly as the server lists it.", true));

        // A local server usually has no key. A remote server almost always needs one.
        var isRemote = Uri.TryCreate(baseUrl?.Trim(), UriKind.Absolute, out var parsed)
                       && parsed.Scheme == Uri.UriSchemeHttps
                       && !IsLocalHost(parsed);

        if (isRemote && string.IsNullOrWhiteSpace(apiKey))
            issues.Add(new ConfigIssue("API key",
                "This looks like a remote server. Add the API key or requests will be rejected.", false));

        var isError = issues.Any(i => i.IsError);
        return new LlmConfigCheck(!isError, issues);
    }

    private static bool IsLocalHost(Uri uri)
        => uri.IsLoopback
           || string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase)
           || uri.Host.StartsWith("192.168.", StringComparison.Ordinal)
           || uri.Host.StartsWith("10.", StringComparison.Ordinal)
           || uri.Host.StartsWith("127.", StringComparison.Ordinal);
}
