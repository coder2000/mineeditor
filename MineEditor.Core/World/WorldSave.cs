using MineEditor.Core.Documents;
using Ubiety.Nbt;

namespace MineEditor.Core.World;

/// <summary>A player whose data can be edited.</summary>
/// <param name="DisplayName">A label for lists.</param>
/// <param name="Document">The document that holds the player.</param>
/// <param name="Path">The keys from the document root to the player's compound; empty when the player is the root.</param>
/// <param name="IsShadowedHost">
/// True for the singleplayer host's own <c>playerdata</c> file, which the game ignores in favour of the copy in
/// <c>level.dat</c>.
/// </param>
public sealed record PlayerEntry(string DisplayName, NbtDocument Document, IReadOnlyList<string> Path, bool IsShadowedHost = false)
{
    /// <summary>Gets the player's compound.</summary>
    public NbtCompound Data => Path.Aggregate(Document.Root, (compound, key) => compound.GetCompound(key));
}

/// <summary>An open Java Edition world folder.</summary>
public sealed class WorldSave
{
    private WorldSave(string path, FileNbtDocument level)
    {
        Path = path;
        Level = level;
        Backups = new BackupStore(path);
        Dimensions = Dimension.Discover(path);
    }

    /// <summary>Gets the world folder.</summary>
    public string Path { get; }

    /// <summary>Gets <c>level.dat</c>.</summary>
    public FileNbtDocument Level { get; }

    /// <summary>Gets the <c>Data</c> compound of <c>level.dat</c>, which holds the world settings.</summary>
    public NbtCompound Data => Level.Root.TryGet<NbtCompound>("Data", out var data) ? data : Level.Root;

    /// <summary>Gets the world's display name.</summary>
    public string Name => Data.TryGet<NbtString>("LevelName", out var name) ? name.Value : System.IO.Path.GetFileName(Path);

    /// <summary>Gets the backup store used when saving.</summary>
    public BackupStore Backups { get; }

    /// <summary>Gets the dimensions with region data.</summary>
    public IReadOnlyList<Dimension> Dimensions { get; }

    /// <summary>Gets the world's <c>icon.png</c>, if it has one.</summary>
    public string? IconPath => File.Exists(System.IO.Path.Combine(Path, "icon.png")) ? System.IO.Path.Combine(Path, "icon.png") : null;

    /// <summary>Opens a world from its folder or its <c>level.dat</c>.</summary>
    /// <exception cref="FileNotFoundException">There is no <c>level.dat</c>.</exception>
    public static WorldSave Open(string path)
    {
        if (File.Exists(path) && System.IO.Path.GetFileName(path).Equals("level.dat", StringComparison.OrdinalIgnoreCase))
        {
            path = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(path))!;
        }

        path = System.IO.Path.GetFullPath(path);
        var levelPath = System.IO.Path.Combine(path, "level.dat");
        if (!File.Exists(levelPath))
        {
            throw new FileNotFoundException("The folder does not contain a level.dat file.", levelPath);
        }

        return new WorldSave(path, FileNbtDocument.Load(levelPath));
    }

    /// <summary>
    /// Determines whether the game currently has the world open. Minecraft holds an exclusive lock on
    /// <c>session.lock</c> while a world is loaded, and writing to the world then risks the game overwriting the
    /// changes or corrupting the files.
    /// </summary>
    public bool IsInUse()
    {
        var lockPath = System.IO.Path.Combine(Path, "session.lock");
        if (!File.Exists(lockPath) || OperatingSystem.IsMacOS())
        {
            return false;
        }

        try
        {
            using var stream = new FileStream(lockPath, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite | FileShare.Delete);
            stream.Lock(0, 1);
            stream.Unlock(0, 1);
            return false;
        }
        catch (IOException)
        {
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            return true;
        }
    }

    /// <summary>
    /// Lists the players: the singleplayer host stored in <c>level.dat</c>, then every file in <c>playerdata</c>.
    /// Player files that can't be read are skipped.
    /// </summary>
    public IReadOnlyList<PlayerEntry> LoadPlayers()
    {
        var players = new List<PlayerEntry>();
        int[]? hostUuid = null;
        if (Level.Root.TryGet<NbtCompound>("Data", out var data) && data.TryGet<NbtCompound>("Player", out var host))
        {
            players.Add(new PlayerEntry("Singleplayer (level.dat)", Level, ["Data", "Player"]));
            hostUuid = host.TryGet<NbtIntArray>("UUID", out var uuid) ? uuid.Value : null;
        }

        var directory = System.IO.Path.Combine(Path, "playerdata");
        if (Directory.Exists(directory))
        {
            foreach (var file in Directory.EnumerateFiles(directory, "*.dat").Order(StringComparer.OrdinalIgnoreCase))
            {
                try
                {
                    var name = System.IO.Path.GetFileNameWithoutExtension(file);
                    var doc = FileNbtDocument.Load(file, $"Player {name}");
                    var shadowed = hostUuid is not null && doc.Root.TryGet<NbtIntArray>("UUID", out var uuid) && uuid.Value.AsSpan().SequenceEqual(hostUuid);
                    players.Add(new PlayerEntry(name, doc, [], shadowed));
                }
                catch (Exception e) when (e is NbtFormatException or IOException or InvalidDataException)
                {
                    // A corrupt player file shouldn't stop the rest of the world from opening.
                }
            }
        }

        return players;
    }
}
