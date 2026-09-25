using System.IO;
using System.Text.Json;
using IELTop.Models;

namespace IELTop.Services.Storage;

/// <summary>
/// Loads reading, listening and writing practice content.
/// Content is read from two places: the user's own folder, so imports survive
/// app updates, and the shipped Assets folder for the sample material.
/// </summary>
public interface IPracticeRepository
{
    IReadOnlyList<ReadingPassage> LoadReading();
    IReadOnlyList<ListeningItem> LoadListening();
    IReadOnlyList<WritingTask> LoadWriting();
    string AudioDir { get; }
    string ReadingDir { get; }
    string ListeningDir { get; }
    string WritingDir { get; }
}

public sealed class PracticeRepository : IPracticeRepository
{
    private readonly JsonSerializerOptions _options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip
    };

    // Shipped samples live next to the app and are replaced on update.
    private static string ShippedDir => Path.Combine(AppContext.BaseDirectory, "Assets");

    // User content lives in the profile and is never touched by an update.
    private static string UserDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "IELTop", "content");

    public string ReadingDir => Path.Combine(UserDir, "Reading");
    public string ListeningDir => Path.Combine(UserDir, "Listening");
    public string WritingDir => Path.Combine(UserDir, "Writing");
    public string AudioDir => Path.Combine(ShippedDir, "Audio");

    public IReadOnlyList<ReadingPassage> LoadReading()
        => LoadList<ReadingPassage>(ReadingDir, Path.Combine(ShippedDir, "Reading"), p => p.Id);

    public IReadOnlyList<ListeningItem> LoadListening()
        => LoadList<ListeningItem>(ListeningDir, Path.Combine(ShippedDir, "Listening"), p => p.Id);

    public IReadOnlyList<WritingTask> LoadWriting()
        => LoadList<WritingTask>(WritingDir, Path.Combine(ShippedDir, "Writing"), p => p.Id);

    /// <summary>
    /// Reads JSON from the user folder and the shipped folder. A user item with
    /// the same id wins, so a user can correct a sample without editing it.
    /// </summary>
    private List<T> LoadList<T>(string userFolder, string shippedFolder, Func<T, string> keySelector)
    {
        var byId = new Dictionary<string, T>(StringComparer.OrdinalIgnoreCase);

        Directory.CreateDirectory(userFolder);
        ReadFolder(shippedFolder, byId, keySelector);
        ReadFolder(userFolder, byId, keySelector);

        return byId.Values
            .OrderBy(keySelector, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private void ReadFolder<T>(string folder, Dictionary<string, T> into, Func<T, string> keySelector)
    {
        // The shipped folder may sit under Program Files and may not exist. Never create it.
        if (!Directory.Exists(folder)) return;

        foreach (var file in Directory.EnumerateFiles(folder, "*.json"))
        {
            try
            {
                var item = JsonSerializer.Deserialize<T>(File.ReadAllText(file), _options);
                if (item is null) continue;
                into[keySelector(item)] = item;
            }
            catch (JsonException)
            {
                // One malformed file must not stop the rest from loading.
            }
        }
    }
}
