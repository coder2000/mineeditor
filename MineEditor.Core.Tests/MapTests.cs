using MineEditor.Core.Map;
using Ubiety.Nbt;
using Ubiety.Nbt.Region;

namespace MineEditor.Core.Tests;

public class PackedLongArrayTests
{
    [Theory]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(9)]
    public void ReadsPaddedAndSpanningLayouts(int bits)
    {
        var values = Enumerable.Range(0, 4096).Select(i => (i * 7) % (1 << bits)).ToArray();

        Assert.True(PackedLongArray.TryCreate(ChunkBuilder.PackPadded(values, bits), 4096, bits, out var padded));
        Assert.True(PackedLongArray.TryCreate(ChunkBuilder.PackSpanning(values, bits), 4096, bits, out var spanning));
        for (var i = 0; i < values.Length; i++)
        {
            Assert.Equal(values[i], padded[i]);
            Assert.Equal(values[i], spanning[i]);
        }
    }

    [Fact]
    public void DetectsHeightmapWidthFromLength()
    {
        var heights = Enumerable.Range(0, 256).Select(i => i + 100).ToArray();
        Assert.True(PackedLongArray.TryCreate(ChunkBuilder.PackPadded(heights, 9), 256, 1, out var array));
        Assert.Equal(355, array[255]);
    }

    [Fact]
    public void RejectsUnexpectedLength() => Assert.False(PackedLongArray.TryCreate(new long[3], 4096, 4, out _));
}

public class BlockColorsTests
{
    [Fact]
    public void MatchesLongerDyeNamesFirst()
    {
        Assert.Equal(0x3AAFD9u, BlockColors.Get("minecraft:light_blue_wool").Color);
        Assert.Equal(0x35399Du, BlockColors.Get("minecraft:blue_wool").Color);
    }

    [Fact]
    public void SeparatesDarkOakFromOak()
    {
        Assert.Equal(0x3B6B1Eu, BlockColors.Get("minecraft:dark_oak_leaves").Color);
        Assert.Equal(0x4A7A2Au, BlockColors.Get("minecraft:oak_leaves").Color);
        Assert.Equal(0xA2834Fu, BlockColors.Get("minecraft:oak_stairs").Color);
    }

    [Theory]
    [InlineData("minecraft:air", BlockKind.Transparent)]
    [InlineData("minecraft:wall_torch", BlockKind.Transparent)]
    [InlineData("minecraft:stone_button", BlockKind.Transparent)]
    [InlineData("minecraft:water", BlockKind.Water)]
    [InlineData("minecraft:kelp_plant", BlockKind.Water)]
    [InlineData("minecraft:stone", BlockKind.Solid)]
    [InlineData("somemod:mystery_block", BlockKind.Solid)]
    public void ClassifiesBlocks(string id, BlockKind kind) => Assert.Equal(kind, BlockColors.Get(id).Kind);

    [Fact]
    public void FallsBackToGrayForUnknownBlocks() => Assert.Equal(BlockColors.UnknownColor, BlockColors.Get("somemod:mystery_block").Color);
}

public class ChunkViewTests
{
    [Fact]
    public void FindsTopBlockUsingHeightmap()
    {
        var chunk = new ChunkBuilder().Column(3, 4, -64, 70, "minecraft:stone").Set(3, 71, 4, "minecraft:grass_block").Build();
        var view = ChunkView.TryCreate(chunk)!;

        var sample = view.SampleColumn(3, 4);

        Assert.NotNull(sample);
        Assert.Equal(71, sample.Value.Height);
        Assert.Equal("minecraft:grass_block", sample.Value.BlockId);
        Assert.Equal(-64, view.MinY);
    }

    [Fact]
    public void ScansFromTopWithoutHeightmap()
    {
        var chunk = new ChunkBuilder { WithHeightmap = false }.Set(0, 5, 0, "minecraft:sand").Build();
        Assert.Equal(5, ChunkView.TryCreate(chunk)!.SampleColumn(0, 0)!.Value.Height);
    }

    [Fact]
    public void SkipsTransparentBlocksAndMeasuresWater()
    {
        var chunk = new ChunkBuilder()
            .Set(1, 60, 1, "minecraft:gravel")
            .Column(1, 1, 61, 63, "minecraft:water")
            .Set(1, 64, 1, "minecraft:torch")
            .Build();

        var sample = ChunkView.TryCreate(chunk)!.SampleColumn(1, 1)!.Value;

        Assert.Equal(3, sample.WaterDepth);
        Assert.Equal(63, sample.Height);
        Assert.Equal("minecraft:water", sample.BlockId);
    }

