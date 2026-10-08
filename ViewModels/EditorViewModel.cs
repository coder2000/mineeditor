using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using MineEditor.Core.Documents;
using MineEditor.Core.Nbt;
using Ubiety.Nbt;

namespace MineEditor.ViewModels;

/// <summary>The NBT editor: a tree of the selected document and edits to the selected tag.</summary>
public sealed partial class EditorViewModel : ObservableObject
{
    /// <summary>Above this length, SNBT is too slow to edit in a text box.</summary>
    public const int MaxSnbtLength = 500_000;

    public EditorViewModel(ShellViewModel shell)
    {
        Shell = shell;
    }

    public ShellViewModel Shell { get; }

    public ObservableCollection<NbtNodeViewModel> RootNodes { get; } = [];

    [ObservableProperty]
    public partial NbtDocument? SelectedDocument { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection), nameof(CanEditValue), nameof(CanRename), nameof(CanAddChild),
        nameof(CanRemove), nameof(AddChildTypes), nameof(FixedListType))]
    public partial NbtNodeViewModel? Selected { get; set; }

    [ObservableProperty]
    public partial string NameText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ValueText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string SnbtText { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    public partial string? Error { get; set; }

    public bool HasError => Error is not null;

    public bool HasSelection => Selected is not null;

    public bool CanEditValue => Selected is { } s && TagValues.IsPrimitive(s.Tag.TagType);

    public bool CanRename => Selected?.IsInCompound == true;

    public bool CanRemove => Selected is { IsRoot: false };

    public bool CanAddChild => Selected?.Tag is NbtCompound or NbtList;

    /// <summary>Gets the element type of the selected list when it already has one, so new entries must match it.</summary>
    public NbtTagType? FixedListType => Selected?.Tag is NbtList { ElementType: not NbtTagType.End } list ? list.ElementType : null;

    /// <summary>Gets the tag types that can be added to the selected container.</summary>
    public IReadOnlyList<NbtTagType> AddChildTypes => FixedListType is { } type
        ? [type]
        : [.. Enum.GetValues<NbtTagType>().Where(t => t != NbtTagType.End)];

    partial void OnSelectedDocumentChanged(NbtDocument? value)
    {
        RootNodes.Clear();
        Selected = null;
        if (value is not null)
        {
            var root = new NbtNodeViewModel(value, null, null, -1, value.Root) { IsExpanded = true };
            root.Realize();
            RootNodes.Add(root);
        }
    }

    partial void OnSelectedChanged(NbtNodeViewModel? value)
    {
        Error = null;
        NameText = value?.Name ?? string.Empty;
        ValueText = value is not null && TagValues.IsPrimitive(value.Tag.TagType) ? TagValues.Format(value.Tag) : string.Empty;
        SnbtText = string.Empty;
    }

    /// <summary>Opens a document and selects the node at a path of compound keys, expanding its ancestors.</summary>
    /// <returns>The selected node.</returns>
    public NbtNodeViewModel? Open(NbtDocument document, IReadOnlyList<string>? path)
    {
        SelectedDocument = document;
        var node = RootNodes.FirstOrDefault();
        foreach (var key in path ?? [])
        {
            if (node is null)
            {
                break;
            }

            node.Realize();
            node.IsExpanded = true;
            node = node.Children.FirstOrDefault(c => c.Name == key) ?? node;
        }

        // Open the target too, so "edit as NBT" lands on its contents rather than a collapsed row.
        if (node is not null && path is { Count: > 0 })
        {
            node.Realize();
            node.IsExpanded = true;
        }

        Selected = node;
        return node;
    }

    public void ApplyValue()
    {
        if (Selected is not { } node || !CanEditValue)
        {
            return;
        }

        if (!TagValues.TrySet(node.Tag, ValueText, out var error))
        {
            Error = error;
            return;
        }

        Error = null;
        ValueText = TagValues.Format(node.Tag);
        node.Refresh();
        node.Document.MarkDirty();
    }

    public void Rename()
    {
        if (Selected is not { Parent.Tag: NbtCompound compound, Name: { } oldName } node)
        {
            return;
        }

        if (string.IsNullOrEmpty(NameText))
        {
            Error = "Enter a name.";
            return;
        }

        try
        {
            TagValues.Rename(compound, oldName, NameText);
        }
        catch (ArgumentException e)
        {
            Error = e.Message.Split(" (Parameter")[0];
            return;
        }

        Error = null;
        node.Name = NameText;
        node.Document.MarkDirty();
    }

    public void AddChild(NbtTagType type)
    {
        if (Selected is not { } parent)
        {
            return;
        }

        var tag = TagValues.CreateDefault(type);
        parent.Realize();
        NbtNodeViewModel child;
        switch (parent.Tag)
        {
            case NbtCompound compound:
                var name = TagValues.UniqueName(compound, "New" + type);
                compound.Add(name, tag);
                child = new NbtNodeViewModel(parent.Document, parent, name, -1, tag);
                break;
            case NbtList list:
                try
                {
                    list.Add(tag);
                }
                catch (ArgumentException e)
                {
                    Error = e.Message.Split(" (Parameter")[0];
                    return;
                }

                child = new NbtNodeViewModel(parent.Document, parent, null, list.Count - 1, tag);
                break;
            default:
                return;
        }

        parent.Children.Add(child);
        parent.IsExpanded = true;
        parent.Refresh();
        parent.Document.MarkDirty();
        Selected = child;
    }

    public void Duplicate()
    {
        if (Selected is not { Parent: { } parent } node)
        {
            return;
        }

        var copy = node.Tag.Clone();
        var position = parent.Children.IndexOf(node) + 1;
        NbtNodeViewModel child;
        switch (parent.Tag)
        {
            case NbtCompound compound:
                var name = TagValues.UniqueName(compound, node.Name!);
                compound.Add(name, copy);
                child = new NbtNodeViewModel(node.Document, parent, name, -1, copy);
                position = parent.Children.Count; // Compounds append new entries.
                break;
            case NbtList list:
                list.Insert(node.Index + 1, copy);
                child = new NbtNodeViewModel(node.Document, parent, null, node.Index + 1, copy);
                break;
            default:
                return;
        }

        parent.Children.Insert(position, child);
        parent.Reindex();
        parent.Refresh();
        node.Document.MarkDirty();
        Selected = child;
    }

    public void Remove()
    {
        if (Selected is not { Parent: { } parent } node)
        {
            return;
        }

        switch (parent.Tag)
        {
            case NbtCompound compound:
                compound.Remove(node.Name!);
                break;
            case NbtList list:
                list.RemoveAt(node.Index);
                break;
        }

        parent.Children.Remove(node);
        parent.Reindex();
        parent.Refresh();
        node.Document.MarkDirty();
        Selected = parent;
    }

    /// <summary>Fills the SNBT box with the selected tag.</summary>
    public void LoadSnbt()
    {
        if (Selected is not { } node)
        {
            return;
        }

        var snbt = node.Tag.ToSnbt(indented: true);
        if (snbt.Length > MaxSnbtLength)
        {
            Error = $"This tag is {snbt.Length:N0} characters of SNBT, too large to edit as text. Select a smaller tag inside it.";
            SnbtText = string.Empty;
            return;
        }

        Error = null;
        SnbtText = snbt;
    }

    /// <summary>Gets the selected tag as compact SNBT, for the clipboard.</summary>
    public string? GetSnbt() => Selected?.Tag.ToSnbt();

    /// <summary>Replaces the selected tag with the parsed SNBT text.</summary>
    public void ApplySnbt()
    {
        if (Selected is not { } node)
        {
            return;
        }

        NbtTag parsed;
        try
        {
            parsed = NbtTag.ParseSnbt(SnbtText);
        }
        catch (NbtFormatException e)
        {
            Error = e.Message;
            return;
        }

        try
        {
            switch (node.Parent?.Tag)
            {
                case null when parsed is NbtCompound replacement && node.Tag is NbtCompound root:
                    // The document root object is kept so the file and its other views stay connected.
                    root.Clear();
                    foreach (var (name, tag) in replacement.ToList())
                    {
                        replacement.Remove(name);
                        root.Add(name, tag);
                    }

                    parsed = root;
                    break;
                case null:
                    Error = "The root must be a compound, written as { ... }.";
                    return;
                case NbtCompound compound:
                    compound[node.Name!] = parsed;
                    break;
                case NbtList list:
                    list[node.Index] = parsed;
                    break;
            }
        }
        catch (ArgumentException e)
        {
            Error = e.Message.Split(" (Parameter")[0];
            return;
        }

        Error = null;
        node.Tag = parsed;
        node.Rebuild();
        node.Parent?.Refresh();
        node.Document.MarkDirty();
        OnSelectedChanged(node);
        OnPropertyChanged(nameof(CanEditValue));
        OnPropertyChanged(nameof(CanAddChild));
        OnPropertyChanged(nameof(AddChildTypes));
    }
}
