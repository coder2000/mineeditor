using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Navigation;
using MineEditor.Core.World;
using MineEditor.ViewModels;
using Ubiety.Nbt;

namespace MineEditor.Views;

/// <summary>The world settings stored in <c>level.dat</c>.</summary>
public sealed partial class WorldPage : Page
{
    public WorldPage()
    {
        InitializeComponent();
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (App.Shell.World is not { } world)
        {
            return;
        }

        WorldName.Text = world.Name;
        WorldPath.Text = world.Path;
        Form.Groups = BuildGroups(world);

        if (world.IconPath is { } iconPath)
        {
            try
            {
                var bitmap = new BitmapImage();
                using var stream = File.OpenRead(iconPath);
                await bitmap.SetSourceAsync(stream.AsRandomAccessStream());
                Icon.Source = bitmap;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Leave the placeholder.
            }
        }
    }

    /// <summary>
    /// Builds the form from what this world's <c>level.dat</c> actually contains, so fields that a version
    /// renamed or removed are left out instead of being created.
    /// </summary>
    private static List<FieldGroup> BuildGroups(WorldSave world)
    {
        var doc = world.Level;
        var data = world.Data;
        var groups = new List<FieldGroup>();

        void Add(string title, params FieldViewModel?[] fields)
        {
            var present = fields.OfType<FieldViewModel>().ToList();
            if (present.Count > 0)
            {
                groups.Add(new FieldGroup(title, present));
            }
        }

        Add("General",
            FieldViewModel.For(doc, data, "LevelName", "World name"),
            FieldViewModel.For(doc, data, "GameType", "Default game mode", FieldKind.Choice, choices: FieldViewModel.GameModes),
            FieldViewModel.For(doc, data, "Difficulty", "Difficulty", FieldKind.Choice, choices: FieldViewModel.Difficulties),
            FieldViewModel.For(doc, data, "DifficultyLocked", "Difficulty locked", FieldKind.Toggle),
            FieldViewModel.For(doc, data, "hardcore", "Hardcore", FieldKind.Toggle),
            FieldViewModel.For(doc, data, "allowCommands", "Allow cheats", FieldKind.Toggle, "Lets players use commands."));

        Add("Spawn point",
            FieldViewModel.For(doc, data, "SpawnX", "X"),
            FieldViewModel.For(doc, data, "SpawnY", "Y"),
            FieldViewModel.For(doc, data, "SpawnZ", "Z"),
            FieldViewModel.For(doc, data, "SpawnAngle", "Facing", description: "Yaw in degrees."));

        Add("Time and weather",
            FieldViewModel.For(doc, data, "DayTime", "Time of day", description: "In ticks: 0 sunrise, 6000 noon, 12000 sunset, 18000 midnight. Adds 24000 per day."),
            FieldViewModel.For(doc, data, "Time", "World age", description: "Ticks since the world was created."),
            FieldViewModel.For(doc, data, "raining", "Raining", FieldKind.Toggle),
            FieldViewModel.For(doc, data, "rainTime", "Rain timer", description: "Ticks until rain starts or stops."),
            FieldViewModel.For(doc, data, "thundering", "Thundering", FieldKind.Toggle),
            FieldViewModel.For(doc, data, "thunderTime", "Thunder timer", description: "Ticks until thunder starts or stops."),
            FieldViewModel.For(doc, data, "clearWeatherTime", "Clear weather timer", description: "Ticks of guaranteed clear weather (set by /weather clear)."));

        Add("World border",
            FieldViewModel.For(doc, data, "BorderSize", "Diameter"),
            FieldViewModel.For(doc, data, "BorderCenterX", "Center X"),
            FieldViewModel.For(doc, data, "BorderCenterZ", "Center Z"),
            FieldViewModel.For(doc, data, "BorderDamagePerBlock", "Damage per block"),
            FieldViewModel.For(doc, data, "BorderWarningBlocks", "Warning distance"));

        if (data.TryGet<NbtCompound>("GameRules", out var rules))
        {
            var fields = rules.OrderBy(r => r.Key, StringComparer.OrdinalIgnoreCase)
                .Select(r => FieldViewModel.For(doc, r.Value, r.Key, IsBoolean(r.Value) ? FieldKind.Toggle : FieldKind.Text))
                .ToArray();
            Add("Game rules", fields);
        }

        var seed = data.TryGet<NbtCompound>("WorldGenSettings", out var gen) && gen.TryGet<NbtLong>("seed", out var s) ? s
            : data.TryGet<NbtLong>("RandomSeed", out var legacy) ? legacy : null;
        Add("About",
            seed is null ? null : FieldViewModel.ReadOnly("Seed", seed.Value.ToString(CultureInfo.InvariantCulture)),
            data.TryGet<NbtCompound>("Version", out var version) && version.TryGet<NbtString>("Name", out var name)
                ? FieldViewModel.ReadOnly("Saved with", name.Value)
                : null,
            FieldViewModel.For(doc, data, "DataVersion", "Data version", FieldKind.ReadOnly),
            data.TryGet<NbtLong>("LastPlayed", out var played)
                ? FieldViewModel.ReadOnly("Last played", DateTimeOffset.FromUnixTimeMilliseconds(played.Value).LocalDateTime.ToString("F", CultureInfo.CurrentCulture))
                : null,
            FieldViewModel.ReadOnly("Dimensions", string.Join(", ", world.Dimensions.Select(d => d.DisplayName))));

        return groups;
    }

    private static bool IsBoolean(NbtTag tag) => tag is NbtByte or NbtString { Value: "true" or "false" };

    private void OnOpenInEditorClick(object sender, RoutedEventArgs e)
    {
        if (App.Shell.World is { } world)
        {
            App.Shell.RequestOpenInEditor(world.Level, ["Data"]);
        }
    }
}
