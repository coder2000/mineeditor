namespace MineEditor.Core.Documents;

/// <summary>
/// Copies files into a timestamped folder inside the world before they are first overwritten in a session.
/// </summary>
/// <remarks>
/// Backups go inside the world (in <c>.mineeditor-backups</c>) rather than next to it, because a folder holding a
/// copy of <c>level.dat</c> in the saves directory would appear as a broken world in the game's world list.
/// </remarks>
public sealed class BackupStore(string worldPath)
{
    public const string FolderName = ".mineeditor-backups";

    private readonly HashSet<string> _done = new(StringComparer.OrdinalIgnoreCase);
    private readonly Lock _lock = new();

    /// <summary>Gets the world folder.</summary>
    public string WorldPath { get; } = Path.GetFullPath(worldPath);

    /// <summary>Gets this session's backup folder.</summary>
    public string SessionPath { get; } = Path.Combine(Path.GetFullPath(worldPath), FolderName, DateTime.Now.ToString("yyyyMMdd-HHmmss"));

    /// <summary>Copies a file into the backup folder, once per session. Files outside the world are backed up by name.</summary>
    /// <returns>The backup path, or null if the file doesn't exist.</returns>
    public string? Backup(string path)
    {
        var full = Path.GetFullPath(path);
        if (!File.Exists(full))
        {
            return null;
        }

        var relative = Path.GetRelativePath(WorldPath, full);
        if (relative.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(relative))
        {
            relative = Path.Combine("external", Path.GetFileName(full));
        }

        var target = Path.Combine(SessionPath, relative);
        lock (_lock)
        {
            if (_done.Add(full))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(full, target, overwrite: true);
            }
        }

        return target;
    }
}
