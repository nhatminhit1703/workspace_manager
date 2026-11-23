using Avalonia.Controls;
using Avalonia.Platform.Storage;
using WorkspaceManager.Core.Settings;

namespace WorkspaceManager.App.Shell;

/// <summary>Dialogs through the Avalonia storage provider of the main window.</summary>
public sealed class AvaloniaDialogService : IDialogService
{
    public Window? Owner { get; set; }

    public async Task<string?> PickFolderAsync(string title, string? startFolder = null)
    {
        if (Owner is null)
        {
            return null;
        }

        var storage = Owner.StorageProvider;
        var options = new FolderPickerOpenOptions { Title = title, AllowMultiple = false };
        if (startFolder is not null && Directory.Exists(startFolder))
        {
            options.SuggestedStartLocation = await storage.TryGetFolderFromPathAsync(startFolder);
        }

        var result = await storage.OpenFolderPickerAsync(options);
        return result.Count > 0 ? result[0].TryGetLocalPath() : null;
    }
}
