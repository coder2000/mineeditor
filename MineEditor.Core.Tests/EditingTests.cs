using MineEditor.Core.Documents;
using MineEditor.Core.Nbt;
using MineEditor.Core.World;
using Ubiety.Nbt;
using Ubiety.Nbt.Region;

namespace MineEditor.Core.Tests;

public class TagValuesTests
{
    [Theory]
    [InlineData("-1", 255)]
    [InlineData("200", 200)]
    [InlineData("true", 1)]
    [InlineData("false", 0)]
    public void SetsBytesFromSignedOrUnsignedText(string text, byte expected)
    {
        var tag = new NbtByte(0);
        Assert.True(TagValues.TrySet(tag, text, out _));
        Assert.Equal(expected, tag.Value);
    }

    [Fact]
    public void FormatsBytesSigned() => Assert.Equal("-1", TagValues.Format(new NbtByte(255)));

    [Theory]
    [InlineData("70000")]
    [InlineData("abc")]
    [InlineData("1.5")]
    public void RejectsInvalidShorts(string text)
    {
        var tag = new NbtShort(5);
        Assert.False(TagValues.TrySet(tag, text, out var error));
        Assert.NotNull(error);
        Assert.Equal(5, tag.Value);
    }

    [Fact]
    public void ParsesFloatsWithInvariantCulture()
    {
        var tag = new NbtFloat(0);
        Assert.True(TagValues.TrySet(tag, "20.5", out _));
        Assert.Equal(20.5f, tag.Value);
    }

    [Fact]
    public void RefusesArraysAsText() => Assert.False(TagValues.TrySet(new NbtIntArray([1]), "1", out _));

    [Fact]
    public void RenameKeepsPosition()
    {
        var compound = new NbtCompound { ["a"] = 1, ["b"] = 2, ["c"] = 3 };
        TagValues.Rename(compound, "b", "x");
        Assert.Equal(["a", "x", "c"], compound.Keys);
        Assert.Throws<ArgumentException>(() => TagValues.Rename(compound, "a", "c"));
    }

    [Fact]
    public void UniqueNameAvoidsClashes() =>
        Assert.Equal("Item3", TagValues.UniqueName(new NbtCompound { ["Item"] = 1, ["Item2"] = 2 }, "Item"));
}

public sealed class WorldTests : IDisposable
{
    private readonly string _world = Directory.CreateTempSubdirectory("mineeditor-world").FullName;

    public WorldTests()
    {
        var level = new NbtFile(new NbtCompound
        {
            ["Data"] = new NbtCompound
            {
                ["LevelName"] = "Test World",
                ["Player"] = new NbtCompound { ["Health"] = 20f, ["UUID"] = new[] { 1, 2, 3, 4 } },
            },
        })
        {
            Compression = NbtCompression.GZip,
        };
        level.Save(Path.Combine(_world, "level.dat"));
        Directory.CreateDirectory(Path.Combine(_world, "region"));
        Directory.CreateDirectory(Path.Combine(_world, "DIM-1", "region"));
        Directory.CreateDirectory(Path.Combine(_world, "playerdata"));
        new NbtFile(new NbtCompound { ["Health"] = 10f }) { Compression = NbtCompression.GZip }
            .Save(Path.Combine(_world, "playerdata", "0b4c7c1e-0000-0000-0000-000000000000.dat"));
        new NbtFile(new NbtCompound { ["Health"] = 5f, ["UUID"] = new[] { 1, 2, 3, 4 } }) { Compression = NbtCompression.GZip }
            .Save(Path.Combine(_world, "playerdata", "00000001-0002-0000-0003-000000000004.dat"));
        File.WriteAllBytes(Path.Combine(_world, "playerdata", "corrupt.dat"), [1, 2, 3]);
    }

    public void Dispose() => Directory.Delete(_world, recursive: true);

    [Fact]
    public void OpensFromFolderOrLevelDat()
    {
        Assert.Equal("Test World", WorldSave.Open(_world).Name);
        Assert.Equal("Test World", WorldSave.Open(Path.Combine(_world, "level.dat")).Name);
        Assert.Throws<FileNotFoundException>(() => WorldSave.Open(Path.Combine(_world, "region")));
    }

    [Fact]
    public void DiscoversDimensionsWithRegionFolders()
    {
        var world = WorldSave.Open(_world);
        Assert.Equal(["minecraft:overworld", "minecraft:the_nether"], world.Dimensions.Select(d => d.Id));
        Assert.Equal(118, world.Dimensions[1].CeilingY);
    }

    [Fact]
    public void LoadsPlayersAndSkipsCorruptFiles()
    {
        var players = WorldSave.Open(_world).LoadPlayers();

        Assert.Equal(3, players.Count);
        Assert.Equal(20f, players[0].Data.GetFloat("Health"));
        Assert.True(players[1].IsShadowedHost);
        Assert.Equal(5f, players[1].Data.GetFloat("Health"));
        Assert.False(players[2].IsShadowedHost);
        Assert.Equal(10f, players[2].Data.GetFloat("Health"));
    }

    [Fact]
    public void DetectsSessionLockHeldByGame()
    {
        var world = WorldSave.Open(_world);
        var lockPath = Path.Combine(_world, "session.lock");
        File.WriteAllText(lockPath, "x");
        Assert.False(world.IsInUse());

        using var game = new FileStream(lockPath, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite);
        game.Lock(0, long.MaxValue);
        Assert.True(world.IsInUse());
        game.Unlock(0, long.MaxValue);
    }

    [Fact]
    public void SaveBacksUpOncePerSessionAndKeepsCompression()
    {
        var world = WorldSave.Open(_world);
        var original = File.ReadAllBytes(Path.Combine(_world, "level.dat"));

        world.Data["LevelName"] = "Renamed";
        world.Level.MarkDirty();
        world.Level.Save(world.Backups);
        world.Data["LevelName"] = "Renamed again";
        world.Level.Save(world.Backups);

        var backup = Path.Combine(world.Backups.SessionPath, "level.dat");
        Assert.Equal(original, File.ReadAllBytes(backup));
        Assert.False(world.Level.IsDirty);
        var reloaded = NbtFile.Load(Path.Combine(_world, "level.dat"));
        Assert.Equal(NbtCompression.GZip, reloaded.Compression);
        Assert.Equal("Renamed again", reloaded.Root.GetCompound("Data").GetString("LevelName"));
    }

    [Fact]
    public void ChunkDocumentRoundTripsAndDeletes()
    {
        var world = WorldSave.Open(_world);
        var regionPath = Path.Combine(_world, "region", "r.-1.0.mca");
        using (var region = RegionFile.Open(regionPath))
        {
            region.WriteChunk(31, 5, new ChunkBuilder().Set(0, 0, 0, "minecraft:stone").Build());
        }

        var doc = ChunkNbtDocument.Load(regionPath, -1, 5)!;
        doc.Root["InhabitedTime"] = 99L;
        doc.Save(world.Backups);

        Assert.Equal(99L, ChunkNbtDocument.Load(regionPath, -1, 5)!.Root.GetLong("InhabitedTime"));
        Assert.True(File.Exists(Path.Combine(world.Backups.SessionPath, "region", "r.-1.0.mca")));
        Assert.True(ChunkNbtDocument.Delete(regionPath, -1, 5, world.Backups));
        Assert.Null(ChunkNbtDocument.Load(regionPath, -1, 5));
    }
}
