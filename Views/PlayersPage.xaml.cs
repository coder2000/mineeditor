using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using MineEditor.Core.World;
using MineEditor.ViewModels;
using Ubiety.Nbt;

namespace MineEditor.Views;

/// <summary>A row in the inventory list.</summary>
public sealed record InventoryItem(int Order, string Slot, string Id, string Count);

/// <summary>Quick edits for player health, experience, position and abilities.</summary>
public sealed partial class PlayersPage : Page
{
    public PlayersPage()
    {
        InitializeComponent();
    }

    private PlayerEntry? Selected => PlayerList.SelectedItem as PlayerEntry;

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        var players = App.Shell.Players;
        PlayerList.ItemsSource = players;
        EmptyText.Visibility = players.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (players.Count > 0)
        {
            PlayerList.SelectedIndex = 0;
        }
    }

    private void OnPlayerSelected(object sender, SelectionChangedEventArgs e)
    {
        if (Selected is not { } player)
        {
            Details.Visibility = Visibility.Collapsed;
            return;
        }

        Details.Visibility = Visibility.Visible;
        PlayerName.Text = player.DisplayName;
        ShadowedNotice.IsOpen = player.IsShadowedHost;
        Form.Groups = BuildGroups(player);

        var items = ReadInventory(player.Data);
        Inventory.ItemsSource = items;
        InventoryEmpty.Visibility = items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private static List<FieldGroup> BuildGroups(PlayerEntry player)
    {
        var doc = player.Document;
        var data = player.Data;
        var groups = new List<FieldGroup>();

        void Add(string title, params FieldViewModel?[] fields)
        {
            var present = fields.OfType<FieldViewModel>().ToList();
            if (present.Count > 0)
            {
                groups.Add(new FieldGroup(title, present));
            }
        }

        Add("Status",
            FieldViewModel.For(doc, data, "playerGameType", "Game mode", FieldKind.Choice, choices: FieldViewModel.GameModes),
            FieldViewModel.For(doc, data, "Health", "Health", description: "20 is full (10 hearts)."),
            FieldViewModel.For(doc, data, "foodLevel", "Food", description: "20 is full."),
            FieldViewModel.For(doc, data, "foodSaturationLevel", "Saturation"),
            FieldViewModel.For(doc, data, "XpLevel", "Experience level"),
            FieldViewModel.For(doc, data, "XpP", "Progress to next level", description: "From 0 to 1."),
            FieldViewModel.For(doc, data, "Air", "Air", description: "300 is a full breath."),
            FieldViewModel.For(doc, data, "Fire", "Fire ticks", description: "Negative when not burning."));

        var position = data.TryGet<NbtList>("Pos", out var pos) && pos.Count == 3 ? pos : null;
        Add("Position",
            position is null ? null : FieldViewModel.For(doc, position[0], "X"),
            position is null ? null : FieldViewModel.For(doc, position[1], "Y"),
            position is null ? null : FieldViewModel.For(doc, position[2], "Z"),
            FieldViewModel.For(doc, data, "Dimension", "Dimension", description: "For example minecraft:overworld or minecraft:the_nether."));

        var abilities = data.TryGet<NbtCompound>("abilities", out var a) ? a : null;
        Add("Abilities",
            FieldViewModel.For(doc, abilities, "mayfly", "Can fly", FieldKind.Toggle),
            FieldViewModel.For(doc, abilities, "flying", "Flying", FieldKind.Toggle),
            FieldViewModel.For(doc, abilities, "invulnerable", "Invulnerable", FieldKind.Toggle),
            FieldViewModel.For(doc, abilities, "instabuild", "Instant build", FieldKind.Toggle, "Creative-style instant block breaking."),
            FieldViewModel.For(doc, abilities, "mayBuild", "Can build", FieldKind.Toggle),
            FieldViewModel.For(doc, abilities, "flySpeed", "Fly speed"),
            FieldViewModel.For(doc, abilities, "walkSpeed", "Walk speed"));

        return groups;
    }

    private static List<InventoryItem> ReadInventory(NbtCompound player)
    {
        var items = new List<InventoryItem>();
        if (!player.TryGet<NbtList>("Inventory", out var inventory))
        {
            return items;
        }

        foreach (var item in inventory.OfType<NbtCompound>())
        {
            var slot = item.TryGet<NbtByte>("Slot", out var s) ? unchecked((sbyte)s.Value) : (int?)null;
            var id = item.TryGet<NbtString>("id", out var i) ? i.Value : "?";

            // 1.20.5 renamed Count (byte) to count (int).
            var count = item.TryGetValue("count", out var c) || item.TryGetValue("Count", out c) ? c switch
            {
                NbtByte b => b.Value,
                NbtInt n => n.Value,
                _ => 1,
            } : 1;
            items.Add(new InventoryItem(slot is -106 ? 200 : slot ?? 300, SlotName(slot), id, $"× {count.ToString(CultureInfo.CurrentCulture)}"));
        }

        return [.. items.OrderBy(i => i.Order)];
    }

    private static string SlotName(int? slot) => slot switch
    {
        null => "—",
        >= 0 and <= 8 => $"Hotbar {slot + 1}",
        >= 9 and <= 35 => $"Inventory {slot - 8:00}",
        100 => "Boots",
        101 => "Leggings",
        102 => "Chestplate",
        103 => "Helmet",
        -106 => "Off hand",
        _ => $"Slot {slot}",
    };

    private void OnOpenInEditorClick(object sender, RoutedEventArgs e)
    {
        if (Selected is { } player)
        {
            App.Shell.RequestOpenInEditor(player.Document, player.Path);
        }
    }
}
