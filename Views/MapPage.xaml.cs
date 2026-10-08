using System.Globalization;
using System.Numerics;
using System.Runtime.InteropServices;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Text;
using Microsoft.Graphics.Canvas.UI;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;
using MineEditor.Core.Map;
using MineEditor.Core.World;
using Ubiety.Nbt;
using Ubiety.Nbt.Region;
using Windows.Foundation;
using Windows.Graphics.DirectX;
using Windows.UI;

namespace MineEditor.Views;

/// <summary>A pannable, zoomable top-down map of a dimension.</summary>
public sealed partial class MapPage : Page
{
    private const double MinScale = 1.0 / 8;
    private const double MaxScale = 32;
    private const int MaxBitmaps = 384;
    private const double DragThreshold = 4;

    private readonly Dictionary<(int X, int Z), Tile> _tiles = [];
    private readonly SemaphoreSlim _renderSlots = new(Math.Max(1, Environment.ProcessorCount - 1));
    private readonly CanvasTextFormat _labelFormat = new() { FontSize = 12, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, WordWrapping = CanvasWordWrapping.NoWrap };

    private WorldSave? _world;
    private Dimension? _dimension;
    private IReadOnlyDictionary<(int X, int Z), string> _regionFiles = new Dictionary<(int, int), string>();
    private CancellationTokenSource _renderCancel = new();
    private int _generation;
    private int _pending;
    private long _frame;

    private double _centerX;
    private double _centerZ;
    private double _scale = 1;
    private Point? _pressPoint;
    private Point _lastPoint;
    private bool _dragging;
    private (int X, int Z)? _selectedChunk;
    private (int X, int Z) _selectedBlock;

    public MapPage()
    {
        InitializeComponent();
        App.Shell.RegionChanged += OnRegionChanged;
        ActualThemeChanged += (_, _) => Canvas.Invalidate();
    }

    private sealed class Tile
    {
        public CanvasBitmap? Bitmap { get; set; }

        public bool Pending { get; set; }

        public bool Stale { get; set; }

        public bool Failed { get; set; }

        public long LastDrawn { get; set; }
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (App.Shell.World is not { } world || world == _world)
        {
            return;
        }

        _world = world;
        _selectedChunk = null;
        ChunkPanel.Visibility = Visibility.Collapsed;
        DimensionPicker.ItemsSource = world.Dimensions;
        DimensionPicker.SelectedIndex = world.Dimensions.Count > 0 ? 0 : -1;
        CenterOnSpawn();
        CursorText.Text = ZoomText();
    }

    private void OnDimensionChanged(object sender, SelectionChangedEventArgs e)
    {
        _dimension = DimensionPicker.SelectedItem as Dimension;
        ResetTiles();
        _regionFiles = _dimension?.GetRegionFiles() ?? new Dictionary<(int, int), string>();
        NoRegionsText.Visibility = _regionFiles.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        _selectedChunk = null;
        ChunkPanel.Visibility = Visibility.Collapsed;

        // The Nether's spawn is meaningless, so center on the middle of the explored area instead.
        if (_dimension?.Id != "minecraft:overworld" && _regionFiles.Count > 0)
        {
            _centerX = (_regionFiles.Keys.Average(k => k.X) * RegionRenderer.Size) + (RegionRenderer.Size / 2.0);
            _centerZ = (_regionFiles.Keys.Average(k => k.Z) * RegionRenderer.Size) + (RegionRenderer.Size / 2.0);
        }
        else
        {
            CenterOnSpawn();
        }

        Canvas.Invalidate();
    }

    private void ResetTiles()
    {
        _renderCancel.Cancel();
        _renderCancel = new CancellationTokenSource();
        _generation++;
        foreach (var tile in _tiles.Values)
        {
            tile.Bitmap?.Dispose();
        }

        _tiles.Clear();
        _pending = 0;
        UpdateProgress();
    }

