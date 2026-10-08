using Ubiety.Nbt;

namespace MineEditor.Core.Map;

/// <summary>What the map shows for one block column.</summary>
/// <param name="Color">The unshaded 0xRRGGBB color.</param>
/// <param name="Height">The Y of the visible surface (the water surface over water).</param>
/// <param name="BlockId">The visible block's id.</param>
/// <param name="WaterDepth">How many water blocks lie over the floor; 0 on land.</param>
public readonly record struct ColumnSample(uint Color, int Height, string BlockId, int WaterDepth);

/// <summary>
/// Read-only block access to one chunk's NBT, for the anvil formats from 1.13 onwards.
/// </summary>
/// <remarks>
/// 1.18+ chunks keep <c>sections</c> at the root with <c>block_states</c> containers; 1.13–1.17 chunks wrap
/// everything in a <c>Level</c> compound with <c>Palette</c>/<c>BlockStates</c> per section.
/// Pre-1.13 numeric block ids are not supported.
/// </remarks>
public sealed class ChunkView
{
    private const int SectionVolume = 16 * 16 * 16;

    private readonly Section?[] _sections;
    private readonly int _minSectionY;
    private readonly PackedLongArray? _surface;

    private ChunkView(Section?[] sections, int minSectionY, int minY, string status, PackedLongArray? surface)
    {
        _sections = sections;
        _minSectionY = minSectionY;
        MinY = minY;
        Status = status;
        _surface = surface;
    }

    /// <summary>Gets the lowest block Y in the chunk.</summary>
    public int MinY { get; }

    /// <summary>Gets one past the highest block Y covered by a section.</summary>
    public int MaxY => (_minSectionY + _sections.Length) * 16;

    /// <summary>Gets the generation status, e.g. <c>minecraft:full</c>.</summary>
    public string Status { get; }

    /// <summary>Gets a value indicating whether world generation has finished for this chunk.</summary>
    public bool IsFullyGenerated => Status is "" or "full" or "minecraft:full" or "postprocessed" or "minecraft:postprocessed";

    /// <summary>Creates a view over a chunk's root compound, or returns null for unsupported formats.</summary>
    public static ChunkView? TryCreate(NbtCompound chunk)
    {
        NbtList? sectionList;
        NbtCompound? heightmaps;
        string status;
        int? yPos = null;
        bool modern;

        if (chunk.TryGet<NbtList>("sections", out var modernSections))
        {
            modern = true;
            sectionList = modernSections;
            chunk.TryGet("Heightmaps", out heightmaps);
            status = chunk.TryGet<NbtString>("Status", out var s) ? s.Value : "";
            if (chunk.TryGet<NbtInt>("yPos", out var y))
            {
                yPos = y.Value;
            }
        }
        else if (chunk.TryGet<NbtCompound>("Level", out var level))
        {
            modern = false;
            level.TryGet("Sections", out sectionList);
            level.TryGet("Heightmaps", out heightmaps);
            status = level.TryGet<NbtString>("Status", out var s) ? s.Value : "";
        }
        else
        {
            return null;
        }

        var parsed = new List<(int Y, Section Section)>();
        foreach (var tag in sectionList ?? [])
        {
            if (tag is NbtCompound section && TryReadSectionY(section, out var sectionY) && Section.TryCreate(section, modern, out var s))
            {
                parsed.Add((sectionY, s));
            }
            else if (tag is NbtCompound legacy && legacy.ContainsKey("Blocks"))
            {
                return null; // Pre-1.13 numeric block ids.
            }
        }

        var minSectionY = yPos ?? (parsed.Count > 0 ? Math.Min(0, parsed.Min(p => p.Y)) : 0);
        var maxSectionY = parsed.Count > 0 ? parsed.Max(p => p.Y) : minSectionY - 1;
        var sections = new Section?[Math.Max(0, maxSectionY - minSectionY + 1)];
        foreach (var (y, section) in parsed)
        {
            if (y >= minSectionY)
            {
                sections[y - minSectionY] = section;
            }
        }

        PackedLongArray? surface = null;
        if (heightmaps is not null && heightmaps.TryGet<NbtLongArray>("WORLD_SURFACE", out var hm)
            && hm.Value.Length > 0 && PackedLongArray.TryCreate(hm.Value, 256, 1, out var packed))
        {
            surface = packed;
        }

        // Pre-1.18 worlds start at Y 0 even though they store an empty section at Y -1 for lighting.
        var minY = modern ? minSectionY * 16 : 0;
        return new ChunkView(sections, minSectionY, minY, status, surface);
    }

    /// <summary>Gets the block id at chunk-local coordinates (x and z 0–15, y absolute).</summary>
    public string GetBlockId(int x, int y, int z)
    {
        var section = GetSection(y);
        return section is null ? "minecraft:air" : section.Ids[section.IndexAt(x, y, z)];
    }

    /// <summary>Gets the map style of the block at chunk-local coordinates.</summary>
    public BlockStyle GetStyle(int x, int y, int z)
    {
        var section = GetSection(y);
        return section is null ? default : section.Styles[section.IndexAt(x, y, z)];
    }

