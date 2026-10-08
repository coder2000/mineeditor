using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using MineEditor.ViewModels;
using MineEditor.Views;
using Ubiety.Nbt;
using Windows.Storage.Pickers;

namespace MineEditor;

/// <summary>The app window: navigation, saving and app-wide messages.</summary>
public sealed partial class MainWindow : Window
{
    private static readonly Dictionary<string, Type> Pages = new()
    {
        ["Home"] = typeof(HomePage),
        ["World"] = typeof(WorldPage),
        ["Players"] = typeof(PlayersPage),
        ["Map"] = typeof(MapPage),
        ["Editor"] = typeof(EditorPage),
    };

    private bool _closeConfirmed;

    public MainWindow()
    {
        InitializeComponent();
        AppWindow.Resize(new Windows.Graphics.SizeInt32(1400, 900));
        AppWindow.Closing += OnClosing;
        Shell.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ShellViewModel.Title))
            {
                Title = Shell.Title;
            }
        };
        Shell.OpenInEditorRequested += (document, path) =>
        {
            NavigateTo("Editor");
            if (ContentFrame.Content is EditorPage editor)
            {
                editor.Open(document, path);
            }
        };

        Nav.SelectedItem = Nav.MenuItems[0];
    }

    public ShellViewModel Shell => App.Shell;

    /// <summary>Switches to a page by its tag: Home, World, Players, Map or Editor.</summary>
    public void NavigateTo(string tag)
    {
        var item = Nav.MenuItems.OfType<NavigationViewItem>().First(i => (string)i.Tag == tag);
        if (!ReferenceEquals(Nav.SelectedItem, item))
        {
            Nav.SelectedItem = item;
        }
    }

    /// <summary>Opens a world after checking for unsaved changes, then shows its overview.</summary>
    public async Task OpenWorldAsync(string path)
    {
        if (!await ConfirmDiscardAsync())
        {
            return;
        }

        try
        {
            await Shell.OpenWorldAsync(path);
            HideStatus();
            NavigateTo("World");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or NbtFormatException or InvalidDataException)
        {
            ShowStatus(InfoBarSeverity.Error, "Couldn't open the world", e.Message);
        }
    }

    /// <summary>Asks for a world folder.</summary>
    public async Task<string?> PickFolderAsync()
    {
        var picker = new FolderPicker { SuggestedStartLocation = PickerLocationId.ComputerFolder };
        picker.FileTypeFilter.Add("*");
        WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(this));
        return (await picker.PickSingleFolderAsync())?.Path;
    }

    /// <summary>Asks for an NBT file.</summary>
    public async Task<string?> PickNbtFileAsync()
    {
        var picker = new FileOpenPicker { SuggestedStartLocation = PickerLocationId.ComputerFolder };
        foreach (var type in new[] { ".dat", ".dat_old", ".nbt", ".schematic", ".litematic", "*" })
        {
            picker.FileTypeFilter.Add(type);
        }

        WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(this));
        return (await picker.PickSingleFileAsync())?.Path;
    }

    public void ShowStatus(InfoBarSeverity severity, string title, string message)
    {
        StatusBar.Severity = severity;
        StatusBar.Title = title;
        StatusBar.Message = message;
        StatusBar.IsOpen = true;
    }

    public void HideStatus() => StatusBar.IsOpen = false;

    /// <summary>Shows a dialog with a primary action and Cancel.</summary>
    public async Task<ContentDialogResult> ShowDialogAsync(string title, string content, string primary, string? secondary = null, bool destructive = false)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = Content.XamlRoot,
            Title = title,
            Content = new TextBlock { Text = content, TextWrapping = TextWrapping.Wrap },
            PrimaryButtonText = primary,
            SecondaryButtonText = secondary ?? string.Empty,
            CloseButtonText = "Cancel",
            DefaultButton = destructive ? ContentDialogButton.Close : ContentDialogButton.Primary,
        };
        return await dialog.ShowAsync();
    }

    /// <summary>Saves all changes, warning first if the game has the world open.</summary>
    /// <returns>True if everything was saved.</returns>
    public async Task<bool> SaveAllAsync()
    {
        if (!Shell.HasUnsavedChanges)
        {
            return true;
        }

        if (Shell.RefreshInUse())
        {
            var answer = await ShowDialogAsync(
                "Minecraft has this world open",
                "Quit the world in Minecraft before saving. If you save now, the game may overwrite your changes when it next saves, or the world could be corrupted.",
                "Save anyway",
                destructive: true);
            if (answer != ContentDialogResult.Primary)
            {
                return false;
            }
        }

        var result = await Shell.SaveAllAsync();
        if (result.Failures.Count > 0)
        {
            var details = string.Join(Environment.NewLine, result.Failures.Select(f => $"{f.Document.Title}: {f.Error.Message}"));
            ShowStatus(InfoBarSeverity.Error, $"{result.Failures.Count} file(s) weren't saved", details);
            return false;
        }

        var backup = result.BackupPath is null ? string.Empty : $" The originals were backed up to {result.BackupPath}.";
        ShowStatus(InfoBarSeverity.Success, $"Saved {result.Saved} file(s)", backup.Trim());
        return true;
    }

    /// <summary>If there are unsaved changes, asks whether to save or discard them.</summary>
    /// <returns>True if it's fine to continue.</returns>
    public async Task<bool> ConfirmDiscardAsync()
    {
        if (!Shell.HasUnsavedChanges)
        {
            return true;
        }

        var changed = string.Join(", ", Shell.Documents.Where(d => d.IsDirty).Select(d => d.Title));
        var answer = await ShowDialogAsync("Save your changes?", $"These have unsaved changes: {changed}.", "Save", "Don't save");
        return answer switch
        {
            ContentDialogResult.Primary => await SaveAllAsync(),
            ContentDialogResult.Secondary => true,
            _ => false,
        };
    }

    private void OnNavigationSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItem is NavigationViewItem { Tag: string tag } && Pages.TryGetValue(tag, out var page)
            && ContentFrame.CurrentSourcePageType != page)
        {
            ContentFrame.Navigate(page);
        }
    }

    private async void OnSaveClick(object sender, RoutedEventArgs e) => await SaveAllAsync();

    private void OnCheckInUseClick(object sender, RoutedEventArgs e) => Shell.RefreshInUse();

    private async void OnClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (_closeConfirmed || !Shell.HasUnsavedChanges)
        {
            return;
        }

        args.Cancel = true;
        if (await ConfirmDiscardAsync())
        {
            _closeConfirmed = true;
            Close();
        }
    }
}
