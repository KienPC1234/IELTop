namespace IELTop_Content_Server.Services;

/// <summary>
/// Cung cấp profile cấu hình cho client kết nối (branding, logo, server lock).
/// Bản mặc định trả về thông tin open source chuẩn.
/// Bản Center ghi đè với branding riêng của từng trung tâm.
/// </summary>
public interface ICustomClientService
{
    Task<ClientProfile> GetProfileAsync(CancellationToken ct = default);
}

public sealed record ClientProfile
{
    public string ServerName { get; init; } = "IELTop Content Server";
    public string? LogoUrl { get; init; }
    public string? BrandColor { get; init; }
    public bool IsExclusive { get; init; }
    public string? SupportUrl { get; init; }
    public Dictionary<string, string> CustomLinks { get; init; } = new();
}
