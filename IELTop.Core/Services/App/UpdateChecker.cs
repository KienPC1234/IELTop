namespace IELTop.Services.App;

/// <summary>
/// Compares a server date against a local file date. Pure logic, used to decide
/// whether a downloaded paper has a newer copy on the server.
/// </summary>
public static class UpdateChecker
{
    public static bool IsNewer(string? serverDate, DateTime localDate)
    {
        if (string.IsNullOrWhiteSpace(serverDate)) return false;
        if (!DateTime.TryParse(serverDate, out var server)) return false;
        return server.Date > localDate.Date;
    }
}
