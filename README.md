# MineEditor

A Minecraft Java Edition save editor and map viewer for Windows, built with WinUI 3 on
[Ubiety.Nbt](../ubiety/ubiety.nbt).

- **World**: edit `level.dat` settings: name, game mode, difficulty, cheats, spawn, time, weather, world border and game rules.
- **Players**: health, food, experience, position, dimension and abilities, plus an inventory list.
- **Map**: a pannable, zoomable top-down map of each dimension (the Nether is drawn below its roof). Click a chunk
  for details, double-click to edit its NBT, or delete it so the game regenerates it.
- **NBT editor**: a tree editor for `level.dat`, player files, chunks, or any NBT file, with SNBT editing.

Chunk formats from 1.13 onwards are supported (1.18+ and the older `Level` layout). Bedrock worlds are not.

## Safety

- Before a file is first overwritten in a session, the original is copied to `<world>/.mineeditor-backups/<time>/`.
- Files are written to a temporary file and then swapped in, so a failed save can't leave a truncated `level.dat`.
- Minecraft locks `session.lock` while a world is open. MineEditor detects this and warns before saving.
- In singleplayer the game loads the host player from `level.dat`, not `playerdata/<uuid>.dat`. The Players page
  marks the host's own file so edits go to the copy that's used.

## Building

Ubiety.Nbt isn't on nuget.org yet, so `nuget.config` restores it from `./packages`. Refresh the package after
changing the library:

```
dotnet pack ..\ubiety\ubiety.nbt\src\ubiety.nbt -c Release -o packages
```

Then build or run from Visual Studio (the `MineEditor (Package)` profile), or:

```
dotnet build MineEditor.csproj -p:Platform=x64
dotnet test MineEditor.Core.Tests
```

`MineEditor.exe <world folder or level.dat>` opens a world directly.

## Layout

| Project | Contents |
|---|---|
| `MineEditor.Core` | UI-free logic: world loading, documents and backups, value editing, chunk reading and map rendering |
| `MineEditor.Core.Tests` | xUnit tests using synthetic chunks and worlds |
| `MineEditor` (root) | The WinUI app: `Views`, `ViewModels`, `Controls` |

Release builds are trimmed. If you bind a new collection or a `MineEditor.Core` type in XAML, check the page in a
trimmed Release build; see `WinRTExposedTypes.cs`.

## License

Apache License 2.0. See [LICENSE](LICENSE).
