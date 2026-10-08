using System.Collections.Concurrent;

namespace MineEditor.Core.Map;

/// <summary>How a block affects the top-down map.</summary>
public enum BlockKind : byte
{
    /// <summary>Not drawn; the scan continues below it (air, torches, rails, ...).</summary>
    Transparent,

    /// <summary>Drawn as water, tinted by the depth to the next solid block.</summary>
    Water,

    /// <summary>Drawn with its own color.</summary>
    Solid,
}

/// <summary>A block's map color (0xRRGGBB) and kind.</summary>
public readonly record struct BlockStyle(uint Color, BlockKind Kind);

/// <summary>
/// Approximate top-down colors for Minecraft blocks, looked up by namespaced id.
/// </summary>
/// <remarks>
/// Exact ids come first, then family rules (dye colors, wood types, suffixes), then keywords.
/// Unrecognised blocks, including modded ones, fall back to a neutral gray.
/// </remarks>
public static class BlockColors
{
    public const uint WaterColor = 0x3F76E4;
    public const uint UnknownColor = 0x8A8A8A;

    private static readonly ConcurrentDictionary<string, BlockStyle> Cache = new(StringComparer.Ordinal);

    private static readonly HashSet<string> TransparentBlocks =
    [
        "air", "cave_air", "void_air", "light", "barrier", "structure_void",
        "torch", "wall_torch", "soul_torch", "soul_wall_torch", "redstone_torch", "redstone_wall_torch",
        "rail", "powered_rail", "detector_rail", "activator_rail", "redstone_wire", "tripwire", "tripwire_hook",
        "lever", "ladder", "cobweb", "string", "end_rod", "lightning_rod", "chain", "iron_bars", "glass_pane",
    ];

    private static readonly HashSet<string> WaterBlocks =
    [
        "water", "flowing_water", "bubble_column", "seagrass", "tall_seagrass", "kelp", "kelp_plant",
    ];

