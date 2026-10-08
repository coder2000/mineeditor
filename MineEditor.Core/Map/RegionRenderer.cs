using Ubiety.Nbt;
using Ubiety.Nbt.Region;

namespace MineEditor.Core.Map;

/// <summary>The state of a chunk slot in a rendered region.</summary>
public enum ChunkState : byte
{
    /// <summary>The region has no data for the chunk.</summary>
    Missing,

    /// <summary>The chunk exists but world generation has not finished, so it is not drawn.</summary>
    Partial,

    /// <summary>The chunk was drawn.</summary>
    Rendered,

    /// <summary>The chunk could not be read.</summary>
    Error,
}

/// <summary>A rendered region: one pixel per block column.</summary>
/// <param name="RegionX">The region X coordinate.</param>
/// <param name="RegionZ">The region Z coordinate.</param>
/// <param name="Pixels">
/// <see cref="RegionRenderer.Size"/>² pixels as 0xAARRGGBB, row-major with Z down; transparent where nothing was drawn.
/// In memory this is B8G8R8A8.
/// </param>
/// <param name="Chunks">The state of each chunk, indexed by <c>z * 32 + x</c>.</param>
public sealed record RegionImage(int RegionX, int RegionZ, uint[] Pixels, ChunkState[] Chunks);

/// <summary>Renders region files to top-down images.</summary>
public static class RegionRenderer
{
    /// <summary>The width and height of a region in blocks.</summary>
    public const int Size = RegionFile.ChunksPerSide * 16;

    /// <summary>Renders a region file.</summary>
    /// <param name="path">The <c>.mca</c> file.</param>
    /// <param name="regionX">The region X coordinate.</param>
    /// <param name="regionZ">The region Z coordinate.</param>
    /// <param name="ceilingY">See <see cref="ChunkView.SampleColumn"/>; set for the Nether.</param>
    /// <param name="cancellationToken">Cancels between chunks.</param>
    public static RegionImage Render(string path, int regionX, int regionZ, int? ceilingY = null, CancellationToken cancellationToken = default)
    {
        var pixels = new uint[Size * Size];
        var heights = new int[Size * Size];
        var water = new bool[Size * Size];
        var chunks = new ChunkState[RegionFile.ChunksPerSide * RegionFile.ChunksPerSide];
        Array.Fill(heights, int.MinValue);

        using (var region = RegionFile.Open(path, readOnly: true))
        {
            foreach (var (cx, cz) in region.GetChunkPositions())
            {
                cancellationToken.ThrowIfCancellationRequested();
                var slot = (cz * RegionFile.ChunksPerSide) + cx;
                NbtCompound? chunk;
                try
                {
                    chunk = region.ReadChunk(cx, cz);
                }
                catch (Exception e) when (e is NbtFormatException or NotSupportedException or IOException)
                {
                    chunks[slot] = ChunkState.Error;
                    continue;
                }

                var view = chunk is null ? null : ChunkView.TryCreate(chunk);
                if (view is null)
                {
                    chunks[slot] = chunk is null ? ChunkState.Missing : ChunkState.Error;
                    continue;
                }

                if (!view.IsFullyGenerated)
                {
                    chunks[slot] = ChunkState.Partial;
                    continue;
                }

                chunks[slot] = ChunkState.Rendered;
                for (var z = 0; z < 16; z++)
                {
                    for (var x = 0; x < 16; x++)
                    {
                        if (view.SampleColumn(x, z, ceilingY) is not { } sample)
                        {
                            continue;
                        }

                        var i = (((cz * 16) + z) * Size) + (cx * 16) + x;
                        pixels[i] = sample.Color;
                        heights[i] = sample.Height;
                        water[i] = sample.WaterDepth > 0;
                    }
                }
            }
        }

        Shade(pixels, heights, water);
        return new RegionImage(regionX, regionZ, pixels, chunks);
    }

    /// <summary>
    /// Brightens slopes facing north and darkens those facing away, like in-game maps, and makes drawn pixels opaque.
    /// </summary>
    private static void Shade(uint[] pixels, int[] heights, bool[] water)
    {
        for (var z = 0; z < Size; z++)
        {
            for (var x = 0; x < Size; x++)
            {
                var i = (z * Size) + x;
                var height = heights[i];
                if (height == int.MinValue)
                {
                    continue;
                }

                var color = pixels[i];
                if (!water[i] && z > 0 && heights[i - Size] is var north && north != int.MinValue && north != height)
                {
                    color = BlockColors.Scale(color, height > north ? 1.1 : 0.84);
                }

                pixels[i] = 0xFF000000 | color;
            }
        }
    }
}
