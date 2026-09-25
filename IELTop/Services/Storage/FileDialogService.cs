namespace IELTop.Services.Storage;

/// <summary>
/// Thin wrapper over the Windows file dialogs so ViewModels stay testable and
/// no UI framework types leak into them.
/// </summary>
public interface IFileDialogService
{
    /// <summary>Returns the picked paths, or an empty list when the user cancels.</summary>
    IReadOnlyList<string> PickFiles(string title, bool multiSelect);

    /// <summary>Returns the picked folder, or an empty string when the user cancels.</summary>
    string PickFolder(string title);
}

public sealed class FileDialogService : IFileDialogService
{
    public IReadOnlyList<string> PickFiles(string title, bool multiSelect)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = title,
            Multiselect = multiSelect,
            Filter = "Study files|*.txt;*.md;*.json;*.csv;*.pdf;*.docx;*.png;*.jpg;*.jpeg;*.webp|Text|*.txt|Markdown|*.md|PDF|*.pdf|Word|*.docx|Images|*.png;*.jpg;*.jpeg;*.webp|All files|*.*",
            CheckFileExists = true
        };

        return dialog.ShowDialog() == true
            ? dialog.FileNames
            : Array.Empty<string>();
    }

    public string PickFolder(string title)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = title,
            Multiselect = false
        };

        return dialog.ShowDialog() == true ? dialog.FolderName : string.Empty;
    }
}
