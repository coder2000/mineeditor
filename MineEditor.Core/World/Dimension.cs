using System.Globalization;
using System.Text.RegularExpressions;

namespace MineEditor.Core.World;

/// <summary>A dimension of a world and its region files.</summary>
public sealed partial class Dimension
{
    private Dimension(string id, string displayName, string regionDirectory, int? ceilingY)
    {
        Id = id;
        DisplayName = displayName;
        RegionDirectory = regionDirectory;
        CeilingY = ceilingY;
    }

    /// <summary>Gets the dimension id, e.g. <c>minecraft:the_nether</c>.</summary>
    public string Id { get; }

    /// <summary>Gets a friendly name.</summary>
    public string DisplayName { get; }

    /// <summary>Gets the directory holding the <c>.mca</c> files.</summary>
    public string RegionDirectory { get; }

    /// <summary>Gets the Y to render below for dimensions with a roof, or null.</summary>
    public int? CeilingY { get; }

    /// <summary>Finds every dimension with region files in a world folder.</summary>
    public static IReadOnlyList<Dimension> Discover(string worldPath)
    {
        var result = new List<Dimension>();

        void Add(string id, string name, string directory, int? ceiling = null)
        {
            var regions = Path.Combine(directory, "region");
            if (Directory.Exists(regions))
            {
                result.Add(new Dimension(id, name, regions, ceiling));
            }
        }

        Add("minecraft:overworld", "Overworld", worldPath);
        Add("minecraft:the_nether", "The Nether", Path.Combine(worldPath, "DIM-1"), ceiling: 118);
        Add("minecraft:the_end", "The End", Path.Combine(worldPath, "DIM1"));

        // Datapack dimensions live at dimensions/<namespace>/<path>/region.
        var custom = Path.Combine(worldPath, "dimensions");
        if (Directory.Exists(custom))
        {
            foreach (var ns in Directory.EnumerateDirectories(custom))
            {
                foreach (var dir in Directory.EnumerateDirectories(ns, "*", SearchOption.AllDirectories))
                {
                    if (Path.GetFileName(dir) == "region")
                    {
                        continue;
                    }

                    var id = $"{Path.GetFileName(ns)}:{Path.GetRelativePath(ns, dir).Replace('\\', '/')}";
                    Add(id, id, dir);
                }
            }
        }

        return result;
    }

    /// <summary>Lists the region files in this dimension, keyed by region coordinates. Empty files are skipped.</summary>
    public IReadOnlyDictionary<(int X, int Z), string> GetRegionFiles()
    {
        var result = new Dictionary<(int, int), string>();
        if (!Directory.Exists(RegionDirectory))
        {
            return result;
        }

        foreach (var file in new DirectoryInfo(RegionDirectory).EnumerateFiles("r.*.*.mca"))
        {
            var match = RegionName().Match(file.Name);
            if (match.Success && file.Length > 0)
            {
                var x = int.Parse(match.Groups[1].ValueSpan, CultureInfo.InvariantCulture);
                var z = int.Parse(match.Groups[2].ValueSpan, CultureInfo.InvariantCulture);
                result[(x, z)] = file.FullName;
            }
        }

        return result;
    }

    public override string ToString() => DisplayName;

    [GeneratedRegex(@"^r\.(-?\d+)\.(-?\d+)\.mca$")]
    private static partial Regex RegionName();
}
