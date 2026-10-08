using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using MineEditor.Core.World;
using Ubiety.Nbt;

namespace MineEditor.Views;

/// <summary>A world in the home page list.</summary>
public sealed partial class WorldCard(WorldSummary summary) : ObservableObject
{
    public WorldSummary Summary { get; } = summary;

    public string Details => Summary.LastPlayed is { } played
        ? $"{Summary.Version} · played {played.LocalDateTime.ToString("g", CultureInfo.CurrentCulture)}"
        : Summary.Version;

    [ObservableProperty]
    public partial ImageSource? Icon { get; private set; }

    public async Task LoadIconAsync()
    {
        if (Summary.IconPath is null)
        {
            return;
        }

        try
        {
            var bitmap = new BitmapImage();
            using var stream = File.OpenRead(Summary.IconPath);
            await bitmap.SetSourceAsync(stream.AsRandomAccessStream());
            Icon = bitmap;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Leave the placeholder.
        }
    }
}

/// <summary>Lists the launcher's worlds and opens worlds or files.</summary>
public sealed partial class HomePage : Page
{
    public HomePage()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnLoaded;
        var cards = (await Task.Run(() => SaveLocator.FindWorlds())).Select(w => new WorldCard(w)).ToList();
        LoadingRing.IsActive = false;
        LoadingRing.Visibility = Visibility.Collapsed;
        EmptyText.Visibility = cards.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        Worlds.ItemsSource = cards;
        foreach (var card in cards)
        {
            await card.LoadIconAsync();
        }
    }

    private async void OnWorldClick(object sender, ItemClickEventArgs e) =>
        await App.MainWindow.OpenWorldAsync(((WorldCard)e.ClickedItem).Summary.Path);

    private async void OnOpenFolderClick(object sender, RoutedEventArgs e)
    {
        if (await App.MainWindow.PickFolderAsync() is { } path)
        {
            await App.MainWindow.OpenWorldAsync(path);
        }
    }

    private async void OnOpenFileClick(object sender, RoutedEventArgs e)
    {
        if (await App.MainWindow.PickNbtFileAsync() is not { } path)
        {
            return;
        }

        try
        {
            var doc = await App.Shell.OpenFileAsync(path);
            App.Shell.RequestOpenInEditor(doc);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NbtFormatException or InvalidDataException)
        {
            App.MainWindow.ShowStatus(InfoBarSeverity.Error, "Couldn't open the file", ex.Message);
        }
    }
}