    private void CenterOnSpawn()
    {
        if (_world is null)
        {
            return;
        }

        var spawn = GetSpawn(_world.Data);
        (_centerX, _centerZ) = spawn is { } s ? (s.X + 0.5, s.Z + 0.5) : (0, 0);
        Canvas.Invalidate();
    }

    private static (int X, int Z)? GetSpawn(NbtCompound data)
    {
        if (data.TryGet<NbtInt>("SpawnX", out var x) && data.TryGet<NbtInt>("SpawnZ", out var z))
        {
            return (x.Value, z.Value);
        }

        // Newer versions keep the spawn in a "spawn" compound with an int array position.
        if (data.TryGet<NbtCompound>("spawn", out var spawn) && spawn.TryGet<NbtIntArray>("pos", out var pos) && pos.Value.Length == 3)
        {
            return (pos.Value[0], pos.Value[2]);
        }

        return null;
    }

    private void OnCreateResources(CanvasControl sender, CanvasCreateResourcesEventArgs args)
    {
        // Bitmaps belong to the old device after a device loss, so render everything again.
        if (args.Reason != CanvasCreateResourcesReason.FirstTime)
        {
            ResetTiles();
        }
    }

    private void OnDraw(CanvasControl sender, CanvasDrawEventArgs args)
    {
        var ds = args.DrawingSession;
        var dark = ActualTheme == ElementTheme.Dark;
        ds.Clear(dark ? Color.FromArgb(255, 28, 28, 28) : Color.FromArgb(255, 232, 232, 232));
        if (_dimension is null)
        {
            return;
        }

        _frame++;
        var size = sender.Size;
        var (minX, minZ) = ToWorld(new Point(0, 0));
        var (maxX, maxZ) = ToWorld(new Point(size.Width, size.Height));
        var interpolation = _scale >= 1 ? CanvasImageInterpolation.NearestNeighbor : CanvasImageInterpolation.Linear;
        var placeholder = dark ? Color.FromArgb(255, 38, 38, 38) : Color.FromArgb(255, 220, 220, 220);

        var rx0 = (int)Math.Floor(minX / RegionRenderer.Size);
        var rx1 = (int)Math.Floor(maxX / RegionRenderer.Size);
        var rz0 = (int)Math.Floor(minZ / RegionRenderer.Size);
        var rz1 = (int)Math.Floor(maxZ / RegionRenderer.Size);
        for (var rz = rz0; rz <= rz1; rz++)
        {
            for (var rx = rx0; rx <= rx1; rx++)
            {
                if (!_regionFiles.TryGetValue((rx, rz), out var path))
                {
                    continue;
                }

                var dest = ToScreenRect(rx * RegionRenderer.Size, rz * RegionRenderer.Size, RegionRenderer.Size, RegionRenderer.Size);
                if (!_tiles.TryGetValue((rx, rz), out var tile))
                {
                    tile = new Tile();
                    _tiles[(rx, rz)] = tile;
                }

                tile.LastDrawn = _frame;
                if (tile.Bitmap is { } bitmap)
                {
                    ds.DrawImage(bitmap, dest, bitmap.Bounds, 1, interpolation);
                }
                else
                {
                    ds.FillRectangle(dest, placeholder);
                }

                if ((tile.Bitmap is null || tile.Stale) && !tile.Pending && !tile.Failed)
                {
                    _ = RenderTileAsync(rx, rz, path, tile);
                }
            }
        }

        if (GridToggle.IsChecked == true && _scale >= 4)
        {
            DrawChunkGrid(ds, minX, minZ, maxX, maxZ, size, dark);
        }

        if (_selectedChunk is { } chunk)
        {
            var accent = Application.Current.Resources.TryGetValue("SystemAccentColor", out var value) && value is Color c ? c : Colors.DodgerBlue;
            ds.DrawRectangle(ToScreenRect(chunk.X * 16, chunk.Z * 16, 16, 16), accent, 2);
        }

        DrawMarkers(ds);
        EvictBitmaps();
    }