    [Fact]
    public void CeilingModeLooksBelowTheRoof()
    {
        var chunk = new ChunkBuilder { MinSectionY = 0, WithHeightmap = false }
            .Column(0, 0, 0, 40, "minecraft:netherrack")
            .Column(0, 0, 100, 127, "minecraft:bedrock")
            .Build();

        var sample = ChunkView.TryCreate(chunk)!.SampleColumn(0, 0, ceilingY: 118)!.Value;

        Assert.Equal(40, sample.Height);
        Assert.Equal("minecraft:netherrack", sample.BlockId);
    }

    [Fact]
    public void CeilingModeDrawsRoofWhenColumnIsSolid()
    {
        var chunk = new ChunkBuilder { MinSectionY = 0, WithHeightmap = false }.Column(0, 0, 0, 127, "minecraft:netherrack").Build();
        Assert.Equal(118, ChunkView.TryCreate(chunk)!.SampleColumn(0, 0, ceilingY: 118)!.Value.Height);
    }

    [Fact]
    public void EmptyColumnHasNoSample() =>
        Assert.Null(ChunkView.TryCreate(new ChunkBuilder().Set(0, 0, 0, "minecraft:stone").Build())!.SampleColumn(5, 5));

    [Fact]
    public void ReadsLegacyLevelFormat()
    {
        var palette = new NbtList { new NbtCompound { ["Name"] = "minecraft:air" }, new NbtCompound { ["Name"] = "minecraft:dirt" } };
        var blocks = new int[4096];
        blocks[(10 << 8) | (2 << 4) | 3] = 1;
        var chunk = new NbtCompound
        {
            ["Level"] = new NbtCompound
            {
                ["Status"] = "full",
                ["Sections"] = new NbtList
                {
                    new NbtCompound { ["Y"] = (byte)4, ["Palette"] = palette, ["BlockStates"] = ChunkBuilder.PackSpanning(blocks, 4) },
                },
            },
        };

        var view = ChunkView.TryCreate(chunk)!;

        Assert.True(view.IsFullyGenerated);
        Assert.Equal("minecraft:dirt", view.GetBlockId(3, 74, 2));
        Assert.Equal(74, view.SampleColumn(3, 2)!.Value.Height);
    }

    [Fact]
    public void RejectsPre113Chunks()
    {
        var chunk = new NbtCompound
        {
            ["Level"] = new NbtCompound { ["Sections"] = new NbtList { new NbtCompound { ["Y"] = (byte)0, ["Blocks"] = new byte[4096] } } },
        };
        Assert.Null(ChunkView.TryCreate(chunk));
    }

    [Fact]
    public void ReportsPartialGeneration() =>
        Assert.False(ChunkView.TryCreate(new ChunkBuilder { Status = "minecraft:noise" }.Set(0, 0, 0, "minecraft:stone").Build())!.IsFullyGenerated);
}

public sealed class RegionRendererTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("mineeditor-tests").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void RendersChunksIntoRegionImage()
    {
        var path = Path.Combine(_dir, "r.0.0.mca");
        using (var region = RegionFile.Open(path))
        {
            var full = new ChunkBuilder();
            for (var x = 0; x < 16; x++)
            {
                for (var z = 0; z < 16; z++)
                {
                    full.Set(x, 64, z, "minecraft:snow_block");
                }
            }

            region.WriteChunk(1, 2, full.Build());
            region.WriteChunk(3, 3, new ChunkBuilder { Status = "minecraft:carvers" }.Set(0, 0, 0, "minecraft:stone").Build());
        }

        var image = RegionRenderer.Render(path, 0, 0);

        Assert.Equal(ChunkState.Rendered, image.Chunks[(2 * 32) + 1]);
        Assert.Equal(ChunkState.Partial, image.Chunks[(3 * 32) + 3]);
        Assert.Equal(ChunkState.Missing, image.Chunks[0]);
        Assert.Equal(0xFFF8FDFDu, image.Pixels[(40 * RegionRenderer.Size) + 20]);
        Assert.Equal(0u, image.Pixels[0]);
    }

    [Fact]
    public void ShadesSlopesLikeInGameMaps()
    {
        var path = Path.Combine(_dir, "r.0.0.mca");
        using (var region = RegionFile.Open(path))
        {
            region.WriteChunk(0, 0, new ChunkBuilder()
                .Set(0, 64, 0, "minecraft:stone")
                .Set(0, 65, 1, "minecraft:stone")
                .Set(0, 60, 2, "minecraft:stone")
                .Build());
        }

        var pixels = RegionRenderer.Render(path, 0, 0).Pixels;
        var flat = pixels[0] & 0xFF;

        Assert.True((pixels[RegionRenderer.Size] & 0xFF) > flat);
        Assert.True((pixels[2 * RegionRenderer.Size] & 0xFF) < flat);
    }
}
