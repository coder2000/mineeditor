using Ubiety.Nbt;

namespace MineEditor.Core.World;

/// <summary>A world found in a saves folder.</summary>
public sealed record WorldSummary(string Path, string Name, string FolderName, string Version, DateTimeOffset? LastPlayed, string? IconPath);

/// <summary>Finds worlds in the Minecraft launcher's saves folder.</summary>
public static class SaveLocator
{
    /// <summary>Gets the default Java Edition saves folder, <c>%APPDATA%\.minecraft\saves</c>.</summary>
    public static string DefaultSavesPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), ".minecraft", "saves");

    /// <summary>Lists the worlds in a saves folder, most recently played first. Unreadable worlds are skipped.</summary>
    public static IReadOnlyList<WorldSummary> FindWorlds(string? savesPath = null)
    {
        savesPath ??= DefaultSavesPath;
        if (!Directory.Exists(savesPath))
        {
            return [];
        }

        var worlds = new List<WorldSummary>();
        foreach (var dir in Directory.EnumerateDirectories(savesPath))
        {
            var levelPath = Path.Combine(dir, "level.dat");
            if (!File.Exists(levelPath))
            {
                continue;
            }

            try
            {
                var data = NbtFile.Load(levelPath).Root.GetCompound("Data");
                var folder = Path.GetFileName(dir);
                var name = data.TryGet<NbtString>("LevelName", out var n) ? n.Value : folder;
                var version = data.TryGet<NbtCompound>("Version", out var v) && v.TryGet<NbtString>("Name", out var vn) ? vn.Value : "Unknown version";
                DateTimeOffset? lastPlayed = data.TryGet<NbtLong>("LastPlayed", out var lp) ? DateTimeOffset.FromUnixTimeMilliseconds(lp.Value) : null;
                var icon = Path.Combine(dir, "icon.png");
                worlds.Add(new WorldSummary(dir, name, folder, version, lastPlayed, File.Exists(icon) ? icon : null));
            }
            catch (Exception e) when (e is NbtFormatException or IOException or InvalidDataException or KeyNotFoundException or InvalidCastException)
            {
                // Skip worlds whose level.dat can't be read.
            }
        }

        return [.. worlds.OrderByDescending(w => w.LastPlayed ?? DateTimeOffset.MinValue)];
    }
}