    private void DrawChunkGrid(CanvasDrawingSession ds, double minX, double minZ, double maxX, double maxZ, Size size, bool dark)
    {
        var line = dark ? Color.FromArgb(60, 0, 0, 0) : Color.FromArgb(50, 0, 0, 0);
        for (var x = Math.Floor(minX / 16) * 16; x <= maxX; x += 16)
        {
            var sx = (float)ToScreen(x, 0).X;
            ds.DrawLine(sx, 0, sx, (float)size.Height, line);
        }

        for (var z = Math.Floor(minZ / 16) * 16; z <= maxZ; z += 16)
        {
            var sz = (float)ToScreen(0, z).Y;
            ds.DrawLine(0, sz, (float)size.Width, sz, line);
        }
    }

    private void DrawMarkers(CanvasDrawingSession ds)
    {
        if (_world is null || _dimension is null)
        {
            return;
        }

        if (_dimension.Id == "minecraft:overworld" && GetSpawn(_world.Data) is { } spawn)
        {
            DrawMarker(ds, spawn.X + 0.5, spawn.Z + 0.5, "Spawn", Colors.Gold);
        }

        foreach (var player in App.Shell.Players.Where(p => !p.IsShadowedHost))
        {
            if (player.Data.TryGet<NbtList>("Pos", out var pos) && pos.Count == 3 && pos[0] is NbtDouble x && pos[2] is NbtDouble z
                && PlayerDimension(player.Data) == _dimension.Id)
            {
                var name = player.Path.Count > 0 ? "Player" : player.DisplayName[..Math.Min(8, player.DisplayName.Length)];
                DrawMarker(ds, x.Value, z.Value, name, Colors.White);
            }
        }
    }

    private static string PlayerDimension(NbtCompound player) => player.TryGetValue("Dimension", out var tag) ? tag switch
    {
        NbtString s => s.Value,
        NbtInt { Value: -1 } => "minecraft:the_nether",
        NbtInt { Value: 1 } => "minecraft:the_end",
        _ => "minecraft:overworld",
    } : "minecraft:overworld";

    private void DrawMarker(CanvasDrawingSession ds, double worldX, double worldZ, string label, Color color)
    {
        var p = ToScreen(worldX, worldZ);
        var center = new Vector2((float)p.X, (float)p.Y);
        ds.FillCircle(center, 6, Colors.Black);
        ds.FillCircle(center, 4.5f, color);

        using var layout = new CanvasTextLayout(ds, label, _labelFormat, 0, 0);
        var bounds = layout.LayoutBounds;
        var origin = new Vector2(center.X + 10, center.Y - (float)(bounds.Height / 2));
        ds.FillRoundedRectangle(new Rect(origin.X - 4, origin.Y - 1, bounds.Width + 8, bounds.Height + 2), 3, 3, Color.FromArgb(180, 0, 0, 0));
        ds.DrawTextLayout(layout, origin, Colors.White);
    }

    private async Task RenderTileAsync(int rx, int rz, string path, Tile tile)
    {
        var generation = _generation;
        var token = _renderCancel.Token;
        var ceiling = _dimension?.CeilingY;
        tile.Pending = true;
        tile.Stale = false;
        _pending++;
        UpdateProgress();
        try
        {
            await _renderSlots.WaitAsync(token);
            RegionImage image;
            try
            {
                image = await Task.Run(() => RegionRenderer.Render(path, rx, rz, ceiling, token), token);
            }
            finally
            {
                _renderSlots.Release();
            }

            if (generation != _generation)
            {
                return;
            }

            var bytes = MemoryMarshal.AsBytes(image.Pixels.AsSpan()).ToArray();
            var bitmap = CanvasBitmap.CreateFromBytes(Canvas, bytes, RegionRenderer.Size, RegionRenderer.Size, DirectXPixelFormat.B8G8R8A8UIntNormalized);
            tile.Bitmap?.Dispose();
            tile.Bitmap = bitmap;
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or NbtFormatException)
        {
            tile.Failed = true;
        }
        finally
        {
            if (generation == _generation)
            {
                tile.Pending = false;
                _pending--;
                UpdateProgress();
                Canvas.Invalidate();
            }
        }
    }

