using System.ComponentModel;
using System.Runtime.CompilerServices;
using Ubiety.Nbt;
using Ubiety.Nbt.Region;

namespace MineEditor.Core.Documents;

/// <summary>An editable NBT tree that can be saved back to where it came from.</summary>
public abstract class NbtDocument : INotifyPropertyChanged
{
    /// <summary>Gets a short title for tabs and lists.</summary>
    public abstract string Title { get; }

    /// <summary>Gets a longer description, such as the file path.</summary>
    public abstract string Location { get; }

    /// <summary>Gets the root compound. Edits are made to it in place.</summary>
    public abstract NbtCompound Root { get; }

    /// <summary>Gets a value indicating whether there are unsaved changes.</summary>
    public bool IsDirty
    {
        get;
        private set => SetField(ref field, value);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Records that the tree was changed.</summary>
    public void MarkDirty() => IsDirty = true;

    /// <summary>Writes the tree back, backing up the original first if a backup store is given, and marks it clean.</summary>
    public void Save(BackupStore? backups = null)
    {
        Write(backups);
        MarkClean();
    }

    /// <summary>
    /// Writes the tree back without changing <see cref="IsDirty"/>. Safe to call from a background thread;
    /// call <see cref="MarkClean"/> afterwards on the thread that owns the UI.
    /// </summary>
    public void Write(BackupStore? backups = null) => SaveCore(backups);

    /// <summary>Records that the tree matches what's on disk.</summary>
    public void MarkClean() => IsDirty = false;

    protected abstract void SaveCore(BackupStore? backups);

    protected void SetField<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (!EqualityComparer<T>.Default.Equals(field, value))
        {
            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}

/// <summary>A standalone NBT file such as <c>level.dat</c> or a player file.</summary>
public sealed class FileNbtDocument(string path, NbtFile file, string? title = null) : NbtDocument
{
    /// <summary>Gets the file path.</summary>
    public string Path { get; } = System.IO.Path.GetFullPath(path);

    /// <summary>Gets the loaded file, which keeps the original format and compression.</summary>
    public NbtFile File { get; } = file;

    public override string Title { get; } = title ?? System.IO.Path.GetFileName(path);

    public override string Location => Path;

    public override NbtCompound Root => File.Root;

    /// <summary>Loads a file, detecting compression.</summary>
    public static FileNbtDocument Load(string path, string? title = null) => new(path, NbtFile.Load(path), title);

    protected override void SaveCore(BackupStore? backups)
    {
        backups?.Backup(Path);

        // Write to a temporary file first so a failure can't leave a truncated level.dat behind.
        var temp = Path + ".mineeditor.tmp";
        File.Save(temp);
        System.IO.File.Move(temp, Path, overwrite: true);
    }
}

/// <summary>One chunk inside a region file.</summary>
public sealed class ChunkNbtDocument : NbtDocument
{
    private ChunkNbtDocument(string regionPath, int chunkX, int chunkZ, NbtCompound root)
    {
        RegionPath = regionPath;
        ChunkX = chunkX;
        ChunkZ = chunkZ;
        Root = root;
    }

    /// <summary>Gets the region file path.</summary>
    public string RegionPath { get; }

    /// <summary>Gets the absolute chunk X coordinate.</summary>
    public int ChunkX { get; }

    /// <summary>Gets the absolute chunk Z coordinate.</summary>
    public int ChunkZ { get; }

    public override string Title => $"Chunk {ChunkX}, {ChunkZ}";

    public override string Location => $"{RegionPath} [{ChunkX & 31}, {ChunkZ & 31}]";

    public override NbtCompound Root { get; }

    /// <summary>Reads a chunk, returning null if it does not exist.</summary>
    public static ChunkNbtDocument? Load(string regionPath, int chunkX, int chunkZ)
    {
        using var region = RegionFile.Open(regionPath, readOnly: true);
        var root = region.ReadChunk(chunkX & 31, chunkZ & 31);
        return root is null ? null : new ChunkNbtDocument(regionPath, chunkX, chunkZ, root);
    }

    /// <summary>Removes a chunk so the game regenerates it, backing up the region file first.</summary>
    public static bool Delete(string regionPath, int chunkX, int chunkZ, BackupStore? backups = null)
    {
        backups?.Backup(regionPath);
        using var region = RegionFile.Open(regionPath);
        var deleted = region.DeleteChunk(chunkX & 31, chunkZ & 31);
        region.Flush();
        return deleted;
    }

    protected override void SaveCore(BackupStore? backups)
    {
        backups?.Backup(RegionPath);
        using var region = RegionFile.Open(RegionPath);
        region.WriteChunk(ChunkX & 31, ChunkZ & 31, Root);
        region.Flush();
    }
}
