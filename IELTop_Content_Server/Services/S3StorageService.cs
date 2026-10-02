using System.Net;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Amazon.S3.Util;
using IELTop_Content_Server.Options;
using Microsoft.Extensions.Options;

namespace IELTop_Content_Server.Services;

public interface IS3StorageService
{
    bool Enabled { get; }
    string BucketName { get; }
    Task<bool> PingAsync(CancellationToken ct = default);
    Task EnsureBucketCreatedAsync(CancellationToken ct = default);
    Task<(bool Ok, string Error)> UploadAsync(string key, Stream data, string contentType, CancellationToken ct = default);
    Task<(bool Ok, string Error, Stream? Stream, string ContentType, long ContentLength)> OpenReadAsync(string key, CancellationToken ct = default);
    Task<bool> DownloadToFileAsync(string key, string destinationFilePath, CancellationToken ct = default);
    Task<bool> ExistsAsync(string key, CancellationToken ct = default);
    Task<bool> DeleteAsync(string key, CancellationToken ct = default);
    Task<List<string>> ListKeysAsync(string prefix = "", CancellationToken ct = default);
    string GetPublicUrl(string key);
}

public sealed class S3StorageService : IS3StorageService, IDisposable
{
    private readonly S3Options _options;
    private readonly ILogger<S3StorageService> _logger;
    private readonly IAmazonS3? _client;

    public bool Enabled => _options.Enabled && _client is not null;
    public string BucketName => _options.BucketName;

    public S3StorageService(IOptions<S3Options> options, ILogger<S3StorageService> logger)
    {
        _options = options.Value;
        _logger = logger;

        if (!_options.Enabled || string.IsNullOrWhiteSpace(_options.ServiceUrl))
        {
            _client = null;
            return;
        }

        try
        {
            var config = new AmazonS3Config
            {
                ServiceURL = _options.ServiceUrl,
                ForcePathStyle = _options.ForcePathStyle,
                AuthenticationRegion = string.IsNullOrWhiteSpace(_options.Region) ? "us-east-1" : _options.Region,
                UseHttp = _options.ServiceUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            };

            var credentials = new BasicAWSCredentials(
                _options.AccessKey ?? string.Empty,
                _options.SecretKey ?? string.Empty);

            _client = new AmazonS3Client(credentials, config);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize S3 storage client for {Url}", _options.ServiceUrl);
            _client = null;
        }
    }

    public async Task<bool> PingAsync(CancellationToken ct = default)
    {
        if (_client is null)
            return false;

        try
        {
            var response = await _client.ListBucketsAsync(ct);
            return response.HttpStatusCode == HttpStatusCode.OK;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "S3 ping failed for bucket {Bucket}", BucketName);
            return false;
        }
    }

    public async Task EnsureBucketCreatedAsync(CancellationToken ct = default)
    {
        if (_client is null || string.IsNullOrWhiteSpace(BucketName))
            return;

        try
        {
            bool exists = await AmazonS3Util.DoesS3BucketExistV2Async(_client, BucketName);
            if (!exists)
            {
                var putRequest = new PutBucketRequest
                {
                    BucketName = BucketName,
                    UseClientRegion = true
                };
                await _client.PutBucketAsync(putRequest, ct);
                _logger.LogInformation("Created S3 bucket {Bucket}", BucketName);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not verify or create bucket {Bucket}", BucketName);
        }
    }

    public async Task<(bool Ok, string Error)> UploadAsync(
        string key, Stream data, string contentType, CancellationToken ct = default)
    {
        if (_client is null)
            return (false, "S3 storage is disabled.");

        try
        {
            string normalizedKey = NormalizeKey(key);
            var request = new PutObjectRequest
            {
                BucketName = BucketName,
                Key = normalizedKey,
                InputStream = data,
                ContentType = string.IsNullOrWhiteSpace(contentType) ? "application/octet-stream" : contentType,
                AutoCloseStream = false
            };

            var response = await _client.PutObjectAsync(request, ct);
            return response.HttpStatusCode is HttpStatusCode.OK or HttpStatusCode.NoContent
                ? (true, string.Empty)
                : (false, $"S3 returned status {(int)response.HttpStatusCode}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to upload object {Key} to S3", key);
            return (false, ex.Message);
        }
    }

    public async Task<(bool Ok, string Error, Stream? Stream, string ContentType, long ContentLength)> OpenReadAsync(
        string key, CancellationToken ct = default)
    {
        if (_client is null)
            return (false, "S3 storage is disabled.", null, string.Empty, 0);

        try
        {
            string normalizedKey = NormalizeKey(key);
            var response = await _client.GetObjectAsync(BucketName, normalizedKey, ct);
            return (true, string.Empty, response.ResponseStream, response.Headers.ContentType, response.ContentLength);
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return (false, "Object not found.", null, string.Empty, 0);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to read object {Key} from S3", key);
            return (false, ex.Message, null, string.Empty, 0);
        }
    }

    public async Task<bool> DownloadToFileAsync(string key, string destinationFilePath, CancellationToken ct = default)
    {
        if (_client is null)
            return false;

        try
        {
            string normalizedKey = NormalizeKey(key);
            var response = await _client.GetObjectAsync(BucketName, normalizedKey, ct);
            string? directory = Path.GetDirectoryName(destinationFilePath);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            await using var fileStream = File.Create(destinationFilePath);
            await response.ResponseStream.CopyToAsync(fileStream, ct);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to download S3 object {Key} to {Path}", key, destinationFilePath);
            return false;
        }
    }

    public async Task<bool> ExistsAsync(string key, CancellationToken ct = default)
    {
        if (_client is null)
            return false;

        try
        {
            string normalizedKey = NormalizeKey(key);
            await _client.GetObjectMetadataAsync(BucketName, normalizedKey, ct);
            return true;
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error checking existence of S3 key {Key}", key);
            return false;
        }
    }

    public async Task<bool> DeleteAsync(string key, CancellationToken ct = default)
    {
        if (_client is null)
            return false;

        try
        {
            string normalizedKey = NormalizeKey(key);
            var response = await _client.DeleteObjectAsync(BucketName, normalizedKey, ct);
            return response.HttpStatusCode is HttpStatusCode.OK or HttpStatusCode.NoContent;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete S3 object {Key}", key);
            return false;
        }
    }

    public async Task<List<string>> ListKeysAsync(string prefix = "", CancellationToken ct = default)
    {
        if (_client is null)
            return new List<string>();

        try
        {
            var results = new List<string>();
            var request = new ListObjectsV2Request
            {
                BucketName = BucketName,
                Prefix = NormalizeKey(prefix)
            };

            ListObjectsV2Response response;
            do
            {
                response = await _client.ListObjectsV2Async(request, ct);
                foreach (var entry in response.S3Objects)
                {
                    results.Add(entry.Key);
                }
                request.ContinuationToken = response.NextContinuationToken;
            } while (response.IsTruncated == true);

            return results;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to list keys from S3 for prefix '{Prefix}'", prefix);
            return new List<string>();
        }
    }

    public string GetPublicUrl(string key)
    {
        string baseUri = !string.IsNullOrWhiteSpace(_options.PublicUrl)
            ? _options.PublicUrl.TrimEnd('/')
            : _options.ServiceUrl.TrimEnd('/');

        string normalizedKey = NormalizeKey(key);
        return $"{baseUri}/{BucketName}/{normalizedKey}";
    }

    private static string NormalizeKey(string key) =>
        key.Trim().Replace('\\', '/').TrimStart('/');

    public void Dispose()
    {
        _client?.Dispose();
    }
}
