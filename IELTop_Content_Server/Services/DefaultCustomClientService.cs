namespace IELTop_Content_Server.Services;

/// <summary>
/// Bản mặc định open source: trả về thông tin chung, không branding trung tâm.
/// </summary>
public sealed class DefaultCustomClientService : ICustomClientService
{
    public Task<ClientProfile> GetProfileAsync(CancellationToken ct = default)
    {
        var profile = new ClientProfile
        {
            ServerName = "IELTop Content Server",
            IsExclusive = false,
            SupportUrl = "https://github.com/KienPC1234/IELTop"
        };
        return Task.FromResult(profile);
    }
}