    /// <summary>Frees the least recently drawn off-screen bitmaps once there are too many.</summary>
    private void EvictBitmaps()
    {
        var loaded = _tiles.Where(t => t.Value.Bitmap is not null).ToList();
        if (loaded.Count <= MaxBitmaps)
        {
            return;
        }

        foreach (var (key, tile) in loaded.Where(t => t.Value.LastDrawn != _frame && !t.Value.Pending).OrderBy(t => t.Value.LastDrawn).Take(loaded.Count - MaxBitmaps))
        {
            tile.Bitmap!.Dispose();
            _tiles.Remove(key);
        }
    }

    private void UpdateProgress()
    {
        Progress.Visibility = _pending > 0 ? Visibility.Visible : Visibility.Collapsed;
        ProgressText.Text = _pending == 1 ? "Rendering 1 region…" : $"Rendering {_pending} regions…";
    }

    private void OnRegionChanged(string path)
    {
        foreach (var (key, file) in _regionFiles)
        {
            if (file.Equals(path, StringComparison.OrdinalIgnoreCase) && _tiles.TryGetValue(key, out var tile))
            {
                tile.Stale = true;
                tile.Failed = false;
            }
        }

        Canvas.Invalidate();
    }

    private (double X, double Z) ToWorld(Point screen) =>
        (_centerX + ((screen.X - (Canvas.ActualWidth / 2)) / _scale), _centerZ + ((screen.Y - (Canvas.ActualHeight / 2)) / _scale));

    private Point ToScreen(double worldX, double worldZ) =>
        new(((worldX - _centerX) * _scale) + (Canvas.ActualWidth / 2), ((worldZ - _centerZ) * _scale) + (Canvas.ActualHeight / 2));

    private Rect ToScreenRect(double worldX, double worldZ, double width, double height)
    {
        var topLeft = ToScreen(worldX, worldZ);
        return new Rect(topLeft.X, topLeft.Y, width * _scale, height * _scale);
    }

    private void ZoomAt(Point screen, double factor)
    {
        var (wx, wz) = ToWorld(screen);
        _scale = Math.Clamp(_scale * factor, MinScale, MaxScale);
        _centerX = wx - ((screen.X - (Canvas.ActualWidth / 2)) / _scale);
        _centerZ = wz - ((screen.Y - (Canvas.ActualHeight / 2)) / _scale);
        Canvas.Invalidate();
        UpdateCursorText(screen);
    }

    private void OnPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        var point = e.GetCurrentPoint(Canvas);
        if (!point.Properties.IsLeftButtonPressed)
        {
            return;
        }

