using Ubiety.Nbt;

namespace MineEditor.Core.Tests;

/// <summary>Builds 1.18+ chunk compounds for tests.</summary>
internal sealed class ChunkBuilder
{
    private readonly Dictionary<int, string[]> _sections = [];

    public int MinSectionY { get; init; } = -4;

    public string Status { get; init; } = "minecraft:full";

    public bool WithHeightmap { get; init; } = true;

    /// <summary>Sets a block, creating its section (filled with air) on first use.</summary>
    public ChunkBuilder Set(int x, int y, int z, string id)
    {
        if (!_sections.TryGetValue(y >> 4, out var blocks))
        {
            blocks = Enumerable.Repeat("minecraft:air", 4096).ToArray();
            _sections[y >> 4] = blocks;
        }

        blocks[((y & 15) << 8) | (z << 4) | x] = id;
        return this;
    }

    /// <summary>Fills a column from <paramref name="fromY"/> to <paramref name="toY"/> inclusive.</summary>
    public ChunkBuilder Column(int x, int z, int fromY, int toY, string id)
    {
        for (var y = fromY; y <= toY; y++)
        {
            Set(x, y, z, id);
        }

        return this;
    }

    public NbtCompound Build()
    {
        var sections = new NbtList();
        foreach (var (sectionY, blocks) in _sections.OrderBy(s => s.Key))
        {
            var palette = blocks.Distinct().ToList();
            var states = new NbtCompound
            {
                ["palette"] = new NbtList(palette.Select(id => (NbtTag)new NbtCompound { ["Name"] = id })),
            };

            if (palette.Count > 1)
            {
                var bits = Math.Max(4, 32 - int.LeadingZeroCount(palette.Count - 1));
                states["data"] = PackPadded(blocks.Select(id => palette.IndexOf(id)).ToArray(), bits);
            }

            sections.Add(new NbtCompound { ["Y"] = unchecked((byte)(sbyte)sectionY), ["block_states"] = states });
        }

        var chunk = new NbtCompound
        {
            ["DataVersion"] = 3953,
            ["Status"] = Status,
            ["yPos"] = MinSectionY,
            ["sections"] = sections,
        };

        if (WithHeightmap)
        {
            var heights = new int[256];
            for (var i = 0; i < 256; i++)
            {
                heights[i] = TopY(i & 15, i >> 4) is { } top ? top - (MinSectionY * 16) + 1 : 0;
            }

            chunk["Heightmaps"] = new NbtCompound { ["WORLD_SURFACE"] = PackPadded(heights, 9) };
        }

        return chunk;
    }

    public static long[] PackPadded(int[] values, int bits)
    {
        var perLong = 64 / bits;
        var data = new long[(values.Length + perLong - 1) / perLong];
        for (var i = 0; i < values.Length; i++)
        {
            data[i / perLong] |= (long)values[i] << (i % perLong * bits);
        }

        return data;
    }

    public static long[] PackSpanning(int[] values, int bits)
    {
        var data = new long[values.Length * bits / 64];
        for (var i = 0; i < values.Length; i++)
        {
            var bit = i * bits;
            data[bit >> 6] |= (long)values[i] << (bit & 63);
            if ((bit & 63) + bits > 64)
            {
                data[(bit >> 6) + 1] |= (long)((ulong)values[i] >> (64 - (bit & 63)));
            }
        }

        return data;
    }

    private int? TopY(int x, int z)
    {
        foreach (var (sectionY, blocks) in _sections.OrderByDescending(s => s.Key))
        {
            for (var y = 15; y >= 0; y--)
            {
                if (blocks[(y << 8) | (z << 4) | x] != "minecraft:air")
                {
                    return (sectionY * 16) + y;
                }
            }
        }

        return null;
    }
}
