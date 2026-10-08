using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using MineEditor.Core.Documents;
using MineEditor.Core.Nbt;
using MineEditor.ViewModels;
using Ubiety.Nbt;
using Windows.ApplicationModel.DataTransfer;
using Windows.System;

namespace MineEditor.Views;

/// <summary>A tree editor for any loaded NBT document.</summary>
public sealed partial class EditorPage : Page
{
    public EditorPage()
    {
        InitializeComponent();
        ViewModel.PropertyChanged += (_, e) =>
        {
            // Keep the tree's highlight in step when the view model picks a node (after add, delete or open).
            if (e.PropertyName == nameof(EditorViewModel.Selected) && ViewModel.Selected is { } node && Tree.SelectedItem != node)
            {
                DispatcherQueue.TryEnqueue(() => Tree.SelectedItem = node);
            }
        };
    }

    public EditorViewModel ViewModel { get; } = new(App.Shell);

    public static Visibility NoSelection(NbtNodeViewModel? node) => node is null ? Visibility.Visible : Visibility.Collapsed;

    public static bool HasText(string? text) => !string.IsNullOrEmpty(text);

    /// <summary>Shows a document, selecting the tag at a path of compound keys.</summary>
    public void Open(NbtDocument document, IReadOnlyList<string>? path) => ViewModel.Open(document, path);

    private void OnTreeExpanding(TreeView sender, TreeViewExpandingEventArgs args)
    {
        if (args.Item is NbtNodeViewModel node)
        {
            node.Realize();
        }
    }

    private void OnTreeSelectionChanged(TreeView sender, TreeViewSelectionChangedEventArgs args)
    {
        if (args.AddedItems.FirstOrDefault() is NbtNodeViewModel node)
        {
            ViewModel.Selected = node;
        }
    }

    private void OnAddMenuOpening(object? sender, object e)
    {
        AddMenu.Items.Clear();
        foreach (var type in ViewModel.AddChildTypes)
        {
            var item = new MenuFlyoutItem { Text = TagValues.TypeName(type) };
            item.Click += (_, _) => ViewModel.AddChild(type);
            AddMenu.Items.Add(item);
        }
    }

    private void OnApplyValueClick(object sender, RoutedEventArgs e) => ViewModel.ApplyValue();

    private void OnRenameClick(object sender, RoutedEventArgs e) => ViewModel.Rename();

    private void OnDuplicateClick(object sender, RoutedEventArgs e) => ViewModel.Duplicate();

    private void OnRemoveClick(object sender, RoutedEventArgs e) => ViewModel.Remove();

    private void OnLoadSnbtClick(object sender, RoutedEventArgs e) => ViewModel.LoadSnbt();

    private void OnApplySnbtClick(object sender, RoutedEventArgs e) => ViewModel.ApplySnbt();

    private void OnCopySnbtClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel.GetSnbt() is { } snbt)
        {
            var package = new DataPackage();
            package.SetText(snbt);
            Clipboard.SetContent(package);
        }
    }

    private void OnValueKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter)
        {
            ViewModel.ApplyValue();
            e.Handled = true;
        }
    }

    private void OnNameKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter)
        {
            ViewModel.Rename();
            e.Handled = true;
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
            ViewModel.Open(await App.Shell.OpenFileAsync(path), null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NbtFormatException or InvalidDataException)
        {
            App.MainWindow.ShowStatus(InfoBarSeverity.Error, "Couldn't open the file", ex.Message);
        }
    }
}
