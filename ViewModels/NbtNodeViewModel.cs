using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using MineEditor.Core.Documents;
using MineEditor.Core.Nbt;
using Ubiety.Nbt;

namespace MineEditor.ViewModels;

/// <summary>A tag in the NBT editor tree. Children are created when the node is first expanded.</summary>
public sealed partial class NbtNodeViewModel : ObservableObject
{
    private const int MaxSummaryLength = 120;

    public NbtNodeViewModel(NbtDocument document, NbtNodeViewModel? parent, string? name, int index, NbtTag tag)
    {
        Document = document;
        Parent = parent;
        Name = name;
        Index = index;
        Tag = tag;
        HasUnrealizedChildren = HasChildTags;
    }

    public NbtDocument Document { get; }

    public NbtNodeViewModel? Parent { get; }

    /// <summary>Gets the entry name when the parent is a compound.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Label))]
    public partial string? Name { get; set; }

    /// <summary>Gets the position when the parent is a list.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Label))]
    public partial int Index { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Badge), nameof(Summary), nameof(TypeName))]
    public partial NbtTag Tag { get; set; }

    [ObservableProperty]
    public partial bool HasUnrealizedChildren { get; private set; }

    [ObservableProperty]
    public partial bool IsExpanded { get; set; }

    public ObservableCollection<NbtNodeViewModel> Children { get; } = [];

    public bool IsRoot => Parent is null;

    public bool IsInCompound => Parent?.Tag is NbtCompound;

    public bool IsInList => Parent?.Tag is NbtList;

    public string Label => Parent is null ? Document.Title : Name ?? $"[{Index}]";

    public string TypeName => TagValues.TypeName(Tag.TagType);

    /// <summary>Gets a short type marker for the tree.</summary>
    public string Badge => Tag.TagType switch
    {
        NbtTagType.Byte => "B",
        NbtTagType.Short => "S",
        NbtTagType.Int => "I",
        NbtTagType.Long => "L",
        NbtTagType.Float => "F",
        NbtTagType.Double => "D",
        NbtTagType.String => "T",
        NbtTagType.ByteArray => "[B]",
        NbtTagType.IntArray => "[I]",
        NbtTagType.LongArray => "[L]",
        NbtTagType.List => "[ ]",
        NbtTagType.Compound => "{ }",
        _ => "?",
    };

    public string Summary
    {
        get
        {
            var text = Tag is NbtString s ? $"\"{s.Value}\"" : TagValues.Format(Tag);
            text = text.ReplaceLineEndings(" ");
            return text.Length > MaxSummaryLength ? text[..MaxSummaryLength] + "…" : text;
        }
    }

    /// <summary>Gets the names from the document root to this node, for display.</summary>
    public string PathText
    {
        get
        {
            var parts = new List<string>();
            for (var node = this; node is not null; node = node.Parent)
            {
                parts.Add(node.Label);
            }

            parts.Reverse();
            return string.Join(" › ", parts);
        }
    }

    private bool HasChildTags => Tag switch
    {
        NbtCompound c => c.Count > 0,
        NbtList l => l.Count > 0,
        _ => false,
    };

    /// <summary>Creates child nodes if they haven't been created yet.</summary>
    public void Realize()
    {
        if (!HasUnrealizedChildren)
        {
            return;
        }

        Children.Clear();
        switch (Tag)
        {
            case NbtCompound compound:
                foreach (var (name, child) in compound)
                {
                    Children.Add(new NbtNodeViewModel(Document, this, name, -1, child));
                }

                break;
            case NbtList list:
                for (var i = 0; i < list.Count; i++)
                {
                    Children.Add(new NbtNodeViewModel(Document, this, null, i, list[i]));
                }

                break;
        }

        HasUnrealizedChildren = false;
    }

    /// <summary>Discards child nodes after the tag's contents were replaced, recreating them if the node is open.</summary>
    public void Rebuild()
    {
        Children.Clear();
        HasUnrealizedChildren = HasChildTags;
        if (IsExpanded)
        {
            Realize();
        }

        Refresh();
    }

    /// <summary>Updates the displayed value after the tag changed.</summary>
    public void Refresh()
    {
        OnPropertyChanged(nameof(Summary));
        OnPropertyChanged(nameof(Label));
    }

    /// <summary>Renumbers list children after an insertion or removal.</summary>
    public void Reindex()
    {
        if (Tag is not NbtList)
        {
            return;
        }

        for (var i = 0; i < Children.Count; i++)
        {
            Children[i].Index = i;
        }
    }
}
