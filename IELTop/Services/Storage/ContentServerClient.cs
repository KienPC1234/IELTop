using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using IELTop.Models;

namespace IELTop.Services.Storage;

/// <summary>
/// Talks to IELTop content servers (protocol ieltop/1). Every call is
/// async with a cancellation token and returns short English errors.
/// </summary>
public interface IContentServerClient
{
    Task<ServerResult<ServerInfo>> InfoAsync(string baseUrl, ServerCredential credential, CancellationToken ct = default);
    Task<ServerResult<List<RemotePaper>>> ListPapersAsync(string baseUrl, ServerCredential credential, string? skill = null, CancellationToken ct = default);
    Task<ServerResult<ExamPaper>> DownloadPaperAsync(string baseUrl, string id, ServerCredential credential, CancellationToken ct = default);
    Task<ServerResult<byte[]>> DownloadAudioAsync(string baseUrl, string file, ServerCredential credential, CancellationToken ct = default);
    Task<ServerResult<string>> LoginAsync(string baseUrl, ServerCredential credential, string username, string password, CancellationToken ct = default);
}

/// <summary>
/// What to send with a request. Token wins over code. AllowInsecure skips
/// certificate checks for self signed https servers the user trusts.
/// </summary>
public sealed record ServerCredential(
    string AccessCode = "",
    string Token = "",
    bool AllowInsecure = false);

public sealed record ServerResult<T>(bool Success, T? Value, string Error)
{
    public static ServerResult<T> Ok(T value) => new(true, value, string.Empty);
    public static ServerResult<T> Fail(string error) => new(false, default, error);
}