    private static readonly Dictionary<string, uint> Exact = new(StringComparer.Ordinal)
    {
        // Ground
        ["grass_block"] = 0x7DAF4A, ["dirt"] = 0x8B6340, ["coarse_dirt"] = 0x77553B, ["rooted_dirt"] = 0x90684C,
        ["podzol"] = 0x6A4A21, ["mycelium"] = 0x6F6266, ["dirt_path"] = 0x947A41, ["farmland"] = 0x6E4A2B,
        ["mud"] = 0x3C393D, ["clay"] = 0xA0A6B3, ["gravel"] = 0x837E7C, ["sand"] = 0xDBD3A0, ["red_sand"] = 0xBE6621,
        ["snow"] = 0xF8FDFD, ["snow_block"] = 0xF8FDFD, ["powder_snow"] = 0xF8FDFD,
        ["ice"] = 0x91B5FB, ["packed_ice"] = 0x8DB4FA, ["blue_ice"] = 0x74A8FD, ["frosted_ice"] = 0x91B5FB,
        ["moss_block"] = 0x596E2D, ["moss_carpet"] = 0x596E2D, ["pale_moss_block"] = 0x6B7069, ["pale_moss_carpet"] = 0x6B7069,

        // Stone
        ["stone"] = 0x7D7D7D, ["granite"] = 0x956755, ["diorite"] = 0xBCBCBC, ["andesite"] = 0x888888,
        ["deepslate"] = 0x505053, ["tuff"] = 0x6C6D66, ["calcite"] = 0xDFE0DC, ["dripstone_block"] = 0x866B5C,
        ["pointed_dripstone"] = 0x866B5C, ["cobblestone"] = 0x7A7A7A, ["mossy_cobblestone"] = 0x6E7D5E,
        ["smooth_stone"] = 0x9E9E9E, ["bedrock"] = 0x555555, ["obsidian"] = 0x0F0B19, ["crying_obsidian"] = 0x200A3C,
        ["sandstone"] = 0xD8CB9B, ["red_sandstone"] = 0xB5621F, ["bricks"] = 0x966153, ["mud_bricks"] = 0x89684F,
        ["packed_mud"] = 0x8E6A4F, ["prismarine"] = 0x63A397, ["dark_prismarine"] = 0x335B4B, ["sea_lantern"] = 0xACC7BE,
        ["end_stone"] = 0xDBDE9E, ["end_stone_bricks"] = 0xDAE0A2, ["purpur_block"] = 0xA97DA9, ["purpur_pillar"] = 0xAB81AB,
        ["magma_block"] = 0x8E3F1F, ["glowstone"] = 0xAB8654, ["bone_block"] = 0xD1CDB0, ["amethyst_block"] = 0x8562BC,
        ["budding_amethyst"] = 0x8562BC, ["sculk"] = 0x0D1E24, ["sculk_vein"] = 0x0D1E24, ["reinforced_deepslate"] = 0x50534F,

        // Nether
        ["netherrack"] = 0x6F3535, ["nether_bricks"] = 0x2C161A, ["red_nether_bricks"] = 0x450709, ["soul_sand"] = 0x513E32,
        ["soul_soil"] = 0x4B3A2E, ["basalt"] = 0x494A4E, ["smooth_basalt"] = 0x48484E, ["blackstone"] = 0x2A2328,
        ["crimson_nylium"] = 0x822222, ["warped_nylium"] = 0x2B7265, ["nether_wart_block"] = 0x732A2A,
        ["warped_wart_block"] = 0x167E86, ["shroomlight"] = 0xF09246, ["ancient_debris"] = 0x5F4038,
        ["nether_quartz_ore"] = 0x75413E, ["nether_gold_ore"] = 0x73362D, ["lava"] = 0xCF5B14, ["flowing_lava"] = 0xCF5B14,
        ["fire"] = 0xD8892D, ["soul_fire"] = 0x33C1C5,

        // Plants
        ["short_grass"] = 0x6F9E3B, ["grass"] = 0x6F9E3B, ["tall_grass"] = 0x6F9E3B, ["fern"] = 0x5F8A33,
        ["large_fern"] = 0x5F8A33, ["dead_bush"] = 0x6B4F28, ["sugar_cane"] = 0x88B45F, ["cactus"] = 0x5A8A2E,
        ["pumpkin"] = 0xC57618, ["carved_pumpkin"] = 0xC57618, ["jack_o_lantern"] = 0xD49A2A, ["melon"] = 0x6F9124,
        ["lily_pad"] = 0x208030, ["vine"] = 0x3E6B1E, ["bamboo"] = 0x5D8C24, ["sweet_berry_bush"] = 0x3E6B33,
        ["hay_block"] = 0xA68B0C, ["wheat"] = 0xC9AE58, ["carrots"] = 0x5F9E2E, ["potatoes"] = 0x5F9E2E,
        ["beetroots"] = 0x5F9E2E, ["poppy"] = 0xC0272D, ["dandelion"] = 0xF1F902, ["blue_orchid"] = 0x2AA2D7,
        ["allium"] = 0xB878E5, ["azure_bluet"] = 0xE3E3E3, ["oxeye_daisy"] = 0xD6D4C6, ["cornflower"] = 0x466AEB,
        ["lily_of_the_valley"] = 0xF0F0F0, ["sunflower"] = 0xF6C421, ["lilac"] = 0xB98FBB, ["rose_bush"] = 0xA42126,
        ["peony"] = 0xE5B7E5, ["azalea"] = 0x5F7A2A, ["flowering_azalea"] = 0x7A6A7A, ["big_dripleaf"] = 0x6F9124,
        ["glow_lichen"] = 0x6F8070, ["spore_blossom"] = 0xD86FA7, ["pink_petals"] = 0xF0A5C8, ["torchflower"] = 0xE8A23A,
        ["red_mushroom_block"] = 0xC82E2D, ["brown_mushroom_block"] = 0x957051, ["mushroom_stem"] = 0xCBC4B9,
        ["red_mushroom"] = 0xC82E2D, ["brown_mushroom"] = 0x957051, ["chorus_plant"] = 0x5E3C5E, ["chorus_flower"] = 0x977D97,

        // Building and utility
        ["glass"] = 0xC0DCE0, ["tinted_glass"] = 0x2C272E, ["iron_block"] = 0xDCDCDC, ["gold_block"] = 0xF6D03D,
        ["diamond_block"] = 0x62EDE4, ["emerald_block"] = 0x2ACB57, ["lapis_block"] = 0x1F438B, ["redstone_block"] = 0xAF1A06,
        ["coal_block"] = 0x101010, ["netherite_block"] = 0x433F42, ["quartz_block"] = 0xECE6DF, ["bookshelf"] = 0x7A5F3A,
        ["crafting_table"] = 0x8E6A3E, ["furnace"] = 0x6E6E6E, ["chest"] = 0x9F7A3B, ["barrel"] = 0x86643A,
        ["tnt"] = 0xB2402C, ["slime_block"] = 0x76BE6D, ["honey_block"] = 0xF0A22E, ["sponge"] = 0xC3C04A,
        ["water_cauldron"] = WaterColor, ["cauldron"] = 0x4A4A4A, ["anvil"] = 0x444444, ["beacon"] = 0x75DDD7,
        ["terracotta"] = 0x985D43, ["dried_kelp_block"] = 0x323B26, ["target"] = 0xE3B0A3, ["scaffolding"] = 0xAA8449,
    };

