using System.Globalization;
using Ubiety.Nbt;

namespace MineEditor.Core.Nbt;

/// <summary>Formats, parses and creates tag values for editing.</summary>
public static class TagValues
{
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    /// <summary>Gets whether a tag type holds a single value that can be edited as text.</summary>
    public static bool IsPrimitive(NbtTagType type) => type is >= NbtTagType.Byte and <= NbtTagType.Double or NbtTagType.String;

    /// <summary>Gets whether a tag type contains other tags.</summary>
    public static bool IsContainer(NbtTagType type) => type is NbtTagType.Compound or NbtTagType.List;

    /// <summary>Gets a short name for a tag type, as shown in the editor.</summary>
    public static string TypeName(NbtTagType type) => type switch
    {
        NbtTagType.ByteArray => "Byte array",
        NbtTagType.IntArray => "Int array",
        NbtTagType.LongArray => "Long array",
        _ => type.ToString(),
    };

    /// <summary>Formats a primitive tag's value for editing, or summarises a container or array.</summary>
    public static string Format(NbtTag tag) => tag switch
    {
        NbtByte b => unchecked((sbyte)b.Value).ToString(Invariant),
        NbtShort s => s.Value.ToString(Invariant),
        NbtInt i => i.Value.ToString(Invariant),
        NbtLong l => l.Value.ToString(Invariant),
        NbtFloat f => f.Value.ToString("R", Invariant),
        NbtDouble d => d.Value.ToString("R", Invariant),
        NbtString s => s.Value,
        NbtByteArray a => Count(a.Value.Length, "byte"),
        NbtIntArray a => Count(a.Value.Length, "int"),
        NbtLongArray a => Count(a.Value.Length, "long"),
        NbtList l => Count(l.Count, "entry", "entries"),
        NbtCompound c => Count(c.Count, "entry", "entries"),
        _ => string.Empty,
    };

    /// <summary>Sets a primitive tag's value from text.</summary>
    /// <param name="tag">The tag to change.</param>
    /// <param name="text">The new value. Bytes accept -128–255 and <c>true</c>/<c>false</c>.</param>
    /// <param name="error">Why the text was rejected.</param>
    /// <returns>True if the value was set.</returns>
    public static bool TrySet(NbtTag tag, string text, out string? error)
    {
        error = null;
        var trimmed = text.Trim();
        const NumberStyles Integer = NumberStyles.AllowLeadingSign | NumberStyles.AllowLeadingWhite | NumberStyles.AllowTrailingWhite;
        const NumberStyles Real = NumberStyles.Float;

        switch (tag)
        {
            case NbtByte b:
                if (bool.TryParse(trimmed, out var flag))
                {
                    b.Value = flag ? (byte)1 : (byte)0;
                    return true;
                }

                if (int.TryParse(trimmed, Integer, Invariant, out var bv) && bv is >= sbyte.MinValue and <= byte.MaxValue)
                {
                    b.Value = unchecked((byte)bv);
                    return true;
                }

                error = "Enter a whole number from -128 to 127, or true/false.";
                return false;
            case NbtShort s when short.TryParse(trimmed, Integer, Invariant, out var sv):
                s.Value = sv;
                return true;
            case NbtShort:
                error = "Enter a whole number from -32768 to 32767.";
                return false;
            case NbtInt i when int.TryParse(trimmed, Integer, Invariant, out var iv):
                i.Value = iv;
                return true;
            case NbtInt:
                error = "Enter a whole number from -2147483648 to 2147483647.";
                return false;
            case NbtLong l when long.TryParse(trimmed, Integer, Invariant, out var lv):
                l.Value = lv;
                return true;
            case NbtLong:
                error = "Enter a whole number within the 64-bit range.";
                return false;
            case NbtFloat f when float.TryParse(trimmed, Real, Invariant, out var fv):
                f.Value = fv;
                return true;
            case NbtFloat:
                error = "Enter a number, such as 20 or 0.5.";
                return false;
            case NbtDouble d when double.TryParse(trimmed, Real, Invariant, out var dv):
                d.Value = dv;
                return true;
            case NbtDouble:
                error = "Enter a number, such as 64 or -12.5.";
                return false;
            case NbtString s:
                s.Value = text;
                return true;
            default:
                error = $"{TypeName(tag.TagType)} values can't be edited as plain text. Use SNBT instead.";
                return false;
        }
    }

    /// <summary>Creates an empty or zero tag of a type.</summary>
    public static NbtTag CreateDefault(NbtTagType type) => type switch
    {
        NbtTagType.Byte => new NbtByte(0),
        NbtTagType.Short => new NbtShort(0),
        NbtTagType.Int => new NbtInt(0),
        NbtTagType.Long => new NbtLong(0),
        NbtTagType.Float => new NbtFloat(0),
        NbtTagType.Double => new NbtDouble(0),
        NbtTagType.ByteArray => new NbtByteArray([]),
        NbtTagType.String => new NbtString(string.Empty),
        NbtTagType.List => new NbtList(),
        NbtTagType.Compound => new NbtCompound(),
        NbtTagType.IntArray => new NbtIntArray([]),
        NbtTagType.LongArray => new NbtLongArray([]),
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "No tag can be created for this type."),
    };

    /// <summary>Gets a name for a new compound entry that doesn't clash with existing ones.</summary>
    public static string UniqueName(NbtCompound compound, string baseName)
    {
        if (!compound.ContainsKey(baseName))
        {
            return baseName;
        }

        for (var i = 2; ; i++)
        {
            var candidate = $"{baseName}{i}";
            if (!compound.ContainsKey(candidate))
            {
                return candidate;
            }
        }
    }

    /// <summary>Renames a compound entry in place, keeping its position.</summary>
    /// <exception cref="ArgumentException">Another entry already has the new name.</exception>
    public static void Rename(NbtCompound compound, string oldName, string newName)
    {
        if (oldName == newName)
        {
            return;
        }

        if (compound.ContainsKey(newName))
        {
            throw new ArgumentException($"An entry named \"{newName}\" already exists.", nameof(newName));
        }

        var entries = compound.ToList();
        compound.Clear();
        foreach (var (name, tag) in entries)
        {
            compound.Add(name == oldName ? newName : name, tag);
        }
    }

    private static string Count(int count, string singular, string? plural = null) =>
        $"{count.ToString("N0", Invariant)} {(count == 1 ? singular : plural ?? singular + "s")}";
}