public sealed class ContentServerClient : IContentServerClient, IDisposable
{
    private const string Protocol = "ieltop/1";
    private readonly HttpClient _http;
    private readonly HttpClient _insecure;
    private bool _disposed;

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip
    };

    public ContentServerClient()
    {
        _http = NewClient(allowInsecure: false);
        _insecure = NewClient(allowInsecure: true);
    }

    private static HttpClient NewClient(bool allowInsecure)
    {
        HttpMessageHandler handler = new HttpClientHandler();
        if (allowInsecure)
        {
            var insecure = new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = (_, _, _, _) => true
            };
            handler = insecure;
        }
        var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("IELTop/1");
        return client;
    }

    private HttpClient For(ServerCredential credential) =>
        credential.AllowInsecure ? _insecure : _http;

    public async Task<ServerResult<ServerInfo>> InfoAsync(string baseUrl, ServerCredential credential, CancellationToken ct = default)
    {
        var result = await GetAsync<ServerInfo>(baseUrl, "api/info", credential, ct);
        if (!result.Success || result.Value is null)
            return result;
        if (!string.Equals(result.Value.Protocol, Protocol, StringComparison.OrdinalIgnoreCase))
            return ServerResult<ServerInfo>.Fail("This address is not an IELTop content server.");
        return result;
    }

    public Task<ServerResult<List<RemotePaper>>> ListPapersAsync(string baseUrl, ServerCredential credential, string? skill = null, CancellationToken ct = default)
    {
        var path = string.IsNullOrWhiteSpace(skill)
            ? "api/papers"
            : $"api/papers?skill={Uri.EscapeDataString(skill)}";
        return GetAsync<List<RemotePaper>>(baseUrl, path, credential, ct);
    }

    public async Task<ServerResult<ExamPaper>> DownloadPaperAsync(
        string baseUrl, string id, ServerCredential credential, CancellationToken ct = default)
    {
        var result = await GetAsync<ExamPaper>(
            baseUrl, $"api/papers/{Uri.EscapeDataString(id)}", credential, ct);
        if (!result.Success || result.Value is null)
            return ServerResult<ExamPaper>.Fail(result.Error);
        if (result.Value.Parts.Count == 0)
            return ServerResult<ExamPaper>.Fail("The paper has no parts.");
        return result;
    }

    public async Task<ServerResult<byte[]>> DownloadAudioAsync(
        string baseUrl, string file, ServerCredential credential, CancellationToken ct = default)
    {
        var request = BuildRequest(baseUrl, $"api/audio/{Uri.EscapeDataString(file)}", credential);
        try
        {
            using var response = await For(credential).SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
                return ServerResult<byte[]>.Fail(DescribeStatus(response.StatusCode));
            var bytes = await response.Content.ReadAsByteArrayAsync(ct);
            if (bytes.Length == 0)
                return ServerResult<byte[]>.Fail("The audio file is empty.");
            return ServerResult<byte[]>.Ok(bytes);
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            return ServerResult<byte[]>.Fail("The server took too long. Try again.");
        }
        catch (HttpRequestException)
        {
            return ServerResult<byte[]>.Fail("Could not reach the server. Check the address.");
        }
        catch (OperationCanceledException)
        {
            return ServerResult<byte[]>.Fail("Download stopped.");
        }
    }

    public async Task<ServerResult<string>> LoginAsync(
        string baseUrl, ServerCredential credential, string username, string password, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
            return ServerResult<string>.Fail("Enter a username and a password.");

        var url = $"{baseUrl.TrimEnd('/')}/api/login";
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(
                    JsonSerializer.Serialize(new { username, password }),
                    Encoding.UTF8, "application/json")
            };
            using var response = await For(credential).SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
                return ServerResult<string>.Fail(DescribeStatus(response.StatusCode));
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            if (doc.RootElement.TryGetProperty("token", out var token)
                && token.GetString() is { Length: > 0 } value)
                return ServerResult<string>.Ok(value);
            return ServerResult<string>.Fail("The server reply could not be read.");
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            return ServerResult<string>.Fail("The server took too long. Try again.");
        }
        catch (HttpRequestException)
        {
            return ServerResult<string>.Fail("Could not reach the server. Check the address.");
        }
        catch (OperationCanceledException)
        {
            return ServerResult<string>.Fail("Login stopped.");
        }
        catch (JsonException)
        {
            return ServerResult<string>.Fail("The server reply could not be read.");
        }
    }

    private async Task<ServerResult<T>> GetAsync<T>(
        string baseUrl, string path, ServerCredential credential, CancellationToken ct)
    {
        var request = BuildRequest(baseUrl, path, credential);
        try
        {
            using var response = await For(credential).SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
                return ServerResult<T>.Fail(DescribeStatus(response.StatusCode));
            var body = await response.Content.ReadAsStringAsync(ct);
            var value = JsonSerializer.Deserialize<T>(body, Json);
            if (value is null)
                return ServerResult<T>.Fail("The server reply could not be read.");
            return ServerResult<T>.Ok(value);
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            return ServerResult<T>.Fail("The server took too long. Try again.");
        }
        catch (HttpRequestException)
        {
            return ServerResult<T>.Fail("Could not reach the server. Check the address.");
        }
        catch (OperationCanceledException)
        {
            return ServerResult<T>.Fail("Stopped.");
        }
        catch (JsonException)
        {
            return ServerResult<T>.Fail("The server reply could not be read.");
        }
    }

    private static HttpRequestMessage BuildRequest(string baseUrl, string path, ServerCredential credential)
    {
        var request = new HttpRequestMessage(
            HttpMethod.Get, $"{baseUrl.TrimEnd('/')}/{path}");
        if (!string.IsNullOrWhiteSpace(credential.Token))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", credential.Token);
        else if (!string.IsNullOrWhiteSpace(credential.AccessCode))
            request.Headers.Add("X-Access-Code", credential.AccessCode);
        return request;
    }

    private static string DescribeStatus(System.Net.HttpStatusCode status) => status switch
    {
        System.Net.HttpStatusCode.Unauthorized => "Access denied. Check the code or login.",
        System.Net.HttpStatusCode.Forbidden => "Access denied. Check the code or login.",
        System.Net.HttpStatusCode.NotFound => "Not found on this server.",
        _ => $"The server returned an error ({(int)status})."
    };

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _http.Dispose();
        _insecure.Dispose();
    }
}