    /// <summary>Finds what a top-down map shows for a column.</summary>
    /// <param name="x">The chunk-local X (0–15).</param>
    /// <param name="z">The chunk-local Z (0–15).</param>
    /// <param name="ceilingY">
    /// For dimensions with a roof (the Nether), start below this Y and skip down to the first open space,
    /// so the map shows the floor instead of the bedrock ceiling.
    /// </param>
    /// <returns>The sample, or null if the column is empty.</returns>
    public ColumnSample? SampleColumn(int x, int z, int? ceilingY = null)
    {
        int y;
        if (ceilingY is { } ceiling)
        {
            var start = Math.Min(ceiling, MaxY - 1);
            y = start;
            while (y >= MinY && GetStyle(x, y, z).Kind != BlockKind.Transparent)
            {
                y--;
            }

            if (y < MinY)
            {
                // Solid all the way down: show the block under the roof, dimmed so open floor stands out.
                var roof = GetStyle(x, start, z);
                return roof.Kind == BlockKind.Transparent ? null : new ColumnSample(BlockColors.Scale(roof.Color, 0.6), start, GetBlockId(x, start, z), 0);
            }
        }
        else if (_surface is { } surface)
        {
            var height = surface[(z * 16) + x];
            if (height == 0)
            {
                return null;
            }

            y = Math.Min(MinY + height - 1, MaxY - 1);
        }
        else
        {
            y = MaxY - 1;
        }

        var waterTop = 0;
        var depth = 0;
        for (; y >= MinY; y--)
        {
            var style = GetStyle(x, y, z);
            switch (style.Kind)
            {
                case BlockKind.Transparent:
                    continue;
                case BlockKind.Water:
                    if (depth++ == 0)
                    {
                        waterTop = y;
                    }

                    continue;
            }

            if (depth == 0)
            {
                return new ColumnSample(style.Color, y, GetBlockId(x, y, z), 0);
            }

            return new ColumnSample(BlendWater(style.Color, depth), waterTop, GetBlockId(x, waterTop, z), depth);
        }

        return depth > 0 ? new ColumnSample(BlendWater(BlockColors.WaterColor, depth), waterTop, GetBlockId(x, waterTop, z), depth) : null;
    }

    private static uint BlendWater(uint floor, int depth)
    {
        var tinted = BlockColors.Lerp(floor, BlockColors.WaterColor, Math.Clamp(0.5 + (depth * 0.06), 0.5, 0.9));
        return BlockColors.Scale(tinted, 1 - (Math.Min(depth, 20) * 0.015));
    }

    private static bool TryReadSectionY(NbtCompound section, out int y)
    {
        switch (section.TryGetValue("Y", out var tag) ? tag : null)
        {
            case NbtByte b:
                y = unchecked((sbyte)b.Value);
                return true;
            case NbtInt i:
                y = i.Value;
                return true;
            default:
                y = 0;
                return false;
        }
    }

    private Section? GetSection(int y)
    {
        var index = (y >> 4) - _minSectionY;
        return (uint)index < (uint)_sections.Length ? _sections[index] : null;
    }

    private sealed class Section
    {
        private readonly PackedLongArray? _data;

        private Section(string[] ids, PackedLongArray? data)
        {
            Ids = ids;
            Styles = Array.ConvertAll(ids, BlockColors.Get);
            _data = data;
        }

        public string[] Ids { get; }

        public BlockStyle[] Styles { get; }

        public static bool TryCreate(NbtCompound section, bool modern, out Section result)
        {
            result = null!;
            NbtList? palette;
            long[]? data;
            if (modern)
            {
                if (!section.TryGet<NbtCompound>("block_states", out var states) || !states.TryGet("palette", out palette))
                {
                    return false;
                }

                data = states.TryGet<NbtLongArray>("data", out var d) ? d.Value : null;
            }
            else
            {
                if (!section.TryGet("Palette", out palette))
                {
                    return false;
                }

                data = section.TryGet<NbtLongArray>("BlockStates", out var d) ? d.Value : null;
            }

            if (palette.Count == 0)
            {
                return false;
            }

            var ids = new string[palette.Count];
            for (var i = 0; i < ids.Length; i++)
            {
                ids[i] = palette[i] is NbtCompound entry && entry.TryGet<NbtString>("Name", out var name) ? name.Value : "minecraft:air";
            }

            PackedLongArray? packed = null;
            if (data is { Length: > 0 } && ids.Length > 1)
            {
                if (!PackedLongArray.TryCreate(data, SectionVolume, Math.Max(4, PackedLongArray.BitsFor(ids.Length)), out var p))
                {
                    return false;
                }

                packed = p;
            }

            result = new Section(ids, packed);
            return true;
        }

        public int IndexAt(int x, int y, int z)
        {
            if (_data is not { } data)
            {
                return 0;
            }

            var index = data[((y & 15) << 8) | (z << 4) | x];
            return index < Ids.Length ? index : 0;
        }
    }
}