    // Ordered so that "light_blue" and "light_gray" match before "blue" and "gray".
    private static readonly (string Name, uint Color)[] DyeColors =
    [
        ("light_blue", 0x3AAFD9), ("light_gray", 0x8E8E86), ("white", 0xE9ECEC), ("orange", 0xF07613),
        ("magenta", 0xBD44B3), ("yellow", 0xF8C527), ("lime", 0x70B919), ("pink", 0xED8DAC), ("gray", 0x3E4447),
        ("cyan", 0x158991), ("purple", 0x792AAC), ("blue", 0x35399D), ("brown", 0x724728), ("green", 0x546D1B),
        ("red", 0xA12722), ("black", 0x141519),
    ];

    private static readonly string[] DyedSuffixes =
    [
        "_wool", "_carpet", "_concrete", "_concrete_powder", "_terracotta", "_glazed_terracotta",
        "_stained_glass", "_stained_glass_pane", "_shulker_box", "_bed", "_banner", "_wall_banner", "_candle",
    ];

    private static readonly (string Wood, uint Planks, uint Log, uint Leaves)[] Woods =
    [
        ("dark_oak", 0x42290F, 0x3C2E1A, 0x3B6B1E), ("pale_oak", 0xE6D9D5, 0x5A504E, 0x8A9A86),
        ("oak", 0xA2834F, 0x6B5432, 0x4A7A2A), ("spruce", 0x735531, 0x3A2A17, 0x3D5E3D),
        ("birch", 0xC0AF79, 0xD5CDB0, 0x6B8F45), ("jungle", 0xA0734D, 0x564419, 0x3F8A1E),
        ("acacia", 0xA85A32, 0x676157, 0x5A8A20), ("mangrove", 0x763631, 0x544030, 0x4F7A1E),
        ("cherry", 0xE3B3AD, 0x36211E, 0xE8B4C8), ("bamboo", 0xC4AF52, 0x5D8C24, 0x5D8C24),
        ("crimson", 0x653147, 0x5C1919, 0x732A2A), ("warped", 0x2B6963, 0x3A3A4D, 0x167E86),
        ("azalea", 0xA2834F, 0x6B5432, 0x5A7A2A),
    ];

