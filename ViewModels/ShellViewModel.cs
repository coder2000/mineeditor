using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using MineEditor.Core.Documents;
using MineEditor.Core.World;

namespace MineEditor.ViewModels;

/// <summary>The result of saving the modified documents.</summary>
public sealed record SaveResult(int Saved, IReadOnlyList<(NbtDocument Document, Exception Error)> Failures, string? BackupPath);

/// <summary>App-wide state: the open world and every document loaded from it.</summary>
public sealed partial class ShellViewModel : ObservableObject
{
    private readonly Dictionary<string, BackupStore> _standaloneBackups = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Raised when a document should be shown in the NBT editor, optionally at a path of keys.</summary>
    public event Action<NbtDocument, IReadOnlyList<string>?>? OpenInEditorRequested;

    /// <summary>Raised after a region file changes on disk, so the map can redraw it.</summary>
    public event Action<string>? RegionChanged;

    /// <summary>Gets the open world, if any.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasWorld), nameof(Title))]
    public partial WorldSave? World { get; private set; }

    /// <summary>Gets the world's players, loaded when the world opens.</summary>
    [ObservableProperty]
    public partial IReadOnlyList<PlayerEntry> Players { get; private set; } = [];

    /// <summary>Gets a value indicating whether the game had the world open when it was checked.</summary>
    [ObservableProperty]
    public partial bool IsWorldInUse { get; private set; }

    /// <summary>Gets every loaded document. Each can be shown in the editor and is saved by Save all.</summary>
    public ObservableCollection<NbtDocument> Documents { get; } = [];

    public bool HasWorld => World is not null;

    public bool HasUnsavedChanges => Documents.Any(d => d.IsDirty);

    public string Title => World is null ? "MineEditor" : $"{World.Name}{(HasUnsavedChanges ? " •" : string.Empty)} — MineEditor";

    /// <summary>Opens a world folder, replacing the current one. Call <see cref="HasUnsavedChanges"/> first.</summary>
    public async Task OpenWorldAsync(string path)
    {
        var (world, players, inUse) = await Task.Run(() =>
        {
            var w = WorldSave.Open(path);
            return (w, w.LoadPlayers(), w.IsInUse());
        });

        ClearDocuments();
        World = world;
        Track(world.Level);
        foreach (var player in players)
        {
            Track(player.Document);
        }

        Players = players;
        IsWorldInUse = inUse;
    }

    /// <summary>Opens a standalone NBT file, or returns the already-loaded document for it.</summary>
    public async Task<NbtDocument> OpenFileAsync(string path)
    {
        var full = Path.GetFullPath(path);
        if (Documents.OfType<FileNbtDocument>().FirstOrDefault(d => d.Path.Equals(full, StringComparison.OrdinalIgnoreCase)) is { } existing)
        {
            return existing;
        }

        var doc = await Task.Run(() => FileNbtDocument.Load(full));
        Track(doc);
        return doc;
    }

    /// <summary>Loads a chunk for editing, or returns the already-loaded document for it.</summary>
    public async Task<ChunkNbtDocument?> OpenChunkAsync(string regionPath, int chunkX, int chunkZ)
    {
        if (Documents.OfType<ChunkNbtDocument>().FirstOrDefault(d => d.ChunkX == chunkX && d.ChunkZ == chunkZ
            && d.RegionPath.Equals(regionPath, StringComparison.OrdinalIgnoreCase)) is { } existing)
        {
            return existing;
        }

        var doc = await Task.Run(() => ChunkNbtDocument.Load(regionPath, chunkX, chunkZ));
        if (doc is not null)
        {
            Track(doc);
        }

        return doc;
    }

    /// <summary>Deletes a chunk so the game regenerates it, discarding any loaded copy.</summary>
    public async Task<bool> DeleteChunkAsync(string regionPath, int chunkX, int chunkZ)
    {
        var deleted = await Task.Run(() => ChunkNbtDocument.Delete(regionPath, chunkX, chunkZ, GetBackups(regionPath)));
        foreach (var doc in Documents.OfType<ChunkNbtDocument>().Where(d => d.ChunkX == chunkX && d.ChunkZ == chunkZ
            && d.RegionPath.Equals(regionPath, StringComparison.OrdinalIgnoreCase)).ToList())
        {
            Untrack(doc);
        }

        RegionChanged?.Invoke(regionPath);
        return deleted;
    }

    public void RequestOpenInEditor(NbtDocument document, IReadOnlyList<string>? path = null) => OpenInEditorRequested?.Invoke(document, path);

    /// <summary>Re-checks whether the game has the world open.</summary>
    public bool RefreshInUse() => IsWorldInUse = World?.IsInUse() ?? false;

    /// <summary>Saves every modified document, backing up each file the first time it is overwritten.</summary>
    public async Task<SaveResult> SaveAllAsync()
    {
        var failures = new List<(NbtDocument, Exception)>();
        var saved = 0;
        string? backupPath = null;
        foreach (var doc in Documents.Where(d => d.IsDirty).ToList())
        {
            var path = doc switch
            {
                FileNbtDocument f => f.Path,
                ChunkNbtDocument c => c.RegionPath,
                _ => null,
            };
            var backups = path is null ? null : GetBackups(path);
            try
            {
                // Only the disk write runs in the background: IsDirty notifications must be raised on the UI thread.
                await Task.Run(() => doc.Write(backups));
                doc.MarkClean();
                saved++;
                backupPath ??= backups?.SessionPath;
                if (doc is ChunkNbtDocument chunk)
                {
                    RegionChanged?.Invoke(chunk.RegionPath);
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
            {
                failures.Add((doc, e));
            }
        }

        return new SaveResult(saved, failures, backupPath);
    }

    private BackupStore GetBackups(string path)
    {
        var full = Path.GetFullPath(path);
        if (World is { } world && full.StartsWith(world.Path + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            return world.Backups;
        }

        var directory = Path.GetDirectoryName(full)!;
        if (!_standaloneBackups.TryGetValue(directory, out var store))
        {
            store = new BackupStore(directory);
            _standaloneBackups[directory] = store;
        }

        return store;
    }

    private void Track(NbtDocument doc)
    {
        if (!Documents.Contains(doc))
        {
            doc.PropertyChanged += OnDocumentChanged;
            Documents.Add(doc);
        }
    }

    private void Untrack(NbtDocument doc)
    {
        doc.PropertyChanged -= OnDocumentChanged;
        Documents.Remove(doc);
        OnDocumentChanged(doc, new PropertyChangedEventArgs(nameof(NbtDocument.IsDirty)));
    }

    private void ClearDocuments()
    {
        foreach (var doc in Documents.ToList())
        {
            Untrack(doc);
        }
    }

    private void OnDocumentChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(NbtDocument.IsDirty))
        {
            OnPropertyChanged(nameof(HasUnsavedChanges));
            OnPropertyChanged(nameof(Title));
        }
    }
}
