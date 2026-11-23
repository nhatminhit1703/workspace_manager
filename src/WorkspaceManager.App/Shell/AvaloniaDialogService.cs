using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
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

    public async Task<bool> ConfirmAsync(string title, string message)
    {
        if (Owner is null)
        {
            return false;
        }

        var ok = new Button { Content = "OK", IsDefault = true, MinWidth = 90, HorizontalContentAlignment = HorizontalAlignment.Center };
        ok.Classes.Add("accent");
        var cancel = new Button { Content = "Cancel", IsCancel = true, MinWidth = 90, HorizontalContentAlignment = HorizontalAlignment.Center };
        var dialog = new Window
        {
            Title = title,
            SizeToContent = SizeToContent.WidthAndHeight,
            MaxWidth = 720,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new StackPanel
            {
                Margin = new Thickness(20),
                Spacing = 16,
                Children =
                {
                    new SelectableTextBlock { Text = message, TextWrapping = Avalonia.Media.TextWrapping.Wrap, MaxWidth = 660 },
                    new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, Children = { ok, cancel } },
                },
            },
        };
        ok.Click += (_, _) => dialog.Close(true);
        cancel.Click += (_, _) => dialog.Close(false);
        return await dialog.ShowDialog<bool?>(Owner) == true;
    }
}
