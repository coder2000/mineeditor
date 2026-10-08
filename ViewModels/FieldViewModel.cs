using CommunityToolkit.Mvvm.ComponentModel;
using MineEditor.Core.Documents;
using MineEditor.Core.Nbt;
using Ubiety.Nbt;

namespace MineEditor.ViewModels;

public enum FieldKind
{
    Text,
    Toggle,
    Choice,
    ReadOnly,
}

/// <summary>An option in a choice field, such as a game mode.</summary>
public sealed record ChoiceOption(string Label, int Value)
{
    public override string ToString() => Label;
}

/// <summary>A titled group of fields on a form page.</summary>
public sealed record FieldGroup(string Title, IReadOnlyList<FieldViewModel> Fields);

/// <summary>
/// A form field bound to an existing tag. Edits change the tag in place and mark its document dirty,
/// so the form and the NBT editor always show the same data.
/// </summary>
public sealed partial class FieldViewModel : ObservableObject
{
    private readonly NbtTag? _tag;
    private readonly NbtDocument? _document;
    private readonly string _fixedText = string.Empty;

    private FieldViewModel(string label, FieldKind kind, NbtTag? tag, NbtDocument? document, string? description, IReadOnlyList<ChoiceOption>? choices)
    {
        Label = label;
        Kind = kind;
        _tag = tag;
        _document = document;
        Description = description;
        Choices = choices ?? new List<ChoiceOption>();
    }

    private FieldViewModel(string label, string text, string? description)
        : this(label, FieldKind.ReadOnly, null, null, description, null)
    {
        _fixedText = text;
    }

    // Concrete lists rather than collection expressions, whose compiler-generated types can't be
    // registered in WinRTExposedTypes.cs for trimmed builds.
    public static IReadOnlyList<ChoiceOption> GameModes { get; } =
        new List<ChoiceOption> { new("Survival", 0), new("Creative", 1), new("Adventure", 2), new("Spectator", 3) };

    public static IReadOnlyList<ChoiceOption> Difficulties { get; } =
        new List<ChoiceOption> { new("Peaceful", 0), new("Easy", 1), new("Normal", 2), new("Hard", 3) };

    public string Label { get; }

    public string? Description { get; }

    public FieldKind Kind { get; }

    public IReadOnlyList<ChoiceOption> Choices { get; }

    /// <summary>Gets the validation message for the last rejected edit.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    public partial string? Error { get; private set; }

    public bool HasError => Error is not null;

    /// <summary>Gets or sets the value as text. Invalid text is rejected and the field shows the stored value again.</summary>
    public string Text
    {
        get => _tag is null ? _fixedText : TagValues.Format(_tag);
        set
        {
            if (_tag is null || value == Text)
            {
                return;
            }

            if (TagValues.TrySet(_tag, value, out var error))
            {
                Error = null;
                _document?.MarkDirty();
            }
            else
            {
                Error = error;
            }

            OnPropertyChanged();
        }
    }

    /// <summary>Gets or sets the value of a toggle, stored as a byte (1/0) or a game rule string ("true"/"false").</summary>
    public bool IsOn
    {
        get => _tag switch
        {
            NbtByte b => b.Value != 0,
            NbtString s => s.Value == "true",
            _ => false,
        };
        set
        {
            if (value == IsOn)
            {
                return;
            }

            switch (_tag)
            {
                case NbtByte b:
                    b.Value = value ? (byte)1 : (byte)0;
                    break;
                case NbtString s:
                    s.Value = value ? "true" : "false";
                    break;
                default:
                    return;
            }

            _document?.MarkDirty();
            OnPropertyChanged();
        }
    }

    /// <summary>Gets or sets the selected option of a choice field.</summary>
    public ChoiceOption? SelectedChoice
    {
        get
        {
            var value = _tag switch
            {
                NbtByte b => b.Value,
                NbtShort s => s.Value,
                NbtInt i => i.Value,
                _ => int.MinValue,
            };
            return Choices.FirstOrDefault(c => c.Value == value);
        }
        set
        {
            if (value is null || value == SelectedChoice)
            {
                return;
            }

            switch (_tag)
            {
                case NbtByte b:
                    b.Value = (byte)value.Value;
                    break;
                case NbtShort s:
                    s.Value = (short)value.Value;
                    break;
                case NbtInt i:
                    i.Value = value.Value;
                    break;
                default:
                    return;
            }

            _document?.MarkDirty();
            OnPropertyChanged();
        }
    }

    /// <summary>Creates a field for a compound entry, or returns null if the entry is missing or can't be shown that way.</summary>
    public static FieldViewModel? For(NbtDocument document, NbtCompound? compound, string key, string label, FieldKind kind = FieldKind.Text,
        string? description = null, IReadOnlyList<ChoiceOption>? choices = null)
    {
        if (compound is null || !compound.TryGetValue(key, out var tag))
        {
            return null;
        }

        return For(document, tag, label, kind, description, choices);
    }

    /// <summary>Creates a field for a tag, or returns null if it can't be shown as <paramref name="kind"/>.</summary>
    public static FieldViewModel? For(NbtDocument document, NbtTag tag, string label, FieldKind kind = FieldKind.Text,
        string? description = null, IReadOnlyList<ChoiceOption>? choices = null)
    {
        var supported = kind switch
        {
            FieldKind.Toggle => tag is NbtByte or NbtString { Value: "true" or "false" },
            FieldKind.Choice => tag is NbtByte or NbtShort or NbtInt,
            _ => TagValues.IsPrimitive(tag.TagType),
        };

        return supported ? new FieldViewModel(label, kind, tag, document, description, choices) : null;
    }

    /// <summary>Creates a read-only field showing fixed text.</summary>
    public static FieldViewModel ReadOnly(string label, string text, string? description = null) => new(label, text, description);
}