        _pressPoint = _lastPoint = point.Position;
        _dragging = false;
        Canvas.CapturePointer(e.Pointer);
    }

    private void OnPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        var position = e.GetCurrentPoint(Canvas).Position;
        UpdateCursorText(position);
        if (_pressPoint is not { } press)
        {
            return;
        }

        if (!_dragging && Math.Abs(position.X - press.X) + Math.Abs(position.Y - press.Y) > DragThreshold)
        {
            _dragging = true;
        }

        if (_dragging)
        {
            _centerX -= (position.X - _lastPoint.X) / _scale;
            _centerZ -= (position.Y - _lastPoint.Y) / _scale;
            _lastPoint = position;
            Canvas.Invalidate();
        }
    }

    private async void OnPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        var wasClick = _pressPoint is not null && !_dragging;
        _pressPoint = null;
        _dragging = false;
        Canvas.ReleasePointerCapture(e.Pointer);
        if (wasClick)
        {
            var (wx, wz) = ToWorld(e.GetCurrentPoint(Canvas).Position);
            await SelectChunkAsync((int)Math.Floor(wx), (int)Math.Floor(wz));
        }
    }

    private void OnPointerCanceled(object sender, PointerRoutedEventArgs e)
    {
        _pressPoint = null;
        _dragging = false;
        Canvas.ReleasePointerCapture(e.Pointer);
    }

    private void OnPointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (_pressPoint is null)
        {
            CursorText.Text = ZoomText();
        }
    }

    private void OnPointerWheelChanged(object sender, PointerRoutedEventArgs e)
    {
        var point = e.GetCurrentPoint(Canvas);
        ZoomAt(point.Position, Math.Pow(1.25, point.Properties.MouseWheelDelta / 120.0));
        e.Handled = true;
    }

    private async void OnDoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        var (wx, wz) = ToWorld(e.GetPosition(Canvas));
        var (cx, cz) = ((int)Math.Floor(wx) >> 4, (int)Math.Floor(wz) >> 4);
        await OpenChunkAsync(cx, cz);
    }

    private void UpdateCursorText(Point screen)
    {
        var (wx, wz) = ToWorld(screen);
        var (bx, bz) = ((int)Math.Floor(wx), (int)Math.Floor(wz));
        CursorText.Text = string.Create(CultureInfo.InvariantCulture,
            $"X {bx}  Z {bz}   chunk {bx >> 4}, {bz >> 4}   region {bx >> 9}, {bz >> 9}   {ZoomText()}");
    }

    private string ZoomText() => _scale >= 1
        ? string.Create(CultureInfo.InvariantCulture, $"{_scale:0.#}×")
        : string.Create(CultureInfo.InvariantCulture, $"1:{1 / _scale:0.#}");

    private bool TryGetRegionPath(int chunkX, int chunkZ, out string path)
    {
        var (rx, rz) = RegionFile.GetRegionCoordinates(chunkX, chunkZ);
        return _regionFiles.TryGetValue((rx, rz), out path!);
    }

    private async Task SelectChunkAsync(int blockX, int blockZ)
    {
        var (cx, cz) = (blockX >> 4, blockZ >> 4);
        if (!TryGetRegionPath(cx, cz, out var path))
        {
            _selectedChunk = null;
            ChunkPanel.Visibility = Visibility.Collapsed;
            Canvas.Invalidate();
            return;
        }

        _selectedChunk = (cx, cz);
        _selectedBlock = (blockX, blockZ);
        Canvas.Invalidate();

        var ceiling = _dimension?.CeilingY;
        var facts = await Task.Run(() => DescribeChunk(path, cx, cz, blockX & 15, blockZ & 15, ceiling));
        if (_selectedChunk != (cx, cz))
        {
            return; // Another chunk was clicked while this one loaded.
        }

        ChunkTitle.Text = string.Create(CultureInfo.InvariantCulture, $"Chunk {cx}, {cz}");
        ChunkFacts.ItemsSource = facts.Lines;
        OpenChunkButton.IsEnabled = DeleteChunkButton.IsEnabled = facts.Exists;
        ChunkPanel.Visibility = Visibility.Visible;
    }

    private static (bool Exists, List<string> Lines) DescribeChunk(string path, int cx, int cz, int localX, int localZ, int? ceiling)
    {
        var lines = new List<string>
        {
            string.Create(CultureInfo.InvariantCulture, $"Blocks {cx * 16}, {cz * 16} to {(cx * 16) + 15}, {(cz * 16) + 15}"),
            $"Region file {Path.GetFileName(path)}",
        };

        NbtCompound? chunk;
        DateTimeOffset? saved;
        try
        {
            using var region = RegionFile.Open(path, readOnly: true);
            chunk = region.ReadChunk(cx & 31, cz & 31);
            saved = region.GetTimestamp(cx & 31, cz & 31);
        }
        catch (Exception e) when (e is IOException or NbtFormatException or NotSupportedException)
        {
            lines.Add($"Couldn't read this chunk: {e.Message}");
            return (false, lines);
        }

        if (chunk is null)
        {
            lines.Add("Not generated yet.");
            return (false, lines);
        }

        var root = chunk.TryGet<NbtCompound>("Level", out var level) ? level : chunk;
        var view = ChunkView.TryCreate(chunk);
        if (view is not null)
        {
            lines.Add($"Status {(view.Status.Length > 0 ? view.Status : "unknown")}");
        }

        if (chunk.TryGet<NbtInt>("DataVersion", out var dataVersion))
        {
            lines.Add(string.Create(CultureInfo.InvariantCulture, $"Data version {dataVersion.Value}"));
        }

        if (root.TryGet<NbtLong>("InhabitedTime", out var inhabited))
        {
            var time = TimeSpan.FromSeconds(inhabited.Value / 20.0);
            lines.Add($"Players have spent {(time.TotalHours >= 1 ? $"{time.TotalHours:0.#} hours" : $"{time.TotalMinutes:0} minutes")} here");
        }

        if (saved is { } s)
        {
            lines.Add($"Last saved {s.LocalDateTime.ToString("g", CultureInfo.CurrentCulture)}");
        }

        if (view?.SampleColumn(localX, localZ, ceiling) is { } sample)
        {
            var water = sample.WaterDepth > 0 ? $", {sample.WaterDepth} deep" : string.Empty;
            lines.Add(string.Create(CultureInfo.InvariantCulture, $"Clicked block: {sample.BlockId} at Y {sample.Height}{water}"));
        }

        return (true, lines);
    }

    private async Task OpenChunkAsync(int cx, int cz)
    {
        if (!TryGetRegionPath(cx, cz, out var path))
        {
            return;
        }

        try
        {
            if (await App.Shell.OpenChunkAsync(path, cx, cz) is { } doc)
            {
                App.Shell.RequestOpenInEditor(doc);
            }
        }
        catch (Exception e) when (e is IOException or NbtFormatException or NotSupportedException)
        {
            App.MainWindow.ShowStatus(InfoBarSeverity.Error, "Couldn't read the chunk", e.Message);
        }
    }

    private async void OnOpenChunkClick(object sender, RoutedEventArgs e)
    {
        if (_selectedChunk is { } chunk)
        {
            await OpenChunkAsync(chunk.X, chunk.Z);
        }
    }

    private async void OnDeleteChunkClick(object sender, RoutedEventArgs e)
    {
        if (_selectedChunk is not { } chunk || !TryGetRegionPath(chunk.X, chunk.Z, out var path))
        {
            return;
        }

        if (App.Shell.RefreshInUse())
        {
            App.MainWindow.ShowStatus(InfoBarSeverity.Warning, "Minecraft has this world open", "Quit the world in Minecraft before deleting chunks.");
            return;
        }

        var answer = await App.MainWindow.ShowDialogAsync(
            $"Delete chunk {chunk.X}, {chunk.Z}?",
            "Minecraft will generate this chunk again from the seed the next time it's loaded. Anything built in it, and any unsaved edits to it, will be lost. The region file is backed up first.",
            "Delete chunk",
            destructive: true);
        if (answer != ContentDialogResult.Primary)
        {
            return;
        }

        try
        {
            await App.Shell.DeleteChunkAsync(path, chunk.X, chunk.Z);
            App.MainWindow.ShowStatus(InfoBarSeverity.Success, "Chunk deleted",
                $"The original region file was backed up to {_world?.Backups.SessionPath}.");
            await SelectChunkAsync(_selectedBlock.X, _selectedBlock.Z);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NbtFormatException)
        {
            App.MainWindow.ShowStatus(InfoBarSeverity.Error, "Couldn't delete the chunk", ex.Message);
        }
    }

    private void OnCloseChunkPanelClick(object sender, RoutedEventArgs e)
    {
        _selectedChunk = null;
        ChunkPanel.Visibility = Visibility.Collapsed;
        Canvas.Invalidate();
    }

    private Point ViewCenter => new(Canvas.ActualWidth / 2, Canvas.ActualHeight / 2);

    private void OnZoomInClick(object sender, RoutedEventArgs e) => ZoomAt(ViewCenter, 2);

    private void OnZoomOutClick(object sender, RoutedEventArgs e) => ZoomAt(ViewCenter, 0.5);

    private void OnCenterClick(object sender, RoutedEventArgs e) => CenterOnSpawn();

    private void OnGridToggled(object sender, RoutedEventArgs e) => Canvas.Invalidate();
}
