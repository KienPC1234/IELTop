namespace IELTop.Services.Storage;

/// <summary>
/// How a content server guards its papers.
/// </summary>
public enum ServerAuthMode
{
    Anonymous,
    AccessCode,
    Login
}

/// <summary>
/// One server from the shipped community list (servers.txt).
/// </summary>
public sealed record CommunityServer(string Name, string BaseUrl);

/// <summary>
/// A server the user added. Secrets are stored protected, never plain.
/// </summary>
public sealed class SavedServer
{
    public string Name { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = string.Empty;
    public ServerAuthMode AuthMode { get; set; } = ServerAuthMode.Anonymous;
    public string Username { get; set; } = string.Empty;
    public bool AllowInsecure { get; set; }
    public string ProtectedSecret { get; set; } = string.Empty;
    public string ProtectedToken { get; set; } = string.Empty;
}

/// <summary>
/// Server greeting. Auth lists what the server accepts.
/// </summary>
public sealed class ServerInfo
{
    public string Name { get; set; } = string.Empty;
    public string Protocol { get; set; } = string.Empty;
    public List<string> Auth { get; set; } = new();
    public List<string> Skills { get; set; } = new();
}

/// <summary>
/// One paper listed by a server, without its questions.
/// </summary>
public sealed class RemotePaper
{
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string Level { get; set; } = string.Empty;
    public List<string> Skills { get; set; } = new();
    public int Parts { get; set; }
    public int Questions { get; set; }
    public string Updated { get; set; } = string.Empty;
    public long Size { get; set; }

    public string Summary
    {
        get
        {
            var bits = new List<string>();
            if (Skills.Count > 0) bits.Add(string.Join(", ", Skills));
            if (!string.IsNullOrWhiteSpace(Category)) bits.Add(Category);
            if (!string.IsNullOrWhiteSpace(Level)) bits.Add("band " + Level);
            bits.Add($"{Parts} parts, {Questions} questions");
            if (!string.IsNullOrWhiteSpace(Updated)) bits.Add("updated " + Updated);
            return string.Join(", ", bits);
        }
    }
}