    private static readonly (string Keyword, uint Color)[] Keywords =
    [
        ("deepslate", 0x505053), ("blackstone", 0x2A2328), ("sandstone", 0xD8CB9B), ("quartz", 0xECE6DF),
        ("oxidized", 0x52A284), ("weathered", 0x6D9F70), ("exposed", 0xA1806B), ("copper", 0xC06C50),
        ("prismarine", 0x63A397), ("purpur", 0xA97DA9), ("end_stone", 0xDBDE9E), ("nether_brick", 0x2C161A),
        ("mud_brick", 0x89684F), ("brick", 0x966153), ("tuff", 0x6C6D66), ("granite", 0x956755), ("diorite", 0xBCBCBC),
        ("andesite", 0x888888), ("cobblestone", 0x7A7A7A), ("stone", 0x7D7D7D), ("ore", 0x7D7D7D),
        ("coral", 0xC35A8A), ("tulip", 0xD45A3A), ("sapling", 0x4A7A2A), ("leaves", 0x4A7A2A),
        ("mushroom", 0x957051), ("ice", 0x91B5FB), ("snow", 0xF8FDFD), ("sand", 0xDBD3A0), ("dirt", 0x8B6340),
        ("amethyst", 0x8562BC), ("iron", 0xDCDCDC), ("gold", 0xF6D03D), ("lantern", 0x4A4A4A), ("glass", 0xC0DCE0),
    ];

    /// <summary>Gets the map style for a block id such as <c>minecraft:oak_leaves</c>.</summary>
    public static BlockStyle Get(string blockId) => Cache.GetOrAdd(blockId, Resolve);

    private static BlockStyle Resolve(string blockId)
    {
        var name = blockId.StartsWith("minecraft:", StringComparison.Ordinal) ? blockId["minecraft:".Length..] : blockId;
        if (TransparentBlocks.Contains(name) || name.EndsWith("_button", StringComparison.Ordinal))
        {
            return new(0, BlockKind.Transparent);
        }

        if (WaterBlocks.Contains(name))
        {
            return new(WaterColor, BlockKind.Water);
        }

        return new(ResolveColor(name), BlockKind.Solid);
    }

    private static uint ResolveColor(string name)
    {
        if (Exact.TryGetValue(name, out var color))
        {
            return color;
        }

        foreach (var suffix in DyedSuffixes)
        {
            if (name.EndsWith(suffix, StringComparison.Ordinal))
            {
                foreach (var (dye, dyeColor) in DyeColors)
                {
                    if (name.StartsWith(dye + "_", StringComparison.Ordinal))
                    {
                        // Terracotta is a muted version of the dye color.
                        return suffix == "_terracotta" ? Scale(dyeColor, 0.62) : dyeColor;
                    }
                }
            }
        }

        foreach (var (wood, planks, log, leaves) in Woods)
        {
            if (!name.StartsWith(wood + "_", StringComparison.Ordinal) && !name.StartsWith("stripped_" + wood + "_", StringComparison.Ordinal))
            {
                continue;
            }

            if (name.EndsWith("_leaves", StringComparison.Ordinal))
            {
                return leaves;
            }

            if (name.EndsWith("_log", StringComparison.Ordinal) || name.EndsWith("_wood", StringComparison.Ordinal)
                || name.EndsWith("_stem", StringComparison.Ordinal) || name.EndsWith("_hyphae", StringComparison.Ordinal)
                || name.EndsWith("_block", StringComparison.Ordinal))
            {
                return name.StartsWith("stripped_", StringComparison.Ordinal) ? planks : log;
            }

            return planks;
        }

        foreach (var (keyword, keywordColor) in Keywords)
        {
            if (name.Contains(keyword, StringComparison.Ordinal))
            {
                return keywordColor;
            }
        }

        return UnknownColor;
    }

    /// <summary>Multiplies each channel of an 0xRRGGBB color by <paramref name="factor"/>.</summary>
    public static uint Scale(uint color, double factor)
    {
        static uint Channel(uint c, int shift, double f) => (uint)Math.Clamp((int)(((c >> shift) & 0xFF) * f), 0, 255) << shift;
        return Channel(color, 16, factor) | Channel(color, 8, factor) | Channel(color, 0, factor);
    }

    /// <summary>Linearly interpolates between two 0xRRGGBB colors.</summary>
    public static uint Lerp(uint from, uint to, double t)
    {
        static uint Channel(uint a, uint b, int shift, double t)
        {
            var ca = (a >> shift) & 0xFF;
            var cb = (b >> shift) & 0xFF;
            return (uint)Math.Round(ca + ((cb - (double)ca) * t)) << shift;
        }

        return Channel(from, to, 16, t) | Channel(from, to, 8, t) | Channel(from, to, 0, t);
    }
}
